using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Sandbox.ModAPI;
using VRage.FileSystem;

namespace Lanyard;

// One player-wide remaining duration, independent of world/server and wall
// clock. Only online monotonic time spends it. The old UTC format is migrated
// once; a v2 zero record prevents old files from reviving completed activity.
internal sealed class CombatCooldownStore
{
    internal const string FileName = "Lanyard.CombatCooldown.v2.txt";
    internal const string LegacyFileName = "Lanyard.CombatCooldown.v1.txt";
    private const string Prefix = "2:";
    private const long MaximumTicks = CombatLockout.CooldownSeconds * TimeSpan.TicksPerSecond;
    private static Func<long> timestamp = Stopwatch.GetTimestamp;
    private static Func<DateTime> utcNow = () => DateTime.UtcNow;
    // Keep fault injection at individual I/O operations; the atomic path itself
    // always creates, owns and publishes the real temporary file.
    private static Action<FileStream, byte[]> writeRecord = (stream, bytes) => stream.Write(bytes, 0, bytes.Length);
    private static Action<FileStream> flushRecord = stream => stream.Flush(true);
    private static Action<string, string> publishRecord = (temporary, path) =>
    {
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    };
    private long savedRemaining = -1;
    private long lastWrite;
    private long knownDeadline;
    private bool retryPending;

    public long Load()
    {
        var now = timestamp();
        long remaining;
        var canonicalize = false;
        if (MyAPIGateway.Utilities.FileExistsInGlobalStorage(FileName))
        {
            var text = ReadBounded(FileName);
            if (!TryParseRemaining(text, out remaining)) { remaining = MaximumTicks; canonicalize = true; }
        }
        else if (MyAPIGateway.Utilities.FileExistsInGlobalStorage(LegacyFileName))
        {
            var text = ReadBounded(LegacyFileName);
            long expiry;
            if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out expiry) &&
                expiry >= DateTime.MinValue.Ticks && expiry <= DateTime.MaxValue.Ticks)
                remaining = Math.Max(0, Math.Min(MaximumTicks, expiry - utcNow().Ticks));
            else remaining = MaximumTicks;
            canonicalize = true;
        }
        else remaining = 0;

        if (canonicalize) Write(remaining, now);
        else { savedRemaining = remaining; lastWrite = now; }
        knownDeadline = remaining == 0 ? 0 : now + ToMonotonic(remaining);
        return knownDeadline;
    }

    public void Save(long deadline, bool force = false)
    {
        var now = timestamp();
        var remaining = deadline <= now ? 0 : Math.Min(MaximumTicks,
            (long)Math.Ceiling((double)(deadline - now) * TimeSpan.TicksPerSecond / Stopwatch.Frequency));
        var increased = deadline > knownDeadline + Math.Max(1, Stopwatch.Frequency / 1000);
        knownDeadline = Math.Max(knownDeadline, deadline);
        // Activity that increases the saved remainder is durable immediately.
        // Otherwise checkpoint once per second, and always write exact remaining
        // time on a clean suspension. Expiry is durable as soon as observed.
        if (!force && !retryPending && savedRemaining >= 0 && remaining > 0 && !increased &&
            now - lastWrite < Stopwatch.Frequency) return;
        if (!force && remaining > 0)
            remaining = Math.Min(MaximumTicks, ((remaining + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond) * TimeSpan.TicksPerMillisecond);
        if (remaining == savedRemaining) return;
        Write(remaining, now);
    }

    private void Write(long remaining, long now)
    {
        retryPending = true;
        if (string.IsNullOrEmpty(MyFileSystem.UserDataPath))
            throw new InvalidOperationException("Game user storage is not available.");
        var path = Path.Combine(MyFileSystem.UserDataPath, "Storage", FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var owned = false;
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(Prefix + remaining.ToString(CultureInfo.InvariantCulture));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                writeRecord(stream, bytes);
                flushRecord(stream);
            }
            publishRecord(temporary, path);
        }
        finally
        {
            try { if (owned && File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
        savedRemaining = remaining;
        lastWrite = now;
        retryPending = false;
    }

    private static long ToMonotonic(long remaining) =>
        (long)Math.Ceiling((double)remaining * Stopwatch.Frequency / TimeSpan.TicksPerSecond);

    private static bool TryParseRemaining(string text, out long remaining)
    {
        remaining = 0;
        return text != null && text.StartsWith(Prefix, StringComparison.Ordinal) &&
            long.TryParse(text.Substring(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out remaining) &&
            remaining >= 0 && remaining <= MaximumTicks;
    }

    private static string ReadBounded(string file)
    {
        using (var reader = MyAPIGateway.Utilities.ReadFileInGlobalStorage(file))
        {
            var buffer = new char[65];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = reader.Read(buffer, count, buffer.Length - count);
                if (read == 0) break;
                count += read;
            }
            return count <= 64 ? new string(buffer, 0, count) : null;
        }
    }
}

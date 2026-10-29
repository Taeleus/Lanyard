using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Sandbox.ModAPI;

namespace Lanyard;

// A single conservative player-side deadline follows Nexus transfers and game
// restarts. It is not keyed by sector/server, which would allow switching to
// discard the restriction. Runtime expiry still uses the monotonic clock.
internal sealed class CombatCooldownStore
{
    internal const string FileName = "Lanyard.CombatCooldown.v1.txt";
    private static Func<long> timestamp = Stopwatch.GetTimestamp;
    private static Func<DateTime> utcNow = () => DateTime.UtcNow;
    private long savedThrough;
    private long actualDeadline;
    private bool dirty;

    public long Load()
    {
        savedThrough = 0;
        actualDeadline = 0;
        dirty = false;
        if (!MyAPIGateway.Utilities.FileExistsInGlobalStorage(FileName)) return 0;
        string text;
        using (var reader = MyAPIGateway.Utilities.ReadFileInGlobalStorage(FileName)) text = reader.ReadToEnd();
        long ticks;
        double remaining;
        var valid = long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ticks) &&
            ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks;
        if (!valid)
            remaining = CombatLockout.CooldownSeconds; // Incomplete write: do not clear a lockout.
        else
        {
            var nowUtc = utcNow();
            var seconds = (new DateTime(ticks, DateTimeKind.Utc) - nowUtc).TotalSeconds;
            remaining = Math.Max(0, Math.Min(CombatLockout.CooldownSeconds, seconds));
            if (seconds > CombatLockout.CooldownSeconds)
            {
                // Persist the clamped absolute expiry once; otherwise each
                // reconnect could restart a far-future file's full interval.
                using (var writer = MyAPIGateway.Utilities.WriteFileInGlobalStorage(FileName))
                    writer.Write(nowUtc.AddSeconds(remaining).Ticks.ToString(CultureInfo.InvariantCulture));
            }
        }
        var deadline = remaining > 0 ? timestamp() + (long)(remaining * Stopwatch.Frequency) : 0;
        if (valid) savedThrough = actualDeadline = deadline;
        return deadline;
    }

    public void Save(long deadline, bool force = false)
    {
        if (deadline <= 0) return;
        var tolerance = Math.Max(1, Stopwatch.Frequency / 1000);
        if (deadline > actualDeadline + tolerance)
        {
            actualDeadline = deadline;
            dirty = true;
        }
        // Restoring an already covered deadline is not new activity. In
        // particular, forced teardown saves must not prepay it again.
        if (!dirty) return;
        var now = timestamp();
        if (!force && (deadline <= now || deadline <= savedThrough + tolerance)) return;
        // Prepay one second so continuous fire writes at most once a second,
        // without losing the latest fraction of a cooldown after a crash.
        // A clean teardown writes the actual expiry, including an expired
        // deadline, so the crash allowance cannot revive a finished cooldown.
        var persistedDeadline = force ? actualDeadline : actualDeadline + Stopwatch.Frequency;
        var utcDeadline = utcNow().AddSeconds((double)(persistedDeadline - now) / Stopwatch.Frequency);
        using (var writer = MyAPIGateway.Utilities.WriteFileInGlobalStorage(FileName))
            writer.Write(utcDeadline.Ticks.ToString(CultureInfo.InvariantCulture));
        savedThrough = persistedDeadline;
        if (force) dirty = false;
    }
}

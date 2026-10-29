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
    private long savedThrough;

    public long Load()
    {
        if (!MyAPIGateway.Utilities.FileExistsInGlobalStorage(FileName)) return 0;
        string text;
        using (var reader = MyAPIGateway.Utilities.ReadFileInGlobalStorage(FileName)) text = reader.ReadToEnd();
        long ticks;
        double remaining;
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ticks) ||
            ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            remaining = CombatLockout.CooldownSeconds; // Incomplete write: do not clear a lockout.
        else
            remaining = Math.Max(0, Math.Min(CombatLockout.CooldownSeconds,
                (new DateTime(ticks, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds));
        return remaining > 0 ? Stopwatch.GetTimestamp() + (long)(remaining * Stopwatch.Frequency) : 0;
    }

    public void Save(long deadline, bool force = false)
    {
        var now = Stopwatch.GetTimestamp();
        if (deadline <= now || (!force && deadline <= savedThrough)) return;
        // Prepay one second so continuous fire writes at most once a second,
        // without losing the latest fraction of a cooldown after a crash.
        var guardedDeadline = deadline + Stopwatch.Frequency;
        var utcDeadline = DateTime.UtcNow.AddSeconds((double)(guardedDeadline - now) / Stopwatch.Frequency);
        using (var writer = MyAPIGateway.Utilities.WriteFileInGlobalStorage(FileName))
            writer.Write(utcDeadline.Ticks.ToString(CultureInfo.InvariantCulture));
        savedThrough = guardedDeadline;
    }
}

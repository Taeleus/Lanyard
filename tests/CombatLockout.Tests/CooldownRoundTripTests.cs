using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Lanyard;
using Sandbox.ModAPI;

internal static class CooldownRoundTripTests
{
    public static void Run(Action<bool, string> check)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var timestamp = typeof(CombatCooldownStore).GetField("timestamp", flags);
        var utcNow = typeof(CombatCooldownStore).GetField("utcNow", flags);
        var oldTimestamp = timestamp.GetValue(null);
        var oldUtcNow = utcNow.GetValue(null);
        var oldUtilities = MyAPIGateway.Utilities;
        long monotonic = Stopwatch.Frequency * 100;
        var utc = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        timestamp.SetValue(null, (Func<long>)(() => monotonic));
        utcNow.SetValue(null, (Func<DateTime>)(() => utc));
        MyAPIGateway.Utilities = new Utilities();
        try
        {
            check(new CombatCooldownStore().Load() == 0 && MyAPIGateway.Utilities.Writes == 0,
                "remaining persistence: fresh storage has no artificial cooldown");
            new CombatCooldownStore().Save(monotonic + 30 * Stopwatch.Frequency, true);
            check(ReadRemaining() == 30 * TimeSpan.TicksPerSecond,
                "remaining persistence: clean save stores duration rather than wall-clock expiry");
            monotonic = 10 * Stopwatch.Frequency;
            utc = utc.AddYears(1);
            check(new CombatCooldownStore().Load() == monotonic + 30 * Stopwatch.Frequency,
                "remaining persistence: reboot and long offline time preserve all remaining seconds");
            var seconds = 30;
            for (var cycle = 0; cycle < 5; cycle++)
            {
                var store = new CombatCooldownStore();
                var restored = store.Load();
                var writes = MyAPIGateway.Utilities.Writes;
                store.Save(restored);
                store.Save(restored, true);
                check(MyAPIGateway.Utilities.Writes == writes,
                    "remaining persistence: a same-instant hop adds no rounding or guard time");
                monotonic += 3 * Stopwatch.Frequency;
                store.Save(restored, true);
                seconds -= 3;
                check(ReadRemaining() == seconds * TimeSpan.TicksPerSecond,
                    "remaining persistence: repeated clean hops spend only actual online seconds");
                monotonic += 2000 * Stopwatch.Frequency;
                utc = utc.AddDays(10);
                check(new CombatCooldownStore().Load() == monotonic + seconds * Stopwatch.Frequency,
                    "remaining persistence: elapsed disconnected time never spends or resets remainder");
            }
            var expiryStore = new CombatCooldownStore();
            var expiry = expiryStore.Load();
            monotonic += seconds * Stopwatch.Frequency;
            expiryStore.Save(expiry);
            check(ReadRemaining() == 0, "remaining persistence: normal online expiry immediately writes durable zero");
            monotonic = Stopwatch.Frequency;
            utc = utc.AddYears(1);
            check(new CombatCooldownStore().Load() == 0, "remaining persistence: completed timer cannot revive after reboot");

            MyAPIGateway.Utilities.Files.Clear();
            var checkpoint = new CombatCooldownStore();
            var activeDeadline = monotonic + 120 * Stopwatch.Frequency;
            checkpoint.Save(activeDeadline);
            var writesBefore = MyAPIGateway.Utilities.Writes;
            monotonic += Stopwatch.Frequency / 2;
            checkpoint.Save(activeDeadline);
            check(MyAPIGateway.Utilities.Writes == writesBefore,
                "remaining persistence: online countdown checkpoints are throttled");
            monotonic += Stopwatch.Frequency * 3 / 5;
            checkpoint.Save(activeDeadline);
            var saved = ReadRemaining();
            check(saved < 120 * TimeSpan.TicksPerSecond && saved > 118 * TimeSpan.TicksPerSecond,
                "remaining persistence: periodic checkpoint decreases durable remainder");
            monotonic += Stopwatch.Frequency / 4;
            var actualAtCrash = (double)(activeDeadline - monotonic) / Stopwatch.Frequency;
            monotonic = 5 * Stopwatch.Frequency;
            utc = utc.AddMonths(6);
            var crashRemaining = (double)(new CombatCooldownStore().Load() - monotonic) / Stopwatch.Frequency;
            check(crashRemaining >= actualAtCrash && crashRemaining - actualAtCrash <= 1.001,
                "remaining persistence: crash restores conservative checkpoint with at most one second plus rounding excess");

            var shortStore = new CombatCooldownStore();
            var shortDeadline = monotonic + Stopwatch.Frequency / 2;
            shortStore.Save(shortDeadline);
            monotonic += Stopwatch.Frequency;
            shortStore.Save(shortDeadline, true);
            check(new CombatCooldownStore().Load() == 0,
                "remaining persistence: expired clean teardown writes zero even inside checkpoint interval");

            MyAPIGateway.Utilities.Files.Clear();
            MyAPIGateway.Utilities.Files[CombatCooldownStore.LegacyFileName] = utc.AddSeconds(30).Ticks.ToString(CultureInfo.InvariantCulture);
            check(new CombatCooldownStore().Load() == monotonic + 30 * Stopwatch.Frequency && ReadRemaining() == 30 * TimeSpan.TicksPerSecond,
                "remaining persistence: legacy UTC deadline migrates remaining time exactly once");
            utc = utc.AddYears(1);
            MyAPIGateway.Utilities.Files[CombatCooldownStore.LegacyFileName] = "0";
            check(new CombatCooldownStore().Load() == monotonic + 30 * Stopwatch.Frequency,
                "remaining persistence: v2 remainder ignores later legacy changes and offline date");
            MyAPIGateway.Utilities.Files.Remove(CombatCooldownStore.FileName);
            MyAPIGateway.Utilities.Files[CombatCooldownStore.LegacyFileName] = utc.AddSeconds(-1).Ticks.ToString(CultureInfo.InvariantCulture);
            check(new CombatCooldownStore().Load() == 0 && ReadRemaining() == 0,
                "remaining persistence: expired legacy state migrates to a durable zero tombstone");
            MyAPIGateway.Utilities.Files[CombatCooldownStore.LegacyFileName] = utc.AddDays(1).Ticks.ToString(CultureInfo.InvariantCulture);
            check(new CombatCooldownStore().Load() == 0,
                "remaining persistence: legacy far-future state cannot revive a completed v2 timer");
            MyAPIGateway.Utilities.Files.Remove(CombatCooldownStore.FileName);
            var capped = new CombatCooldownStore();
            var capDeadline = capped.Load();
            check(capDeadline == monotonic + 120 * Stopwatch.Frequency,
                "remaining persistence: far-future legacy expiry caps migration to configured duration");
            monotonic += 7 * Stopwatch.Frequency;
            capped.Save(capDeadline, true);
            utc = utc.AddYears(1);
            check(new CombatCooldownStore().Load() == monotonic + 113 * Stopwatch.Frequency,
                "remaining persistence: migrated clamp cannot restart on another offline handoff");

            foreach (var corrupt in new[] { "", "incomplete", "2:-1", "2:" + long.MaxValue, "3:0", "2:1\n", new string('9', 10000) })
            {
                MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName] = corrupt;
                var recovery = new CombatCooldownStore();
                var deadline = recovery.Load();
                check(deadline == monotonic + 120 * Stopwatch.Frequency && ReadRemaining() == 120 * TimeSpan.TicksPerSecond,
                    "remaining persistence: malformed/interrupted/oversized v2 state fails closed and is canonicalized");
                monotonic += Stopwatch.Frequency;
                recovery.Save(deadline, true);
                check(new CombatCooldownStore().Load() == monotonic + 119 * Stopwatch.Frequency,
                    "remaining persistence: repaired corrupt state counts down rather than repeatedly resetting");
            }
            foreach (var legacyCorrupt in new[] { "", "incomplete", "-1", new string('9', 10000) })
            {
                MyAPIGateway.Utilities.Files.Clear();
                MyAPIGateway.Utilities.Files[CombatCooldownStore.LegacyFileName] = legacyCorrupt;
                check(new CombatCooldownStore().Load() == monotonic + 120 * Stopwatch.Frequency && ReadRemaining() == 120 * TimeSpan.TicksPerSecond,
                    "remaining persistence: corrupt legacy migration is bounded, fails closed and writes canonical v2 state");
            }
            MyAPIGateway.Utilities.Files.Clear();
            var retryStore = new CombatCooldownStore();
            MyAPIGateway.Utilities.FailWrites = true;
            var threw = false;
            try { retryStore.Save(monotonic + 20 * Stopwatch.Frequency); }
            catch (System.IO.IOException) { threw = true; }
            MyAPIGateway.Utilities.FailWrites = false;
            retryStore.Save(monotonic + 20 * Stopwatch.Frequency);
            check(threw && ReadRemaining() == 20 * TimeSpan.TicksPerSecond,
                "remaining persistence: failed write never suppresses a successful retry");
            var loadedRetry = new CombatCooldownStore();
            loadedRetry.Load();
            MyAPIGateway.Utilities.FailWrites = true;
            threw = false;
            try { loadedRetry.Save(monotonic + 120 * Stopwatch.Frequency); }
            catch (System.IO.IOException) { threw = true; }
            MyAPIGateway.Utilities.FailWrites = false;
            loadedRetry.Save(monotonic + 120 * Stopwatch.Frequency);
            check(threw && ReadRemaining() == 120 * TimeSpan.TicksPerSecond,
                "remaining persistence: a failed activity extension retries immediately despite checkpoint throttling");
        }
        finally
        {
            timestamp.SetValue(null, oldTimestamp);
            utcNow.SetValue(null, oldUtcNow);
            MyAPIGateway.Utilities = oldUtilities;
        }
    }

    private static long ReadRemaining()
    {
        var text = MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName];
        return long.Parse(text.Substring(2), CultureInfo.InvariantCulture);
    }
}

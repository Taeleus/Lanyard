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
        var utc = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        timestamp.SetValue(null, (Func<long>)(() => monotonic));
        utcNow.SetValue(null, (Func<DateTime>)(() => utc));
        MyAPIGateway.Utilities = new Utilities();
        try
        {
            var originalExpiry = utc.AddSeconds(30).Ticks;
            var runtimeDeadline = monotonic + 30 * Stopwatch.Frequency;
            new CombatCooldownStore().Save(runtimeDeadline, true);
            check(ReadSavedExpiry() == originalExpiry, "cooldown persistence: clean save records the actual expiry without a prepaid second");
            for (var cycle = 0; cycle < 5; cycle++)
            {
                monotonic += 3 * Stopwatch.Frequency;
                utc = utc.AddSeconds(3);
                var store = new CombatCooldownStore();
                var restored = store.Load();
                check(restored == runtimeDeadline, "cooldown persistence: reconnect cycle retains the original monotonic expiry");
                var writes = MyAPIGateway.Utilities.Writes;
                store.Save(restored);
                check(MyAPIGateway.Utilities.Writes == writes,
                    "cooldown persistence: loaded coverage does not add another prepaid second");
                store.Save(restored, true);
                check(ReadSavedExpiry() == originalExpiry, "cooldown persistence: repeated clean unload never extends expiry");
            }

            utc = new DateTime(originalExpiry, DateTimeKind.Utc).AddSeconds(-5);
            monotonic = 10 * Stopwatch.Frequency;
            check(new CombatCooldownStore().Load() == monotonic + 5 * Stopwatch.Frequency,
                "cooldown persistence: process restart counts downtime and reconstructs only remaining time");
            utc = utc.AddSeconds(6);
            check(new CombatCooldownStore().Load() == 0, "cooldown persistence: a deadline that expires offline is already clear on rejoin");

            var prepaidStore = new CombatCooldownStore();
            var shortDeadline = monotonic + Stopwatch.Frequency;
            prepaidStore.Save(shortDeadline);
            monotonic += Stopwatch.Frequency * 3 / 2;
            utc = utc.AddSeconds(1.5);
            prepaidStore.Save(shortDeadline, true);
            check(new CombatCooldownStore().Load() == 0,
                "cooldown persistence: clean unload clears an expired actual deadline even while prepaid coverage remains");

            MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName] = utc.AddHours(1).Ticks.ToString(CultureInfo.InvariantCulture);
            var cappedDeadline = monotonic + 120 * Stopwatch.Frequency;
            var cappedUtc = utc.AddSeconds(120).Ticks;
            check(new CombatCooldownStore().Load() == cappedDeadline,
                "cooldown persistence: valid far-future timestamp is capped to 120 seconds");
            check(ReadSavedExpiry() == cappedUtc, "cooldown persistence: capped timestamp is canonicalized to an absolute stored expiry");
            monotonic += 7 * Stopwatch.Frequency;
            utc = utc.AddSeconds(7);
            check(new CombatCooldownStore().Load() == cappedDeadline,
                "cooldown persistence: reloading a capped timestamp counts down instead of restarting the cap");

            foreach (var corrupt in new[] { "", "incomplete", "-1", long.MaxValue.ToString(CultureInfo.InvariantCulture) })
            {
                MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName] = corrupt;
                check(new CombatCooldownStore().Load() == monotonic + CombatLockout.CooldownSeconds * Stopwatch.Frequency,
                    "cooldown persistence: corrupt or interrupted deadline fails closed");
            }
            MyAPIGateway.Utilities.Files.Clear();
            var retryStore = new CombatCooldownStore();
            MyAPIGateway.Utilities.FailWrites = true;
            var threw = false;
            try { retryStore.Save(monotonic + 20 * Stopwatch.Frequency); }
            catch (System.IO.IOException) { threw = true; }
            MyAPIGateway.Utilities.FailWrites = false;
            retryStore.Save(monotonic + 20 * Stopwatch.Frequency);
            check(threw && MyAPIGateway.Utilities.Files.ContainsKey(CombatCooldownStore.FileName),
                "cooldown persistence: failed writes do not suppress the next successful save");
        }
        finally
        {
            timestamp.SetValue(null, oldTimestamp);
            utcNow.SetValue(null, oldUtcNow);
            MyAPIGateway.Utilities = oldUtilities;
        }
    }

    private static long ReadSavedExpiry() => long.Parse(MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName], CultureInfo.InvariantCulture);
}

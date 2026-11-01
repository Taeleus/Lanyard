using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Lanyard;
using Sandbox.ModAPI;

internal static class CooldownCoveredDurationTests
{
    private sealed class Storage : IDisposable
    {
        private readonly object oldClock = CooldownTestStorage.Field("timestamp").GetValue(null);
        private readonly Utilities oldUtilities = MyAPIGateway.Utilities;
        public long Now = 100 * Stopwatch.Frequency;
        public readonly CombatCooldownStore Store = new CombatCooldownStore();
        public Utilities Api => MyAPIGateway.Utilities;
        public string Path => Api.Files.PathFor(CombatCooldownStore.FileName);
        public long Remaining => long.Parse(Api.Files[CombatCooldownStore.FileName].Substring(2));
        public Storage()
        {
            MyAPIGateway.Utilities = new Utilities();
            CooldownTestStorage.Field("timestamp").SetValue(null, (Func<long>)(() => Now));
        }
        public long Deadline(long ticks) => Now + (long)Math.Ceiling((double)ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond);
        public void Dispose()
        {
            CooldownTestStorage.Field("timestamp").SetValue(null, oldClock);
            MyAPIGateway.Utilities = oldUtilities;
        }
    }

    public static void Probe()
    {
        using (var storage = new Storage())
        {
            DelayedShots(storage);
            Console.WriteLine("300 delayed shots at 5/sec for 60sec, delay 0-16ms: writes=" + storage.Api.WriteAttempts +
                ", durable flushes=" + storage.Api.Flushes + ", publications=" + storage.Api.Publications + ".");
        }
        using (var storage = new Storage())
        {
            storage.Store.Save(storage.Deadline(20 * TimeSpan.TicksPerSecond + 5000), true);
            storage.Now += Stopwatch.Frequency;
            storage.Store.Save(storage.Deadline(20 * TimeSpan.TicksPerSecond + 4000));
            Console.WriteLine("Forced 20.0005s then covered 20.0004s: writes=" + storage.Api.WriteAttempts +
                ", durable flushes=" + storage.Api.Flushes + ", publications=" + storage.Api.Publications + ".");
        }
    }

    public static void Run(Action<bool, string> check)
    {
        using (var storage = new Storage())
        {
            storage.Store.Save(storage.Now + 120 * Stopwatch.Frequency);
            var before = File.ReadAllBytes(storage.Path);
            DelayedShots(storage);
            check(storage.Api.WriteAttempts == 1 && storage.Api.Flushes == 1 && storage.Api.Publications == 1 && storage.Api.Commits == 1,
                "covered duration: 300 shots at 5/sec with 0-16ms delay cause no writes/flushes/publications after first save");
            check(File.ReadAllBytes(storage.Path).SequenceEqual(before), "covered duration: delayed shots leave committed canonical bytes unchanged");
            var restored = new CombatCooldownStore().Load();
            check(restored == storage.Now + 120 * Stopwatch.Frequency,
                "covered duration: fresh store restores unchanged conservative complete record");
        }
        using (var storage = new Storage())
        {
            var exact = 20 * TimeSpan.TicksPerSecond + 5000;
            storage.Store.Save(storage.Deadline(exact), true);
            check(storage.Remaining == exact, "covered fractional: forced save preserves sub-millisecond remaining precision");
            var before = File.ReadAllBytes(storage.Path);
            storage.Now += Stopwatch.Frequency;
            storage.Store.Save(storage.Deadline(20 * TimeSpan.TicksPerSecond + 4000));
            check(storage.Api.WriteAttempts == 1 && File.ReadAllBytes(storage.Path).SequenceEqual(before),
                "covered fractional: raw 20.0004s is covered by exact 20.0005s despite optional millisecond rounding");
            foreach (var gapTicks in new long[] { 1, 9999, 10001, 19999, 20001, 160000 })
            {
                storage.Now += Stopwatch.Frequency;
                var capturedDeadline = storage.Deadline(20 * TimeSpan.TicksPerSecond + 5000);
                storage.Now += (long)Math.Ceiling((double)gapTicks * Stopwatch.Frequency / TimeSpan.TicksPerSecond);
                storage.Store.Save(capturedDeadline);
            }
            check(storage.Api.WriteAttempts == 1 && storage.Api.Flushes == 1 && storage.Api.Publications == 1 && File.ReadAllBytes(storage.Path).SequenceEqual(before),
                "covered fractional: crossing 1ms/2ms rounding and deadline-to-save gaps remains write-free under exact durable coverage");
            storage.Now += Stopwatch.Frequency;
            storage.Store.Save(storage.Deadline(20 * TimeSpan.TicksPerSecond + 6000));
            check(storage.Remaining == 20 * TimeSpan.TicksPerSecond + TimeSpan.TicksPerMillisecond && storage.Api.Commits == 2,
                "uncovered fractional: raw 20.0006s exceeds exact 20.0005s and immediately publishes rounded 20.001s");
            var forcedDeadline = storage.Deadline(7 * TimeSpan.TicksPerSecond + 1570);
            storage.Store.Save(forcedDeadline, true);
            check(storage.Remaining == 7 * TimeSpan.TicksPerSecond + 1570 && storage.Api.Commits == 3,
                "covered forced decrease: publishes exact fractional remainder despite longer committed coverage");
            storage.Store.Save(forcedDeadline, true);
            check(storage.Api.WriteAttempts == 3 && storage.Api.Flushes == 3 && storage.Api.Publications == 3,
                "covered forced equality: identical exact forced save performs no extra I/O");
            storage.Now = forcedDeadline;
            storage.Store.Save(forcedDeadline);
            check(storage.Remaining == 0 && storage.Api.Commits == 4, "covered expiry: positive durable record still publishes one zero");
            for (var frame = 0; frame < 100; frame++) storage.Store.Save(forcedDeadline);
            storage.Store.Save(forcedDeadline, true);
            check(storage.Api.WriteAttempts == 4 && storage.Api.Flushes == 4 && storage.Api.Publications == 4,
                "covered expiry equality: expired updates and identical forced zero never repeat zero writes");
        }
        using (var storage = new Storage())
        {
            storage.Store.Save(storage.Deadline(20 * TimeSpan.TicksPerSecond), true);
            var before = File.ReadAllBytes(storage.Path);
            var pendingDeadline = storage.Deadline(30 * TimeSpan.TicksPerSecond);
            storage.Api.FailWrites = true;
            var threw = false;
            try { storage.Store.Save(pendingDeadline); }
            catch (IOException) { threw = true; }
            storage.Api.FailWrites = false;
            storage.Now += 12 * Stopwatch.Frequency;
            storage.Store.Save(pendingDeadline);
            check(threw && storage.Api.WriteAttempts == 2 && storage.Api.Commits == 1 && File.ReadAllBytes(storage.Path).SequenceEqual(before),
                "covered failed extension: aged 18s resolves against committed 20s without touching canonical bytes");
            var retry = typeof(CombatCooldownStore).GetField("retryPending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            check(!(bool)retry.GetValue(storage.Store), "covered failed extension: satisfied durable coverage clears pending retry");
            storage.Now += Stopwatch.Frequency;
            storage.Store.Save(pendingDeadline);
            check(storage.Api.WriteAttempts == 2 && storage.Api.Flushes == 1 && storage.Api.Publications == 1,
                "covered failed extension: later countdown does not perform a deferred write");
        }
        using (var storage = new Storage())
        {
            check(storage.Store.Load() == 0, "covered idle: fresh storage loads ready");
            for (var frame = 0; frame < 100; frame++) storage.Store.Save(0);
            storage.Store.Save(0, true);
            check(storage.Api.WriteAttempts == 0 && storage.Api.Flushes == 0 && storage.Api.Publications == 0 && !File.Exists(storage.Path),
                "covered idle equality: loaded fresh store and identical forced zero create no artificial record");
        }
    }

    private static void DelayedShots(Storage storage)
    {
        var start = storage.Now;
        storage.Store.Save(start + 120 * Stopwatch.Frequency);
        for (var shot = 1; shot <= 300; shot++)
        {
            var captured = start + shot * Stopwatch.Frequency / 5;
            var deadline = captured + 120 * Stopwatch.Frequency;
            storage.Now = captured + (shot % 17) * Stopwatch.Frequency / 1000;
            storage.Store.Save(deadline);
        }
    }
}

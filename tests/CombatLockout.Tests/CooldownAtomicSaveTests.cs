using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Lanyard;
using Sandbox.ModAPI;
using VRage.FileSystem;

internal static class CooldownAtomicSaveTests
{
    public static void Run(Action<bool, string> check)
    {
        var oldUtilities = MyAPIGateway.Utilities;
        var clock = CooldownTestStorage.Field("timestamp");
        var oldClock = clock.GetValue(null);
        var writeField = CooldownTestStorage.Field("writeRecord");
        var write = (Action<FileStream, byte[]>)writeField.GetValue(null);
        var flushField = CooldownTestStorage.Field("flushRecord");
        var flush = (Action<FileStream>)flushField.GetValue(null);
        var publishField = CooldownTestStorage.Field("publishRecord");
        var publish = (Action<string, string>)publishField.GetValue(null);
        long now = 100 * Stopwatch.Frequency;
        clock.SetValue(null, (Func<long>)(() => now));
        try
        {
            // Every fault operates on production-created files. The narrowly
            // replaced operation fails before publication; no fake atomic
            // writer is substituted for the production atomic path.
            foreach (var fault in new[] { "prefix", "write", "flush", "replace" })
            {
                MyAPIGateway.Utilities = new Utilities();
                var files = MyAPIGateway.Utilities.Files;
                files[CombatCooldownStore.FileName] = "2:" + 100 * TimeSpan.TicksPerSecond;
                files["unrelated.txt"] = "keep";
                files[CombatCooldownStore.FileName + ".stale.tmp"] = "2:1";
                var canonical = files.PathFor(CombatCooldownStore.FileName);
                var before = File.ReadAllBytes(canonical);
                var store = new CombatCooldownStore();
                check(store.Load() == now + 100 * Stopwatch.Frequency, "atomic " + fault + ": starts with committed 100 seconds");
                string temporary = null;
                writeField.SetValue(null, (Action<FileStream, byte[]>)((stream, bytes) =>
                {
                    temporary = stream.Name;
                    check(Path.GetDirectoryName(stream.Name) == Path.GetDirectoryName(canonical) && stream.Name != canonical,
                        "atomic " + fault + ": replacement is a distinct file in the canonical directory");
                    if (fault == "prefix")
                    {
                        stream.Write(bytes, 0, 3);
                        stream.Flush(true);
                        stream.Dispose();
                        check(Encoding.UTF8.GetString(File.ReadAllBytes(stream.Name)) == "2:1",
                            "atomic prefix: actual pending file contains valid numeric prefix before failure");
                        throw new IOException("Injected failure after valid numeric prefix 2:1.");
                    }
                    if (fault == "write") throw new IOException("Injected write failure.");
                    write(stream, bytes);
                }));
                flushField.SetValue(null, (Action<FileStream>)(stream =>
                {
                    if (fault == "flush") throw new IOException("Injected durable flush failure.");
                    flush(stream);
                }));
                publishField.SetValue(null, (Action<string, string>)((temp, path) =>
                {
                    if (fault == "replace") throw new IOException("Injected replace failure.");
                    publish(temp, path);
                }));
                var threw = ThrowsIo(() => store.Save(now + 120 * Stopwatch.Frequency));
                writeField.SetValue(null, write);
                flushField.SetValue(null, flush);
                publishField.SetValue(null, publish);
                check(threw, "atomic " + fault + ": failure is reported for existing frozen/retry handling");
                check(File.ReadAllBytes(canonical).SequenceEqual(before), "atomic " + fault + ": previous canonical bytes survive untouched");
                check(new CombatCooldownStore().Load() == now + 100 * Stopwatch.Frequency,
                    "atomic " + fault + ": fresh store still restores the complete committed 100 seconds");
                check(temporary != null && !File.Exists(temporary), "atomic " + fault + ": only owned failed temporary is cleaned up");
                check(files["unrelated.txt"] == "keep" && files[CombatCooldownStore.FileName + ".stale.tmp"] == "2:1",
                    "atomic " + fault + ": unrelated and stale temporary records are preserved and ignored");
                store.Save(now + 120 * Stopwatch.Frequency);
                check(new CombatCooldownStore().Load() == now + 120 * Stopwatch.Frequency,
                    "atomic " + fault + ": pending activity retries within checkpoint interval");
                var count = MyAPIGateway.Utilities.Writes;
                now += Stopwatch.Frequency / 2;
                store.Save(now + 119 * Stopwatch.Frequency + Stopwatch.Frequency / 2);
                check(MyAPIGateway.Utilities.Writes == count, "atomic " + fault + ": successful retry resumes checkpoint throttling");
            }

            // Initial creation must leave no canonical record when interrupted.
            MyAPIGateway.Utilities = new Utilities();
            var first = new CombatCooldownStore();
            var firstPath = MyAPIGateway.Utilities.Files.PathFor(CombatCooldownStore.FileName);
            writeField.SetValue(null, (Action<FileStream, byte[]>)((stream, bytes) =>
            {
                stream.Write(bytes, 0, 3);
                throw new IOException("First-save prefix failure.");
            }));
            check(ThrowsIo(() => first.Save(now + 120 * Stopwatch.Frequency)) && !File.Exists(firstPath),
                "atomic initial: partial first save never publishes a canonical record");
            check(!Directory.GetFiles(Path.GetDirectoryName(firstPath)).Any(), "atomic initial: failed temporary is removed");
            writeField.SetValue(null, write);
            first.Save(now + 120 * Stopwatch.Frequency);
            check(new CombatCooldownStore().Load() == now + 120 * Stopwatch.Frequency,
                "atomic initial: first save retry creates the canonical record");
            check(File.ReadAllBytes(firstPath).SequenceEqual(Encoding.UTF8.GetBytes("2:" + 120 * TimeSpan.TicksPerSecond)),
                "atomic initial: complete version-two record has UTF8 bytes without BOM or extra text");

            // The default publish delegate also handles real native filesystem
            // errors, independently of the deterministic operation seams above.
            MyAPIGateway.Utilities = new Utilities();
            var realFiles = MyAPIGateway.Utilities.Files;
            realFiles[CombatCooldownStore.FileName] = "2:" + 100 * TimeSpan.TicksPerSecond;
            var realPath = realFiles.PathFor(CombatCooldownStore.FileName);
            var locked = new CombatCooldownStore();
            locked.Load();
            using (var held = File.Open(realPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                check(ThrowsIo(() => locked.Save(now + 120 * Stopwatch.Frequency)) && realFiles[CombatCooldownStore.FileName] == "2:" + 100 * TimeSpan.TicksPerSecond,
                    "atomic default replace: real sharing violation preserves committed file");
            locked.Save(now + 120 * Stopwatch.Frequency);
            check(new CombatCooldownStore().Load() == now + 120 * Stopwatch.Frequency,
                "atomic default replace: successful retry publishes after real sharing violation");
            MyAPIGateway.Utilities = new Utilities();
            var movePath = MyAPIGateway.Utilities.Files.PathFor(CombatCooldownStore.FileName);
            Directory.CreateDirectory(movePath);
            var blockedMove = new CombatCooldownStore();
            check(ThrowsIo(() => blockedMove.Save(now + 120 * Stopwatch.Frequency)) && !File.Exists(movePath) && Directory.Exists(movePath),
                "atomic default move: real destination obstruction cannot publish partial first save");
            Directory.Delete(movePath);
            blockedMove.Save(now + 120 * Stopwatch.Frequency);
            check(new CombatCooldownStore().Load() == now + 120 * Stopwatch.Frequency,
                "atomic default move: retries after real first-creation publication failure");

            MyAPIGateway.Utilities = new Utilities();
            firstPath = MyAPIGateway.Utilities.Files.PathFor(CombatCooldownStore.FileName);
            new CombatCooldownStore().Save(now + 120 * Stopwatch.Frequency);

            // A failed durable zero cannot erase a committed cooldown. A
            // successful zero remains authoritative over a future legacy file.
            var zero = new CombatCooldownStore();
            zero.Load();
            publishField.SetValue(null, (Action<string, string>)((temp, path) => { throw new IOException("Zero replacement failure."); }));
            check(ThrowsIo(() => zero.Save(0, true)) && new CombatCooldownStore().Load() == now + 120 * Stopwatch.Frequency,
                "atomic zero: failed tombstone preserves previous committed cooldown");
            publishField.SetValue(null, publish);
            zero.Save(0, true);
            MyAPIGateway.Utilities.Files[CombatCooldownStore.LegacyFileName] = DateTime.UtcNow.AddDays(1).Ticks.ToString(CultureInfo.InvariantCulture);
            check(File.ReadAllText(firstPath) == "2:0" && new CombatCooldownStore().Load() == 0,
                "atomic zero: retried tombstone is durable and blocks legacy revival");

            // Failure during first-creation publication retains the legacy
            // record, then normal Load retries the unchanged migration policy.
            MyAPIGateway.Utilities = new Utilities();
            var utcField = CooldownTestStorage.Field("utcNow");
            var oldUtc = utcField.GetValue(null);
            var utc = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            utcField.SetValue(null, (Func<DateTime>)(() => utc));
            try
            {
                MyAPIGateway.Utilities.Files[CombatCooldownStore.LegacyFileName] = utc.AddSeconds(30).Ticks.ToString(CultureInfo.InvariantCulture);
                publishField.SetValue(null, (Action<string, string>)((temp, path) => { throw new IOException("Initial move failure."); }));
                check(ThrowsIo(() => new CombatCooldownStore().Load()) && !MyAPIGateway.Utilities.Files.ContainsKey(CombatCooldownStore.FileName),
                    "atomic migration: failed initial move publishes no partial version-two record");
                check(MyAPIGateway.Utilities.Files[CombatCooldownStore.LegacyFileName] == utc.AddSeconds(30).Ticks.ToString(CultureInfo.InvariantCulture),
                    "atomic migration: failed publication preserves complete legacy source");
                publishField.SetValue(null, publish);
                check(new CombatCooldownStore().Load() == now + 30 * Stopwatch.Frequency && MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName] == "2:" + 30 * TimeSpan.TicksPerSecond,
                    "atomic migration: retry publishes exact remaining legacy duration");
            }
            finally { utcField.SetValue(null, oldUtc); }

            // Reject missing game storage before any relative Storage path can
            // be created, even if the process happens to have a writable cwd.
            var cwd = Environment.CurrentDirectory;
            var isolated = CooldownTestStorage.CreateRoot();
            try
            {
                Environment.CurrentDirectory = isolated;
                foreach (var missing in new[] { null, "" })
                {
                    MyFileSystem.UserDataPath = missing;
                    var rejected = false;
                    try { new CombatCooldownStore().Save(now + Stopwatch.Frequency); }
                    catch (InvalidOperationException) { rejected = true; }
                    check(rejected && !Directory.Exists(Path.Combine(isolated, "Storage")),
                        "atomic unavailable root: reject absent user-data path without relative storage fallback");
                }
            }
            finally { Environment.CurrentDirectory = cwd; }
        }
        finally
        {
            writeField.SetValue(null, write);
            flushField.SetValue(null, flush);
            publishField.SetValue(null, publish);
            clock.SetValue(null, oldClock);
            MyAPIGateway.Utilities = oldUtilities;
        }
    }

    private static bool ThrowsIo(Action action)
    {
        try { action(); return false; }
        catch (IOException) { return true; }
    }
}

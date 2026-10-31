using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Utils;

internal static class CooldownWriteRecoveryTests
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(CombatLockout).GetField(name, PrivateStatic);
    private static CombatCooldown Timer => (CombatCooldown)Field("cooldown").GetValue(null);
    private static bool Failed => (bool)Field("failed").GetValue(null);
    private static long ReadRemaining() => long.Parse(MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName].Substring(2));

    public static void Run(Action<bool, string> check)
    {
        StoreBoundaries(check);
        var clock = Field("persistenceTimestamp");
        var original = clock.GetValue(null);
        long retryNow = Stopwatch.GetTimestamp();
        clock.SetValue(null, (Func<long>)(() => retryNow));
        try
        {
            var grid = World();
            var native = new Gun();
            var core = new CoreGun();
            var functional = new FunctionalBlock();
            grid.Add(native); grid.Add(core); grid.Add(functional);
            var provider = new FakeWeaponCore();
            provider.Add(core, 0); provider.RespondOnRequest();
            CombatLockout.Start();
            check(CombatLockout.CanConnect(grid), "write recovery: loaded quiet monitor starts ready");
            ((CombatCooldownStore)Field("store").GetValue(null)).Save(Stopwatch.GetTimestamp() + 20 * Stopwatch.Frequency, true);
            var canonical = MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName];
            var attempts = MyAPIGateway.Utilities.WriteAttempts;
            var logs = MyLog.Default.Errors;
            MyAPIGateway.Utilities.FailWrites = true;
            grid.Damage(1);
            var firstDeadline = Timer.Deadline;
            check(!Failed && CombatLockout.IsBlocked && CombatLockout.RemainingSeconds == -1 && CombatLockout.BlockReason.Contains("storage"),
                "write recovery: transient save blocks availability without permanently failing monitoring");
            check(MyAPIGateway.Utilities.WriteAttempts == attempts + 1 && MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName] == canonical,
                "write recovery: failed activity save attempts once and preserves committed canonical bytes");
            var start = retryNow;
            var polls = provider.ShotCalls;
            for (var frame = 1; frame < 60; frame++)
            {
                retryNow = start + frame * Stopwatch.Frequency / 60;
                grid.Damage(1);
                provider.State(core, 0).Shots++;
                native.IsShooting = true;
                CombatLockout.Update();
            }
            check(provider.ShotCalls > polls && provider.ObserverCount == 1 && grid.Subscribers == 7 && !Failed && Timer.Deadline > firstDeadline,
                "write recovery: native/grid/WC observations continue and retain newer combat throughout disk outage");
            CoreSystems.Session.I.ActiveEwarCubes[functional.EntityId] = new object();
            CombatLockout.Update();
            check(CombatLockout.BlockReason.Contains("storage") && Timer.Deadline >= firstDeadline && !Failed,
                "write recovery: optional effect polling remains live while storage is unavailable");
            check(MyAPIGateway.Utilities.WriteAttempts == attempts + 1 && MyLog.Default.Errors == logs + 1,
                "write recovery: fresh activity every frame cannot bypass retry throttle or spam error logs");
            retryNow = start + Stopwatch.Frequency;
            CombatLockout.Update();
            check(MyAPIGateway.Utilities.WriteAttempts == attempts + 2 && MyLog.Default.Errors == logs + 1 && !Failed,
                "write recovery: continuing outage makes one due retry per second and logs only once");
            MyAPIGateway.Utilities.FailWrites = false;
            retryNow += Stopwatch.Frequency / 2;
            CombatLockout.Update();
            check(CombatLockout.RemainingSeconds == -1 && !CombatLockout.CanConnect(grid) && MyAPIGateway.Utilities.WriteAttempts == attempts + 2,
                "write recovery: apparent disk recovery cannot bypass a pending durable save before due retry");
            retryNow = start + 2 * Stopwatch.Frequency;
            CombatLockout.Update();
            check(!Failed && CombatLockout.RemainingSeconds >= 119 && ReadRemaining() >= 119 * TimeSpan.TicksPerSecond,
                "write recovery: due successful publication preserves latest activity and restores normal cooldown state");
            check(CombatLockout.IsBlocked && !CombatLockout.CanConnect(grid),
                "write recovery: durable recovery does not waive the actual live combat restriction");
            CoreSystems.Session.I.ActiveEwarCubes.Clear();
            native.IsShooting = false;
            provider.State(core, 0).Shots++;
            polls = provider.ShotCalls;
            CombatLockout.Update();
            check(provider.ShotCalls > polls && !Failed && CombatLockout.RemainingSeconds >= 119,
                "write recovery: monitoring still records subsequent combat after recovery");
            MyAPIGateway.GridGroups.Throw = true;
            CombatLockout.Update();
            MyAPIGateway.GridGroups.Throw = false;
            CombatLockout.Update();
            check(Failed && CombatLockout.RemainingSeconds == -1,
                "write recovery: unrelated observer failure remains fatal rather than being treated as transient storage");

            grid = World();
            CombatLockout.Start(); CombatLockout.CanConnect(grid); grid.Damage(1);
            ExpireLiveHistory();
            MyAPIGateway.Utilities.FailWrites = true;
            attempts = MyAPIGateway.Utilities.WriteAttempts;
            CombatLockout.Update();
            check(!Failed && CombatLockout.IsBlocked && CombatLockout.RemainingSeconds == -1 && !CombatLockout.CanConnect(grid),
                "write recovery expiry: pending failed zero blocks even after all live combat timers expire");
            MyAPIGateway.Utilities.FailWrites = false;
            retryNow += Stopwatch.Frequency;
            CombatLockout.Update();
            check(ReadRemaining() == 0 && CombatLockout.CanConnect(grid) && MyAPIGateway.Utilities.WriteAttempts == attempts + 2,
                "write recovery expiry: durable zero retry permits use only after successful publication");
            var commits = MyAPIGateway.Utilities.Commits;
            for (var frame = 0; frame < 100; frame++) CombatLockout.Update();
            check(MyAPIGateway.Utilities.Commits == commits, "write recovery expiry: quiet expired updates never repeat zero publication");

            grid = World();
            CombatLockout.Start(); CombatLockout.CanConnect(grid);
            ((CombatCooldownStore)Field("store").GetValue(null)).Save(Stopwatch.GetTimestamp() + 20 * Stopwatch.Frequency, true);
            MyAPIGateway.Utilities.FailWrites = true;
            grid.Damage(1);
            attempts = MyAPIGateway.Utilities.WriteAttempts;
            CombatLockout.Clear();
            check(MyAPIGateway.Utilities.WriteAttempts == attempts && (long)Field("frozenRemaining").GetValue(null) >= 119 * Stopwatch.Frequency,
                "write recovery handoff: throttled clean suspension freezes latest live duration without another disk attempt");
            MySession.Static = new MySession();
            MyEntities.Entities.Clear();
            grid = new MyCubeGrid(); MyEntities.Add(grid);
            CombatLockout.Start();
            check(!Failed && Timer.Deadline >= Stopwatch.GetTimestamp() + 119 * Stopwatch.Frequency && CombatLockout.RemainingSeconds == -1 &&
                MyAPIGateway.Utilities.WriteAttempts == attempts && !CombatLockout.CanConnect(grid),
                "write recovery handoff: rebind retains frozen combat and pending-save gate without retry burst or stale shorter reload");
            MyAPIGateway.Utilities.FailWrites = false;
            retryNow += Stopwatch.Frequency;
            CombatLockout.Update();
            check(!Failed && CombatLockout.RemainingSeconds >= 119 && ReadRemaining() >= 119 * TimeSpan.TicksPerSecond && MyAPIGateway.Utilities.WriteAttempts == attempts + 1,
                "write recovery handoff: due retry saves frozen duration and recovers on the new observer session");
        }
        finally
        {
            MyAPIGateway.Utilities.FailWrites = false;
            retryNow += Stopwatch.Frequency;
            CombatLockout.Clear();
            clock.SetValue(null, original);
        }
    }

    private static void StoreBoundaries(Action<bool, string> check)
    {
        var clock = CooldownTestStorage.Field("timestamp");
        var original = clock.GetValue(null);
        var oldUtilities = MyAPIGateway.Utilities;
        long now = 100 * Stopwatch.Frequency;
        clock.SetValue(null, (Func<long>)(() => now));
        try
        {
            MyAPIGateway.Utilities = new Utilities();
            var store = new CombatCooldownStore();
            var deadline = now + 120 * Stopwatch.Frequency;
            store.Save(deadline);
            for (var frame = 1; frame < 7200; frame++)
            {
                now = 100 * Stopwatch.Frequency + frame * Stopwatch.Frequency / 60;
                store.Save(deadline);
            }
            check(MyAPIGateway.Utilities.WriteAttempts == 1 && MyAPIGateway.Utilities.Flushes == 1 && MyAPIGateway.Utilities.Publications == 1,
                "save boundaries: all 7199 active countdown frames cause zero writes/flushes/publications after first activity");
            now = deadline;
            store.Save(deadline);
            check(ReadRemaining() == 0 && MyAPIGateway.Utilities.Commits == 2 && MyAPIGateway.Utilities.Flushes == 2,
                "save boundaries: exact 120-second expiry writes one durable zero");
            for (var frame = 0; frame < 120; frame++) store.Save(deadline);
            check(MyAPIGateway.Utilities.WriteAttempts == 2, "save boundaries: repeated expired updates never write again");
            now += 10 * Stopwatch.Frequency;
            deadline = now + 120 * Stopwatch.Frequency;
            store.Save(deadline);
            check(ReadRemaining() == 120 * TimeSpan.TicksPerSecond && MyAPIGateway.Utilities.Commits == 3,
                "save boundaries: new activity after expiry is immediately durable");
            now += 10 * Stopwatch.Frequency;
            store.Save(deadline, true);
            check(ReadRemaining() == 110 * TimeSpan.TicksPerSecond && MyAPIGateway.Utilities.Commits == 4,
                "save boundaries: clean suspension publishes exact online remaining duration");
            store.Save(deadline, true);
            check(MyAPIGateway.Utilities.Commits == 4, "save boundaries: same-instant clean suspension does not duplicate identical bytes");
            deadline = now + 120 * Stopwatch.Frequency;
            store.Save(deadline);
            check(ReadRemaining() == 120 * TimeSpan.TicksPerSecond && MyAPIGateway.Utilities.Commits == 5,
                "save boundaries: real extension beyond shorter committed remainder publishes immediately");
            now += Stopwatch.Frequency;
            deadline = now + 120 * Stopwatch.Frequency;
            store.Save(deadline);
            check(MyAPIGateway.Utilities.Commits == 5,
                "save boundaries: activity already covered by committed full duration needs no identical disk rewrite");

            MyAPIGateway.Utilities = new Utilities();
            store = new CombatCooldownStore();
            deadline = now + 20 * Stopwatch.Frequency;
            store.Save(deadline);
            MyAPIGateway.Utilities.FailWrites = true;
            var threw = false;
            try { store.Save(now + 30 * Stopwatch.Frequency); }
            catch (System.IO.IOException) { threw = true; }
            var attempts = MyAPIGateway.Utilities.WriteAttempts;
            now += 10 * Stopwatch.Frequency;
            MyAPIGateway.Utilities.FailWrites = false;
            store.Save(now + 20 * Stopwatch.Frequency);
            check(threw && ReadRemaining() == 20 * TimeSpan.TicksPerSecond && MyAPIGateway.Utilities.WriteAttempts == attempts,
                "save equality retry: verified canonical duration already covers aged pending extension without another write");
            now += Stopwatch.Frequency;
            store.Save(now + 19 * Stopwatch.Frequency);
            check(MyAPIGateway.Utilities.WriteAttempts == attempts,
                "save equality retry: satisfying pending save from canonical bytes clears retry and avoids later countdown writes");
        }
        finally { clock.SetValue(null, original); MyAPIGateway.Utilities = oldUtilities; }
    }

    private static MyCubeGrid World()
    {
        MyAPIGateway.Utilities.FailWrites = false;
        CombatLockout.Clear();
        Timer.Clear();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Multiplayer = new Multiplayer();
        MyAPIGateway.GridGroups = new GridGroups();
        MyAPIGateway.Session = new BoundsSession();
        MySession.Static = new MySession();
        MyEntities.Entities.Clear();
        CoreSystems.Session.I = new CoreSystems.Session();
        VanillaPlusFramework.VanillaPlusFramework.I = null;
        VanillaPlusFramework.BeamLogic.Beams.I = null;
        var grid = new MyCubeGrid(); MyEntities.Add(grid);
        return grid;
    }

    private static void ExpireLiveHistory()
    {
        Timer.Clear();
        ((IDictionary)Field("recentGridHistory").GetValue(null)).Clear();
        foreach (var activity in ((IDictionary)Field("grids").GetValue(null)).Values)
            activity.GetType().GetProperty("Until").SetValue(activity, 0L);
    }
}

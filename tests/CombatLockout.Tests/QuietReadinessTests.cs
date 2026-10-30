using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

internal static class QuietReadinessTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static CombatCooldown Timer => (CombatCooldown)typeof(CombatLockout).GetField("cooldown", PrivateStatic).GetValue(null);

    private static MyCubeGrid World()
    {
        CombatLockout.Clear();
        Timer.Clear();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Multiplayer = new Multiplayer();
        MyAPIGateway.GridGroups = new GridGroups();
        MyAPIGateway.Session = new BoundsSession();
        MySession.Static = new MySession();
        CoreSystems.Session.I = new CoreSystems.Session();
        VanillaPlusFramework.VanillaPlusFramework.I = null;
        VanillaPlusFramework.BeamLogic.Beams.I = null;
        MyEntities.Entities.Clear();
        var grid = new MyCubeGrid();
        MyEntities.Add(grid);
        return grid;
    }

    public static void Run(Action<bool, string> check)
    {
        var grid = World();
        var gun = new Gun();
        grid.Add(gun);
        CombatLockout.Start();
        check(CombatLockout.CanConnect(grid) && CombatLockout.RemainingSeconds == 0 && Timer.Deadline == 0,
            "quiet readiness: first join and default native weapon timestamp have no cooldown");
        check(!MyAPIGateway.Utilities.Files.ContainsKey(CombatCooldownStore.FileName),
            "quiet readiness: observing an idle world writes no active timer");
        var docked = new MyCubeGrid();
        MyEntities.Add(docked);
        MyAPIGateway.GridGroups.LogicalGroups[grid] = new List<IMyCubeGrid> { grid, docked };
        grid.ConnectionsChanged();
        check(!CombatLockout.IsBlocked && Timer.Deadline == 0,
            "quiet readiness: a newly observed quiet docked grid adds no timer");
        var fragment = new MyCubeGrid();
        MyEntities.Add(fragment);
        grid.Split(fragment);
        CombatLockout.FollowGrid(fragment);
        check(!CombatLockout.IsBlocked, "quiet readiness: a real quiet split event is immediately ready");
        fragment.Merge(docked);
        check(!CombatLockout.IsBlocked, "quiet readiness: a quiet merge event is immediately ready");

        grid = World();
        CombatLockout.Start();
        grid.Damage(1);
        fragment = new MyCubeGrid();
        MyEntities.Add(fragment);
        grid.Split(fragment);
        check(!CombatLockout.CanConnect(fragment) && CombatLockout.RemainingSeconds >= 119,
            "quiet readiness: split inherits actual parent combat history without a synthetic timer");
        grid = World();
        var removed = new MyCubeGrid();
        MyEntities.Add(removed);
        CombatLockout.Start();
        removed.Damage(1);
        MyEntities.Remove(removed);
        removed.Closed = true;
        grid.Merge(removed);
        check(!CombatLockout.CanConnect(grid) && CombatLockout.BlockReason.Contains("damage"),
            "quiet readiness: merge imports real damage after native source disposal");

        grid = World();
        gun = new Gun();
        gun.GunBase.LastShootTime = DateTime.UtcNow.AddSeconds(-50);
        grid.Add(gun);
        CombatLockout.Start();
        check(!CombatLockout.CanConnect(grid) && CombatLockout.RemainingSeconds >= 69 && CombatLockout.RemainingSeconds <= 70,
            "quiet readiness: trustworthy recent native shot history retains only its actual remaining interval");
        grid = World();
        gun = new Gun();
        gun.GunBase.LastShootTime = DateTime.UtcNow.AddSeconds(-121);
        grid.Add(gun);
        CombatLockout.Start();
        check(CombatLockout.CanConnect(grid), "quiet readiness: an old native shot is not fresh combat");

        grid = World();
        var coreGun = new CoreGun();
        grid.Add(coreGun);
        var provider = new FakeWeaponCore();
        provider.Add(coreGun, 0);
        provider.State(coreGun, 0).Shots = 1000;
        provider.RespondOnRequest();
        CombatLockout.Start();
        check(CombatLockout.CanConnect(grid) && Timer.Deadline == 0,
            "quiet readiness: positive WC lifetime baseline is not evidence of recent firing");
        provider.State(coreGun, 0).Shots++;
        CombatLockout.Update();
        check(CombatLockout.IsBlocked && CombatLockout.RemainingSeconds >= 119,
            "quiet readiness: new WC shot after baseline still starts the full real combat timer");

        FinalPollBeforeNativeMerge(check);
        PersistenceFailureRetainsRemaining(check);
        CombatLockout.Clear();
        Timer.Clear();
        MyAPIGateway.Utilities.Files.Clear();
    }

    private static void FinalPollBeforeNativeMerge(Action<bool, string> check)
    {
        foreach (var kind in new[] { "quiet", "WC counter", "vanilla shot" })
            foreach (var selectedSource in new[] { false, true })
            {
                var survivor = World();
                var source = new MyCubeGrid();
                var coreGun = new CoreGun();
                var nativeGun = new Gun();
                source.Add(coreGun);
                source.Add(nativeGun);
                MyEntities.Add(source);
                var provider = new FakeWeaponCore();
                provider.Add(coreGun, 0);
                provider.RespondOnRequest();
                CombatLockout.Start();
                check(CombatLockout.CanConnect(selectedSource ? source : survivor),
                    "final merge poll: established quiet source starts available");
                if (kind == "WC counter") provider.State(coreGun, 0).Shots++;
                if (kind == "vanilla shot") nativeGun.GunBase.LastShootTime = DateTime.UtcNow;
                // Native ordering: mark closing, remove entity, then transfer
                // live blocks and raise merge. No intervening plugin Update.
                source.MarkedForClose = true;
                MyEntities.Remove(source);
                check(CombatLockout.IsBlocked == (selectedSource && kind != "quiet"),
                    "final merge poll: " + kind + " imports globally only for the previously selected construct");
                check(provider.ObserverCount == 0,
                    "final merge poll: removed source detaches its established WC observer");
                if (selectedSource && kind != "quiet") check(!CombatLockout.CanConnect(survivor) && CombatLockout.RemainingSeconds >= 119,
                    "direct source removal: final " + kind + " blocks relinking to a quiet grid before any merge event");
                survivor.Add(coreGun);
                survivor.Add(nativeGun);
                source.Closed = true;
                survivor.Merge(source);
                CombatLockout.FollowGrid(survivor);
                CombatLockout.Update();
                check(CombatLockout.IsBlocked == (kind != "quiet"),
                    "final merge poll: " + kind + " preserves only genuine activity through survivor baselines");
                if (kind != "quiet") check(CombatLockout.RemainingSeconds >= 119,
                    "final merge poll: genuine pre-removal shot keeps the full active restriction");
            }
    }

    private static void PersistenceFailureRetainsRemaining(Action<bool, string> check)
    {
        var grid = World();
        CombatLockout.Start();
        CombatLockout.CanConnect(grid);
        var store = (CombatCooldownStore)typeof(CombatLockout).GetField("store", PrivateStatic).GetValue(null);
        store.Save(Stopwatch.GetTimestamp() + 20 * Stopwatch.Frequency, true);
        MyAPIGateway.Utilities.FailWrites = true;
        grid.Damage(1);
        check(CombatLockout.IsBlocked && CombatLockout.RemainingSeconds == -1,
            "remaining persistence: active save failure fails monitoring closed");
        CombatLockout.Clear();
        MyEntities.Entities.Clear();
        MySession.Static = new MySession();
        var next = new MyCubeGrid();
        MyEntities.Add(next);
        MyAPIGateway.Utilities.FailWrites = false;
        CombatLockout.Start();
        check(CombatLockout.IsBlocked && CombatLockout.RemainingSeconds >= 119,
            "remaining persistence: failed teardown cannot load a stale shorter disk remainder in process");
        var value = MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName];
        check(long.Parse(value.Substring(2)) >= 119 * TimeSpan.TicksPerSecond,
            "remaining persistence: recovered storage receives the longer frozen fallback");

        CombatLockout.Clear();
        MyAPIGateway.Utilities.FailReads = true;
        CombatLockout.Start();
        check(CombatLockout.IsBlocked && CombatLockout.RemainingSeconds == -1,
            "remaining persistence: unreadable storage never silently starts a ready session");
        var before = MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName];
        CombatLockout.Clear();
        check(MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName] == before,
            "remaining persistence: failed load teardown cannot overwrite unknown active state with zero");
        MyAPIGateway.Utilities.FailReads = false;
    }
}

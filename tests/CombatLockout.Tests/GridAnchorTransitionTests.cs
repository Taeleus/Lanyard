using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;

internal static class GridAnchorTransitionTests
{
    private const BindingFlags StaticFields = BindingFlags.NonPublic | BindingFlags.Static;

    private static CombatCooldown Timer => (CombatCooldown)typeof(CombatLockout).GetField("cooldown", StaticFields).GetValue(null);

    private static MyCubeGrid World()
    {
        CombatLockout.Clear();
        Timer.Clear();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Multiplayer = new Multiplayer();
        MyAPIGateway.GridGroups = new GridGroups();
        MySession.Static = new MySession();
        CoreSystems.Session.I = new CoreSystems.Session();
        VanillaPlusFramework.VanillaPlusFramework.I = null;
        VanillaPlusFramework.BeamLogic.Beams.I = null;
        MyEntities.Entities.Clear();
        var grid = new MyCubeGrid();
        MyEntities.Add(grid);
        return grid;
    }

    private static void Quiet()
    {
        Timer.Clear();
        var activities = (IDictionary)typeof(CombatLockout).GetField("grids", StaticFields).GetValue(null);
        foreach (var activity in activities.Values)
        {
            activity.GetType().GetProperty("Until").SetValue(activity, 0L);
            activity.GetType().GetProperty("ObservationUntil").SetValue(activity, 0L);
        }
        MyAPIGateway.Utilities.Files.Clear();
    }

    private static void Move(MyCubeBlock selected, MyCubeGrid destination)
    {
        var original = selected.CubeGrid;
        original.IsBlockTrasferInProgress = true;
        original.Remove(selected);
        destination.Add(selected);
        original.IsBlockTrasferInProgress = false;
    }

    public static void Run(Action<bool, string> check)
    {
        var original = World();
        var selected = new MyCubeBlock(); original.Add(selected);
        CombatLockout.Start(); Quiet();
        check(CombatLockout.CanConnect(selected.CubeGrid), "anchor: selected container starts on an observed quiet grid");
        var fragment = new MyCubeGrid(); MyEntities.Add(fragment);
        Move(selected, fragment);
        CombatLockout.FollowGrid(selected.CubeGrid);
        check(CombatLockout.IsBlocked, "anchor: live selected container imports split fragment observation without reconnecting");
        check(Timer.Deadline > Stopwatch.GetTimestamp() + (CombatLockout.CooldownSeconds - 1) * Stopwatch.Frequency,
            "anchor: split fragment requires the full initial quiet interval");

        Quiet(); CombatLockout.Update();
        check(!CombatLockout.IsBlocked, "anchor: split fragment becomes available after quiet observation");
        fragment.Damage(10);
        check(CombatLockout.IsBlocked, "anchor: subsequent damage follows the same selected container onto its fragment");
        Quiet(); CombatLockout.Update();
        original.Damage(10);
        check(!CombatLockout.IsBlocked, "anchor: damage on detached original grid no longer controls the selected container");
        var gun = new Gun(); fragment.Add(gun);
        gun.GunBase.LastShootTime = DateTime.UtcNow;
        CombatLockout.Update();
        check(CombatLockout.IsBlocked, "anchor: subsequent weapon fire on selected fragment is observed");

        original = World(); selected = new MyCubeBlock(); original.Add(selected);
        var survivor = new MyCubeGrid(); MyEntities.Add(survivor);
        CombatLockout.Start(); Quiet(); CombatLockout.CanConnect(selected.CubeGrid);
        original.MarkedForClose = true;
        MyEntities.Remove(original);
        Move(selected, survivor);
        original.Closed = true;
        survivor.Merge(original);
        CombatLockout.FollowGrid(selected.CubeGrid);
        check(CombatLockout.IsBlocked, "anchor: merge survivor observation replaces the removed closed anchor");
        Quiet(); CombatLockout.Update();
        survivor.Damage(10);
        check(CombatLockout.IsBlocked, "anchor: survivor damage remains observed after the old grid closes");

        original = World(); survivor = new MyCubeGrid(); MyEntities.Add(survivor);
        CombatLockout.Start(); Quiet(); CombatLockout.CanConnect(original);
        original.Damage(10);
        var previousDeadline = Timer.Deadline;
        CombatLockout.FollowGrid(survivor);
        check(CombatLockout.IsBlocked && Timer.Deadline >= previousDeadline,
            "anchor: changing the selected grid preserves the player-wide existing cooldown");

        original = World(); survivor = new MyCubeGrid(); MyEntities.Add(survivor);
        gun = new Gun(); original.Add(gun);
        CombatLockout.Start(); Quiet(); CombatLockout.CanConnect(original);
        gun.GunBase.LastShootTime = DateTime.UtcNow;
        CombatLockout.FollowGrid(survivor);
        check(CombatLockout.IsBlocked, "anchor: final-poll previous construct before replacing its membership");
        Quiet(); CombatLockout.Update();
        var before = Timer.Deadline;
        CombatLockout.FollowGrid(survivor);
        check(Timer.Deadline == before, "anchor: unchanged grid does not restart observation or poll the world");
        CombatLockout.FollowGrid(new MyCubeGrid());
        check(CombatLockout.IsBlocked, "anchor: moving onto a grid whose observer is not registered fails closed");
        CombatLockout.Clear();
    }
}

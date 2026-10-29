using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;

internal static class EnemyOverlapCooldownTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static CombatCooldown Timer => (CombatCooldown)typeof(CombatLockout).GetField("cooldown", PrivateStatic).GetValue(null);
    private static void SetDeadline(long deadline) => typeof(CombatCooldown)
        .GetField("until", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(Timer, deadline);

    private static MyCubeGrid Prepare(out MyCubeGrid other)
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
        other = new MyCubeGrid();
        MyEntities.Add(grid);
        MyEntities.Add(other);
        CombatLockout.Start();
        QuietGridHistory();
        Timer.Clear();
        MyAPIGateway.Utilities.Files.Clear();
        if (!CombatLockout.CanConnect(grid)) throw new InvalidOperationException("Overlap fixture must start available.");
        return grid;
    }

    private static void QuietGridHistory()
    {
        var activities = (IDictionary)typeof(CombatLockout).GetField("grids", PrivateStatic).GetValue(null);
        foreach (var activity in activities.Values)
        {
            activity.GetType().GetProperty("Until").SetValue(activity, 0L);
            activity.GetType().GetProperty("ObservationUntil").SetValue(activity, 0L);
        }
    }

    public static void Run(Action<bool, string> check)
    {
        try
        {
            var grid = Prepare(out var other);
            var before = Stopwatch.GetTimestamp();
            CombatLockout.ObserveEnemyOverlap();
            var after = Stopwatch.GetTimestamp();
            var deadline = Timer.Deadline;
            check(deadline >= before + 120 * Stopwatch.Frequency && deadline <= after + 120 * Stopwatch.Frequency,
                "overlap cooldown: actual production observation uses the existing 120-second duration");
            check(CombatLockout.IsBlocked && CombatLockout.BlockReason.IndexOf("overlap", StringComparison.OrdinalIgnoreCase) >= 0,
                "overlap cooldown: actual hostile observation blocks and records its cause");
            check(MyAPIGateway.Utilities.Files.ContainsKey(CombatCooldownStore.FileName),
                "overlap cooldown: deadline is persisted through the existing cooldown store");
            check(Timer.IsBlocked(deadline - 1) && !Timer.IsBlocked(deadline),
                "overlap cooldown: production timer releases exactly at its deadline");
            SetDeadline(Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency);
            var shortened = Timer.Deadline;
            before = Stopwatch.GetTimestamp();
            CombatLockout.ObserveEnemyOverlap();
            check(Timer.Deadline > shortened && Timer.Deadline >= before + 120 * Stopwatch.Frequency,
                "overlap cooldown: continued presence refreshes a partially elapsed timer to the full quiet interval");
            deadline = Timer.Deadline;
            CombatLockout.FollowGrid(other);
            check(CombatLockout.IsBlocked && Timer.Deadline >= deadline,
                "overlap cooldown: changing selected constructs cannot clear hostile activity");
            CombatLockout.Clear();
            check(Timer.Deadline >= deadline, "overlap cooldown: session teardown does not erase the deadline");
            Timer.Clear();
            CombatLockout.Start();
            check(CombatLockout.IsBlocked && CombatLockout.BlockReason.IndexOf("Restored cooldown", StringComparison.OrdinalIgnoreCase) >= 0,
                "overlap cooldown: restarting the observer restores persisted hostile activity");
            QuietGridHistory();
            SetDeadline(Stopwatch.GetTimestamp());
            CombatLockout.Update();
            check(!CombatLockout.IsBlocked && CombatLockout.CanConnect(grid),
                "overlap cooldown: expired quiet deadline becomes usable without resetting the monitor");
            before = Stopwatch.GetTimestamp();
            CombatLockout.ObserveEnemyOverlap();
            check(CombatLockout.IsBlocked && Timer.Deadline >= before + 120 * Stopwatch.Frequency,
                "overlap cooldown: later hostile presence starts a new full quiet interval");
        }
        finally
        {
            CombatLockout.Clear();
            Timer.Clear();
            MyAPIGateway.Utilities.Files.Clear();
        }
    }
}

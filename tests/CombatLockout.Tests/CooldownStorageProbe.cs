using System;
using System.Diagnostics;
using System.Reflection;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;

// Run before and after the production correction. This reports real operation
// counts and recovery state; focused assertions live in the regression tests.
internal static class CooldownStorageProbe
{
    public static void Run()
    {
        var timestamp = CooldownTestStorage.Field("timestamp");
        var oldClock = timestamp.GetValue(null);
        long now = 100 * Stopwatch.Frequency;
        timestamp.SetValue(null, (Func<long>)(() => now));
        MyAPIGateway.Utilities = new Utilities();
        var store = new CombatCooldownStore();
        var deadline = now + 120 * Stopwatch.Frequency;
        store.Save(deadline);
        for (var frame = 1; frame <= 7200; frame++)
        {
            now = 100 * Stopwatch.Frequency + frame * Stopwatch.Frequency / 60;
            store.Save(deadline);
        }
        Console.WriteLine("Full 120-second cooldown at 60 updates/sec: write attempts=" + MyAPIGateway.Utilities.WriteAttempts +
            ", durable flushes=" + MyAPIGateway.Utilities.Flushes + ", atomic publications=" + MyAPIGateway.Utilities.Publications + ".");
        timestamp.SetValue(null, oldClock);
        CombatLockout.Clear();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Multiplayer = new Multiplayer();
        MyAPIGateway.GridGroups = new GridGroups();
        MyAPIGateway.Session = new BoundsSession();
        MySession.Static = new MySession();
        MyEntities.Entities.Clear();
        CoreSystems.Session.I = new CoreSystems.Session();
        VanillaPlusFramework.VanillaPlusFramework.I = null;
        VanillaPlusFramework.BeamLogic.Beams.I = null;
        var grid = new MyCubeGrid();
        MyEntities.Add(grid);
        CombatLockout.Start();
        CombatLockout.CanConnect(grid);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var retryClock = typeof(CombatLockout).GetField("persistenceTimestamp", flags);
        var oldRetryClock = retryClock?.GetValue(null);
        long retryNow = Stopwatch.GetTimestamp();
        retryClock?.SetValue(null, (Func<long>)(() => retryNow));
        try
        {
            MyAPIGateway.Utilities.FailWrites = true;
            grid.Damage(1);
            var monitorFailed = (bool)typeof(CombatLockout).GetField("failed", flags).GetValue(null);
            MyAPIGateway.Utilities.FailWrites = false;
            retryNow += 2 * Stopwatch.Frequency;
            CombatLockout.Update();
            var recovered = !(bool)typeof(CombatLockout).GetField("failed", flags).GetValue(null) && CombatLockout.RemainingSeconds >= 119;
            Console.WriteLine("After one injected Save IOException: permanent monitor failed=" + monitorFailed +
                "; after storage recovery and next due Update: monitoring recovered=" + recovered + ".");
        }
        finally
        {
            retryClock?.SetValue(null, oldRetryClock);
            CombatLockout.Clear();
        }
    }
}

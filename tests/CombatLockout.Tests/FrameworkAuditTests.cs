using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Sandbox.ModAPI;
using Lanyard;
using VRageMath;

internal static class FrameworkAuditTests
{
    public static void Run(Action<bool, string> check)
    {
        CombatLockout.Clear();
        MyAPIGateway.Multiplayer = new Multiplayer();
        VanillaPlusFramework.VanillaPlusFramework.I = new VanillaPlusFramework.VanillaPlusFramework();
        var beams = new VanillaPlusFramework.BeamLogic.Beams();
        VanillaPlusFramework.BeamLogic.Beams.I = beams;
        int observations = 0;
        Vector3D observed = default;
        var monitor = new NonDamageEffectMonitor(x => {}, (p, r, o, f) => { observations++; observed = p; }, true, false);
        monitor.Update(null);
        check(!monitor.IsBlocking, "beam audit: fixed effect contract binds");
        var def = new VanillaPlusFramework.BeamLogic.BeamDefinition
        {
            EMP_Stats = new VanillaPlusFramework.Utilities.EmpStats(),
            End = new Vector3D(1000, 0, 0),
            HitPosition = new Vector3D(5, 0, 0)
        };
        int applications = VanillaPlusFramework.Utilities.FrameworkUtilities.EmpApplications;
        beams.GenerateBeam(default, default, null, ref def, null);
        check(observations == 1 && observed.X == 5, "beam audit: actual impact observed instead of max trajectory endpoint");
        check(VanillaPlusFramework.Utilities.FrameworkUtilities.EmpApplications == applications,
            "beam audit: client observation does not execute server EMP effect");
        def.EndBeam = false;
        beams.GenerateBeam(default, default, null, ref def, null);
        check(observations == 1, "beam audit: non-detonating guidance segment ignored");
        def.EndBeam = true;
        def.EMP_Stats = null;
        beams.GenerateBeam(default, default, null, ref def, null);
        check(observations == 1, "beam audit: non-EMP beam ignored");
        def.EMP_Stats = new VanillaPlusFramework.Utilities.EmpStats { EMP_TimeDisabled = 0 };
        beams.GenerateBeam(default, default, null, ref def, null);
        check(observations == 1, "beam audit: zero-duration effect ignored");
        def.EMP_Stats = new VanillaPlusFramework.Utilities.EmpStats();
        MyAPIGateway.Multiplayer.IsServer = true;
        beams.GenerateBeam(default, default, null, ref def, null);
        check(observations == 2 && VanillaPlusFramework.Utilities.FrameworkUtilities.EmpApplications == applications + 1,
            "beam audit: host observes once and original effect executes once");
        monitor.Dispose();
        monitor.Dispose();
        monitor.Update(null);
        beams.GenerateBeam(default, default, null, ref def, null);
        check(observations == 2 && VanillaPlusFramework.Utilities.FrameworkUtilities.EmpApplications == applications + 2,
            "beam audit: disposed observer cannot rebind; original operation preserved");
        var transpiler = typeof(NonDamageEffectMonitor).GetMethod("ObserveBeamImpact", BindingFlags.NonPublic | BindingFlags.Static);
        bool rejected = false;
        try { transpiler.Invoke(null, new object[] { new List<CodeInstruction>() }); }
        catch (TargetInvocationException exception) { rejected = exception.InnerException is InvalidOperationException; }
        check(rejected, "beam audit: unsupported impact IL rejected rather than falling back to guessed location");
        MyAPIGateway.Multiplayer.IsServer = false;
    }
}

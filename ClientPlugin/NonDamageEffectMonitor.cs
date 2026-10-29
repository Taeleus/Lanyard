using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Lanyard;

// Optional adapters for frameworks whose electronic warfare does not reduce
// integrity/health. Only fixed members of already-loaded, recognized framework
// types are read. No mod DLL is loaded; registrations only observe effect paths.
internal sealed class NonDamageEffectMonitor : IDisposable
{
    private const string HarmonyId = "Taeleus.Lanyard.NonDamageEffects";
    private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static NonDamageEffectMonitor active;
    private readonly Action<object> hit;
    private readonly Action<Vector3D, float, MyEntity, bool> pulse;
    private readonly bool expectVanillaPlus;
    private bool weaponCoreInstalled;
    private Harmony harmony;
    private Type coreSessionType;
    private FieldInfo coreInstance;
    private FieldInfo coreClientEffects;
    private FieldInfo coreServerEffects;
    private FieldInfo plusInstance;
    private FieldInfo plusDisabledBlocks;
    private bool plusReady;
    private bool coreReady;
    private bool failed;
    private string failureReason;
    private bool disposed;
    private int discoveryTicks;
    private readonly Dictionary<MyCubeGrid, MyEntity> affectedGrids = new Dictionary<MyCubeGrid, MyEntity>();

    public string BlockingReason => failed ? "EMP/jump-inhibition observer failed: " + failureReason :
        weaponCoreInstalled && !coreReady ? "WeaponCore EMP/jump-inhibition observer not initialized" :
        expectVanillaPlus && !plusReady ? "Vanilla+ EMP/jump-inhibition observer not initialized" : null;
    public bool IsBlocking => failed || (weaponCoreInstalled && !coreReady) ||
        (expectVanillaPlus && !weaponCoreInstalled && !plusReady);

    public NonDamageEffectMonitor(Action<object> hit, Action<Vector3D, float, MyEntity, bool> pulse,
        bool expectVanillaPlus, bool weaponCoreInstalled)
    {
        this.hit = hit; this.pulse = pulse;
        this.expectVanillaPlus = expectVanillaPlus;
        this.weaponCoreInstalled = weaponCoreInstalled;
        active = this;
    }

    public void Update(Assembly coreAssembly)
    {
        if (failed || disposed) return;
        try
        {
            if (coreAssembly != null && !coreReady)
            {
                weaponCoreInstalled = true;
                BindCore(coreAssembly);
            }
            if (expectVanillaPlus && !weaponCoreInstalled && !plusReady && discoveryTicks-- <= 0)
            {
                discoveryTicks = 60;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType("VanillaPlusFramework.VanillaPlusFramework", false);
                    if (type != null && BindPlus(assembly, type)) break;
                }
            }
            if (coreReady)
            {
                var session = coreInstance.GetValue(null);
                if (session == null) throw new InvalidOperationException("WeaponCore effect session unavailable.");
                ObserveIds(coreClientEffects.GetValue(session) as IDictionary);
                ObserveIds(coreServerEffects.GetValue(session) as IDictionary);
            }
            if (plusReady)
            {
                var session = plusInstance.GetValue(null);
                if (session == null) throw new InvalidOperationException("Vanilla+ effect session unavailable.");
                ObserveIds(plusDisabledBlocks.GetValue(session) as IDictionary);
            }
            // A grid needs one deadline update, even if hundreds of its blocks
            // appear in both client and server effect collections.
            foreach (var entity in affectedGrids.Values) hit(entity);
        }
        catch (Exception exception) { Fail(exception); }
        finally { affectedGrids.Clear(); }
    }

    private void BindCore(Assembly assembly)
    {
        coreSessionType = assembly.GetType("CoreSystems.Session", true);
        coreInstance = Field(coreSessionType, "I");
        coreClientEffects = Field(coreSessionType, "ActiveEwarCubes");
        coreServerEffects = Field(coreSessionType, "EffectedCubes");
        coreReady = true;
    }

    private bool BindPlus(Assembly assembly, Type sessionType)
    {
        var instance = Field(sessionType, "I");
        var session = instance.GetValue(null);
        if (session == null) return false; // Mod compiled, but not active in this world.
        var disabled = Field(sessionType, "Disabled");
        if ((bool)disabled.GetValue(session)) return false;

        var utilities = assembly.GetType("VanillaPlusFramework.Utilities.FrameworkUtilities", true);
        var missile = assembly.GetType("VanillaPlusFramework.Missiles.VPFMissile", true);
        var beams = assembly.GetType("VanillaPlusFramework.BeamLogic.Beams", true);
        if (Field(beams, "I").GetValue(null) == null) return false;

        // Resolve the complete fixed contract before applying any observers.
        var jumpHit = utilities.GetMethod("JDI_Hit", Fields, null,
            new[] { typeof(MyEntity), typeof(float), typeof(bool), typeof(IMyEntity), typeof(bool) }, null);
        var disable = utilities.GetMethod("DisableBlock", Fields, null,
            new[] { typeof(int), typeof(IMyEntity), typeof(IMyFunctionalBlock) }, null);
        var close = missile.GetMethod("Close", Fields, null, Type.EmptyTypes, null);
        MethodInfo generate = null;
        foreach (var method in beams.GetMethods(Fields))
            if (method.Name == "GenerateBeam")
            {
                if (generate != null) throw new InvalidOperationException("Ambiguous Vanilla+ beam observer.");
                generate = method;
            }
        if (jumpHit == null || disable == null || close == null || generate == null || generate.GetParameters().Length < 5)
            throw new InvalidOperationException("Unsupported Vanilla+ effect signatures.");
        Field(missile, "IsClosed"); Field(missile, "IsDisarmed"); Field(missile, "EMP_Stats");
        Field(missile, "MissileLauncher");
        if (missile.GetProperty("CurrentPosition", Fields)?.PropertyType != typeof(Vector3D))
            throw new InvalidOperationException("Unsupported Vanilla+ missile position.");

        harmony = new Harmony(HarmonyId);
        harmony.Patch(jumpHit, postfix: Patch(nameof(JumpHit)));
        harmony.Patch(disable, postfix: Patch(nameof(BlockDisabled)));
        harmony.Patch(close, prefix: Patch(nameof(MissileClosing)));
        harmony.Patch(generate, transpiler: Patch(nameof(ObserveBeamImpact)));
        plusInstance = instance;
        plusDisabledBlocks = Field(sessionType, "DisabledBlocks");
        plusReady = true;
        return true;
    }

    private static HarmonyMethod Patch(string name)
    {
        return new HarmonyMethod(typeof(NonDamageEffectMonitor).GetMethod(name, Fields));
    }

    private static FieldInfo Field(Type type, string name)
    {
        return type.GetField(name, Fields) ?? throw new MissingFieldException(type.FullName, name);
    }

    private static object Read(object value, string name)
    {
        if (value == null) return null;
        return Field(value.GetType(), name).GetValue(value);
    }

    private void ObserveIds(IDictionary effects)
    {
        if (effects == null) throw new InvalidOperationException("Unsupported framework effect-state collection.");
        // Keys are IDs of blocks actually marked as affected, not nearby targets.
        foreach (var key in effects.Keys)
        {
            var entity = MyEntities.GetEntityById((long)key);
            if (entity == null || entity.Closed || entity.MarkedForClose) continue;
            var grid = (entity as MyCubeBlock)?.CubeGrid ?? entity as MyCubeGrid;
            if (grid != null && !affectedGrids.ContainsKey(grid)) affectedGrids.Add(grid, entity);
        }
    }

    private void Emp(object stats, Vector3D position, MyEntity owner)
    {
        if (stats == null) return;
        var radius = (float)Read(stats, "EMP_Radius");
        var duration = (int)Read(stats, "EMP_TimeDisabled");
        var friendly = (bool)Read(stats, "EMP_AllowFriendlyFire");
        if (radius > 0 && duration > 0 && !float.IsInfinity(radius)) pulse(position, radius, owner, friendly);
    }

    private static void JumpHit(MyEntity __0, float __1, bool __2, IMyEntity __3, bool __4)
    {
        var monitor = active;
        if (monitor == null || monitor.failed || monitor.disposed || __1 <= 0) return;
        try
        {
            var target = Grid(__0);
            var owner = Grid(__3);
            if (target != null && (__4 || target != owner)) monitor.hit(target);
        }
        catch (Exception exception) { monitor.Fail(exception); }
    }

    private static void BlockDisabled(int __0, IMyEntity __1, IMyFunctionalBlock __2)
    {
        var monitor = active;
        if (monitor == null || monitor.failed || monitor.disposed || __0 <= 0 || !MyAPIGateway.Multiplayer.IsServer) return;
        try { monitor.hit(__2); }
        catch (Exception exception) { monitor.Fail(exception); }
    }

    private static void MissileClosing(object __instance)
    {
        var monitor = active;
        if (monitor == null || monitor.failed || monitor.disposed) return;
        try
        {
            if ((bool)Read(__instance, "IsClosed") || (bool)Read(__instance, "IsDisarmed")) return;
            var position = (Vector3D)__instance.GetType().GetProperty("CurrentPosition", Fields).GetValue(__instance);
            monitor.Emp(Read(__instance, "EMP_Stats"), position, Read(__instance, "MissileLauncher") as MyEntity);
        }
        catch (Exception exception) { monitor.Fail(exception); }
    }

    private static IEnumerable<CodeInstruction> ObserveBeamImpact(IEnumerable<CodeInstruction> original)
    {
        var code = new List<CodeInstruction>(original);
        int empCall = -1;
        for (int i = 0; i < code.Count; i++)
            if (code[i].operand is MethodInfo method && method.Name == "EMP" &&
                method.DeclaringType?.FullName == "VanillaPlusFramework.Utilities.FrameworkUtilities" &&
                method.ReturnType == typeof(void))
            {
                var args = method.GetParameters();
                if (empCall >= 0 || args.Length != 5 || args[0].ParameterType != typeof(Vector3D) ||
                    args[1].ParameterType != typeof(float) || args[2].ParameterType != typeof(int) ||
                    args[3].ParameterType != typeof(IMyEntity) || args[4].ParameterType != typeof(bool))
                    throw new InvalidOperationException("Unsupported Vanilla+ beam EMP call.");
                empCall = i;
            }
        if (empCall < 0) throw new InvalidOperationException("Vanilla+ beam EMP call not found.");

        // The fixed framework contract gates EMP with `stats != null && IsServer`.
        // Duplicate only its pure argument loads just before the IsServer test:
        // the impact branch and stats guard remain intact on both host and client.
        int server = -1;
        for (int i = empCall - 1; i >= Math.Max(0, empCall - 80); i--)
            if (code[i].operand is MethodInfo method && method.Name == "get_IsServer" &&
                method.ReturnType == typeof(bool) && method.GetParameters().Length == 0)
            { server = i; break; }
        if (server < 1 || server + 1 >= empCall || empCall + 1 >= code.Count ||
            (code[server + 1].opcode != OpCodes.Brfalse && code[server + 1].opcode != OpCodes.Brfalse_S) ||
            !(code[server + 1].operand is Label exit) || !code[empCall + 1].labels.Contains(exit))
            throw new InvalidOperationException("Unsupported Vanilla+ beam server guard.");
        int gateway = server - 1;
        bool loadsMultiplayer = code[gateway].operand is FieldInfo field && field.DeclaringType == typeof(MyAPIGateway) &&
            field.Name == "Multiplayer" && code[gateway].opcode == OpCodes.Ldsfld;
        if (!loadsMultiplayer && code[gateway].operand is MethodInfo getter)
            loadsMultiplayer = getter.DeclaringType == typeof(MyAPIGateway) && getter.Name == "get_Multiplayer" &&
                code[gateway].opcode == OpCodes.Call;
        if (!loadsMultiplayer) throw new InvalidOperationException("Unsupported Vanilla+ multiplayer access.");
        if (gateway < 2 ||
            (code[gateway - 1].opcode != OpCodes.Brfalse && code[gateway - 1].opcode != OpCodes.Brfalse_S) ||
            !(code[gateway - 1].operand is Label noStats) || !code[empCall + 1].labels.Contains(noStats) ||
            !(code[gateway - 2].operand is MethodInfo hasStats) || hasStats.Name != "get_HasValue" ||
            !hasStats.DeclaringType.IsGenericType || hasStats.DeclaringType.GetGenericTypeDefinition() != typeof(Nullable<>))
            throw new InvalidOperationException("Unsupported Vanilla+ beam EMP stats guard.");

        var observation = new List<CodeInstruction>();
        for (int i = server + 2; i < empCall; i++)
        {
            var instruction = code[i];
            var op = instruction.opcode;
            bool pure = op.Name.StartsWith("ldarg", StringComparison.Ordinal) ||
                op.Name.StartsWith("ldloc", StringComparison.Ordinal) ||
                op == OpCodes.Ldfld || op == OpCodes.Ldflda || op == OpCodes.Ldobj || op == OpCodes.Ldind_Ref || op == OpCodes.Nop ||
                (op == OpCodes.Call && instruction.operand is MethodInfo getValue && getValue.Name == "get_Value" &&
                    getValue.DeclaringType.IsGenericType && getValue.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>));
            if (!pure || instruction.blocks.Count != 0 || instruction.labels.Count != 0)
                throw new InvalidOperationException("Unsupported Vanilla+ beam EMP argument loads.");
            observation.Add(new CodeInstruction(op, instruction.operand));
        }
        if (observation.Count == 0 || !observation[0].opcode.Name.StartsWith("ldloc", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsupported Vanilla+ beam impact position.");
        observation.Add(new CodeInstruction(OpCodes.Call,
            typeof(NonDamageEffectMonitor).GetMethod(nameof(BeamEmp), Fields)));
        observation[0].labels.AddRange(code[gateway].labels);
        code[gateway].labels.Clear();
        code.InsertRange(gateway, observation);
        return code;
    }

    private static void BeamEmp(Vector3D position, float radius, int duration, IMyEntity owner, bool friendly)
    {
        var monitor = active;
        if (monitor == null || monitor.failed || monitor.disposed) return;
        try
        {
            if (radius > 0 && duration > 0 && !float.IsInfinity(radius))
                monitor.pulse(position, radius, owner as MyEntity, friendly);
        }
        catch (Exception exception) { monitor.Fail(exception); }
    }

    private static MyCubeGrid Grid(object entity)
    {
        return entity as MyCubeGrid ?? (entity as MyCubeBlock)?.CubeGrid ?? (entity as MySlimBlock)?.CubeGrid;
    }

    private void Fail(Exception exception)
    {
        if (!failed) MyLog.Default.WriteLineAndConsole("Lanyard non-damage effect observation unavailable: " + exception);
        failureReason = exception.GetType().Name + ": " + exception.Message;
        failed = true; // Only the optional feature's detected world is affected.
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        affectedGrids.Clear();
        if (active == this) active = null;
        try { harmony?.UnpatchAll(HarmonyId); }
        catch (Exception exception) { MyLog.Default.WriteLineAndConsole("Lanyard effect observer cleanup: " + exception.Message); }
        finally { harmony = null; }
    }
}

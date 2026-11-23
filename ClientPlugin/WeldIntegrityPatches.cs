using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Lanyard;

internal static class WeldIntegrityPatches
{
    // Native construction steps just past a component boundary, then rolls back
    // that step when the block is finished or the next component is missing.
    private const float MountThreshold = 1.5259022E-05f;
    private static Action<MyComponentStack, float> setIntegrity;

    private struct Correction
    {
        public MyComponentStack Stack;
        public float RatioChange;
    }

    [ThreadStatic] private static Correction pending;

    public static void TryApply(Harmony harmony)
    {
        try
        {
            var stack = typeof(MyComponentStack);
            var mount = AccessTools.DeclaredMethod(stack, "IncreaseMountLevelInternal",
                new[] { typeof(float), typeof(MyConstructionStockpile) });
            var setter = AccessTools.PropertySetter(stack, nameof(MyComponentStack.Integrity));
            if (mount == null || setter == null)
            {
                MyLog.Default?.WriteLineAndConsole("Lanyard weld correction filter unavailable: unsupported construction method.");
                return;
            }
            setIntegrity = AccessTools.MethodDelegate<Action<MyComponentStack, float>>(setter);
            harmony.Patch(mount, transpiler: new HarmonyMethod(typeof(WeldIntegrityPatches), nameof(Transpiler)));
        }
        catch (Exception error)
        {
            // If the game changes, keep monitoring damage even if welding can
            // again cause a false positive. Other plugin features still load.
            MyLog.Default?.WriteLineAndConsole("Lanyard weld correction filter unavailable: " + error);
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = new List<CodeInstruction>(instructions);
        var setter = AccessTools.PropertySetter(typeof(MyComponentStack), nameof(MyComponentStack.Integrity));
        var maximum = AccessTools.PropertyGetter(typeof(MyComponentStack), nameof(MyComponentStack.MaxIntegrity));
        var integrity = AccessTools.PropertyGetter(typeof(MyComponentStack), nameof(MyComponentStack.Integrity));
        var completion = new List<CodeInstruction>();
        var missingComponent = new List<CodeInstruction>();
        for (int i = 3; i < code.Count; i++)
        {
            if (!code[i].Calls(setter)) continue;
            if (code[i - 1].Calls(maximum))
                completion.Add(code[i]);
            else if (code[i - 3].Calls(integrity) && code[i - 2].opcode == OpCodes.Ldc_R4 &&
                Equals(code[i - 2].operand, MountThreshold) && code[i - 1].opcode == OpCodes.Sub)
                missingComponent.Add(code[i]);
        }

        if (completion.Count != 1 || missingComponent.Count != 1)
        {
            MyLog.Default?.WriteLineAndConsole("Lanyard weld correction filter unavailable: unsupported construction instructions.");
            return code;
        }
        var wrapper = AccessTools.Method(typeof(WeldIntegrityPatches), nameof(SetWeldCorrection));
        foreach (var call in new[] { completion[0], missingComponent[0] })
        {
            call.opcode = OpCodes.Call;
            call.operand = wrapper;
        }
        return code;
    }

    private static void SetWeldCorrection(MyComponentStack stack, float value)
    {
        var previous = pending;
        float maximum = stack.MaxIntegrity;
        pending = new Correction { Stack = stack, RatioChange = value / maximum - stack.Integrity / maximum };
        try { setIntegrity(stack, value); }
        finally { pending = previous; }
    }

    internal static bool ConsumeCorrection(MySlimBlock block, float amount, MyHitInfo? hit, long attacker)
    {
        if (pending.Stack == null || block == null || !ReferenceEquals(pending.Stack, block.ComponentStack) ||
            amount != pending.RatioChange || hit.HasValue || attacker != 0) return false;
        // Only one native OnIntegrityChanged notification belongs to this setter.
        pending = default;
        return true;
    }
}

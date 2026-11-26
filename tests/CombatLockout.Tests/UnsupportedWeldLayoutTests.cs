using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Lanyard;
using Sandbox.Game.Entities;
using VRage.Utils;

internal static partial class Program
{
    // Preserve the setter's behavior while making the missing-component
    // instruction sequence unrecognizable to the production transpiler.
    private static IEnumerable<CodeInstruction> InsertNopAfterSubtract(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.opcode == OpCodes.Sub) yield return new CodeInstruction(OpCodes.Nop);
        }
    }

    private static void UnsupportedWeldLayoutTest()
    {
        var grid = LinkedWeldGrid();
        var block = WeldBlock(grid, 100f);
        var target = AccessTools.DeclaredMethod(typeof(MyComponentStack), "IncreaseMountLevelInternal");
        var probe = new Harmony("Lanyard.Tests.UnsupportedWeldLayout");
        var messages = MyLog.Default.Lines.Count;
        try
        {
            probe.Patch(target, transpiler: new HarmonyMethod(typeof(Program), nameof(InsertNopAfterSubtract))
            {
                before = new[] { "Taeleus.Lanyard.OwnGrinding" }
            });
            Check(MyLog.Default.Lines.FindIndex(messages, line => line.Contains("unsupported construction instructions")) >= 0,
                "welding: unsupported construction layout reports the unavailable filter");
            block.ComponentStack.IncreaseMountLevel(25f);
            Check(block.ComponentStack.Integrity == 100f && CombatLockout.IsBlocked && CombatLockout.RemainingSeconds == 120,
                "welding: unknown layout preserves the weld and continues to count damage on the other correction path");
        }
        finally { probe.UnpatchAll(probe.Id); }
    }
}

using System;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using VRage.Game.Entity;
using VRage.Utils;

namespace Lanyard;

/// <summary>Supply a missing starting component after a normal placement attempt.</summary>
internal static class PlacementSupply
{
    private const string HarmonyId = "Taeleus.Lanyard.PlacementSupply";
    private static Harmony harmony;

    public static void Initialize()
    {
        if (harmony != null) return;
        try
        {
            var method = AccessTools.Method(typeof(MyCubeBuilder), nameof(MyCubeBuilder.CanStartConstruction),
                new[] { typeof(MyEntity) });
            if (method == null) throw new MissingMethodException("Block placement check is unavailable.");
            harmony = new Harmony(HarmonyId);
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(PlacementSupply), nameof(Postfix)));
        }
        catch (Exception exception)
        {
            Dispose();
            LogFailure("initialization", exception);
        }
    }

    public static void Dispose()
    {
        try { harmony?.UnpatchAll(HarmonyId); }
        catch (Exception exception) { LogFailure("unpatch", exception); }
        finally { harmony = null; }
    }

    // Keep the game's result untouched: the player clicks again once the item arrives.
    private static void Postfix(MyCubeBuilder __instance, MyEntity buildingEntity, bool __result)
    {
        try
        {
            var session = MySession.Static;
            var character = session?.LocalCharacter;
            if (!ReferenceEquals(__instance, MyCubeBuilder.Static) || !__instance.IsActivated ||
                character == null || !ReferenceEquals(buildingEntity, character) ||
                !(character.EquippedTool is MyBlockPlacerBase) ||
                session.CreativeMode || session.CreativeToolsEnabled(Sync.MyId)) return;
            LinkController.RequestPlacementComponent(__instance.CurrentBlockDefinition, __result);
        }
        catch (Exception exception) { LogFailure("request", exception); }
    }

    private static void LogFailure(string operation, Exception exception)
    {
        try { MyLog.Default?.WriteLineAndConsole("Lanyard placement " + operation + " failed: " + exception); }
        catch { }
    }
}

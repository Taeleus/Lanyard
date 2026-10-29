using System;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using VRage.Game;

namespace Lanyard;

// Observe the actual deconstruction operations. A held/active grinder alone
// must never exempt unrelated incoming damage from the combat lockout.
internal static class OwnGrindingPatches
{
    private const string HarmonyId = "Taeleus.Lanyard.OwnGrinding";
    private static Harmony harmony;
    [ThreadStatic] private static Scope current;

    public static void Initialize()
    {
        if (harmony != null) return;
        harmony = new Harmony(HarmonyId);
        try { harmony.PatchAll(typeof(OwnGrindingPatches).Assembly); }
        catch { Dispose(); throw; }
    }

    public static void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        current = null;
    }

    internal static Scope Begin(MySlimBlock block, long actor, bool deconstructing)
    {
        var identity = MySession.Static?.LocalHumanPlayer?.Identity?.IdentityId ?? 0;
        var exempt = deconstructing && identity != 0 && IsFriendly(actor, identity) &&
            FriendlyTarget(block, identity, actor);
        var scope = new Scope(block, exempt, current);
        current = scope;
        return scope;
    }

    private static bool IsFriendly(long owner, long identity)
    {
        if (owner == 0 || identity == 0) return false;
        if (owner == identity) return true;
        // Inventory sharing must not make a hostile grinder or target friendly.
        var relation = MyIDModule.GetRelationPlayerBlock(owner, identity, MyOwnershipShareModeEnum.None);
        return relation == MyRelationsBetweenPlayerAndBlock.Owner || relation == MyRelationsBetweenPlayerAndBlock.Friends;
    }

    private static bool FriendlyTarget(MySlimBlock block, long identity, long actor)
    {
        if (block == null) return false;
        if (block.OwnerId != 0) return IsFriendly(block.OwnerId, identity) && IsFriendly(block.OwnerId, actor);
        var owners = block.CubeGrid?.BigOwners;
        if (owners == null || owners.Count == 0) return false;
        foreach (var owner in owners)
            if (!IsFriendly(owner, identity) || !IsFriendly(owner, actor)) return false;
        return true;
    }

    internal static bool AppliesTo(MySlimBlock block)
    {
        return current != null && current.Exempt && ReferenceEquals(current.Block, block);
    }

    internal static void End(Scope scope, Exception exception)
    {
        if (scope == null) return;
        try
        {
            // Raze/removal is a later operation. Remember only a block that
            // this exact friendly deconstruction successfully reduced to zero.
            if (exception == null && scope.Exempt && scope.Block.IsFullyDismounted && scope.Block.BuildIntegrity <= 0)
                CombatLockout.OwnBlockDismantled(scope.Block);
        }
        finally { current = scope.Previous; }
    }

    internal sealed class Scope
    {
        public readonly MySlimBlock Block;
        public readonly bool Exempt;
        public readonly Scope Previous;
        public Scope(MySlimBlock block, bool exempt, Scope previous) { Block = block; Exempt = exempt; Previous = previous; }
    }
}

[HarmonyPatch(typeof(MySlimBlock), nameof(MySlimBlock.DecreaseMountLevel))]
internal static class OwnGrindingLocalPatch
{
    private static void Prefix(MySlimBlock __instance, long identityId, out OwnGrindingPatches.Scope __state)
    {
        __state = OwnGrindingPatches.Begin(__instance, identityId, true);
    }

    // Finalizers restore scope even if vanilla or another mod throws. They do
    // not suppress exceptions, skip the original, or change game arguments.
    private static void Finalizer(OwnGrindingPatches.Scope __state, Exception __exception)
    {
        OwnGrindingPatches.End(__state, __exception);
    }
}

[HarmonyPatch(typeof(MySlimBlock), nameof(MySlimBlock.SetIntegrity))]
internal static class OwnGrindingReplicatedPatch
{
    private static void Prefix(MySlimBlock __instance, float buildIntegrity, long grinderOwner, out OwnGrindingPatches.Scope __state)
    {
        // Grinding decreases build level; combat damage leaves build level in
        // place. The RPC carries the grinding inventory owner's identity.
        __state = OwnGrindingPatches.Begin(__instance, grinderOwner, buildIntegrity < __instance.BuildIntegrity);
    }

    private static void Finalizer(OwnGrindingPatches.Scope __state, Exception __exception)
    {
        OwnGrindingPatches.End(__state, __exception);
    }
}

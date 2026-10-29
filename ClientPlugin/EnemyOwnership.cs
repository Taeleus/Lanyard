using Sandbox.Game.Entities;
using VRage.Game;

namespace Lanyard;

internal static class EnemyOwnership
{
    public static bool IsEnemy(long owner, long identity)
    {
        // Public inventory sharing must not hide an enemy owner's overlapping grid.
        return owner != 0 && MyIDModule.GetRelationPlayerBlock(owner, identity,
            MyOwnershipShareModeEnum.None) == MyRelationsBetweenPlayerAndBlock.Enemies;
    }
}

using System;
using System.Collections.Generic;
using Lanyard;
using Sandbox.Game.Entities;
using VRage.Game;

internal static class OwnershipRelationTests
{
    public static void Run(Action<bool, string> check)
    {
        const long identity = 1;
        const long enemy = 2;
        MyIDModule.Relations.Clear();
        MyIDModule.PairRelations.Clear();
        MyIDModule.Relations[enemy] = MyRelationsBetweenPlayerAndBlock.Enemies;
        MyIDModule.Relations[3] = MyRelationsBetweenPlayerAndBlock.Neutral;
        MyIDModule.Relations[4] = MyRelationsBetweenPlayerAndBlock.Friends;
        MyIDModule.Relations[5] = MyRelationsBetweenPlayerAndBlock.FactionShare;

        check(MyIDModule.GetRelationPlayerBlock(enemy, identity, MyOwnershipShareModeEnum.All) ==
            MyRelationsBetweenPlayerAndBlock.FactionShare,
            "enemy ownership: native Share All relation can mask an enemy inventory owner");
        var block = new MyCubeBlock { OwnerId = enemy, IDModule = new OwnershipModule() };
        foreach (MyOwnershipShareModeEnum share in Enum.GetValues(typeof(MyOwnershipShareModeEnum)))
        {
            block.IDModule.ShareMode = share;
            check(EnemyOwnership.IsEnemy(block.OwnerId, identity),
                "enemy ownership: overlapping enemy owner remains hostile with " + share + " sharing");
            check(MyIDModule.LastOwner == enemy && MyIDModule.LastIdentity == identity &&
                MyIDModule.LastShare == MyOwnershipShareModeEnum.None,
                "enemy ownership: native owner relation excludes inventory sharing for " + share);
        }

        foreach (long owner in new[] { identity, 3L, 4L, 5L })
        {
            block.OwnerId = owner;
            block.IDModule.ShareMode = MyOwnershipShareModeEnum.All;
            check(!EnemyOwnership.IsEnemy(block.OwnerId, identity),
                "enemy ownership: own, neutral, friendly and faction owners stay allowed (owner " + owner + ")");
            check(MyIDModule.LastOwner == owner && MyIDModule.LastShare == MyOwnershipShareModeEnum.None,
                "enemy ownership: nonenemy owner still uses the actual owner relation (owner " + owner + ")");
        }

        int calls = MyIDModule.Calls;
        check(!EnemyOwnership.IsEnemy(0, identity) && MyIDModule.Calls == calls,
            "enemy ownership: unowned blocks remain excluded from enemy classification");

        MyIDModule.Relations[enemy] = MyRelationsBetweenPlayerAndBlock.Neutral;
        check(!EnemyOwnership.IsEnemy(enemy, identity),
            "enemy ownership: changing an owner's relation to neutral is read live");
        MyIDModule.Relations[enemy] = MyRelationsBetweenPlayerAndBlock.Enemies;
        check(EnemyOwnership.IsEnemy(enemy, identity),
            "enemy ownership: changing an owner's relation back to enemy is read live");
    }
}

// Native API boundary double. Model the relevant Share All override so the
// regression test would fail if the production helper forwarded block sharing.
namespace Sandbox.Game.Entities
{
    public static class MyIDModule
    {
        public static readonly Dictionary<long, MyRelationsBetweenPlayerAndBlock> Relations =
            new Dictionary<long, MyRelationsBetweenPlayerAndBlock>();
        public static readonly Dictionary<(long Owner, long Identity), MyRelationsBetweenPlayerAndBlock> PairRelations =
            new Dictionary<(long, long), MyRelationsBetweenPlayerAndBlock>();
        public static long LastOwner, LastIdentity;
        public static MyOwnershipShareModeEnum LastShare;
        public static int Calls;

        public static MyRelationsBetweenPlayerAndBlock GetRelationPlayerBlock(long owner, long identity,
            MyOwnershipShareModeEnum share = MyOwnershipShareModeEnum.None)
        {
            Calls++;
            LastOwner = owner;
            LastIdentity = identity;
            LastShare = share;
            if (owner == identity) return MyRelationsBetweenPlayerAndBlock.Owner;
            if (owner == 0 || identity == 0) return MyRelationsBetweenPlayerAndBlock.NoOwnership;
            if (share == MyOwnershipShareModeEnum.All) return MyRelationsBetweenPlayerAndBlock.FactionShare;
            if (PairRelations.TryGetValue((owner, identity), out var pairRelation)) return pairRelation;
            return Relations.TryGetValue(owner, out var relation) ? relation : MyRelationsBetweenPlayerAndBlock.Enemies;
        }
    }
}

namespace VRage.Game
{
    public enum MyRelationsBetweenPlayerAndBlock { NoOwnership, Owner, FactionShare, Neutral, Friends, Enemies }
}

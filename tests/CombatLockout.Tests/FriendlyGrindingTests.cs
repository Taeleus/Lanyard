using System;
using System.Collections;
using System.Reflection;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;

internal static class FriendlyGrindingTests
{
    private static MyCubeGrid World()
    {
        CombatLockout.Clear();
        ((CombatCooldown)typeof(CombatLockout).GetField("cooldown", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Clear();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Multiplayer = new Multiplayer();
        MyAPIGateway.GridGroups = new GridGroups();
        MyAPIGateway.Session = new BoundsSession();
        MySession.Static = new MySession();
        CoreSystems.Session.I = new CoreSystems.Session();
        VanillaPlusFramework.VanillaPlusFramework.I = null;
        VanillaPlusFramework.BeamLogic.Beams.I = null;
        MyEntities.Entities.Clear();
        MyIDModule.Relations.Clear();
        MyIDModule.PairRelations.Clear();
        var grid = new MyCubeGrid();
        grid.BigOwners.Add(1);
        MyEntities.Add(grid);
        CombatLockout.Start();
        var grids = (IDictionary)typeof(CombatLockout).GetField("grids", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        foreach (var activity in grids.Values)
        if (!CombatLockout.CanConnect(grid)) throw new Exception("Friendly grinding fixture must start available");
        return grid;
    }

    private static void Friendly(long first, long second)
    {
        MyIDModule.PairRelations[(first, second)] = MyRelationsBetweenPlayerAndBlock.Friends;
        MyIDModule.PairRelations[(second, first)] = MyRelationsBetweenPlayerAndBlock.Friends;
    }

    private static MySlimBlock Block(MyCubeGrid grid, long owner = 1)
    {
        var block = new MySlimBlock { CubeGrid = grid, OwnerId = owner };
        grid.CubeBlocks.Add(block);
        return block;
    }

    private static void Grind(MySlimBlock block, long actor, bool replicated, float amount)
    {
        if (replicated) block.SetIntegrity(block.BuildIntegrity - amount, block.Integrity - amount, null, actor);
        else block.DecreaseMountLevel(amount, identityId: actor);
    }

    public static void Run(Action<bool, string> check)
    {
        try
        {
            foreach (bool replicated in new[] { false, true })
            {
                foreach (long actor in new[] { 2L, 3L })
                {
                    var grid = World(); Friendly(actor, 1);
                    var block = Block(grid);
                    Grind(block, actor, replicated, 20);
                    check(!CombatLockout.IsBlocked, "friendly grinding: faction/allied actor partial deconstruction is allowed, actor " + actor + ", replicated " + replicated);
                    check(block.Integrity == 80 && block.BuildIntegrity == 80, "friendly grinding: original deconstruction still executes");
                    Grind(block, actor, replicated, 80); grid.RemoveSlim(block);
                    check(!CombatLockout.IsBlocked, "friendly grinding: faction/allied final removal stays exempt");
                }

                foreach (long actor in new[] { 0L, 4L, 5L })
                {
                    var grid = World();
                    MyIDModule.PairRelations[(5, 1)] = MyRelationsBetweenPlayerAndBlock.Neutral;
                    Grind(Block(grid), actor, replicated, 20);
                    check(CombatLockout.IsBlocked, "friendly grinding: unknown/enemy/neutral grinder still blocks, actor " + actor + ", replicated " + replicated);
                }

                foreach (long owner in new[] { 4L, 5L, 0L })
                {
                    var grid = World(); Friendly(2, 1);
                    MyIDModule.PairRelations[(5, 1)] = MyRelationsBetweenPlayerAndBlock.Neutral;
                    if (owner == 0) grid.BigOwners.Clear();
                    Grind(Block(grid, owner), 2, replicated, 20);
                    check(CombatLockout.IsBlocked, "friendly grinding: friendly actor cannot exempt hostile/neutral/unowned target, owner " + owner + ", replicated " + replicated);
                }

                var friendlyGrid = World(); Friendly(2, 1); Friendly(3, 1); Friendly(3, 2);
                friendlyGrid.BigOwners.Add(3);
                var armor = Block(friendlyGrid, 0);
                Grind(armor, 2, replicated, 20);
                check(!CombatLockout.IsBlocked, "friendly grinding: armor with exclusively mutually friendly owners is allowed");
                Grind(armor, 2, replicated, 80); friendlyGrid.RemoveSlim(armor);
                check(!CombatLockout.IsBlocked, "friendly grinding: friendly armor final removal is allowed");

                foreach (long actor in new[] { 1L, 2L })
                {
                    var mixedGrid = World(); Friendly(2, 1); mixedGrid.BigOwners.Add(4);
                    Grind(Block(mixedGrid, 0), actor, replicated, 20);
                    check(CombatLockout.IsBlocked, "friendly grinding: a friendly big owner cannot mask hostile mixed-grid armor");
                }

                foreach (var relation in new[] { MyRelationsBetweenPlayerAndBlock.Enemies, MyRelationsBetweenPlayerAndBlock.Neutral })
                {
                    var grid = World(); Friendly(2, 1); Friendly(3, 1);
                    MyIDModule.PairRelations[(3, 2)] = relation;
                    Grind(Block(grid, 3), 2, replicated, 20);
                    check(CombatLockout.IsBlocked, "friendly grinding: separate allies must also be friendly to each other");
                }

                var revokedGrid = World(); Friendly(2, 1);
                var revokedBlock = Block(revokedGrid); Grind(revokedBlock, 2, replicated, 20);
                check(!CombatLockout.IsBlocked, "friendly grinding: initial live friendly relation allows work");
                MyIDModule.PairRelations[(2, 1)] = MyRelationsBetweenPlayerAndBlock.Enemies;
                Grind(revokedBlock, 2, replicated, 20);
                check(CombatLockout.IsBlocked, "friendly grinding: leaving friendly relations takes effect on the next operation");

                var damageGrid = World(); Friendly(2, 1);
                var damageBlock = Block(damageGrid); Grind(damageBlock, 2, replicated, 20);
                damageGrid.Damage(damageBlock, -0.1f);
                check(CombatLockout.IsBlocked, "friendly grinding: later negative damage is never covered by a grace window");

                var unchangedGrid = World(); Friendly(2, 1);
                var unchangedBlock = Block(unchangedGrid);
                unchangedBlock.SetIntegrity(100, 80, null, 2);
                check(CombatLockout.IsBlocked, "friendly grinding: friendly actor without decreasing build level does not exempt damage");
            }

            var directGrid = World(); Friendly(2, 1);
            var directBlock = Block(directGrid);
            directBlock.DuringGrinding = () => directGrid.Damage(directBlock, 10);
            directBlock.DecreaseMountLevel(20, identityId: 2);
            check(CombatLockout.IsBlocked, "friendly grinding: positive combat damage on the same block still counts during grinding");

            var otherGrid = World(); Friendly(2, 1);
            var grinding = Block(otherGrid); var other = Block(otherGrid);
            grinding.DuringGrinding = () => otherGrid.Damage(other, -0.1f);
            grinding.DecreaseMountLevel(20, identityId: 2);
            check(CombatLockout.IsBlocked, "friendly grinding: negative damage on another block is not exempt");

            var nestedGrid = World(); Friendly(2, 1);
            var nestedBlock = Block(nestedGrid);
            nestedBlock.DuringGrinding = () => nestedBlock.SetIntegrity(nestedBlock.BuildIntegrity, 70, null, 2);
            nestedBlock.DecreaseMountLevel(20, identityId: 2);
            check(CombatLockout.IsBlocked, "friendly grinding: nested integrity damage cannot inherit an outer friendly scope");

            var exceptionGrid = World(); Friendly(2, 1);
            var exceptionBlock = Block(exceptionGrid); exceptionBlock.ThrowDuringGrinding = true;
            bool threw = false;
            try { exceptionBlock.DecreaseMountLevel(20, identityId: 2); }
            catch (InvalidOperationException) { threw = true; }
            check(threw && !OwnGrindingPatches.AppliesTo(exceptionBlock), "friendly grinding: original exceptions propagate and scope clears");
            exceptionGrid.Damage(exceptionBlock, -0.1f);
            check(CombatLockout.IsBlocked, "friendly grinding: damage after a failed friendly operation still blocks");

            var shareGrid = World();
            var sharedBlock = Block(shareGrid); sharedBlock.FatBlock = new MyCubeBlock
            {
                OwnerId = 1, IDModule = new OwnershipModule { ShareMode = MyOwnershipShareModeEnum.All }
            };
            sharedBlock.DecreaseMountLevel(20, identityId: 4);
            check(CombatLockout.IsBlocked && MyIDModule.LastShare == MyOwnershipShareModeEnum.None,
                "friendly grinding: Share All does not turn an enemy actor into a friend");

            var ownerChangedGrid = World(); Friendly(2, 1); Friendly(3, 1); Friendly(3, 2);
            var ownerChangedBlock = Block(ownerChangedGrid, 3);
            ownerChangedBlock.DecreaseMountLevel(20, identityId: 2);
            check(!CombatLockout.IsBlocked, "friendly grinding: mutually friendly block ownership allows work");
            MyIDModule.PairRelations[(3, 2)] = MyRelationsBetweenPlayerAndBlock.Neutral;
            ownerChangedBlock.DecreaseMountLevel(20, identityId: 2);
            check(CombatLockout.IsBlocked, "friendly grinding: target-owner relations are read live for every operation");

            var removedGrid = World(); Friendly(2, 1);
            var removedBlock = Block(removedGrid); removedBlock.DecreaseMountLevel(20, identityId: 2);
            removedGrid.RemoveSlim(removedBlock);
            check(CombatLockout.IsBlocked, "friendly grinding: partial friendly grinding cannot excuse unexplained later removal");

            var unknownGrid = World(); Friendly(2, 1); unknownGrid.BigOwners.Clear(); unknownGrid.BigOwners.Add(0);
            Block(unknownGrid, 0).DecreaseMountLevel(20, identityId: 2);
            check(CombatLockout.IsBlocked, "friendly grinding: unknown grid owner cannot establish friendly armor");

            var noPlayerGrid = World(); Friendly(2, 1); MySession.Static.LocalHumanPlayer = null;
            Block(noPlayerGrid).DecreaseMountLevel(20, identityId: 2);
            check(CombatLockout.IsBlocked, "friendly grinding: absent local identity grants no exemption");
        }
        finally
        {
            CombatLockout.Clear();
            MyIDModule.Relations.Clear();
            MyIDModule.PairRelations.Clear();
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Lanyard;
using VRage.Game;
using VRage.Game.ModAPI;

internal static class TopologyNotificationTests
{
    private sealed class CoreConnector : MyShipConnector, IMyTerminalBlock
    {
        public CoreConnector() { BlockDefinition.Id = new MyDefinitionId("CoreGun"); }
    }

    private static MyCubeGrid World()
    {
        CombatLockout.Clear();
        ResetCooldown();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Multiplayer = new Multiplayer();
        MyAPIGateway.GridGroups = new GridGroups();
        MyAPIGateway.Session = new BoundsSession();
        MySession.Static = new MySession();
        CoreSystems.Session.I = new CoreSystems.Session();
        VanillaPlusFramework.VanillaPlusFramework.I = null;
        VanillaPlusFramework.BeamLogic.Beams.I = null;
        MyEntities.Entities.Clear();
        var grid = new MyCubeGrid { DisplayName = "Docking ship" };
        MyEntities.Add(grid);
        return grid;
    }

    private static void ResetCooldown()
    {
        ((CombatCooldown)typeof(CombatLockout).GetField("cooldown", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
    }

    private static void ObserveQuiet()
    {
        ResetCooldown();
        var grids = (IDictionary)typeof(CombatLockout).GetField("grids", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (var activity in grids.Values)
        {
            activity.GetType().GetProperty("Until").SetValue(activity, 0L);
        }
        MyAPIGateway.Utilities.Files.Clear();
    }

    public static void Run(Action<bool, string> check)
    {
        var ship = World();
        var station = new MyCubeGrid { DisplayName = "Docking station" };
        var shipConnector = new CoreConnector();
        var stationConnector = new CoreConnector();
        ship.Add(shipConnector);
        station.Add(stationConnector);
        MyEntities.Add(station);
        var shipSlim = shipConnector.SlimBlock;
        var stationSlim = stationConnector.SlimBlock;
        var provider = new FakeWeaponCore();
        provider.Add(shipConnector, 0);
        provider.Add(stationConnector, 0);
        provider.RespondOnRequest();
        CombatLockout.Start(); ObserveQuiet();
        MyAPIGateway.GridGroups.LogicalGroups[ship] = new List<IMyCubeGrid> { ship, station };
        check(CombatLockout.CanConnect(ship), "topology: quiet linked construct initially usable");
        check(provider.ObserverCount == 2, "topology: each owned connector component observed once");

        // Vanilla connector/advanced-rotor conveyor bookkeeping reports the
        // other grid's live block without moving or destroying that block.
        ship.NotifyBlockAdded(stationSlim);
        station.NotifyBlockAdded(shipSlim);
        CombatLockout.Update();
        check(!CombatLockout.IsBlocked, "topology: foreign live connector additions do not create observation timeouts");
        check(provider.ObserverCount == 2, "topology: foreign addition notifications do not duplicate WeaponCore observers");
        ship.NotifyBlockRemoved(stationSlim);
        station.NotifyBlockRemoved(shipSlim);
        MyAPIGateway.GridGroups.LogicalGroups.Remove(ship);
        ship.ConnectionsChanged();
        CombatLockout.Update();
        check(!CombatLockout.IsBlocked, "topology: mirrored connector detach notifications are not destruction");
        check(provider.ObserverCount == 2, "topology: foreign removal does not dispose the owning grid's observer");
        check(shipConnector.CubeGrid == ship && stationConnector.CubeGrid == station &&
            ship.Blocks.Contains(shipConnector) && station.Blocks.Contains(stationConnector),
            "topology: bookkeeping notifications leave both live connectors on their own grids");
        ship.NotifyBlockAdded(stationSlim);
        ship.NotifyBlockRemoved(stationSlim);
        check(!CombatLockout.IsBlocked, "topology: repeated foreign bookkeeping still does not count as connector hits");
        ship.NotifyBlockRemoved(shipSlim);
        CombatLockout.Update();
        check(!CombatLockout.IsBlocked, "topology: notification for a same-grid resident block is not destruction");
        check(provider.ObserverCount == 2, "topology: notification for a live local block keeps its weapon observer");
        provider.Event(shipConnector, 0, 1, true);
        check(CombatLockout.IsBlocked, "topology: genuine local weapon callback still works after foreign bookkeeping");
        ObserveQuiet(); CombatLockout.CanConnect(ship);
        ship.RemoveSlim(shipSlim);
        check(CombatLockout.IsBlocked && CombatLockout.BlockReason.Contains("Block removal"),
            "topology: actual local connector removal remains an immediate destruction trigger");
        check(provider.ObserverCount == 1, "topology: actual local removal disposes only that connector observer");
        CombatLockout.Clear();
        check(provider.ObserverCount == 0 && ship.Subscribers == 0 && station.Subscribers == 0,
            "topology: all real grid observers detach on teardown");

        ship = World();
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(ship);
        var armor = new MySlimBlock { CubeGrid = ship };
        ship.RemoveSlim(armor);
        check(CombatLockout.IsBlocked, "topology: actual local armor removal remains an immediate trigger");

        ship = World();
        var localConnector = new MyShipConnector(); ship.Add(localConnector);
        var localSlim = localConnector.SlimBlock;
        localSlim.IsMovedBySplit = true;
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(ship);
        ship.RemoveSlim(localSlim);
        check(!CombatLockout.IsBlocked, "topology: moving a live block during a split is not destruction");
        var transferredConnector = new MyShipConnector(); ship.Add(transferredConnector);
        ship.IsBlockTrasferInProgress = true;
        ship.RemoveSlim(transferredConnector.SlimBlock);
        check(!CombatLockout.IsBlocked, "topology: moving live grid content during transfer is not destruction");
        ship.IsBlockTrasferInProgress = false;
        var fragment = new MyCubeGrid(); MyEntities.Add(fragment);
        check(CombatLockout.CanConnect(fragment), "topology: a quiet split fragment has no artificial observation interval");
        ObserveQuiet(); CombatLockout.CanConnect(ship);
        var destroyedConnector = new MyShipConnector(); ship.Add(destroyedConnector);
        ship.RemoveSlim(destroyedConnector.SlimBlock);
        check(CombatLockout.IsBlocked, "topology: a real connector removal after transfer still triggers");
        CombatLockout.Clear();
    }
}

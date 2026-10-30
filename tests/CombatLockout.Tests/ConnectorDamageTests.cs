using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Lanyard;
using VRage.Game.ModAPI;

internal static class ConnectorDamageTests
{
    private static MyCubeGrid World(out MySlimBlock connector)
    {
        CombatLockout.Clear();
        ((CombatCooldown)typeof(CombatLockout).GetField("cooldown", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Multiplayer = new Multiplayer();
        MyAPIGateway.GridGroups = new GridGroups();
        MyAPIGateway.Session = new BoundsSession();
        MySession.Static = new MySession();
        CoreSystems.Session.I = new CoreSystems.Session();
        VanillaPlusFramework.VanillaPlusFramework.I = null;
        VanillaPlusFramework.BeamLogic.Beams.I = null;
        MyEntities.Entities.Clear();
        var grid = new MyCubeGrid();
        var fat = new MyShipConnector();
        grid.Add(fat);
        connector = new MySlimBlock { CubeGrid = grid, FatBlock = fat };
        MyEntities.Add(grid);
        return grid;
    }

    private static void ObserveQuiet()
    {
        ((CombatCooldown)typeof(CombatLockout).GetField("cooldown", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
        var grids = (IDictionary)typeof(CombatLockout).GetField("grids", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (var activity in grids.Values)
        {
            activity.GetType().GetProperty("Until").SetValue(activity, 0L);
        }
        MyAPIGateway.Utilities.Files.Clear();
    }

    public static void Run(Action<bool, string> check)
    {
        var gate = new ConnectorDamageWindow(1000);
        check(!gate.Observe(ConnectorDamageSource.NativeDamage, 0), "connector window: first native hit is tolerated");
        check(!gate.Observe(ConnectorDamageSource.ReplicatedIntegrity, 1), "connector window: replicated copy is not a second hit");
        check(!gate.Observe(ConnectorDamageSource.WeaponCore, 2), "connector window: WC copy is not a second hit");
        check(gate.Observe(ConnectorDamageSource.NativeDamage, 1000), "connector window: repeated native hit at exact one-second boundary blocks");
        check(gate.Observe(ConnectorDamageSource.NativeDamage, 1500), "connector window: sustained damage continues to qualify");
        check(!gate.Observe(ConnectorDamageSource.NativeDamage, 2501), "connector window: isolated hit after expired window is tolerated");
        check(gate.Observe(ConnectorDamageSource.NativeDamage, 2600), "connector window: later repeated hit starts a fresh burst");
        gate = new ConnectorDamageWindow(1000);
        check(!gate.Observe(ConnectorDamageSource.ReplicatedIntegrity, 20), "connector window: first client-only integrity loss tolerated");
        check(gate.Observe(ConnectorDamageSource.ReplicatedIntegrity, 900), "connector window: repeated client-only integrity losses qualify");
        gate = new ConnectorDamageWindow(1000);
        check(!gate.Observe(ConnectorDamageSource.WeaponCore, 30), "connector window: first WC-only hit tolerated");
        check(gate.Observe(ConnectorDamageSource.WeaponCore, 31), "connector window: repeated WC-only hits qualify");
        check(!gate.Observe(ConnectorDamageSource.WeaponCore, 29), "connector window: a backwards timestamp does not manufacture a burst");

        var grid = World(out var connector);
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        grid.Damage(connector, 1);
        grid.Damage(connector, -0.1f);
        check(!CombatLockout.IsBlocked, "connector integration: one docking hit plus its integrity callback tolerated");
        grid.Damage(connector, 1);
        check(CombatLockout.IsBlocked, "connector integration: second native hit within one second blocks");

        grid = World(out connector);
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        grid.Damage(connector, -0.1f);
        check(!CombatLockout.IsBlocked, "connector integration: first replicated-only hit tolerated");
        grid.Damage(connector, -0.1f);
        grid.Damage(connector, -0.1f);
        check(!CombatLockout.IsBlocked, "connector integration: multiple component losses within one frame are one damage observation");
        MySession.Static.GameplayFrameCounter++;
        grid.Damage(connector, -0.1f);
        check(CombatLockout.IsBlocked, "connector integration: second replicated-only hit in a later frame blocks");

        grid = World(out connector);
        var core = new FakeWeaponCore(); core.RespondOnRequest();
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        grid.Damage(connector, 1); grid.Damage(connector, -0.1f); core.Hit(connector, 1);
        check(!CombatLockout.IsBlocked, "connector integration: native/replicated/WC evidence for one hit is not triple-counted");
        core.Hit(connector, 1);
        check(CombatLockout.IsBlocked, "connector integration: repeated WC connector hits still block");

        grid = World(out connector);
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        grid.Damage(connector, 1);
        grid.Damage(new MySlimBlock { CubeGrid = grid }, 1);
        check(CombatLockout.IsBlocked, "connector integration: damage to any other block remains immediate");

        grid = World(out connector);
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        grid.RemoveSlim(connector);
        check(CombatLockout.IsBlocked, "connector integration: outright connector destruction remains immediate");

        grid = World(out connector);
        core = new FakeWeaponCore(); core.RespondOnRequest();
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        MySession.Static.LocalCharacter.StatComp.Health.Change(90, 100);
        core.Hit(MySession.Static.LocalCharacter, 10);
        check(!CombatLockout.IsBlocked, "connector integration: health loss and WC character damage no longer lock out");
        CoreSystems.Session.I.ActiveEwarCubes[connector.FatBlock.EntityId] = new object();
        CombatLockout.Update();
        check(CombatLockout.IsBlocked, "connector integration: non-damage EMP on a connector remains immediate");

        grid = World(out connector);
        var docked = new MyCubeGrid(); MyEntities.Add(docked);
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        MyAPIGateway.GridGroups.LogicalGroups[grid] = new List<IMyCubeGrid> { grid, docked };
        grid.ConnectionsChanged();
        check(!CombatLockout.IsBlocked, "connector integration: docking two observed quiet grids does not itself block");
        MyAPIGateway.GridGroups.LogicalGroups.Remove(grid);
        grid.ConnectionsChanged();
        check(!CombatLockout.IsBlocked, "connector integration: quiet undocking does not itself block");
        var newlyObserved = new MyCubeGrid(); MyEntities.Add(newlyObserved);
        MyAPIGateway.GridGroups.LogicalGroups[grid] = new List<IMyCubeGrid> { grid, newlyObserved };
        grid.ConnectionsChanged();
        check(!CombatLockout.IsBlocked, "connector integration: docking a newly observed quiet grid creates no cooldown");
        MyAPIGateway.GridGroups.LogicalGroups.Remove(grid);
        grid.ConnectionsChanged();
        check(!CombatLockout.IsBlocked, "connector integration: quiet undocking remains ready");
        CombatLockout.Clear();
    }
}

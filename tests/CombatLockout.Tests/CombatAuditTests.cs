using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Lanyard;
using VRage.Game.ModAPI;

internal static class CombatAuditTests
{
    private sealed class KnownCoreHand : CoreHand, IMyGunBaseUser
    {
        // Use the existing fake API's known definition to exercise the fallback
        // after the provider has already removed its entity component.
        public VRage.Game.MyDefinitionId PhysicalItemId => new VRage.Game.MyDefinitionId("CoreGun");
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
        var grid = new MyCubeGrid();
        grid.BigOwners.Add(1);
        MyEntities.Add(grid);
        return grid;
    }

    private static void ResetCooldown()
    {
        ((CombatCooldown)typeof(CombatLockout).GetField("cooldown", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Clear();
    }

    private static void ObserveQuiet()
    {
        ResetCooldown();
        var grids = (IDictionary)typeof(CombatLockout).GetField("grids", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        foreach (var activity in grids.Values)
        {
            activity.GetType().GetProperty("Until").SetValue(activity, 0L);
        }
        MyAPIGateway.Utilities.Files.Clear();
    }

    public static void Run(Action<bool, string> check)
    {
        var grid = World();
        CombatLockout.Start();
        check(!CombatLockout.IsBlocked, "audit: observing unknown grids does not reveal nearby activity");
        check(CombatLockout.CanConnect(grid), "audit: newly observed quiet grid is immediately ready");
        ObserveQuiet();
        check(CombatLockout.CanConnect(grid), "audit: observed quiet grid becomes usable");

        grid = World(); CombatLockout.Start(); ObserveQuiet();
        grid.Damage(1);
        check(!CombatLockout.IsBlocked, "audit: unselected damage remains private");
        MyEntities.Remove(grid); MyEntities.Add(grid);
        check(!CombatLockout.CanConnect(grid), "audit: replication removal/readd cannot erase recent grid combat");

        grid = World(); CombatLockout.Start(); ObserveQuiet();
        grid.Damage(1); CombatLockout.Clear(); CombatLockout.Start();
        check(!CombatLockout.CanConnect(grid), "audit: reconnect before first selection cannot erase grid combat");

        grid = World(); CombatLockout.Start(); ObserveQuiet();
        grid.Damage(1);
        var fragment = new MyCubeGrid(); MyEntities.Add(fragment); grid.Split(fragment);
        check(!CombatLockout.CanConnect(fragment), "audit: newly split fragment cannot lose parent combat history");

        grid = World();
        var other = new MyCubeGrid(); MyEntities.Add(other);
        CombatLockout.Start(); ObserveQuiet();
        other.Damage(1); MyEntities.Remove(other); grid.Merge(other);
        check(!CombatLockout.CanConnect(grid), "audit: merge survivor cannot lose disposed source combat history");

        grid = World();
        var gun = new Gun(); MySession.Static.LocalCharacter.CurrentWeapon = gun;
        CombatLockout.Start(); ObserveQuiet();
        gun.GunBase.LastShootTime = DateTime.UtcNow;
        MySession.Static.LocalCharacter.CurrentWeapon = null;
        CombatLockout.Update();
        check(CombatLockout.IsBlocked, "audit: holstering a tracked gun cannot discard a single shot");

        grid = World(); CombatLockout.Start(); ObserveQuiet();
        gun = new Gun(); MySession.Static.LocalCharacter.CurrentWeapon = gun;
        gun.GunBase.LastShootTime = DateTime.UtcNow;
        MyEntities.RemoveEntity(gun); MySession.Static.LocalCharacter.CurrentWeapon = null;
        check(CombatLockout.IsBlocked, "audit: equip/fire/holster entirely between updates is caught on removal");
        ObserveQuiet();
        var remoteGun = new Gun(); remoteGun.GunBase.LastShootTime = DateTime.UtcNow;
        MyEntities.RemoveEntity(remoteGun);
        check(!CombatLockout.IsBlocked, "audit: unrelated handheld weapon removal does not signal combat");

        grid = World();
        var core = new FakeWeaponCore(); core.RespondOnRequest();
        CombatLockout.Start(); ObserveQuiet();
        var coreHand = new CoreHand(); core.Add(coreHand, 0);
        MySession.Static.LocalCharacter.CurrentWeapon = coreHand;
        core.State(coreHand, 0).Shooting = true;
        MyEntities.RemoveEntity(coreHand); MySession.Static.LocalCharacter.CurrentWeapon = null;
        check(CombatLockout.IsBlocked, "audit: actual WC firing state during removal starts combat cooldown");
        ObserveQuiet();
        var closedHand = new KnownCoreHand { Closed = true };
        MySession.Static.LocalCharacter.CurrentWeapon = closedHand;
        MyEntities.RemoveEntity(closedHand); MySession.Static.LocalCharacter.CurrentWeapon = null;
        check(!CombatLockout.IsBlocked, "audit: a closed known rifle without recent fire evidence creates no invented timeout");
        ObserveQuiet();
        var grinder = new Tool(); MySession.Static.LocalCharacter.CurrentWeapon = grinder;
        MyEntities.RemoveEntity(grinder); MySession.Static.LocalCharacter.CurrentWeapon = null;
        check(!CombatLockout.IsBlocked, "audit: own engineer tool removal does not trigger WC observation cooldown");

        grid = World(); core = new FakeWeaponCore(); core.RespondOnRequest();
        coreHand = new CoreHand(); core.Add(coreHand, 0);
        MySession.Static.LocalCharacter.CurrentWeapon = coreHand;
        CombatLockout.Start(); ObserveQuiet();
        MyEntities.RemoveEntity(coreHand); MySession.Static.LocalCharacter.CurrentWeapon = null;
        check(!CombatLockout.IsBlocked, "audit: holstering a fully observed quiet WC rifle adds no cooldown");

        grid = World(); gun = new Gun(); grid.Add(gun);
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        gun.GunBase.LastShootTime = DateTime.UtcNow; grid.Remove(gun);
        check(CombatLockout.IsBlocked, "audit: removing a just-fired ship gun preserves its final shot");

        grid = World(); other = new MyCubeGrid(); MyEntities.Add(other);
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        MyAPIGateway.GridGroups.LogicalGroups[grid] = new List<IMyCubeGrid> { grid, other };
        // Deliberately skip an intervening plugin update/connection callback.
        other.Damage(1);
        MyAPIGateway.GridGroups.LogicalGroups.Remove(grid);
        check(CombatLockout.IsBlocked, "audit: damage observes live docked membership between updates");

        grid = World(); other = new MyCubeGrid(); gun = new Gun(); other.Add(gun); MyEntities.Add(other);
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        MyAPIGateway.GridGroups.LogicalGroups[grid] = new List<IMyCubeGrid> { grid, other };
        grid.ConnectionsChanged();
        gun.GunBase.LastShootTime = DateTime.UtcNow;
        MyAPIGateway.GridGroups.LogicalGroups.Remove(grid);
        grid.ConnectionsChanged();
        check(CombatLockout.IsBlocked, "audit: dock/fire/undock between updates preserves weapon fire");

        ObserveQuiet(); CombatLockout.Update();
        other.Damage(1);
        check(!CombatLockout.IsBlocked, "audit: detached construct no longer promotes unrelated combat");
        CombatLockout.Clear();
        check(grid.Subscribers == 0 && other.Subscribers == 0 && MyEntities.Subscribers == 0,
            "audit: added merge/connection observers detach on teardown");
    }
}

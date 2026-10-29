using System;
using System.Collections;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Lanyard;
using VRage.Game.ModAPI;

internal sealed class ThrowingRemovalGun : CoreGun, IMyGunObject<MyGunBase>
{
    public bool StateUnavailable;
    public bool IsShooting => StateUnavailable ? throw new InvalidOperationException("gun already closed") : false;
    public MyGunBase GunBase { get; } = new MyGunBase();
}

internal static class RemovalCleanupTests
{
    public static void Run(Action<bool, string> check)
    {
        CombatLockout.Clear();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Multiplayer = new Multiplayer();
        MyAPIGateway.GridGroups = new GridGroups();
        MyAPIGateway.Session = new BoundsSession();
        MySession.Static = new MySession();
        CoreSystems.Session.I = new CoreSystems.Session();
        MyEntities.Entities.Clear();
        var grid = new MyCubeGrid();
        var gun = new ThrowingRemovalGun();
        grid.Add(gun);
        MyEntities.Add(grid);
        var provider = new FakeWeaponCore();
        provider.Add(gun, 0);
        provider.RespondOnRequest();
        CombatLockout.Start();
        check(provider.ObserverCount == 1, "removal cleanup: framework gun observer registered");
        gun.StateUnavailable = true;
        bool escaped = false;
        try { grid.Remove(gun); }
        catch { escaped = true; }
        check(!escaped, "removal cleanup: closed-gun getter exception does not escape game event");
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        check((bool)typeof(CombatLockout).GetField("failed", flags).GetValue(null) && !CombatLockout.CanConnect(grid),
            "removal cleanup: unavailable final shot state fails closed");
        var activities = (IDictionary)typeof(CombatLockout).GetField("grids", flags).GetValue(null);
        var activity = activities[grid];
        var native = (IDictionary)activity.GetType().GetField("weapons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(activity);
        var core = (IDictionary)activity.GetType().GetField("coreWeapons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(activity);
        check(native.Count == 0 && core.Count == 0 && provider.ObserverCount == 0,
            "removal cleanup: both gun caches and optional observers removed despite polling failure");
        CombatLockout.Clear();
        check(grid.Subscribers == 0 && MyEntities.Subscribers == 0 && MyAPIGateway.Utilities.Handlers.Count == 0,
            "removal cleanup: teardown detaches remaining observers after a failed final poll");
    }
}

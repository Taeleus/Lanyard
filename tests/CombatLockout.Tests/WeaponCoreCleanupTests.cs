using System;
using System.Reflection;
using Sandbox.ModAPI;
using Lanyard;
using VRage.Game.Entity;

internal static class WeaponCoreCleanupTests
{
    public static void Run(Action<bool, string> check)
    {
        MyAPIGateway.Utilities = new Utilities();
        var provider = new FakeWeaponCore();
        var gun = new CoreGun();
        var hand = new CoreHand();
        provider.Add(gun, 0);
        provider.Add(hand, 0);
        provider.RespondOnRequest();
        int activity = 0, hits = 0;
        var api = new WeaponCoreMonitor((target, amount) => hits++);
        api.Start(false);
        var weapon = api.Watch(gun, gun.BlockDefinition.Id, () => activity++);
        weapon.Poll();
        var handWeapon = api.Watch(hand, null, () => activity++);
        handWeapon.Poll();
        check(activity == 2 && provider.ObserverCount == 2, "WC cleanup: both observers initialized");
        provider.Hit(gun, 1);
        check(hits == 1, "WC cleanup: damage observer active before disposal");
        provider.ThrowCleanup = true;
        weapon.Dispose();
        provider.Event(gun, 0, 1, true);
        check(activity == 2, "WC cleanup: failed weapon unregistration leaves an inert callback");
        weapon.Poll();
        check(provider.ObserverCount == 2, "WC cleanup: disposed weapon cannot register again");
        var queuedMessage = MyAPIGateway.Utilities.Handlers[0];
        api.Dispose();
        provider.Event(hand, 0, 8, true);
        provider.Hit(hand, 1);
        check(activity == 2 && hits == 1, "WC cleanup: API disposal invalidates retained weapon and damage callbacks");
        check(MyAPIGateway.Utilities.Handlers.Count == 0, "WC cleanup: message observer detached despite provider failure");
        // Simulate a message queued before unregistration, delivered after teardown.
        MyAPIGateway.Utilities.Handlers.Add(queuedMessage);
        provider.Respond();
        MyAPIGateway.Utilities.Handlers.Remove(queuedMessage);
        check(!api.Ready, "WC cleanup: queued endpoint response cannot revive a disposed API");
        api.Dispose();
        handWeapon.Dispose();

        MyAPIGateway.Utilities = new Utilities();
        provider = new FakeWeaponCore();
        gun = new CoreGun();
        provider.Add(gun, 0);
        provider.RespondOnRequest();
        api = new WeaponCoreMonitor((target, amount) => { });
        api.Start(false);
        var registration = typeof(WeaponCoreMonitor).GetField("register", BindingFlags.NonPublic | BindingFlags.Instance);
        var original = (Action<MyEntity, int, Action<int, bool>>)registration.GetValue(api);
        bool throwOnce = true;
        registration.SetValue(api, new Action<MyEntity, int, Action<int, bool>>((entity, id, observer) =>
        {
            original(entity, id, observer);
            if (throwOnce) { throwOnce = false; throw new InvalidOperationException("registered before failure"); }
        }));
        weapon = api.Watch(gun, gun.BlockDefinition.Id, () => { });
        weapon.Poll();
        check(weapon.Uncertain && provider.ObserverCount == 1, "WC cleanup: partial registration remains uncertain and tracked");
        weapon.Poll();
        check(!weapon.Uncertain && provider.ObserverCount == 1, "WC cleanup: retry retires partial registration without accumulating callbacks");
        weapon.Dispose();
        check(provider.ObserverCount == 0, "WC cleanup: recovered registration is fully detached");
        api.Dispose();
    }
}

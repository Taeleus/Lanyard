using System;
using Lanyard;
using Sandbox.ModAPI;
using DamageBatch = VRage.Collections.ListReader<VRage.MyTuple<ulong, long, int, VRage.Game.Entity.MyEntity, VRage.Game.Entity.MyEntity, VRage.Collections.ListReader<VRage.MyTuple<VRageMath.Vector3D, object, float>>>>;

internal static class WeaponCoreRegistrationTests
{
    private const long IndependentObserverId = 578196204731;
    private const long ApiChannel = 67549756549;

    public static void Run(Action<bool, string> check)
    {
        check(WeaponCoreMonitor.Channel == ApiChannel, "WC registration: public API channel is unchanged");

        foreach (var monitorFirst in new[] { false, true })
            foreach (var removeMonitorFirst in new[] { false, true })
                Coexistence(check, monitorFirst, removeMonitorFirst);

        ProviderRegistrationSemantics(check);
    }

    private static void Coexistence(Action<bool, string> check, bool monitorFirst, bool removeMonitorFirst)
    {
        var utilities = new Utilities();
        MyAPIGateway.Utilities = utilities;
        var provider = new FakeWeaponCore();
        provider.RespondOnRequest();
        int lanyardHits = 0, independentHits = 0;
        Action<DamageBatch> independent = batch => independentHits++;
        var monitor = new WeaponCoreMonitor((target, amount) => lanyardHits++);
        var context = "WC registration: " + (monitorFirst ? "Lanyard first" : "independent observer first") +
            ", " + (removeMonitorFirst ? "Lanyard removed first" : "independent observer removed first") + ": ";

        if (!monitorFirst) provider.RegisterDamageObserver(IndependentObserverId, 1, independent);
        monitor.Start(false);
        if (monitorFirst) provider.RegisterDamageObserver(IndependentObserverId, 1, independent);
        check(monitor.Ready && provider.DamageObserverCount == 2, context + "both listeners register");
        provider.Hit(new object(), 1);
        check(lanyardHits == 1 && independentHits == 1, context + "both listeners receive damage");

        if (removeMonitorFirst)
        {
            monitor.Dispose();
            check(provider.DamageObserverCount == 1, context + "cleanup preserves independent observer");
            provider.Hit(new object(), 1);
            check(lanyardHits == 1 && independentHits == 2, context + "independent observer remains active");
            provider.RegisterDamageObserver(IndependentObserverId, 0, null);
        }
        else
        {
            provider.RegisterDamageObserver(IndependentObserverId, 0, null);
            check(provider.DamageObserverCount == 1, context + "independent cleanup preserves Lanyard");
            provider.Hit(new object(), 1);
            check(lanyardHits == 2 && independentHits == 1, context + "Lanyard remains active");
            monitor.Dispose();
        }
        check(provider.DamageObserverCount == 0 && utilities.Handlers.Count == 0, context + "final cleanup detaches every listener");
        check(utilities.RegisteredMessageChannel == ApiChannel && utilities.RequestedMessageChannel == ApiChannel &&
            utilities.UnregisteredMessageChannel == ApiChannel, context + "API discovery and cleanup keep the public channel");
        monitor.Dispose();
        check(provider.DamageObserverCount == 0, context + "repeated cleanup stays inert");
    }

    private static void ProviderRegistrationSemantics(Action<bool, string> check)
    {
        var provider = new FakeWeaponCore();
        int originalHits = 0, replacementHits = 0;
        Action<DamageBatch> original = batch => originalHits++;
        Action<DamageBatch> replacement = batch => replacementHits++;
        provider.RegisterDamageObserver(IndependentObserverId, 1, original);
        provider.RegisterDamageObserver(IndependentObserverId, 1, replacement);
        check(provider.DamageObserverCount == 1, "WC provider: repeated listener key keeps one registration");
        provider.Hit(new object(), 1);
        check(originalHits == 1 && replacementHits == 0, "WC provider: first callback survives repeated registration");
        provider.RegisterDamageObserver(IndependentObserverId, 0, replacement);
        provider.Hit(new object(), 1);
        check(provider.DamageObserverCount == 0 && originalHits == 1,
            "WC provider: event type zero removes by key regardless of callback");

    }
}

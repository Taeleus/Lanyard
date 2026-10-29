using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Utils;
using VRageMath;
using DamageBatch = VRage.Collections.ListReader<VRage.MyTuple<ulong, long, int, VRage.Game.Entity.MyEntity, VRage.Game.Entity.MyEntity, VRage.Collections.ListReader<VRage.MyTuple<VRageMath.Vector3D, object, float>>>>;

namespace Lanyard;

// Only observes firing/hits. No targeting, radar, threat, or projectile-lock
// queries are used to decide whether a link is available.
internal sealed class WeaponCoreMonitor : IDisposable
{
    internal const long Channel = 67549756549;
    private const long DamageRegistration = 578196204731;
    private readonly HashSet<MyDefinitionId> definitions = new HashSet<MyDefinitionId>();
    private readonly HashSet<Weapon> watched = new HashSet<Weapon>();
    private Func<MyEntity, bool> hasWeapon;
    private Func<IMyTerminalBlock, IDictionary<string, int>, bool> getMap;
    private Func<MyEntity, int, bool> shooting;
    private Func<MyEntity, int, int> shotsFired;
    private Action<MyEntity, int, Action<int, bool>> register;
    private Action<MyEntity, int, Action<int, bool>> unregister;
    private Action<long, int, Action<DamageBatch>> damageHandler;
    private Action<object, float> onDamage;
    private bool disposed;
    private bool subscribed;
    private Action detachMessages;
    private int requestTicks;
    public System.Reflection.Assembly ProviderAssembly { get; private set; }
    public bool Required { get; private set; }
    public bool Ready { get; private set; }
    public bool Failed { get; private set; }
    public string FailureReason { get; private set; }

    // Only actual static weapons, turrets and rifles require weapon parts.
    // Support/upgrade definitions also have cores, but never weapon maps.
    public bool IsCandidate(MyDefinitionId definition) => Ready && definitions.Contains(definition);

    public WeaponCoreMonitor(Action<object, float> onDamage) { this.onDamage = onDamage; }

    public void Start(bool expected)
    {
        if (disposed) throw new ObjectDisposedException(nameof(WeaponCoreMonitor));
        Required = expected;
        var utilities = MyAPIGateway.Utilities;
        utilities.RegisterMessageHandler(Channel, Receive);
        detachMessages = () => utilities.UnregisterMessageHandler(Channel, Receive);
        subscribed = true;
        Request();
    }

    public void Update()
    {
        if (!disposed && !Ready && !Failed && ++requestTicks >= 60) { requestTicks = 0; Request(); }
    }

    private void Request() { MyAPIGateway.Utilities.SendModMessage(Channel, "ApiEndpointRequest"); }

    private static T Bind<T>(IReadOnlyDictionary<string, Delegate> methods, string name) where T : class
    {
        if (!methods.TryGetValue(name, out var method) || !(method is T))
            throw new InvalidOperationException("Missing/incompatible WeaponCore observer API: " + name);
        return method as T;
    }

    private void Receive(object message)
    {
        if (disposed || Ready || Failed || !(message is IReadOnlyDictionary<string, Delegate> methods)) return;
        Required = true;
        try
        {
            var getStaticLaunchers = Bind<Action<ICollection<MyDefinitionId>>>(methods, "GetCoreStaticLaunchers");
            var getTurrets = Bind<Action<ICollection<MyDefinitionId>>>(methods, "GetCoreTurrets");
            var getRifles = Bind<Action<ICollection<MyDefinitionId>>>(methods, "GetCoreRifles");
            hasWeapon = Bind<Func<MyEntity, bool>>(methods, "HasCoreWeaponBase");
            getMap = Bind<Func<IMyTerminalBlock, IDictionary<string, int>, bool>>(methods, "GetBlockWeaponMap");
            shooting = Bind<Func<MyEntity, int, bool>>(methods, "IsWeaponShootingBase");
            shotsFired = Bind<Func<MyEntity, int, int>>(methods, "GetShotsFiredBase");
            register = Bind<Action<MyEntity, int, Action<int, bool>>>(methods, "RegisterEventMonitor");
            unregister = Bind<Action<MyEntity, int, Action<int, bool>>>(methods, "UnRegisterEventMonitor");
            damageHandler = Bind<Action<long, int, Action<DamageBatch>>>(methods, "DamageHandler");
            ProviderAssembly = getStaticLaunchers.Method.DeclaringType?.Assembly;
            getStaticLaunchers(definitions);
            getTurrets(definitions);
            getRifles(definitions);
            damageHandler(DamageRegistration, 1, ReceiveDamage);
            Ready = true;
        }
        catch (Exception exception)
        {
            Failed = true;
            FailureReason = "WeaponCore observer API: " + exception.Message;
            MyLog.Default.WriteLineAndConsole("Lanyard WeaponCore monitoring unavailable: " + exception);
        }
    }

    private void ReceiveDamage(DamageBatch projectiles)
    {
        if (disposed || Failed) return;
        try
        {
            foreach (var projectile in projectiles)
                foreach (var hit in projectile.Item6)
                    if (hit.Item3 > 0) onDamage?.Invoke(hit.Item2, hit.Item3);
        }
        catch (Exception exception)
        {
            Failed = true;
            FailureReason = "WeaponCore hit observation: " + exception.Message;
            MyLog.Default.WriteLineAndConsole("Lanyard WeaponCore hit observation failed: " + exception);
        }
    }

    public Weapon Watch(MyEntity entity, MyDefinitionId? definition, Action activity)
    {
        return Watch(entity, definition, reason => activity());
    }

    public Weapon Watch(MyEntity entity, MyDefinitionId? definition, Action<string> activity)
    {
        if (disposed) throw new ObjectDisposedException(nameof(WeaponCoreMonitor));
        var weapon = new Weapon(this, entity, definition, activity);
        watched.Add(weapon);
        return weapon;
    }

    public void ObserveRemovedHandheld(MyEntity entity, MyDefinitionId? definition, Action activity)
    {
        ObserveRemovedHandheld(entity, definition, reason => activity());
    }

    public void ObserveRemovedHandheld(MyEntity entity, MyDefinitionId? definition, Action<string> activity)
    {
        if (disposed || !Ready) return;
        // Physical rifle definitions survive teardown of the core component.
        // The provider query also covers custom handheld entities. Ordinary
        // engineer tools satisfy neither check and receive no lockout.
        if ((definition.HasValue && definitions.Contains(definition.Value)) || hasWeapon(entity))
            activity("WeaponCore handheld observation after holster");
    }

    public void Dispose()
    {
        if (disposed) return;
        // Invalidate callbacks before calling a provider that may already be gone.
        disposed = true;
        onDamage = null;
        foreach (var weapon in new List<Weapon>(watched)) weapon.Dispose();
        // WeaponCore can unload first; cleanup must still detach our message handler.
        try { damageHandler?.Invoke(DamageRegistration, 0, ReceiveDamage); }
        catch (Exception exception) { MyLog.Default.WriteLineAndConsole("Lanyard WeaponCore cleanup: " + exception.Message); }
        finally
        {
            if (subscribed)
            {
                try { detachMessages?.Invoke(); }
                catch (Exception exception) { MyLog.Default.WriteLineAndConsole("Lanyard WeaponCore message cleanup: " + exception.Message); }
            }
            subscribed = false;
            Ready = false;
            detachMessages = null;
            hasWeapon = null;
            getMap = null;
            shooting = null;
            shotsFired = null;
            register = null;
            unregister = null;
            damageHandler = null;
            ProviderAssembly = null;
            definitions.Clear();
        }
    }

    internal sealed class Weapon : IDisposable
    {
        private readonly WeaponCoreMonitor api;
        private readonly MyEntity entity;
        private readonly MyDefinitionId? definition;
        private readonly Action<string> activity;
        private readonly Dictionary<int, Part> parts = new Dictionary<int, Part>();
        private readonly Dictionary<string, int> map = new Dictionary<string, int>();
        private bool identified;
        private bool disposed;
        private bool logged;
        public bool Uncertain { get; private set; }
        public string UnavailableReason { get; private set; }
        public bool IsObserved => identified && !Uncertain && !disposed && !api.disposed &&
            api.Ready && !api.Failed && !entity.Closed && !entity.MarkedForClose;

        public Weapon(WeaponCoreMonitor api, MyEntity entity, MyDefinitionId? definition, Action<string> activity)
        {
            this.api = api; this.entity = entity;
            this.definition = definition ?? (entity as IMyGunBaseUser)?.PhysicalItemId;
            this.activity = activity;
        }

        public void Poll()
        {
            if (disposed || api.disposed || !api.Ready || entity.Closed || entity.MarkedForClose) return;
            try
            {
                if (!identified)
                {
                    var candidate = definition.HasValue && api.definitions.Contains(definition.Value);
                    if (!api.hasWeapon(entity))
                    {
                        Uncertain = candidate;
                        UnavailableReason = candidate ? "WeaponCore component not initialized" : null;
                        return;
                    }
                    Uncertain = true;
                    UnavailableReason = "WeaponCore weapon parts not initialized";
                    // Reuse the scratch map while a component/part is loading.
                    // Empty maps must remain retryable; they are not proof that
                    // a candidate can never become an initialized weapon.
                    map.Clear();
                    if (entity is IMyTerminalBlock terminal)
                    {
                        if (!api.getMap(terminal, map) || map.Count == 0)
                        {
                            // HasCoreWeapon can also report support/upgrade core
                            // components. Only actual weapon definitions should
                            // deny linking while their parts are uninitialized.
                            Uncertain = candidate;
                            UnavailableReason = candidate ? "WeaponCore weapon map unavailable or empty" : null;
                            return;
                        }
                    }
                    else map.Add("Handheld", 0);

                    foreach (var id in new HashSet<int>(map.Values))
                    {
                        if (parts.TryGetValue(id, out var previous))
                        {
                            if (previous.Registered) continue;
                            // An external registration may retain the callback and
                            // then throw. Retire that attempt before retrying it.
                            previous.Deactivate();
                            api.unregister(entity, id, previous.OnEvent);
                            parts.Remove(id);
                        }
                        var count = api.shotsFired(entity, id);
                        if (count < 0)
                        {
                            UnavailableReason = $"WeaponCore shot counter unavailable (part {id})";
                            return;
                        }
                        // Probe initialization before registering: some core entities
                        // exist before their individual weapon parts are ready.
                        api.shooting(entity, id);
                        var part = new Part(activity, count, id);
                        parts.Add(id, part);
                        api.register(entity, id, part.OnEvent);
                        part.Registered = true;
                    }
                    identified = true;
                    // WC has no reliable last-shot timestamp before observation.
                    // Start a quiet observation cooldown instead of assuming peace.
                    activity("WeaponCore weapon observation started");
                }
                foreach (var entry in parts)
                {
                    var count = api.shotsFired(entity, entry.Key);
                    if (count < 0)
                    {
                        Uncertain = true;
                        UnavailableReason = $"WeaponCore shot counter unavailable (part {entry.Key})";
                        return;
                    }
                    if (api.shooting(entity, entry.Key))
                        activity($"WeaponCore firing state (part {entry.Key})");
                    else if (count != entry.Value.Shots)
                        activity($"WeaponCore shot counter changed (part {entry.Key}: {entry.Value.Shots}->{count})");
                    entry.Value.Shots = count;
                }
                Uncertain = false;
                UnavailableReason = null;
            }
            catch (Exception exception)
            {
                Uncertain = true;
                UnavailableReason = "WeaponCore weapon observer: " + exception.Message;
                if (!logged) MyLog.Default.WriteLineAndConsole("Lanyard waiting for WeaponCore weapon observation: " + exception.Message);
                logged = true;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            api.watched.Remove(this);
            foreach (var part in parts)
            {
                // Even a retained provider delegate must become inert and stop
                // retaining the grid/player callback if unregistration fails.
                part.Value.Deactivate();
                try { api.unregister?.Invoke(entity, part.Key, part.Value.OnEvent); }
                catch (Exception exception) { MyLog.Default.WriteLineAndConsole("Lanyard WeaponCore weapon cleanup: " + exception.Message); }
            }
            parts.Clear();
        }

        private sealed class Part
        {
            private Action<string> activity;
            private readonly int id;
            public int Shots;
            public bool Registered;
            public Part(Action<string> activity, int shots, int id) { this.activity = activity; Shots = shots; this.id = id; }
            public void Deactivate() { activity = null; }
            public void OnEvent(int kind, bool active)
            {
                // WC EventTriggers: Firing=1, PreFire=8 (e.g. railgun charging).
                if (active && kind == 1) activity?.Invoke($"WeaponCore firing event (part {id})");
                else if (active && kind == 8) activity?.Invoke($"WeaponCore prefire/charge event (part {id})");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;
using DamageBatch = VRage.Collections.ListReader<VRage.MyTuple<ulong, long, int, VRage.Game.Entity.MyEntity, VRage.Game.Entity.MyEntity, VRage.Collections.ListReader<VRage.MyTuple<VRageMath.Vector3D, object, float>>>>;

internal class CoreGun : MyCubeBlock, IMyTerminalBlock
{
    public CoreGun() { BlockDefinition.Id = new MyDefinitionId("CoreGun"); }
}
internal class CoreHand : MyEntity, IMyGunObject<MyDeviceBase>
{
    public bool IsShooting => false;
    public MyDeviceBase GunBase => null;
}
internal sealed class CoreRifle : CoreHand, IMyGunBaseUser
{
    public MyDefinitionId PhysicalItemId { get; set; } = new MyDefinitionId("CoreRifle");
}
internal sealed class FakeWeaponCore
{
    internal class Part
    {
        public bool Shooting;
        public int Shots;
        public List<Action<int, bool>> Observers = new List<Action<int, bool>>();
    }
    private readonly Dictionary<MyEntity, Dictionary<int, Part>> weapons = new Dictionary<MyEntity, Dictionary<int, Part>>();
    private readonly Dictionary<long, Action<DamageBatch>> damageObservers = new Dictionary<long, Action<DamageBatch>>();
    public bool ThrowPolling;
    public bool ThrowCleanup;
    public int HasCalls, MapCalls, ShotCalls;
    public bool MapUnavailable;
    public string MissingMethod;
    public string IncompatibleMethod;
    public readonly HashSet<MyDefinitionId> StaticDefinitions = new HashSet<MyDefinitionId> { new MyDefinitionId("CoreGun") };
    public readonly HashSet<MyDefinitionId> TurretDefinitions = new HashSet<MyDefinitionId>();
    public readonly HashSet<MyDefinitionId> RifleDefinitions = new HashSet<MyDefinitionId>();
    public readonly HashSet<MyDefinitionId> ExtraDefinitions = new HashSet<MyDefinitionId>();
    public readonly HashSet<object> MapInstances = new HashSet<object>();
    public readonly HashSet<MyEntity> SupportEntities = new HashSet<MyEntity>();
    public int ObserverCount => weapons.Values.Sum(parts => parts.Values.Sum(p => p.Observers.Count));
    public bool DamageRegistered => damageObservers.Count != 0;
    public int DamageObserverCount => damageObservers.Count;
    public void RegisterDamageObserver(long id, int eventType, Action<DamageBatch> observer)
    {
        if (ThrowCleanup) throw new InvalidOperationException("WC already unloaded");
        if (eventType == 0) damageObservers.Remove(id);
        else if (!damageObservers.ContainsKey(id)) damageObservers.Add(id, observer);
    }
    public void Add(MyEntity entity, params int[] ids) { weapons[entity] = ids.ToDictionary(id => id, id => new Part()); }
    public Part State(MyEntity entity, int id) { return weapons[entity][id]; }
    public void Event(MyEntity entity, int id, int kind, bool active)
    {
        foreach (var callback in weapons[entity][id].Observers.ToArray()) callback(kind, active);
    }
    public void RespondOnRequest() { MyAPIGateway.Utilities.OnApiRequest = Respond; }
    public void Respond()
    {
        var methods = new Dictionary<string, Delegate>
        {
            ["GetCoreWeapons"] = new Action<ICollection<MyDefinitionId>>(defs =>
            {
                foreach (var definition in StaticDefinitions) defs.Add(definition);
                foreach (var definition in TurretDefinitions) defs.Add(definition);
                foreach (var definition in RifleDefinitions) defs.Add(definition);
                foreach (var definition in ExtraDefinitions) defs.Add(definition);
            }),
            ["GetCoreStaticLaunchers"] = new Action<ICollection<MyDefinitionId>>(defs =>
            {
                foreach (var definition in StaticDefinitions) defs.Add(definition);
            }),
            ["GetCoreTurrets"] = new Action<ICollection<MyDefinitionId>>(defs =>
            {
                foreach (var definition in TurretDefinitions) defs.Add(definition);
            }),
            ["GetCoreRifles"] = new Action<ICollection<MyDefinitionId>>(defs =>
            {
                foreach (var definition in RifleDefinitions) defs.Add(definition);
            }),
            ["HasCoreWeaponBase"] = new Func<MyEntity, bool>(entity => { HasCalls++; return weapons.ContainsKey(entity); }),
            ["GetBlockWeaponMap"] = new Func<IMyTerminalBlock, IDictionary<string, int>, bool>((entity, map) =>
            {
                MapCalls++;
                MapInstances.Add(map);
                if (MapUnavailable) return false;
                if (SupportEntities.Contains((MyEntity)entity) || !weapons.TryGetValue((MyEntity)entity, out var parts)) return false;
                foreach (var id in parts.Keys) map.Add("Part" + id, id);
                return true;
            }),
            ["IsWeaponShootingBase"] = new Func<MyEntity, int, bool>((entity, id) =>
            {
                if (ThrowPolling) throw new InvalidOperationException("API temporarily unavailable");
                return State(entity, id).Shooting;
            }),
            ["GetShotsFiredBase"] = new Func<MyEntity, int, int>((entity, id) => { ShotCalls++; return State(entity, id).Shots; }),
            ["RegisterEventMonitor"] = new Action<MyEntity, int, Action<int, bool>>((entity, id, callback) => State(entity, id).Observers.Add(callback)),
            ["UnRegisterEventMonitor"] = new Action<MyEntity, int, Action<int, bool>>((entity, id, callback) =>
            {
                if (ThrowCleanup) throw new InvalidOperationException("WC already unloaded");
                State(entity, id).Observers.Remove(callback);
            }),
            ["DamageHandler"] = new Action<long, int, Action<DamageBatch>>(RegisterDamageObserver)
        };
        if (MissingMethod != null) methods.Remove(MissingMethod);
        if (IncompatibleMethod != null) methods[IncompatibleMethod] = new Action(() => { });
        MyAPIGateway.Utilities.Broadcast(methods);
    }
    public void Hit(object target, float amount)
    {
        var hits = new List<MyTuple<Vector3D, object, float>> { new MyTuple<Vector3D, object, float>(default, target, amount) };
        var batch = new List<MyTuple<ulong, long, int, MyEntity, MyEntity, ListReader<MyTuple<Vector3D, object, float>>>>
        {
            new MyTuple<ulong, long, int, MyEntity, MyEntity, ListReader<MyTuple<Vector3D, object, float>>>(1, 1, 0, null, null, new ListReader<MyTuple<Vector3D, object, float>>(hits))
        };
        foreach (var observer in damageObservers.Values.ToArray()) observer(new DamageBatch(batch));
    }
}

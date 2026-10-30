using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

// Offline boundary doubles only. The linked production LinkController decides
// eligibility, timing, item categories, access and destination selection.
namespace Sandbox.Game
{
    public static class MyVisualScriptLogicProvider
    {
        public static bool ThrowHighlightOnce;
        public static Action Highlighted;
        public static void SetHighlightLocal(string name, int thickness, int pulse, VRageMath.Color color, long player, object ignored)
        {
            if (ThrowHighlightOnce) { ThrowHighlightOnce = false; throw new InvalidOperationException("Renderer is not ready."); }
            if (thickness == 2) Highlighted?.Invoke();
        }
    }
}
namespace Sandbox.Game.Entities.Inventory
{
    public static class MyInventoryBaseExtensions
    {
        public static Sandbox.Game.Entities.MyInventory GetInventory(this MyEntity entity, MyStringHash id) =>
            (entity as MyCharacter)?.GetInventory();
    }
}

namespace VRageMath
{
    public struct Vector3D
    {
        public double X, Y, Z;
        public Vector3D(double x, double y = 0, double z = 0) { X = x; Y = y; Z = z; }
    }
    public struct Color { public static readonly Color White = new Color(); public static readonly Color ForestGreen = new Color(); }
}

namespace VRage.Utils
{
    public readonly struct MyStringHash
    {
        public readonly string Value;
        private MyStringHash(string value) { Value = value; }
        public static MyStringHash GetOrCompute(string value) => new MyStringHash(value);
    }

    public sealed class MyLog
    {
        public static readonly MyLog Default = new MyLog();
        public readonly List<string> Messages = new List<string>();
        public void WriteLineAndConsole(string message) { Messages.Add(message); }
    }
}

namespace VRage.Game
{
    public enum MyItemFlags { None }
    public enum MyOwnershipShareModeEnum { None, Faction, All }

    public readonly struct MyObjectBuilderType
    {
        private readonly string name;
        public MyObjectBuilderType(string name) { this.name = name; }
        public override string ToString() => name;
    }

    public readonly struct MyDefinitionId : IEquatable<MyDefinitionId>
    {
        public readonly MyObjectBuilderType TypeId;
        public readonly string SubtypeName;
        public MyDefinitionId(string type, string subtype) { TypeId = new MyObjectBuilderType(type); SubtypeName = subtype; }
        public bool Equals(MyDefinitionId other) => TypeId.ToString() == other.TypeId.ToString() && SubtypeName == other.SubtypeName;
        public override bool Equals(object value) => value is MyDefinitionId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(TypeId.ToString(), SubtypeName);
        public override string ToString() => TypeId + "/" + SubtypeName;
    }

    public sealed class PhysicalContent
    {
        private readonly MyDefinitionId id;
        public PhysicalContent(MyDefinitionId id) { this.id = id; }
        public MyDefinitionId GetObjectId() => id;
    }

    public readonly struct MyPhysicalInventoryItem
    {
        public readonly PhysicalContent Content;
        public readonly double Amount;
        public MyPhysicalInventoryItem(MyDefinitionId id, double amount) { Content = new PhysicalContent(id); Amount = amount; }
    }
}

namespace VRage.Game.Entity
{
    public sealed class PositionComponent
    {
        public Vector3D Position;
        public Vector3D GetPosition() => Position;
    }

    public class MyEntity
    {
        private static long nextId;
        public long EntityId = ++nextId;
        public string Name { get; set; }
        public bool Closed, MarkedForClose;
        public readonly PositionComponent PositionComp = new PositionComponent();
        public readonly ComponentContainer Components = new ComponentContainer();
    }
    public sealed class ComponentContainer
    {
        private readonly Dictionary<Type, object> values = new Dictionary<Type, object>();
        public T Get<T>() where T : class => values.TryGetValue(typeof(T), out var value) ? value as T : null;
        public void Set<T>(T value) where T : class { if (value == null) values.Remove(typeof(T)); else values[typeof(T)] = value; }
    }
}

namespace VRage.Game.ModAPI
{
    public interface IMyTerminalBlock
    {
        bool Closed { get; }
        bool MarkedForClose { get; }
        bool HasInventory { get; }
        long OwnerId { get; }
        long EntityId { get; }
        string Name { get; set; }
        ComponentContainer Components { get; }
        string CustomName { get; }
        MyCubeGrid CubeGrid { get; }
        bool HasPlayerAccess(long identity);
        MyInventory GetInventory(int index = 0);
    }
}

namespace Sandbox.Game.Entities
{
    public sealed class TransferRequest
    {
        public MyInventory Source, Destination;
        public MyDefinitionId Item;
        public double Amount;
        public bool Spawn;
    }

    public sealed class MyInventory
    {
        public static readonly List<TransferRequest> Transfers = new List<TransferRequest>();
        public static Action<TransferRequest> AfterTransfer;
        public static bool DeferTransfers;
        public readonly List<MyPhysicalInventoryItem> Items = new List<MyPhysicalInventoryItem>();
        public MyEntity Owner;
        public bool AcceptsItem = true;
        public double Capacity = double.MaxValue;
        public Action CapacityCheck;
        public bool ThrowAmount;
        public Action AmountRead;
        public int Reads, AmountReads, ConstraintChecks, CapacityChecks;
        public IEnumerable<MyPhysicalInventoryItem> GetItems() { Reads++; return Items; }
        public void Add(string type, string subtype, double amount = 1) => Items.Add(new MyPhysicalInventoryItem(new MyDefinitionId(type, subtype), amount));
        public double GetItemAmount(MyDefinitionId item)
        {
            AmountReads++;
            if (ThrowAmount) throw new InvalidOperationException("Inventory snapshot unavailable.");
            var amount = Items.FindAll(value => value.Content.GetObjectId().Equals(item)).ConvertAll(value => value.Amount).Sum();
            AmountRead?.Invoke();
            return amount;
        }
        public bool CheckConstraint(MyDefinitionId item) { ConstraintChecks++; return AcceptsItem; }
        public double ComputeAmountThatFits(MyDefinitionId item) { CapacityChecks++; CapacityCheck?.Invoke(); return Capacity; }

        public static void TransferByPlanner(MyInventory source, MyInventory destination, MyDefinitionId item, MyItemFlags flags, double amount, bool spawn = false)
        {
            var request = new TransferRequest { Source = source, Destination = destination, Item = item, Amount = amount, Spawn = spawn };
            Transfers.Add(request);
            // Hosting may mutate the enumerated inventory immediately.
            if (!DeferTransfers)
            {
                var remainder = source.GetItemAmount(item) - amount;
                source.Items.RemoveAll(value => value.Content.GetObjectId().Equals(item));
                if (remainder > 0) source.Items.Add(new MyPhysicalInventoryItem(item, remainder));
                destination.Items.Add(new MyPhysicalInventoryItem(item, amount));
            }
            AfterTransfer?.Invoke(request);
        }

        public static void Reset() { Transfers.Clear(); AfterTransfer = null; DeferTransfers = false; }
    }

    public sealed class MyIDModule
    {
        public MyOwnershipShareModeEnum ShareMode;
    }

    public class MyCubeBlock : MyEntity, IMyTerminalBlock
    {
        public MyCubeGrid CubeGrid { get; set; }
        public bool HasInventory { get; set; } = true;
        public long OwnerId { get; set; } = 1;
        public string CustomName { get; set; } = "Selected container";
        public bool PlayerAccess = true;
        public int AccessChecks, InventoryLookups;
        public Action AccessCheck;
        public readonly MyIDModule IDModule = new MyIDModule();
        public Sandbox.Definitions.MyCubeBlockDefinition BlockDefinition = new Sandbox.Definitions.MyCubeBlockDefinition
            { Id = new MyDefinitionId("MyObjectBuilder_CargoContainer", "Cargo") };
        public readonly List<MyInventory> Inventories = new List<MyInventory> { new MyInventory() };
        bool IMyTerminalBlock.Closed => Closed;
        bool IMyTerminalBlock.MarkedForClose => MarkedForClose;
        long IMyTerminalBlock.EntityId => EntityId;
        ComponentContainer IMyTerminalBlock.Components => Components;
        public bool HasPlayerAccess(long identity) { AccessChecks++; AccessCheck?.Invoke(); return PlayerAccess; }
        public MyInventory GetInventory(int index = 0) { InventoryLookups++; return Inventories[index]; }
    }

    public sealed class MyCubeGrid : MyEntity
    {
        public readonly List<MyCubeBlock> Blocks = new List<MyCubeBlock>();
    }
    public static class MyEntities
    {
        public static readonly Dictionary<long, MyEntity> Entities = new Dictionary<long, MyEntity>();
        public static bool TryGetEntityById(long id, out MyEntity entity) => Entities.TryGetValue(id, out entity);
        public static void SetEntityName(MyEntity entity) { }
    }
}

namespace Sandbox.Game.Weapons
{
    public class MyAngleGrinder : MyEntity { public bool IsShooting; }
    public class MyWelder : MyEntity { }
    public sealed class MyHandDrill : MyEntity { public bool IsShooting; }
}

namespace Sandbox.Game.Entities.Character
{
    public sealed class HandItemDefinition { public MyDefinitionId Id; }
    public sealed class MyCharacter : MyEntity
    {
        public MyEntity EquippedTool;
        public HandItemDefinition HandItemDefinition;
        public bool IsDead;
        public readonly List<Sandbox.Game.World.MyIdentity.BuildPlanItem> BuildPlanner = new List<Sandbox.Game.World.MyIdentity.BuildPlanItem>();
        public readonly MyInventory Inventory = new MyInventory();
        public int InventoryLookups;
        public MyInventory GetInventory(int index = 0) { InventoryLookups++; return Inventory; }
    }
}

namespace Sandbox.Game.World
{
    public sealed class MyIdentity
    {
        public long IdentityId = 1;
        public sealed class BuildPlanItem
        {
            public sealed class Component { public Sandbox.Definitions.MyComponentDefinition ComponentDefinition; public int Count; }
            public List<Component> Components = new List<Component>();
        }
    }
    public sealed class MyPlayerId { public ulong SteamId = 42; }
    public sealed class MyPlayer { public readonly MyIdentity Identity = new MyIdentity(); public readonly MyPlayerId Id = new MyPlayerId(); }
    public sealed class MySession
    {
        public static event Action OnUnloading;
        public static int UnloadingSubscribers => OnUnloading?.GetInvocationList().Length ?? 0;
        public static void RaiseUnloading() { if (Static != null) Static.IsUnloading = true; OnUnloading?.Invoke(); }
        public static MySession Static;
        public MyCharacter LocalCharacter;
        public MyPlayer LocalHumanPlayer = new MyPlayer();
        public Guid WorldId = Guid.NewGuid();
        public bool IsUnloading;
        public long LocalPlayerId => LocalHumanPlayer?.Identity.IdentityId ?? 0;
        public bool CreativeMode;
        public bool CreativeTools;
        public bool CreativeToolsEnabled(ulong id) => CreativeTools;
    }
}

namespace Sandbox.ModAPI
{
    public sealed class Faction { public long FactionId; }
    public sealed class FactionCollection
    {
        public readonly Dictionary<long, Faction> ByPlayer = new Dictionary<long, Faction>();
        public Faction TryGetPlayerFaction(long identity) => ByPlayer.TryGetValue(identity, out var faction) ? faction : null;
    }
    public sealed class GatewaySession { public readonly FactionCollection Factions = new FactionCollection(); }
    public sealed class Utilities
    {
        public readonly List<string> Messages = new List<string>();
        public bool ThrowNotification;
        public void ShowNotification(string message, int duration, string font) { if (ThrowNotification) throw new InvalidOperationException("HUD unavailable."); Messages.Add(message); }
    }
    public static class MyAPIGateway
    {
        public static GatewaySession Session;
        public static Utilities Utilities;
        public static Multiplayer Multiplayer = new Multiplayer();
    }
    public sealed class Multiplayer { public ulong ServerId = 1; }
}

namespace Lanyard
{
    public sealed class InventoryPort : VRage.Game.Entity.UseObject.IMyUseObject
    {
        public MyEntity Owner { get; set; }
        public VRageRender.Import.MyModelDummy Dummy { get; set; } = new VRageRender.Import.MyModelDummy { Name = "Inventory" };
        public VRage.Game.Entity.UseObject.UseActionEnum PrimaryAction { get; set; } = VRage.Game.Entity.UseObject.UseActionEnum.OpenInventory;
    }

    internal static class BoundsPreview
    {
        public static int Connections, Disconnections;
        public static Action Disconnected;
        public static void LinkConnected() { Connections++; }
        public static void LinkDisconnected() { Disconnections++; Disconnected?.Invoke(); }
        public static void Clear() { }
        public static void Draw() { }
        public static void Toggle() { }
        public static void ToggleLinked() { }
        public static void Update() { }
    }

    internal static class CombatLockout
    {
        public static bool IsBlocked;
        public static int RemainingSeconds;
        public static int Updates, Reports, EnemyOverlapObservations;
        public static MyCubeGrid FollowedGrid;
        public static Action<MyCubeGrid> Following;
        public static Action<MyCubeGrid> Connecting;
        public static Action Updating;
        public static int Starts, Clears;
        public static CombatResumeState StartedResume;
        public static CombatResumeState ResumeToCapture = new CombatResumeState();
        public static void Start(CombatResumeState resume = null) { Starts++; StartedResume = resume; }
        public static void Clear() { Clears++; }
        public static CombatResumeState CaptureResumeState() => ResumeToCapture;
        public static bool CanConnect(MyCubeGrid grid) { FollowGrid(grid); Connecting?.Invoke(grid); return !IsBlocked; }
        public static void FollowGrid(MyCubeGrid grid) { FollowedGrid = grid; Following?.Invoke(grid); }
        public static void Update() { Updates++; Updating?.Invoke(); }
        public static void ReportBlocked() { Reports++; }
        public static void ObserveEnemyOverlap() { EnemyOverlapObservations++; IsBlocked = true; }
    }

    internal static class LinkArea
    {
        public const double BreakDistance = 100;
        public static double Outside;
        public static bool NoEnemies = true;
        public static bool EnemyScanFails;
        public static int BoundsChecks, EnemyChecks, CacheClears;
        public static double DistanceOutside(MyCubeGrid grid, Vector3D position) { BoundsChecks++; return Outside; }
        public static bool CheckEnemies(MyCubeGrid grid, out string reason) => CheckEnemies(grid, out reason, out _);
        public static bool CheckEnemies(MyCubeGrid grid, out string reason, out bool hostileOverlap)
        {
            EnemyChecks++;
            hostileOverlap = !EnemyScanFails && !NoEnemies;
            reason = EnemyScanFails ? "Enemy scan unavailable." : "Enemy nearby.";
            return !EnemyScanFails && NoEnemies;
        }
        public static void ClearCache() { CacheClears++; }
    }

    internal static class LanyardDiagnostics
    {
        public static readonly List<string> Messages = new List<string>();
        public static Action<string> Showing;
        public static void Show(string message) { Showing?.Invoke(message); Messages.Add(message); }
    }

    internal static class Reflections { public static MethodInfo Withdraw; public static MethodInfo PlacementSources; public static void Initialize() { } }
}

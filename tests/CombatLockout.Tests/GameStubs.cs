// Test doubles only. The production project is separately built against the
// installed game assemblies to check its real API compatibility.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities.Cube;

namespace VRageMath
{
    public struct Vector3D
    {
        public double X, Y, Z;
        public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    }
    public struct BoundingBoxD
    {
        public Vector3D Min, Max;
        public BoundingBoxD(Vector3D min, Vector3D max) { Min = min; Max = max; }
        public double Distance(Vector3D point)
        {
            double x = Math.Max(Min.X - point.X, Math.Max(0, point.X - Max.X));
            double y = Math.Max(Min.Y - point.Y, Math.Max(0, point.Y - Max.Y));
            double z = Math.Max(Min.Z - point.Z, Math.Max(0, point.Z - Max.Z));
            return Math.Sqrt(x * x + y * y + z * z);
        }
    }
}
namespace VRage
{
    public class Ref<T> { public T Value; public Ref() { } public Ref(T value) { Value = value; } }
    public struct MyTuple<A, B, C>
    {
        public A Item1; public B Item2; public C Item3;
        public MyTuple(A a, B b, C c) { Item1 = a; Item2 = b; Item3 = c; }
    }
    public struct MyTuple<A, B, C, D, E, F>
    {
        public A Item1; public B Item2; public C Item3; public D Item4; public E Item5; public F Item6;
        public MyTuple(A a, B b, C c, D d, E e, F f) { Item1 = a; Item2 = b; Item3 = c; Item4 = d; Item5 = e; Item6 = f; }
    }
    public struct MyTuple<A, B, C, D, E>
    {
        public A Item1; public B Item2; public C Item3; public D Item4; public E Item5;
    }
}
namespace VRage.Collections
{
    public struct ListReader<T>
    {
        private List<T> list;
        public ListReader(List<T> list) { this.list = list; }
        public List<T>.Enumerator GetEnumerator() { return list.GetEnumerator(); }
    }
}
namespace VRage.Game
{
    public struct MyDefinitionId
    {
        public string Value;
        public MyDefinitionId(string value) { Value = value; }
    }
    public class MyObjectBuilder_Checkpoint
    {
        public struct ModItem { public ulong PublishedFileId; }
    }
}
namespace VRage.Game.ModAPI
{
    public interface IMyCubeGrid { }
    public interface IMySlimBlock { }
    public struct MyHitInfo { }
    public enum GridLinkTypeEnum { Mechanical, Logical }
    public class MyDeviceBase { }
    public interface IMyGunObject<out T> where T : MyDeviceBase
    {
        bool IsShooting { get; }
        T GunBase { get; }
    }
}
namespace VRage.Game.Entity
{
    public class PositionComponent
    {
        public VRageMath.BoundingBoxD WorldAABB = new VRageMath.BoundingBoxD(new VRageMath.Vector3D(-0.5, -0.5, -0.5), new VRageMath.Vector3D(0.5, 0.5, 0.5));
    }
    public class MyEntity : VRage.ModAPI.IMyEntity
    {
        private static long nextId;
        public string DisplayName;
        public long EntityId = ++nextId;
        public PositionComponent PositionComp = new PositionComponent();
        public bool Closed;
        public bool MarkedForClose;
    }
}
namespace VRage.ModAPI { public interface IMyEntity { } }
namespace VRage.Utils
{
    public class MyLog
    {
        public static MyLog Default = new MyLog();
        public int Errors;
        public readonly List<string> Lines = new List<string>();
        public void WriteLineAndConsole(string value) { Errors++; Lines.Add(value); }
    }
}
namespace Sandbox.Game.Weapons
{
    public class MyGunBase : MyDeviceBase { public DateTime LastShootTime; }
}
namespace Sandbox.Game.Entities.Cube
{
    public class MyTerminalBlock : Sandbox.Game.Entities.MyCubeBlock
    {
        public event Action<MyTerminalBlock> OwnershipChanged;
        public int OwnershipSubscribers => OwnershipChanged?.GetInvocationList().Length ?? 0;
        public void ChangeOwnership() { OwnershipChanged?.Invoke(this); }
    }
    public class MyShipConnector : MyTerminalBlock { }
    public class MySlimBlock : IMySlimBlock
    {
        public Sandbox.Game.Entities.MyCubeBlock FatBlock;
        public Sandbox.Game.Entities.MyCubeGrid CubeGrid;
        public long OwnerId;
        public float BuildIntegrity = 100;
        public float Integrity = 100;
        public bool IsFullyDismounted => Integrity <= 0;
        public bool IsMovedBySplit;
        public bool ThrowDuringGrinding;
        public Action DuringGrinding;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void DecreaseMountLevel(float grinderAmount, object outputInventory = null, bool useDefaultDeconstructEfficiency = false, long identityId = 0, bool testingMode = false)
        {
            Integrity -= grinderAmount;
            CubeGrid.Damage(this, -grinderAmount / 100);
            DuringGrinding?.Invoke();
            if (ThrowDuringGrinding) throw new InvalidOperationException("Simulated grinding failure");
            BuildIntegrity = Integrity;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void SetIntegrity(float buildIntegrity, float integrity, object integrityChangeType, long grinderOwner)
        {
            float previous = Integrity;
            Integrity = integrity;
            CubeGrid.Damage(this, (integrity - previous) / 100);
            BuildIntegrity = buildIntegrity;
        }
    }
}
namespace Sandbox.Game.Entities
{
    public interface IMyGunBaseUser { VRage.Game.MyDefinitionId PhysicalItemId { get; } }
    public class OwnershipModule { public VRage.Game.MyOwnershipShareModeEnum ShareMode; }
    public class BlockDefinition { public VRage.Game.MyDefinitionId Id; }
    public class MyCubeBlock : VRage.Game.Entity.MyEntity
    {
        public long OwnerId;
        public OwnershipModule IDModule;
        public MySlimBlock SlimBlock;
        public BlockDefinition BlockDefinition = new BlockDefinition();
        public MyCubeGrid CubeGrid;
    }
    public class MyEntityStat
    {
        public event Action<float, float, object> OnStatChanged;
        public int Subscribers => OnStatChanged?.GetInvocationList().Length ?? 0;
        public void Change(float value, float oldValue) { OnStatChanged?.Invoke(value, oldValue, null); }
    }
    public class MyCubeGrid : VRage.Game.Entity.MyEntity, IMyCubeGrid
    {
        public bool IsBlockTrasferInProgress;
        public float Speed;
        public readonly List<long> BigOwners = new List<long>();
        public readonly List<MyCubeBlock> Blocks = new List<MyCubeBlock>();
        public readonly HashSet<MySlimBlock> CubeBlocks = new HashSet<MySlimBlock>();
        public event Action<IMySlimBlock, float, MyHitInfo?, long> OnGridBlockDamaged;
        public event Action<MySlimBlock> OnBlockRemoved;
        public event Action<MyCubeBlock> OnFatBlockAdded;
        public event Action<MyCubeBlock> OnFatBlockRemoved;
        public event Action<MyCubeGrid, MyCubeGrid> OnGridMerge;
        public event Action<MyCubeGrid, MyCubeGrid> OnGridSplit;
        public event Action<MyCubeGrid, GridLinkTypeEnum> OnConnectionChangeCompleted;
        public event Action<MyCubeGrid> OnBlockOwnershipChanged;
        public int Subscribers => (OnGridBlockDamaged?.GetInvocationList().Length ?? 0)
            + (OnBlockRemoved?.GetInvocationList().Length ?? 0)
            + (OnFatBlockAdded?.GetInvocationList().Length ?? 0)
            + (OnFatBlockRemoved?.GetInvocationList().Length ?? 0)
            + (OnGridMerge?.GetInvocationList().Length ?? 0)
            + (OnGridSplit?.GetInvocationList().Length ?? 0)
            + (OnConnectionChangeCompleted?.GetInvocationList().Length ?? 0)
            + (OnBlockOwnershipChanged?.GetInvocationList().Length ?? 0);
        public void OwnershipChanged() { OnBlockOwnershipChanged?.Invoke(this); }
        public IEnumerable<MyCubeBlock> GetFatBlocks() { return Blocks; }
        public void Damage(float amount) { OnGridBlockDamaged?.Invoke(new MySlimBlock(), amount, null, 0); }
        public void Damage(MySlimBlock block, float amount) { OnGridBlockDamaged?.Invoke(block, amount, null, 0); }
        public void RemoveSlim() { RemoveSlim(new MySlimBlock { CubeGrid = this }); }
        public void RemoveSlim(MySlimBlock block) { CubeBlocks.Remove(block); if (block.FatBlock != null && block.CubeGrid == this) Blocks.Remove(block.FatBlock); NotifyBlockRemoved(block); }
        public void Add(MyCubeBlock block) { block.CubeGrid = this; block.SlimBlock = new MySlimBlock { CubeGrid = this, FatBlock = block }; CubeBlocks.Add(block.SlimBlock); Blocks.Add(block); OnFatBlockAdded?.Invoke(block); }
        public void Remove(MyCubeBlock block) { Blocks.Remove(block); if (block.SlimBlock != null) CubeBlocks.Remove(block.SlimBlock); OnFatBlockRemoved?.Invoke(block); }
        public void NotifyBlockAdded(MySlimBlock block) { if (block.FatBlock != null) OnFatBlockAdded?.Invoke(block.FatBlock); }
        public void NotifyBlockRemoved(MySlimBlock block)
        {
            OnBlockRemoved?.Invoke(block);
            if (block.FatBlock != null) OnFatBlockRemoved?.Invoke(block.FatBlock);
        }
        public void Merge(MyCubeGrid removed) { OnGridMerge?.Invoke(this, removed); }
        public void Split(MyCubeGrid fragment) { OnGridSplit?.Invoke(this, fragment); }
        public void ConnectionsChanged() { OnConnectionChangeCompleted?.Invoke(this, GridLinkTypeEnum.Logical); }
    }
    public static class MyEntities
    {
        public static readonly List<VRage.Game.Entity.MyEntity> Entities = new List<VRage.Game.Entity.MyEntity>();
        public static event Action<VRage.Game.Entity.MyEntity> OnEntityAdd;
        public static event Action<VRage.Game.Entity.MyEntity> OnEntityRemove;
        public static int Subscribers => (OnEntityAdd?.GetInvocationList().Length ?? 0) + (OnEntityRemove?.GetInvocationList().Length ?? 0);
        public static IEnumerable<VRage.Game.Entity.MyEntity> GetEntities() { return Entities; }
        public static VRage.Game.Entity.MyEntity GetEntityById(long id)
        {
            foreach (var entity in Entities)
            {
                if (entity.EntityId == id) return entity;
                if (entity is MyCubeGrid grid)
                    foreach (var block in grid.Blocks) if (block.EntityId == id) return block;
            }
            return null;
        }
        public static void Add(MyCubeGrid grid) { Entities.Add(grid); OnEntityAdd?.Invoke(grid); }
        public static void Remove(MyCubeGrid grid) { Entities.Remove(grid); OnEntityRemove?.Invoke(grid); }
        public static void RemoveEntity(VRage.Game.Entity.MyEntity entity) { Entities.Remove(entity); OnEntityRemove?.Invoke(entity); }
    }
}
namespace Sandbox.Game.World
{
    public class Identity { public long IdentityId = 1; }
    public class Player { public Identity Identity = new Identity(); }
    public class CharacterStats { public Sandbox.Game.Entities.MyEntityStat Health = new Sandbox.Game.Entities.MyEntityStat(); }
    public class Character : VRage.Game.Entity.MyEntity
    {
        public CharacterStats StatComp = new CharacterStats();
        public IMyGunObject<MyDeviceBase> CurrentWeapon;
    }
    public class MySession
    {
        public Guid WorldId = Guid.NewGuid();
        public int GameplayFrameCounter;
        public static MySession Static = new MySession();
        public Character LocalCharacter = new Character();
        public Player LocalHumanPlayer = new Player();
        public List<VRage.Game.MyObjectBuilder_Checkpoint.ModItem> Mods = new List<VRage.Game.MyObjectBuilder_Checkpoint.ModItem>();
    }
}
namespace Sandbox.ModAPI
{
    public interface IMyTerminalBlock { }
    public interface IMyFunctionalBlock : IMyTerminalBlock, VRage.ModAPI.IMyEntity { bool Enabled { get; set; } }
    public static class MyAPIGateway
    {
        public static BoundsSession Session = new BoundsSession();
        public static GridGroups GridGroups = new GridGroups();
        private static Utilities utilities = new Utilities();
        public static Utilities Utilities
        {
            get => utilities;
            set { utilities = value; VRage.FileSystem.MyFileSystem.UserDataPath = value?.StoragePath; }
        }
        public static Multiplayer Multiplayer = new Multiplayer();
    }
    public class Multiplayer { public ulong ServerId = 1; public bool IsServer { get; set; } }
    public class Utilities
    {
        public readonly List<string> ChatMessages = new List<string>();
        public readonly List<string> Notifications = new List<string>();
        public void ShowMessage(string sender, string text) { ChatMessages.Add(sender + ": " + text); }
        public void ShowNotification(string text, int disappearTimeMs = 2000, string font = "White") { Notifications.Add(text); }
        public readonly string StoragePath = CooldownTestStorage.CreateRoot();
        public readonly GlobalStorageFiles Files;
        public Utilities()
        {
            Files = new GlobalStorageFiles(StoragePath);
            VRage.FileSystem.MyFileSystem.UserDataPath = StoragePath;
        }
        public bool FailReads;
        public bool FailWrites;
        public int Writes;
        public int WriteAttempts, Flushes, Publications, Commits;
        public Action OnApiRequest;
        public long RegisteredMessageChannel, UnregisteredMessageChannel, RequestedMessageChannel;
        public readonly List<Action<object>> Handlers = new List<Action<object>>();
        public bool FileExistsInGlobalStorage(string file) { if (FailReads) throw new IOException("Read failed"); return Files.ContainsKey(file); }
        public TextReader ReadFileInGlobalStorage(string file) { if (FailReads) throw new IOException("Read failed"); return File.OpenText(Files.PathFor(file)); }
        public TextWriter WriteFileInGlobalStorage(string file)
        {
            if (FailWrites) throw new IOException("Write failed");
            Writes++;
            Directory.CreateDirectory(Path.GetDirectoryName(Files.PathFor(file)));
            // Native global storage opens FileMode.Create, so opening this
            // boundary really truncates the current record.
            return new StreamWriter(new FileStream(Files.PathFor(file), FileMode.Create, FileAccess.Write, FileShare.Read));
        }
        public void RegisterMessageHandler(long channel, Action<object> handler) { RegisteredMessageChannel = channel; Handlers.Add(handler); }
        public void UnregisterMessageHandler(long channel, Action<object> handler) { UnregisteredMessageChannel = channel; Handlers.Remove(handler); }
        public void SendModMessage(long channel, object value) { RequestedMessageChannel = channel; if (value is string) OnApiRequest?.Invoke(); }
        public void Broadcast(object value) { foreach (var handler in Handlers.ToArray()) handler(value); }
    }
    public class GridGroups
    {
        public bool Throw;
        public int Calls;
        public Dictionary<IMyCubeGrid, List<IMyCubeGrid>> Groups = new Dictionary<IMyCubeGrid, List<IMyCubeGrid>>();
        public Dictionary<IMyCubeGrid, List<IMyCubeGrid>> LogicalGroups = new Dictionary<IMyCubeGrid, List<IMyCubeGrid>>();
        public bool HasConnection(IMyCubeGrid first, IMyCubeGrid second, GridLinkTypeEnum type)
        {
            return first == second || (Groups.TryGetValue(first, out var members) && members.Contains(second));
        }
        public void GetGroup(IMyCubeGrid anchor, GridLinkTypeEnum type, ICollection<IMyCubeGrid> result)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("Simulated group lookup failure");
            var groups = type == GridLinkTypeEnum.Logical ? LogicalGroups : Groups;
            if (groups.TryGetValue(anchor, out var members)) foreach (var member in members) result.Add(member);
            else result.Add(anchor);
        }
    }
}

namespace VRage.Game
{
    public enum MyOwnershipShareModeEnum { None, Faction, All }
}
namespace Sandbox.ModAPI
{
    public class BoundsSession { public BoundsFactions Factions = new BoundsFactions(); }
    public class BoundsFaction { public long FactionId; }
    public class BoundsFactions
    {
        public readonly Dictionary<long, BoundsFaction> Members = new Dictionary<long, BoundsFaction>();
        public BoundsFaction TryGetPlayerFaction(long identity) { return Members.TryGetValue(identity, out var value) ? value : null; }
    }
}

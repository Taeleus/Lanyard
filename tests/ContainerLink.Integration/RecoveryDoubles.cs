using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.Game.Entities;
using VRage.Game.Entity;

namespace VRage.FileSystem
{
    public static class MyFileSystem
    {
        public static string UserDataPath = Path.Combine(AppContext.BaseDirectory, "test-state", Guid.NewGuid().ToString("N"));
    }
}

namespace VRage.Game.Components
{
    public enum MyUpdateOrder { NoUpdate }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MySessionComponentDescriptor : Attribute { public MySessionComponentDescriptor(MyUpdateOrder order) { } }
    public abstract class MySessionComponentBase
    {
        public virtual void BeforeStart() { }
        protected virtual void UnloadData() { }
        public virtual void Draw() { }
    }
}

namespace VRage.Plugins
{
    public interface IPlugin { void Init(object game); void Update(); void Dispose(); }
}

namespace Sandbox.ModAPI
{
    public delegate void MessageEnteredHandler(string message, ref bool sendToOthers);
    public sealed class MyAPIUtilities
    {
        public static readonly MyAPIUtilities Static = new MyAPIUtilities();
        public event MessageEnteredHandler MessageEntered;
        public bool Enter(string message) { var send = true; MessageEntered?.Invoke(message, ref send); return send; }
    }
}

namespace Lanyard
{
    internal static class LanyardPreferences
    {
        public static LanyardSettings Current = new LanyardSettings();
        public static event Action Changed;
        public static void EnsureLoaded() { }
        public static void Change(Action<LanyardSettings> change) { change(Current); Changed?.Invoke(); }
        public static void Reset() { Current = new LanyardSettings(); Changed?.Invoke(); }
    }
    internal static class LanyardShortcuts { public static void ClearTransient() { } public static void Update() { } }
    internal static class LanyardHud { public static void Draw() { } public static void Update() { } public static void Clear() { } }
    internal static class OwnGrindingPatches { public static void Initialize() { } public static void Dispose() { } }
    internal static class TerminalMenu { public static void Initialize() { } public static void Clear() { } public static void Dispose() { } }
    internal static class LanyardScreen { public static void Open() { } public static void Close() { } }
}

namespace VRageRender.Import
{
    public sealed class MyModelDummy { public string Name; }
}

namespace VRage.Game.Entity.UseObject
{
    public enum UseActionEnum { None, OpenInventory }
    public interface IMyUseObject
    {
        MyEntity Owner { get; }
        VRageRender.Import.MyModelDummy Dummy { get; }
        UseActionEnum PrimaryAction { get; }
    }
}

namespace Sandbox.Game.EntityComponents
{
    public sealed class MyCasterComponent { public TargetedBlock HitBlock; }
    public sealed class TargetedBlock { public MyCubeBlock FatBlock; }
    public sealed class MyUseObjectsComponentBase
    {
        public readonly List<VRage.Game.Entity.UseObject.IMyUseObject> Ports = new List<VRage.Game.Entity.UseObject.IMyUseObject>();
        public bool Throw;
        public int Resolutions;
        public Action Resolving;
        public void GetInteractiveObjects(List<VRage.Game.Entity.UseObject.IMyUseObject> ports)
        {
            Resolutions++;
            Resolving?.Invoke();
            if (Throw) throw new InvalidOperationException("Port components are not ready.");
            ports.AddRange(Ports);
        }
    }
}

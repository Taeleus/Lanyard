using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Entity;

namespace Sandbox.Definitions
{
    public sealed class MyComponentDefinition { public MyDefinitionId Id; }
    public sealed class MyCubeBlockDefinition
    {
        public sealed class Component { public MyComponentDefinition Definition; public int Count; }
        public Component[] Components;
    }
}

namespace Sandbox.Game.Entities
{
    public sealed class MyCubeBuilder
    {
        public static MyCubeBuilder Static;
        public bool IsActivated = true;
        public bool NativeResult;
        public int NativeCalls;
        public Sandbox.Definitions.MyCubeBlockDefinition CurrentBlockDefinition;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool CanStartConstruction(MyEntity buildingEntity) { NativeCalls++; return NativeResult; }
    }
}

namespace Sandbox.Game.Weapons
{
    public sealed class MyBlockPlacerBase : MyEntity { }
}

namespace Sandbox.Game.Multiplayer
{
    public static class Sync { public static ulong MyId = 123; }
}

internal static class PlacementResolver
{
    internal sealed class Request
    {
        public MyEntity DestinationOwner, PortOwner;
        public MyDefinitionId Item;
        public List<MyInventory> Sources;
        public bool RequireAmount;
    }
    public static readonly List<Request> Requests = new List<Request>();
    public static readonly List<MyInventory> Sources = new List<MyInventory>();
    public static Action Resolving;
    public static bool Throw;
    public static void GetSources(MyEntity destinationOwner, MyDefinitionId item, List<MyInventory> sources,
        MyEntity portOwner, bool requireAmount)
    {
        Requests.Add(new Request { DestinationOwner = destinationOwner, Item = item, Sources = sources,
            PortOwner = portOwner, RequireAmount = requireAmount });
        if (Throw) throw new InvalidOperationException("Resolver failure");
        sources.AddRange(Sources);
        Resolving?.Invoke();
    }
    public static void Reset() { Requests.Clear(); Sources.Clear(); Resolving = null; Throw = false; }
}

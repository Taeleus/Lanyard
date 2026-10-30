using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Lanyard;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;

internal static partial class Program
{
    private static readonly MyDefinitionId FirstComponent = new MyDefinitionId(Component, "SteelPlate");
    private static readonly MyDefinitionId OtherComponent = new MyDefinitionId(Component, "Construction");
    private static Dictionary<MyDefinitionId, long> PendingPlacement => (Dictionary<MyDefinitionId, long>)
        typeof(LinkController).GetField("pendingPlacement", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    private static MyCubeBlockDefinition PlacementDefinition() => new MyCubeBlockDefinition
    {
        Components = new[]
        {
            new MyCubeBlockDefinition.Component { Definition = new MyComponentDefinition { Id = FirstComponent }, Count = 20 },
            new MyCubeBlockDefinition.Component { Definition = new MyComponentDefinition { Id = OtherComponent }, Count = 5 }
        }
    };

    private static World PlacementWorld()
    {
        var world = new World();
        world.Equip("BlockPlacerItem", new MyBlockPlacerBase());
        world.Destination.Add(Component, "SteelPlate", 10);
        world.Destination.Add(Component, "Construction", 50);
        world.Other.Inventories[0].Add(Component, "SteelPlate", 50);
        PlacementResolver.Sources.Add(world.Destination);
        MyCubeBuilder.Static = new MyCubeBuilder { CurrentBlockDefinition = PlacementDefinition() };
        return world;
    }

    private static void RequestPlacement() => LinkController.RequestPlacementComponent(PlacementDefinition(), false);

    private static void PlacementSupplyTests()
    {
        PlacementOnlyFirstComponent();
        PlacementPendingAndLifecycle();
        PlacementGuardRevocation();
        PlacementSourceBoundaries();
        PlacementNativeHook();
    }

    private static void PlacementOnlyFirstComponent()
    {
        var world = PlacementWorld();
        var planned = new MyIdentity.BuildPlanItem();
        world.Character.BuildPlanner.Add(planned);
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 1, "placement: exactly one transfer request");
        var transfer = MyInventory.Transfers.Single();
        Check(transfer.Item.Equals(FirstComponent) && transfer.Amount == 1 && !transfer.Spawn,
            "placement: only one first-definition component requested, without spawning");
        Check(transfer.Source == world.Destination && transfer.Destination == world.Character.Inventory,
            "placement: source is native-resolved inventory and destination is local character");
        Check(world.Destination.GetItemAmount(FirstComponent) == 9 && world.Character.Inventory.GetItemAmount(FirstComponent) == 1,
            "placement: synchronous host transfer moves one item from a larger stack");
        Check(world.Destination.GetItemAmount(OtherComponent) == 50 && world.Other.InventoryLookups == 0,
            "placement: other component needs and arbitrary same-grid inventory remain untouched");
        Check(world.Character.BuildPlanner.SequenceEqual(new[] { planned }) && WithdrawRecorder.Calls.Count == 0,
            "placement: build planner is neither edited nor executed");
        var request = PlacementResolver.Requests.Single();
        Check(ReferenceEquals(request.DestinationOwner, world.Character) && ReferenceEquals(request.PortOwner, world.PortOwner) &&
            request.Item.Equals(FirstComponent) && request.RequireAmount,
            "placement: native resolver receives character owner, first component, selected port and require-amount flag");
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 1, "placement: existing carried first component prevents another request");

        foreach (var invalid in new[] { (MyCubeBlockDefinition)null, new MyCubeBlockDefinition(),
            new MyCubeBlockDefinition { Components = Array.Empty<MyCubeBlockDefinition.Component>() },
            new MyCubeBlockDefinition { Components = new[] { new MyCubeBlockDefinition.Component() } } })
        {
            world = PlacementWorld();
            LinkController.RequestPlacementComponent(invalid, false);
            Check(MyInventory.Transfers.Count == 0 && PlacementResolver.Requests.Count == 0,
                "placement: absent first component definition does no work");
        }
        world = PlacementWorld();
        world.Character.Inventory.Add(Component, "SteelPlate");
        RequestPlacement();
        Check(PlacementResolver.Requests.Count == 0 && MyInventory.Transfers.Count == 0,
            "placement: already carried component skips source discovery");
        world = PlacementWorld();
        world.Character.Inventory.AcceptsItem = false;
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 0 && PlacementResolver.Requests.Count == 0,
            "placement: character inventory constraint rejects supply");
        world = PlacementWorld();
        world.Character.Inventory.Capacity = 0.5;
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 0 && PlacementResolver.Requests.Count == 0,
            "placement: insufficient room for one whole component rejects supply");
    }

    private static void PlacementPendingAndLifecycle()
    {
        var world = PlacementWorld();
        MyInventory.DeferTransfers = true;
        MyInventory.AfterTransfer = _ => RequestPlacement();
        RequestPlacement();
        for (var index = 0; index < 100; index++) RequestPlacement();
        Check(MyInventory.Transfers.Count == 1 && PlacementResolver.Requests.Count == 1,
            "placement: deferred MP inventory and reentrant callbacks cannot flood duplicate requests");
        Check(world.Character.Inventory.GetItemAmount(FirstComponent) == 0 && PendingPlacement.ContainsKey(FirstComponent),
            "placement: pending request waits for observed inventory without assuming delivery");
        MyInventory.AfterTransfer = null;
        PendingPlacement[FirstComponent] = 0;
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 2 && !ReferenceEquals(PlacementResolver.Requests[0].Sources, PlacementResolver.Requests[1].Sources),
            "placement: expired request retries using a fresh native source list");
        PendingPlacement[OtherComponent] = long.MaxValue;
        LinkController.RequestPlacementComponent(PlacementDefinition(), true);
        Check(!PendingPlacement.ContainsKey(FirstComponent) && PendingPlacement.ContainsKey(OtherComponent),
            "placement: native success clears only that component's pending request");
        RequestPlacement();
        world.Character.Inventory.Add(Component, "SteelPlate");
        RequestPlacement();
        Check(!PendingPlacement.ContainsKey(FirstComponent), "placement: observed delivered component clears pending state");
        world.Character.Inventory.Items.Clear();
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 4, "placement: a later consumed component permits a new request");
        LinkController.Disconnect();
        Check(PendingPlacement.Count == 0, "placement: disconnect clears pending state");
        GetTargetedBlock.selectedBlock = world.Selected;
        GetTargetedBlock.selectedObject = new InventoryPort { Owner = world.PortOwner };
        PendingPlacement[FirstComponent] = long.MaxValue;
        LinkController.Connected();
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 5, "placement: a new connection clears old pending throttles");
    }

    private static void PlacementGuardRevocation()
    {
        var revocations = new (string Name, Action<World> Revoke)[]
        {
            ("combat", world => CombatLockout.IsBlocked = true),
            ("outside bounds", world => LinkArea.Outside = 1),
            ("past break distance", world => LinkArea.Outside = 101),
            ("enemy overlap", world => LinkArea.NoEnemies = false),
            ("lost access", world => world.Selected.PlayerAccess = false),
            ("Share All", world => { world.Selected.OwnerId = 2; world.Selected.IDModule.ShareMode = MyOwnershipShareModeEnum.All; }),
            ("closed selected block", world => world.Selected.Closed = true),
            ("missing inventory", world => world.Selected.HasInventory = false),
            ("dead character", world => world.Character.IsDead = true),
            ("changed character", world => MySession.Static.LocalCharacter = new MyCharacter()),
            ("missing player", world => MySession.Static.LocalHumanPlayer = null),
            ("changed selected block", world => GetTargetedBlock.selectedBlock = world.Other),
            ("changed selected port", world => GetTargetedBlock.selectedObject = new InventoryPort { Owner = world.PortOwner })
        };
        foreach (var entry in revocations)
        {
            var world = PlacementWorld();
            PlacementResolver.Resolving = () => entry.Revoke(world);
            RequestPlacement();
            Check(MyInventory.Transfers.Count == 0, "placement: resolver-time " + entry.Name + " prevents transfer");
            if (entry.Name == "changed selected block" || entry.Name == "changed selected port") continue;
            world = PlacementWorld();
            entry.Revoke(world);
            RequestPlacement();
            Check(MyInventory.Transfers.Count == 0 && PlacementResolver.Requests.Count == 0,
                "placement: initial " + entry.Name + " prevents source discovery");
        }
        var current = PlacementWorld();
        CombatLockout.Updating = () => CombatLockout.IsBlocked = true;
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 0 && CombatLockout.Updates > 0,
            "placement: polls fresh combat state before supplying");
        current = PlacementWorld();
        var fragment = new MyCubeGrid();
        current.Selected.CubeGrid = fragment;
        bool followedBeforePoll = false;
        CombatLockout.Updating = () => { followedBeforePoll = CombatLockout.FollowedGrid == fragment; CombatLockout.IsBlocked = true; };
        RequestPlacement();
        Check(followedBeforePoll && MyInventory.Transfers.Count == 0,
            "placement: follows split/merge construct before polling combat");
    }

    private static void PlacementSourceBoundaries()
    {
        var world = PlacementWorld();
        PlacementResolver.Sources.Clear();
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 0 && world.Other.InventoryLookups == 0,
            "placement: empty native source list never falls back to arbitrary grid cargo");
        world = PlacementWorld();
        PlacementResolver.Sources.Clear();
        var invalid = new MyInventory { Owner = new MyCubeBlock { Closed = true } };
        invalid.Add(Component, "SteelPlate", 20);
        var connected = world.Other.Inventories[0];
        PlacementResolver.Sources.AddRange(new[] { null, world.Character.Inventory, invalid, connected, world.Destination });
        MyInventory.DeferTransfers = true;
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 1 && MyInventory.Transfers[0].Source == connected,
            "placement: skips invalid sources and permits the first eligible vanilla-returned connected inventory");
        Check(world.Destination.GetItemAmount(FirstComponent) == 10,
            "placement: deferred transfer does not continue through other eligible native sources");
        world = PlacementWorld();
        Reflections.PlacementSources = null;
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 0 && GetTargetedBlock.selectedBlock == world.Selected,
            "placement: absent native resolver disables supply without breaking the link");
        world = PlacementWorld();
        PlacementResolver.Throw = true;
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 0 && GetTargetedBlock.selectedBlock == null && PendingPlacement.Count == 0,
            "placement: resolver exception disconnects safely and clears pending requests");
    }

    private static void PlacementNativeHook()
    {
        PlacementSupply.Initialize();
        PlacementSupply.Initialize();
        var method = AccessTools.Method(typeof(MyCubeBuilder), nameof(MyCubeBuilder.CanStartConstruction));
        Check(Harmony.GetPatchInfo(method).Postfixes.Count(patch => patch.owner == "Taeleus.Lanyard.PlacementSupply") == 1,
            "placement hook: initialization installs one optional postfix");
        var world = PlacementWorld();
        var builder = MyCubeBuilder.Static;
        Check(!builder.CanStartConstruction(world.Character) && builder.NativeCalls == 1 && MyInventory.Transfers.Count == 1,
            "placement hook: failed native check remains false, executes once and supplies one first component");
        var guards = new (string Name, Action<World> Change)[]
        {
            ("inactive builder", value => MyCubeBuilder.Static.IsActivated = false),
            ("non-placement tool", value => value.Character.EquippedTool = new MyWelder()),
            ("creative mode", value => MySession.Static.CreativeMode = true),
            ("creative tools", value => MySession.Static.CreativeTools = true)
        };
        foreach (var entry in guards)
        {
            world = PlacementWorld();
            entry.Change(world);
            Check(!MyCubeBuilder.Static.CanStartConstruction(world.Character) && MyInventory.Transfers.Count == 0,
                "placement hook: " + entry.Name + " leaves native result and inventory intact");
        }
        world = PlacementWorld();
        Check(!MyCubeBuilder.Static.CanStartConstruction(new MyCharacter()) && MyInventory.Transfers.Count == 0,
            "placement hook: foreign building character cannot request supply");
        Check(!new MyCubeBuilder { CurrentBlockDefinition = PlacementDefinition() }.CanStartConstruction(world.Character) && MyInventory.Transfers.Count == 0,
            "placement hook: foreign builder instance cannot request supply");
        world = PlacementWorld();
        MyCubeBuilder.Static.NativeResult = true;
        Check(MyCubeBuilder.Static.CanStartConstruction(world.Character) && MyInventory.Transfers.Count == 0,
            "placement hook: native construction success remains true without supply");
        PlacementSupply.Dispose();
        world = PlacementWorld();
        Check(!MyCubeBuilder.Static.CanStartConstruction(world.Character) && MyInventory.Transfers.Count == 0 &&
            !(Harmony.GetPatchInfo(method)?.Owners.Contains("Taeleus.Lanyard.PlacementSupply") ?? false),
            "placement hook: disposal removes optional behavior and preserves native check");
    }
}

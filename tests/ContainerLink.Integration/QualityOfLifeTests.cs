using System;
using System.Collections.Generic;
using System.Linq;
using Lanyard;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;

internal static partial class Program
{
    private static MyIdentity.BuildPlanItem Plan(string subtype, int count, string display = null) => new MyIdentity.BuildPlanItem
    {
        Components = new List<MyIdentity.BuildPlanItem.Component>
        {
            new MyIdentity.BuildPlanItem.Component { ComponentDefinition = new MyComponentDefinition
                { Id = new MyDefinitionId(Component, subtype), DisplayNameText = display ?? subtype }, Count = count }
        }
    };

    private static void AimAt(World world, MyCubeBlock target, bool port = true)
    {
        world.Character.EquippedTool.Components.Set(new MyCasterComponent { HitBlock = new TargetedBlock { FatBlock = target } });
        var objects = new MyUseObjectsComponentBase();
        if (port) objects.Ports.Add(new InventoryPort { Owner = target });
        target.Components.Set(objects);
    }

    private static void QualityOfLifeTests()
    {
        SafeLinkSwitching();
        PersonalPause();
        FunctionPreferences();
        ClearStatePresentation();
        FeedbackUsesObservedResults();
        ReadOnlyPlannerPreview();
    }

    private static void SafeLinkSwitching()
    {
        foreach (var failure in new[] { "no port", "no access", "ports unavailable", "highlight", "blocked candidate" })
        {
            var world = new World(true);
            var previous = GetTargetedBlock.selectedObject;
            world.Other.CubeGrid = new MyCubeGrid();
            AimAt(world, world.Other, failure != "no port");
            if (failure == "no access") world.Other.PlayerAccess = false;
            if (failure == "ports unavailable") world.Other.Components.Get<MyUseObjectsComponentBase>().Throw = true;
            if (failure == "highlight") Sandbox.Game.MyVisualScriptLogicProvider.ThrowHighlightOnce = true;
            if (failure == "blocked candidate") CombatLockout.Connecting = grid => CombatLockout.IsBlocked = true;
            GetTargetedBlock.GetPort();
            Check(GetTargetedBlock.selectedBlock == world.Selected && ReferenceEquals(GetTargetedBlock.selectedObject, previous),
                "safe switch: " + failure + " keeps the previous exact selection");
            Check(CombatLockout.FollowedGrid == world.Grid, "safe switch: " + failure + " restores the old combat anchor");
            Check(new LinkConnectionStore().Load(42)?.BlockId == world.Selected.EntityId,
                "safe switch: " + failure + " leaves the old durable selection intact");
        }

        var success = new World(true);
        AimAt(success, success.Other);
        GetTargetedBlock.GetPort();
        Check(GetTargetedBlock.selectedBlock == success.Other && GetTargetedBlock.selectedObject.Owner == success.Other &&
            new LinkConnectionStore().Load(42)?.BlockId == success.Other.EntityId,
            "safe switch: validated target replaces both live and saved selection");
        var invalidOld = new World(true);
        AimAt(invalidOld, invalidOld.Other, false);
        invalidOld.Selected.PlayerAccess = false;
        GetTargetedBlock.GetPort();
        Check(GetTargetedBlock.selectedBlock == null && new LinkConnectionStore().Load(42) == null,
            "safe switch: an invalid old connection still breaks instead of being preserved");
        foreach (var change in new[] { "session", "character", "unlink", "access callback" })
        {
            var world = new World(true);
            AimAt(world, world.Other);
            if (change == "access callback") world.Other.AccessCheck = () =>
            {
                if (world.Other.AccessChecks == 2) MySession.Static = new MySession { LocalCharacter = world.Character };
            };
            else Sandbox.Game.MyVisualScriptLogicProvider.Highlighted = () =>
            {
                if (change == "session") MySession.Static = new MySession { LocalCharacter = world.Character };
                if (change == "character") MySession.Static.LocalCharacter = new Sandbox.Game.Entities.Character.MyCharacter();
                if (change == "unlink") LinkController.Disconnect();
            };
            GetTargetedBlock.GetPort();
            Check(new LinkConnectionStore().Load(42)?.BlockId != world.Other.EntityId,
                "safe switch: " + change + " callback never persists stale candidate into a changed context");
            if (change != "access callback") Check(GetTargetedBlock.selectedBlock == null,
                "safe switch: " + change + " after highlight leaves no blessed stale live connection");
        }
        var rollback = new World(true);
        AimAt(rollback, rollback.Other);
        Sandbox.Game.MyVisualScriptLogicProvider.ThrowHighlightOnce = true;
        Sandbox.Game.MyVisualScriptLogicProvider.Highlighted = () =>
            MySession.Static = new MySession { LocalCharacter = rollback.Character };
        GetTargetedBlock.GetPort();
        Check(GetTargetedBlock.selectedBlock == null && new LinkConnectionStore().Load(42)?.BlockId == rollback.Selected.EntityId,
            "safe switch: context change during rollback highlight preserves origin record without blessing stale previous selection");
        rollback = new World(true);
        AimAt(rollback, rollback.Other);
        Sandbox.Game.MyVisualScriptLogicProvider.ThrowHighlightOnce = true;
        rollback.Selected.AccessCheck = () => { if (rollback.Selected.AccessChecks > 1) throw new InvalidOperationException("Access snapshot unavailable."); };
        GetTargetedBlock.GetPort();
        Check(GetTargetedBlock.selectedBlock == null && new LinkConnectionStore().Load(42)?.BlockId == rollback.Selected.EntityId,
            "safe switch: rollback validation exception suspends safely and preserves origin intent");
    }

    private static void PersonalPause()
    {
        foreach (var tool in new[] { "grinder", "drill", "welder", "starting component" })
        {
            var world = tool == "starting component" ? PlacementWorld() : new World();
            if (tool == "drill") world.Equip("HandDrillItem", new MyHandDrill());
            if (tool == "welder") { world.Equip("WelderItem", new MyWelder()); world.Character.BuildPlanner.Add(Plan("SteelPlate", 5)); }
            if (tool != "starting component") world.AddDepositItems();
            LinkController.SetUserPaused(true);
            var updates = CombatLockout.Updates;
            Tick(65);
            if (tool == "starting component") RequestPlacement();
            Check(LanyardPreferences.Current.ManualPaused && LinkController.IsPaused && GetTargetedBlock.selectedBlock == world.Selected,
                "personal pause: " + tool + " retains selection and reports paused");
            Check(MyInventory.Transfers.Count == 0 && WithdrawRecorder.Calls.Count == 0 && PlacementResolver.Requests.Count == 0,
                "personal pause: " + tool + " issues no transfer or source requests");
            Check(CombatLockout.Updates > updates, "personal pause: " + tool + " keeps observing combat");
            CombatLockout.IsBlocked = true;
            LinkController.SetUserPaused(false);
            Tick(55);
            Check(MyInventory.Transfers.Count == 0 && WithdrawRecorder.Calls.Count == 0 && CombatLockout.IsBlocked,
                "personal resume: " + tool + " cannot clear or bypass combat lockout");
            world.Selected.PlayerAccess = false;
            Tick(1);
            Check(GetTargetedBlock.selectedBlock == null, "personal pause: access loss still breaks " + tool + " connection");
        }

        var idle = new World();
        Tick(1);
        idle.Grinder.IsShooting = false;
        LinkController.SetUserPaused(true);
        LinkController.SetUserPaused(false);
        idle.Character.Inventory.Add(Component, "SteelPlate", 2);
        Tick(15);
        Check(MyInventory.Transfers.Count == 0, "personal resume: an old idle-grinder grace window is not replayed");
    }

    private static void FunctionPreferences()
    {
        foreach (var tool in new[] { "grinder", "drill", "welder", "starting component" })
        {
            var world = tool == "starting component" ? PlacementWorld() : new World();
            if (tool == "drill") world.Equip("HandDrillItem", new MyHandDrill());
            if (tool == "welder") { world.Equip("WelderItem", new MyWelder()); world.Character.BuildPlanner.Add(Plan("SteelPlate", 5)); }
            world.AddDepositItems();
            LanyardPreferences.Change(settings =>
            {
                if (tool == "grinder") settings.GrinderReturns = false;
                if (tool == "drill") settings.DrillReturns = false;
                if (tool == "welder") settings.WelderSupply = false;
                if (tool == "starting component") settings.StartingComponents = false;
            });
            Tick(60);
            if (tool == "starting component") { world.Character.Inventory.Items.Clear(); RequestPlacement(); }
            Check(MyInventory.Transfers.Count == 0 && WithdrawRecorder.Calls.Count == 0 && PlacementResolver.Requests.Count == 0,
                "tool preferences: disabled " + tool + " does no work");
            Check(GetTargetedBlock.selectedBlock == world.Selected, "tool preferences: disabled " + tool + " keeps selection");
        }
        var drilling = new World();
        var drill = new MyHandDrill();
        drilling.Equip("HandDrillItem", drill);
        drilling.AddDepositItems();
        LanyardPreferences.Change(settings => settings.DrillActiveOnly = true);
        Tick(60);
        Check(MyInventory.Transfers.Count == 0, "drill preference: merely equipped drill stays idle in active-only mode");
        drill.IsShooting = true;
        Tick(51);
        Check(MyInventory.Transfers.Count == 1 && MyInventory.Transfers[0].Item.TypeId.ToString() == Ore,
            "drill preference: active drilling returns ore with normal categories");
        foreach (var stop in new[] { "release", "switch" })
        {
            drilling = new World();
            drill = new MyHandDrill { IsShooting = true };
            drilling.Equip("HandDrillItem", drill);
            drilling.Character.Inventory.Add(Ore, "Iron", 2);
            drilling.Character.Inventory.Add(Ore, "Nickel", 3);
            LanyardPreferences.Change(settings => settings.DrillActiveOnly = true);
            var currentWorld = drilling;
            var currentDrill = drill;
            MyInventory.AfterTransfer = request =>
            {
                if (stop == "release") currentDrill.IsShooting = false;
                else currentWorld.Equip("WelderItem", new MyWelder());
            };
            Tick(51);
            Check(MyInventory.Transfers.Count == 1 && drilling.Character.Inventory.GetItemAmount(new MyDefinitionId(Ore, "Nickel")) == 3,
                "drill preference: " + stop + " between hosted items stops remaining ore requests");
        }
    }

    private static void ClearStatePresentation()
    {
        var world = new World();
        world.Selected.CustomName = new string('A', 130) + "\nnot another line";
        Check(LinkController.StateText == "Active" && LinkController.StatusText.Length < 190,
            "state: active state and long container name remain bounded");
        CombatLockout.IsBlocked = true;
        CombatLockout.RemainingSeconds = 84;
        Check(LinkController.StateText.Contains("1:24") && !LinkController.StateText.Contains("enemy"),
            "state: safe countdown presents remaining time without hostile details");
        CombatLockout.RemainingSeconds = -1;
        Check(LinkController.StateText.Contains("safety checks") && !LinkController.StateText.Contains(":"),
            "state: unavailable monitor never promises countdown to resume");
        CombatLockout.IsBlocked = false;
        LinkArea.Outside = 5;
        Tick(1);
        Check(LinkController.StateText.Contains("outside working area"), "state: outside bounds pause is distinct");
    }

    private static void FeedbackUsesObservedResults()
    {
        var world = PlacementWorld();
        MyInventory.DeferTransfers = true;
        RequestPlacement();
        Check(TransferFeedback.Text.Contains("requested") && !TransferFeedback.Text.Contains("received"),
            "feedback: an asynchronous request is not presented as receipt");
        world.Character.Inventory.Add(Component, "SteelPlate", 1);
        Tick(1);
        Check(TransferFeedback.Text.Contains("received") && TransferFeedback.Text.Contains("place again"),
            "feedback: an observed starting component arrival prompts the second placement attempt");
        Check(MyInventory.Transfers.Count == 1, "feedback: arrival observation never requests another transfer");

        world = new World();
        world.AddDepositItems();
        world.Destination.Capacity = 0;
        Tick(1);
        Check(MyInventory.Transfers.Count == 0 && TransferFeedback.CapacityText.Contains("container full"),
            "feedback: known destination capacity explains a stalled grinder return");
        world.Destination.Capacity = 100;
        MyAPIGateway.Utilities.ThrowNotification = true;
        TransferFeedback.Report("Optional notification failure");
        Tick(8);
        Check(GetTargetedBlock.selectedBlock == world.Selected && MyInventory.Transfers.Count == 3,
            "feedback: failing optional notifications cannot break a valid link or block transfers");

        world = PlacementWorld();
        MyInventory.DeferTransfers = true;
        RequestPlacement();
        world.Character.Inventory.ThrowAmount = true;
        TransferFeedback.Update();
        world.Character.Inventory.ThrowAmount = false;
        Check(GetTargetedBlock.selectedBlock == world.Selected,
            "feedback: a failed optional inventory snapshot is contained");
        LinkController.SuspendForWorldChange();
        Check(TransferFeedback.Text == "", "feedback: world suspension clears result observations");
        world = PlacementWorld();
        var probes = 0;
        world.Character.Inventory.AmountRead = () =>
        {
            if (++probes == 3) LanyardPreferences.Current.StartingComponents = false;
        };
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 0 && TransferFeedback.Text == "",
            "feedback: disabling starting supply during arrival probe cancels request and its observation");
        world = new World();
        world.AddDepositItems();
        world.Destination.AmountRead = () => LanyardPreferences.Current.GrinderReturns = false;
        Tick(1);
        Check(MyInventory.Transfers.Count == 0 && TransferFeedback.Text == "",
            "feedback: disabling grinder returns during arrival probe cancels request and its observation");
        world = new World();
        world.Equip("WelderItem", new MyWelder());
        world.Character.BuildPlanner.Add(Plan("SteelPlate", 2));
        var welding = world;
        world.Character.Inventory.AmountRead = () => welding.Equip("HandDrillItem", new MyHandDrill());
        Tick(51);
        Check(WithdrawRecorder.Calls.Count == 0 && TransferFeedback.Text == "",
            "feedback: switching tool during planner arrival baseline cancels withdrawal and observations");
        world = PlacementWorld();
        MyInventory.DeferTransfers = true;
        RequestPlacement();
        world.Character.Inventory.Add(Component, "SteelPlate", 1);
        world.Character.Inventory.AmountRead = () => LinkController.SetUserPaused(true);
        TransferFeedback.Update();
        Check(GetTargetedBlock.selectedBlock == world.Selected && LinkController.IsPaused && TransferFeedback.Text == "",
            "feedback: reentrant personal pause clears arrival safely without stale receipt or disconnect");
        foreach (var change in new[] { "character", "owner closed" })
        {
            world = PlacementWorld();
            MyInventory.DeferTransfers = true;
            RequestPlacement();
            world.Character.Inventory.Add(Component, "SteelPlate", 1);
            var observed = world;
            world.Character.Inventory.AmountRead = () =>
            {
                if (change == "character") MySession.Static.LocalCharacter = new Sandbox.Game.Entities.Character.MyCharacter();
                else observed.Character.Closed = true;
            };
            TransferFeedback.Update();
            Check(!TransferFeedback.Text.Contains("received"),
                "feedback: " + change + " during amount probe discards apparent receipt");
        }
    }

    private static void ReadOnlyPlannerPreview()
    {
        var world = new World(true);
        world.Character.BuildPlanner.Add(Plan("SteelPlate", 4, "Steel plate"));
        world.Character.BuildPlanner.Add(Plan("SteelPlate", 3, "Steel plate"));
        world.Character.BuildPlanner.Add(Plan("Construction", 2, "Construction"));
        world.Destination.Add(Component, "SteelPlate", 5);
        world.Destination.Add(Component, "Construction", 2);
        world.Character.Inventory.Add(Component, "SteelPlate", 999);
        PlacementResolver.Sources.Add(world.Destination);
        PlacementResolver.Sources.Add(world.Destination);
        PlacementResolver.Sources.Add(world.Character.Inventory);
        var summary = PlannerAvailability.Summary;
        Check(summary.Contains("Steel plate: 7 / 5 — short 2") && summary.Contains("Construction: 2 / 2 — ready"),
            "planner preview: duplicate needs aggregate, duplicate inventories and backpack stock are excluded");
        Check(world.Character.BuildPlanner.Count == 3 && world.Character.BuildPlanner[0].Components[0].Count == 4 && MyInventory.Transfers.Count == 0,
            "planner preview: preview never changes planner or transfers inventory");
        var calls = PlacementResolver.Requests.Count;
        _ = PlannerAvailability.Summary;
        Check(PlacementResolver.Requests.Count == calls, "planner preview: repeated refresh is cached within one second");
        LinkController.SetUserPaused(true);
        Check(PlannerAvailability.Summary.Contains("active link") && PlacementResolver.Requests.Count == calls,
            "planner preview: user pause prevents native source discovery and hides cached counts");
        LinkController.SetUserPaused(false);
        CombatLockout.IsBlocked = true;
        _ = PlannerAvailability.Summary;
        Check(PlacementResolver.Requests.Count == calls, "planner preview: combat pause prevents native source discovery");
        CombatLockout.IsBlocked = false;
        world.Selected.PlayerAccess = false;
        Check(!PlannerAvailability.Summary.Contains("7 / 5") && GetTargetedBlock.selectedBlock == null,
            "planner preview: revoked access clears a cached snapshot and breaks link");

        world = new World(true);
        world.Character.BuildPlanner.Add(Plan("SteelPlate", 2));
        PlacementResolver.Resolving = () => MySession.Static = new MySession { LocalCharacter = world.Character };
        Check(PlannerAvailability.Summary.Contains("waiting for connection") && MyInventory.Transfers.Count == 0,
            "planner preview: native callback changing session discards all stock results");
        foreach (var change in new[] { "session", "manual pause" })
        {
            world = new World(true);
            world.Character.BuildPlanner.Add(Plan("SteelPlate", 2));
            world.Destination.Add(Component, "SteelPlate", 5);
            PlacementResolver.Sources.Add(world.Destination);
            var current = world;
            world.Destination.AmountRead = () =>
            {
                if (change == "session") MySession.Static = new MySession { LocalCharacter = current.Character };
                else LinkController.SetUserPaused(true);
            };
            var result = PlannerAvailability.Summary;
            Check(!result.Contains("2 / 5") && result.Contains("waiting for connection") && PlannerAvailability.SupplyIssue == null,
                "planner preview: " + change + " during final stock read prevents stale publication or shortage feedback");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;

internal static partial class Program
{
    private static int assertions;
    private const string Ore = "MyObjectBuilder_Ore";
    private const string Ingot = "MyObjectBuilder_Ingot";
    private const string Component = "MyObjectBuilder_Component";
    private static long grinderNow;
    private static readonly long GrinderFrameTicks = (long)Math.Ceiling(Stopwatch.Frequency / 60d);
    private static readonly MethodInfo CanTransferMethod = typeof(LinkController).GetMethod("CanTransfer", BindingFlags.NonPublic | BindingFlags.Static);

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }

    // Calls the real controller guard while a valid production connection exists.
    // This avoids replacing its lifecycle and access checks with a permission stub.
    private static bool CanTransfer() => (bool)CanTransferMethod.Invoke(null, null);
    private static void Tick(int count = 51)
    {
        for (int i = 0; i < count; i++) { grinderNow += GrinderFrameTicks; LinkController.Update(); }
    }
    private static void AdvanceGrinder(double seconds)
    {
        grinderNow += (long)Math.Round(seconds * Stopwatch.Frequency);
        LinkController.Update();
    }

    private sealed class World
    {
        public readonly MyCharacter Character = new MyCharacter();
        public readonly MyCubeGrid Grid = new MyCubeGrid();
        public readonly MyCubeBlock Selected = new MyCubeBlock();
        public readonly MyCubeBlock Other = new MyCubeBlock { CustomName = "Other container" };
        public readonly MyEntity PortOwner;
        public readonly MyAngleGrinder Grinder = new MyAngleGrinder { IsShooting = true };
        public MyInventory Destination => Selected.Inventories[0];

        public World(bool persistentPort = false)
        {
            LanyardPreferences.Reset();
            Sandbox.Game.MyVisualScriptLogicProvider.Highlighted = null;
            PortOwner = persistentPort ? Selected : new MyEntity();
            LinkController.Disconnect();
            grinderNow = Stopwatch.Frequency * 10;
            typeof(LinkController).GetField("grinderClock", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, (Func<long>)(() => grinderNow));
            MyInventory.Reset();
            PlacementResolver.Reset();
            MyLogReset();
            CombatLockout.IsBlocked = false;
            CombatLockout.Updates = CombatLockout.Reports = 0;
            CombatLockout.EnemyOverlapObservations = 0;
            CombatLockout.FollowedGrid = null;
            CombatLockout.Following = null;
            CombatLockout.Updating = null;
            CombatLockout.Connecting = null;
            CombatLockout.RemainingSeconds = 0;
            LinkArea.NoEnemies = true;
            LinkArea.EnemyScanFails = false;
            LinkArea.Outside = 0;
            LinkArea.BoundsChecks = LinkArea.EnemyChecks = LinkArea.CacheClears = 0;
            BoundsPreview.Connections = BoundsPreview.Disconnections = 0;
            LanyardDiagnostics.Messages.Clear();
            WithdrawRecorder.Calls.Clear();
            Reflections.Withdraw = typeof(WithdrawRecorder).GetMethod(nameof(WithdrawRecorder.ProcessWithdraw));
            Reflections.PlacementSources = typeof(PlacementResolver).GetMethod(nameof(PlacementResolver.GetSources));
            MySession.Static = new MySession { LocalCharacter = Character };
            MyAPIGateway.Session = new GatewaySession();
            MyAPIGateway.Utilities = new Utilities();
            MyAPIGateway.Session.Factions.ByPlayer[1] = new Faction { FactionId = 10 };
            MyAPIGateway.Session.Factions.ByPlayer[2] = new Faction { FactionId = 10 };
            MyAPIGateway.Session.Factions.ByPlayer[99] = new Faction { FactionId = 20 };
            Selected.CubeGrid = Other.CubeGrid = Grid;
            Character.Inventory.Owner = Character;
            Selected.Inventories[0].Owner = Selected;
            Other.Inventories[0].Owner = Other;
            Selected.Inventories.Add(new MyInventory());
            Grid.Blocks.Add(Selected);
            Grid.Blocks.Add(Other);
            Equip("AngleGrinder4Item", Grinder);
            GetTargetedBlock.selectedBlock = Selected;
            GetTargetedBlock.selectedObject = new InventoryPort { Owner = PortOwner };
            LinkController.Connected();
        }

        public void Equip(string name, MyEntity tool)
        {
            Character.HandItemDefinition = new HandItemDefinition { Id = new MyDefinitionId("MyObjectBuilder_PhysicalGunObject", name) };
            Character.EquippedTool = tool;
        }

        public void AddDepositItems()
        {
            Character.Inventory.Add(Ore, "Iron", 3.25);
            Character.Inventory.Add(Ingot, "Nickel", 2.5);
            Character.Inventory.Add(Component, "SteelPlate", 7);
        }
    }

    private static void MyLogReset() => VRage.Utils.MyLog.Default.Messages.Clear();

    private sealed class WithdrawRequest
    {
        public MyEntity Owner;
        public MyInventory Destination;
        public object ThirdArgument;
    }

    private static class WithdrawRecorder
    {
        public static readonly List<WithdrawRequest> Calls = new List<WithdrawRequest>();
        public static void ProcessWithdraw(MyEntity owner, MyInventory destination, object thirdArgument)
        {
            Calls.Add(new WithdrawRequest { Owner = owner, Destination = destination, ThirdArgument = thirdArgument });
        }
    }

    private static void GrinderFiringAndCategories()
    {
        var world = new World();
        world.AddDepositItems();
        world.Grinder.IsShooting = false;
        Check(CanTransfer(), "idle grinder retains an eligible link");
        Tick(153);
        Check(MyInventory.Transfers.Count == 0, "holding an idle grinder never issues transfers");
        Check(world.Character.Inventory.Reads == 0 && world.Selected.InventoryLookups == 0,
            "idle grinder does not enumerate source or resolve destination inventory");

        world.Grinder.IsShooting = true;
        Tick(1);
        Check(MyInventory.Transfers.Count == 3, "first observed grinder firing transfers immediately, before the old 51-update gate");
        Check(MyInventory.Transfers.All(request => request.Source == world.Character.Inventory && request.Destination == world.Destination),
            "every grinder transfer uses current character and selected container inventory");
        Check(MyInventory.Transfers.Select(request => request.Amount).SequenceEqual(new[] { 3.25, 2.5, 7d }),
            "original deposited stack quantities are preserved");
        Check(world.Character.Inventory.Items.Count == 0, "snapshot permits all accepted stacks to transfer when hosting mutates inventory immediately");
        Check(world.Selected.Inventories[1].Items.Count == 0 && world.Other.InventoryLookups == 0 && world.Other.Inventories[0].Items.Count == 0,
            "no secondary inventory or other grid container receives deposits");

        world = new World();
        world.Character.Inventory.Add("MyObjectBuilder_PhysicalGunObject", "AngleGrinder4Item");
        world.Character.Inventory.Add("MyObjectBuilder_AmmoMagazine", "NATO_5p56x45mm");
        world.Character.Inventory.Add("MyObjectBuilder_ConsumableItem", "Medkit");
        world.Character.Inventory.Add("MyObjectBuilder_OxygenContainerObject", "OxygenBottle");
        world.Character.Inventory.Add("MyObjectBuilder_GasContainerObject", "HydrogenBottle");
        world.AddDepositItems();
        Tick();
        Check(MyInventory.Transfers.Count == 3 && MyInventory.Transfers.All(request =>
                request.Item.TypeId.ToString() == Ore || request.Item.TypeId.ToString() == Ingot || request.Item.TypeId.ToString() == Component),
            "fixed deposit categories exclude tools, ammunition, consumables and bottles");
        Check(world.Character.Inventory.Items.Count == 5, "excluded inventory items remain carried");

        world = new World();
        world.AddDepositItems();
        world.Equip("AngleGrinder4Item", new MyWelder());
        Tick();
        Check(MyInventory.Transfers.Count == 0, "grinder item name cannot substitute for an actual equipped MyAngleGrinder");
    }

    private static void FiringStopsBetweenItems()
    {
        var world = new World();
        world.AddDepositItems();
        MyInventory.AfterTransfer = request => world.Grinder.IsShooting = false;
        Tick(1);
        Check(MyInventory.Transfers.Count == 3, "same-grinder release during a batch allows the short grace to finish eligible stacks");
        Check(world.Character.Inventory.Items.Count == 0 && GetTargetedBlock.selectedBlock == world.Selected,
            "same-grinder release keeps the link and drains eligible stacks");
        MyInventory.AfterTransfer = null;
        world.Character.Inventory.Add(Component, "Computer");
        AdvanceGrinder(0.51);
        Check(MyInventory.Transfers.Count == 3, "grinder deposits stop once the grace expires");
        world.Grinder.IsShooting = true;
        Tick(1);
        Check(MyInventory.Transfers.Count == 4, "firing again permits remaining eligible stacks immediately");

        world = new World();
        world.AddDepositItems();
        MyInventory.AfterTransfer = request => world.Character.EquippedTool = new MyWelder();
        Tick();
        Check(MyInventory.Transfers.Count == 1, "swapping away from the firing grinder stops a batch between items");

        world = new World();
        world.AddDepositItems();
        // Release the same grinder inside the real per-update access check.
        var accessPasses = 0;
        world.Selected.AccessCheck = () => { if (++accessPasses >= 2) world.Grinder.IsShooting = false; };
        Tick(1);
        Check(MyInventory.Transfers.Count == 3,
            "same-grinder release after observation remains eligible during the grace");
    }

    private static void OtherToolBehavior()
    {
        var world = new World();
        world.Equip("HandDrill2Item", new MyHandDrill());
        world.AddDepositItems();
        Tick();
        Check(MyInventory.Transfers.Count == 1 && MyInventory.Transfers[0].Item.TypeId.ToString() == Ore,
            "drill retains original ore-only deposit behavior");
        Check(world.Character.Inventory.Items.Count == 2, "drill leaves ingots and components carried");

        world = new World();
        world.Equip("Welder2Item", new MyWelder());
        world.AddDepositItems();
        Tick();
        Check(WithdrawRecorder.Calls.Count == 0 && MyInventory.Transfers.Count == 0, "welder with an empty build planner does no work");
        var plannedItem = new MyIdentity.BuildPlanItem();
        world.Character.BuildPlanner.Add(plannedItem);
        Tick();
        Check(WithdrawRecorder.Calls.Count == 1, "welder with a build planner invokes the normal game withdrawal method once");
        var call = WithdrawRecorder.Calls.Single();
        Check(call.Owner == world.PortOwner && call.Owner != world.Selected,
            "normal withdrawal receives selected inventory port Owner");
        Check(call.Destination == world.Character.Inventory && call.ThirdArgument == null,
            "normal withdrawal receives character inventory and the original null argument");
        Check(world.Character.BuildPlanner.Count == 1 && world.Character.BuildPlanner[0] == plannedItem,
            "controller preserves planner contents for the normal game method");
        Check(MyInventory.Transfers.Count == 0 && world.Other.InventoryLookups == 0,
            "welder dispatch does not deposit or enumerate other grid containers");

        world = new World();
        world.Equip("WelderItem", new MyWelder());
        world.Character.BuildPlanner.Add(new MyIdentity.BuildPlanItem());
        Reflections.Withdraw = null;
        Tick();
        Check(GetTargetedBlock.selectedBlock == null && WithdrawRecorder.Calls.Count == 0,
            "missing normal withdrawal method disconnects without substitute transfers");

        world = new World();
        world.Equip("AutomaticRifleItem", new MyEntity());
        world.AddDepositItems();
        Tick();
        Check(MyInventory.Transfers.Count == 0 && WithdrawRecorder.Calls.Count == 0, "unrelated tools cannot initiate inventory actions");
    }

    private static void LiveGuardRevocation()
    {
        var revocations = new (string Name, Action<World> Revoke, bool KeepLink)[]
        {
            ("manual disconnect", world => LinkController.Disconnect(), false),
            ("combat", world => CombatLockout.IsBlocked = true, true),
            ("access", world => world.Selected.PlayerAccess = false, false),
            ("outside bounds", world => LinkArea.Outside = 0.001, true),
            ("break distance", world => LinkArea.Outside = 100.001, false),
            ("closed container", world => world.Selected.Closed = true, false),
            ("closing container", world => world.Selected.MarkedForClose = true, false),
            ("missing inventory", world => world.Selected.HasInventory = false, false),
            ("closed grid", world => world.Grid.Closed = true, false),
            ("closing grid", world => world.Grid.MarkedForClose = true, false),
            ("dead character", world => world.Character.IsDead = true, false),
            ("closed character", world => world.Character.Closed = true, false),
            ("changed character", world => MySession.Static.LocalCharacter = new MyCharacter(), false),
            ("missing player", world => MySession.Static.LocalHumanPlayer = null, false),
            ("unowned container", world => world.Selected.OwnerId = 0, false),
            ("enemy owner", world => world.Selected.OwnerId = 99, false)
        };
        foreach (var entry in revocations)
        {
            var world = new World();
            world.AddDepositItems();
            Check(CanTransfer(), entry.Name + ": real guard accepts initial connection");
            entry.Revoke(world);
            Check(!CanTransfer(), entry.Name + ": real guard denies revoked connection");
            Tick();
            Check(MyInventory.Transfers.Count == 0 && WithdrawRecorder.Calls.Count == 0, entry.Name + ": revoked link issues no actions");
            Check((GetTargetedBlock.selectedBlock == world.Selected) == entry.KeepLink, entry.Name + ": expected pause/disconnect lifecycle");

            world = new World();
            world.AddDepositItems();
            MyInventory.AfterTransfer = request => entry.Revoke(world);
            Tick();
            Check(MyInventory.Transfers.Count == 1, entry.Name + ": live revocation stops the remaining deposit batch");
            Check(world.Character.Inventory.Items.Count == 2, entry.Name + ": revoked batch leaves unrequested stacks carried");
        }
    }

    private static void SelectedContainerGridTransitions()
    {
        var world = new World(); world.AddDepositItems();
        var fragment = new MyCubeGrid();
        world.Selected.CubeGrid = fragment;
        bool followedBeforeUpdate = false;
        CombatLockout.Following = grid => { if (grid == fragment) CombatLockout.IsBlocked = true; };
        CombatLockout.Updating = () => followedBeforeUpdate = CombatLockout.FollowedGrid == fragment;
        Tick();
        Check(followedBeforeUpdate, "grid transition: controller binds selected container's live grid before polling combat");
        Check(GetTargetedBlock.selectedBlock == world.Selected && MyInventory.Transfers.Count == 0,
            "grid transition: new fragment observation pauses the retained container before any transfer");

        world = new World(); world.AddDepositItems();
        var survivor = new MyCubeGrid();
        world.Selected.CubeGrid = survivor;
        world.Grid.Closed = true;
        CombatLockout.Following = grid => { if (grid == survivor) CombatLockout.IsBlocked = true; };
        Check(!CanTransfer() && CombatLockout.FollowedGrid == survivor,
            "grid transition: direct transfer guard follows the same container after its original grid closes");
        Check(GetTargetedBlock.selectedBlock == world.Selected && MyInventory.Transfers.Count == 0,
            "grid transition: merge-survivor combat blocks the preserved selected container");

        world = new World(); world.AddDepositItems();
        fragment = new MyCubeGrid();
        CombatLockout.Following = grid => { if (grid == fragment) CombatLockout.IsBlocked = true; };
        MyInventory.AfterTransfer = request => world.Selected.CubeGrid = fragment;
        Tick();
        Check(MyInventory.Transfers.Count == 1 && world.Character.Inventory.Items.Count == 2,
            "grid transition: hosted container move after the first deposit stops remaining requests");
        Check(CombatLockout.FollowedGrid == fragment && GetTargetedBlock.selectedBlock == world.Selected,
            "grid transition: per-item guard imports the new grid restriction without waiting for another update");

        world = new World(); world.AddDepositItems();
        survivor = new MyCubeGrid();
        CombatLockout.Following = grid => { if (grid == survivor) CombatLockout.IsBlocked = true; };
        CombatLockout.Updating = () =>
        {
            // Combat session rebinding can clear its anchor during Update.
            CombatLockout.FollowedGrid = null;
            world.Selected.CubeGrid = survivor;
        };
        Tick(1);
        Check(CombatLockout.FollowedGrid == survivor && MyInventory.Transfers.Count == 0,
            "grid transition: transfer guard repairs an anchor cleared by a session update before dispatch");
    }

    private static void AccessOwnershipAndBounds()
    {
        var world = new World();
        world.Selected.OwnerId = 2;
        world.Selected.IDModule.ShareMode = MyOwnershipShareModeEnum.Faction;
        world.AddDepositItems();
        Check(CanTransfer(), "accessible faction-shared selected inventory is eligible");
        Tick();
        Check(MyInventory.Transfers.Count == 3 && world.Other.InventoryLookups == 0, "faction sharing still limits deposits to the selected inventory");

        world = new World();
        world.Selected.OwnerId = 2;
        world.Selected.IDModule.ShareMode = MyOwnershipShareModeEnum.All;
        Check(!CanTransfer(), "Share All is insufficient for another identity's selected container");

        world = new World();
        world.Selected.OwnerId = 99;
        world.Selected.IDModule.ShareMode = MyOwnershipShareModeEnum.Faction;
        Check(!CanTransfer(), "faction sharing cannot permit another faction's container");

        world = new World();
        world.AddDepositItems();
        LinkArea.Outside = 100;
        Check(!CanTransfer() && GetTargetedBlock.selectedBlock == world.Selected, "exact break distance pauses and preserves the link");
        Tick();
        Check(MyInventory.Transfers.Count == 0, "paused link cannot deposit");
        LinkArea.Outside = 0;
        Check(CanTransfer(), "reentering working bounds resumes the same connection");
        Tick();
        Check(MyInventory.Transfers.Count == 3, "resumed connection deposits only after returning inside bounds");

        world = new World();
        world.AddDepositItems();
        LinkArea.NoEnemies = false;
        Tick(1);
        Check(MyInventory.Transfers.Count == 0 && GetTargetedBlock.selectedBlock == world.Selected, "enemy overlap pauses before a transfer batch");
        Check(LinkArea.EnemyChecks == 1 && CombatLockout.EnemyOverlapObservations == 1,
            "first hostile overlap starts the cooldown without dropping the selected container");

        world = new World();
        world.Equip("WelderItem", new MyWelder());
        world.Character.BuildPlanner.Add(new MyIdentity.BuildPlanItem());
        world.AddDepositItems();
        Tick(50);
        Check(MyInventory.Transfers.Count == 0 && WithdrawRecorder.Calls.Count == 0 && LinkArea.EnemyChecks == 1,
            "periodic enemy check runs while waiting for an unchanged welder transfer batch");
        LinkArea.NoEnemies = false;
        Tick(1);
        Check(MyInventory.Transfers.Count == 0 && GetTargetedBlock.selectedBlock == world.Selected,
            "batch boundary enemy scan pauses for enemies appearing after the periodic scan");
    }

    public static void Main()
    {
        PendingLinkFeedbackTests();
        WelderDispatchTests();
        GrinderFiringAndCategories();
        FiringStopsBetweenItems();
        OtherToolBehavior();
        LiveGuardRevocation();
        SelectedContainerGridTransitions();
        AccessOwnershipAndBounds();
        GrinderGraceTests();
        CombatPauseTests();
        PlacementSupplyTests();
        ConnectionPersistenceTests();
        QualityOfLifeTests();
        PersistentCapacityFeedbackTests();
        Console.WriteLine($"PASS: {assertions} container-link integration assertions (linked production LinkController; offline game API doubles).");
    }
}

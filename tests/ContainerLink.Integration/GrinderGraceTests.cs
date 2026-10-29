using System;
using System.Diagnostics;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

internal static partial class Program
{
    private static void GrinderGraceTests()
    {
        var world = new World();
        Tick(1);
        world.Character.Inventory.Add(Component, "SteelPlate");
        Tick(5);
        Check(MyInventory.Transfers.Count == 0, "grinder cadence: active retries wait roughly 0.1 seconds");
        Tick(1);
        Check(MyInventory.Transfers.Count == 1, "grinder cadence: active deposit runs at six normal updates rather than 51");

        world = new World();
        Tick(1);
        world.Grinder.IsShooting = false;
        world.Character.Inventory.Add(Component, "SteelPlate");
        AdvanceGrinder(0.01);
        Check(MyInventory.Transfers.Count == 1, "grinder stop: release flushes immediately before the active retry deadline");
        world.Character.Inventory.Add(Component, "Computer");
        AdvanceGrinder(0.11);
        Check(MyInventory.Transfers.Count == 2, "grinder stop: delayed final material is collected by a grace retry");

        world = new World();
        Tick(1);
        world.Grinder.IsShooting = false;
        AdvanceGrinder(0);
        world.Character.Inventory.Add(Component, "SteelPlate");
        AdvanceGrinder(0.49);
        Check(MyInventory.Transfers.Count == 1, "grinder grace: delayed material at 0.49 seconds is still eligible");
        world.Character.Inventory.Add(Component, "Computer");
        AdvanceGrinder(0.01);
        Check(MyInventory.Transfers.Count == 1 && world.Character.Inventory.Items.Count == 1,
            "grinder grace: exactly 0.5 seconds expires the window without extending it");
        AdvanceGrinder(0.6);
        Check(MyInventory.Transfers.Count == 1, "grinder grace: later idle observations cannot renew the window");

        world = new World();
        Tick(1);
        world.Grinder.IsShooting = false;
        AdvanceGrinder(0);
        world.Character.Inventory.Add(Component, "SteelPlate");
        world.Equip("WelderItem", new MyWelder());
        Tick(1);
        world.Equip("AngleGrinder4Item", world.Grinder);
        Tick(1);
        Check(MyInventory.Transfers.Count == 0, "grinder identity: switching away and back cancels the previous grace");
        world.Equip("AngleGrinder4Item", new MyAngleGrinder { IsShooting = false });
        Tick(1);
        Check(MyInventory.Transfers.Count == 0, "grinder identity: another idle grinder cannot inherit the window");

        world = new World();
        Tick(1);
        world.Grinder.IsShooting = false;
        AdvanceGrinder(0);
        LinkController.Connected();
        world.Character.Inventory.Add(Component, "SteelPlate");
        Tick(1);
        Check(MyInventory.Transfers.Count == 0, "grinder lifecycle: connecting again cancels grace even for the same tool");

        world = new World();
        Tick(1);
        world.Grinder.IsShooting = false;
        AdvanceGrinder(0);
        LinkController.Disconnect();
        GetTargetedBlock.selectedBlock = world.Selected;
        GetTargetedBlock.selectedObject = new InventoryPort { Owner = world.PortOwner };
        LinkController.Connected();
        world.Character.Inventory.Add(Component, "SteelPlate");
        Tick(1);
        Check(MyInventory.Transfers.Count == 0, "grinder lifecycle: disconnect/relink cannot reuse a grace window");

        world = new World();
        Tick(1);
        LinkArea.Outside = 1;
        world.Grinder.IsShooting = false;
        AdvanceGrinder(0);
        AdvanceGrinder(0.6);
        LinkArea.Outside = 0;
        world.Character.Inventory.Add(Component, "SteelPlate");
        Tick(1);
        Check(MyInventory.Transfers.Count == 0, "grinder pause: stop is observed outside bounds and cannot arm a fresh tail on late reentry");

        world = new World();
        Tick(1);
        world.Grinder.IsShooting = false;
        AdvanceGrinder(0);
        var gateway = MyAPIGateway.Session;
        MyAPIGateway.Session = null;
        Tick(1);
        MyAPIGateway.Session = gateway;
        GetTargetedBlock.selectedBlock = world.Selected;
        GetTargetedBlock.selectedObject = new InventoryPort { Owner = world.PortOwner };
        LinkController.Connected();
        world.Character.Inventory.Add(Component, "SteelPlate");
        Tick(1);
        Check(MyInventory.Transfers.Count == 0, "grinder lifecycle: world unload clears recent firing state");

        var revocations = new (string Name, Action<World> Revoke)[]
        {
            ("combat", value => CombatLockout.IsBlocked = true),
            ("access", value => value.Selected.PlayerAccess = false),
            ("bounds", value => LinkArea.Outside = 1),
            ("character", value => Sandbox.Game.World.MySession.Static.LocalCharacter = new MyCharacter()),
            ("tool swap", value => value.Character.EquippedTool = new MyWelder()),
            ("disconnect", value => LinkController.Disconnect()),
            ("expired grace", value => grinderNow += Stopwatch.Frequency)
        };
        foreach (var entry in revocations)
        {
            world = new World();
            Tick(1);
            world.Grinder.IsShooting = false;
            AdvanceGrinder(0);
            world.AddDepositItems();
            var captured = world;
            MyInventory.AfterTransfer = _ => entry.Revoke(captured);
            AdvanceGrinder(0.11);
            Check(MyInventory.Transfers.Count == 1 && world.Character.Inventory.Items.Count == 2,
                "grinder grace per-item: " + entry.Name + " aborts remaining requests");
        }

        world = new World();
        for (var index = 0; index < 40; index++) world.Character.Inventory.Add(Component, "Component" + index);
        Tick(1);
        Check(MyInventory.Transfers.Count == 40 && LinkArea.EnemyChecks == 1,
            "grinder enemy scan: forty same-grid stacks use one fresh batch scan");

        world = new World();
        Tick(1);
        world.Grinder.IsShooting = false;
        AdvanceGrinder(0);
        world.AddDepositItems();
        MyInventory.AfterTransfer = _ => LinkArea.NoEnemies = false;
        AdvanceGrinder(0.11);
        MyInventory.AfterTransfer = null;
        world.Character.Inventory.Add(Component, "Computer");
        AdvanceGrinder(0.11);
        Check(MyInventory.Transfers.Count == 3 && world.Character.Inventory.Items.Count == 1 && GetTargetedBlock.selectedBlock == null,
            "grinder enemy scan: new overlap blocks the next fast grace batch");

        world = new World();
        world.AddDepositItems();
        var fragment = new MyCubeGrid();
        var selected = world.Selected;
        MyInventory.AfterTransfer = _ => selected.CubeGrid = fragment;
        Tick(1);
        Check(MyInventory.Transfers.Count == 3 && LinkArea.EnemyChecks == 2,
            "grinder enemy scan: anchor-grid change causes one extra scan within the batch");

        world = new World();
        world.AddDepositItems();
        selected = world.Selected;
        MyInventory.AfterTransfer = _ => { selected.CubeGrid = new MyCubeGrid(); LinkArea.NoEnemies = false; };
        Tick(1);
        Check(MyInventory.Transfers.Count == 1 && LinkArea.EnemyChecks == 2 && GetTargetedBlock.selectedBlock == null,
            "grinder enemy scan: hostile changed anchor blocks remaining batch requests");

        world = new World();
        Tick(1);
        world.Grinder.IsShooting = false;
        AdvanceGrinder(0);
        world.Equip("BlockPlacerItem", new MyBlockPlacerBase());
        world.Destination.Add(Component, "SteelPlate", 5);
        PlacementResolver.Sources.Add(world.Destination);
        LinkController.RequestPlacementComponent(PlacementDefinition(), false);
        Tick(1);
        Check(MyInventory.Transfers.Count == 1 && world.Character.Inventory.GetItemAmount(FirstComponent) == 1 &&
            MyInventory.Transfers[0].Destination == world.Character.Inventory,
            "grinder grace: swapping to placement preserves the newly supplied starting component");
    }
}

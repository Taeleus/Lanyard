using System;
using System.Collections.Generic;
using System.Linq;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.ModAPI;
using VRage.Game;

internal static partial class Program
{
    private static void PersistentCapacityFeedbackTests()
    {
        var world = new World();
        world.AddDepositItems();
        world.Destination.Capacity = 0;
        Tick(1);
        Check(TransferFeedback.CapacityText == "Linked container full." && MyInventory.Transfers.Count == 0,
            "capacity: a verified full selected destination has dedicated persistent text");
        Check(!TransferFeedback.Text.Contains("full") && !MyAPIGateway.Utilities.Messages.Any(message => message.Contains("full")),
            "capacity: full status does not produce transient feedback or notification spam");
        world.Equip("None", null);
        Tick(180);
        Check(TransferFeedback.CapacityText == "Linked container full." && MyInventory.Transfers.Count == 0,
            "capacity: no equipped tool does not expire a still-full container warning");
        LinkController.SetUserPaused(true);
        CombatLockout.IsBlocked = true;
        Tick(1);
        Check(TransferFeedback.CapacityText == "Linked container full." && LinkController.IsPaused,
            "capacity: manual and combat pauses retain the actual full condition");
        LanyardPreferences.Reset();
        Tick(1);
        Check(TransferFeedback.CapacityText == "Linked container full.",
            "capacity: preference reset does not erase an observed still-full destination");
        world.Destination.Capacity = 100;
        Tick(1);
        Check(TransferFeedback.CapacityText == "" && MyInventory.Transfers.Count == 0,
            "capacity: making room clears the warning while combat is paused without any transfer");

        world = PlacementWorld();
        world.Character.Inventory.Capacity = 0;
        RequestPlacement();
        Check(TransferFeedback.CapacityText == "Backpack full." && MyInventory.Transfers.Count == 0,
            "capacity: failed starter supply captures the local backpack condition");
        world.Equip("None", null);
        LinkController.SetUserPaused(true);
        Tick(120);
        Check(TransferFeedback.CapacityText == "Backpack full.",
            "capacity: backpack full persists independently of the requesting tool and personal pause");
        world.Character.Inventory.Capacity = 1;
        Tick(1);
        Check(TransferFeedback.CapacityText == "" && MyInventory.Transfers.Count == 0,
            "capacity: room for the captured whole starter component clears backpack feedback");

        world = new World();
        world.Equip("WelderItem", new Sandbox.Game.Weapons.MyWelder());
        world.Character.BuildPlanner.Add(Plan("SteelPlate", 2));
        world.Character.Inventory.Capacity = 0;
        Tick(51);
        Check(TransferFeedback.CapacityText == "Backpack full.",
            "capacity: planner observations use the same persistent backpack warning");
        var inventory = world.Character.Inventory;
        inventory.Capacity = 100;
        world.Equip("None", null);
        Tick(1);
        Check(TransferFeedback.CapacityText == "", "capacity: planner full warning clears after space becomes available");

        world = new World();
        world.Character.Inventory.Capacity = world.Destination.Capacity = 0;
        for (var index = 0; index < 20; index++)
            TransferFeedback.WatchCapacity(world.Character.Inventory, new MyDefinitionId(Component, "Planned" + index), true);
        TransferFeedback.WatchCapacity(world.Destination, new MyDefinitionId(Component, "SteelPlate"), false);
        Check(TransferFeedback.CapacityText == "Backpack full. Linked container full.",
            "capacity: bounded planner observations leave space to retain both local full conditions");
        world.Character.Inventory.Capacity = 1;
        TransferFeedback.UpdateCapacity();
        Check(TransferFeedback.CapacityText == "Linked container full." && MyInventory.Transfers.Count == 0,
            "capacity: freeing the backpack clears only its full condition");

        foreach (var backpack in new[] { true, false })
        foreach (var failure in new[] { "throw", "NaN", "infinity" })
        {
            world = backpack ? PlacementWorld() : new World();
            var captured = backpack ? world.Character.Inventory : world.Destination;
            captured.Capacity = 0;
            TransferFeedback.WatchCapacity(captured, new MyDefinitionId(Component, "SteelPlate"), backpack);
            var expected = backpack ? "Backpack full." : "Linked container full.";
            Check(TransferFeedback.CapacityText == expected, "capacity: " + failure + " test starts with verified full state");
            world.Equip("None", null);
            LinkController.SetUserPaused(true);
            captured.CapacityCheck = failure == "throw" ? (Action)(() => throw new InvalidOperationException("Capacity temporarily unavailable.")) : null;
            captured.Capacity = failure == "NaN" ? double.NaN : failure == "infinity" ? double.PositiveInfinity : 0;
            Tick(3);
            Check(TransferFeedback.CapacityText == expected && MyInventory.Transfers.Count == 0,
                "capacity: paused no-tool " + failure + " probe preserves the last verified " + (backpack ? "backpack" : "container") + " condition");
            captured.CapacityCheck = null;
            captured.Capacity = 0;
            Tick(1);
            Check(TransferFeedback.CapacityText == expected,
                "capacity: a recovered still-full probe retains the warning without requesting tools again");
            captured.Capacity = 100;
            Tick(1);
            Check(TransferFeedback.CapacityText == "" && MyInventory.Transfers.Count == 0,
                "capacity: verified room after a transient " + failure + " clears the paused warning");
        }

        world = new World();
        world.Destination.Capacity = 0;
        TransferFeedback.WatchCapacity(world.Destination, new MyDefinitionId(Component, "SteelPlate"), false);
        world.Equip("None", null);
        LinkController.SetUserPaused(true);
        world.Destination.CapacityCheck = () => throw new InvalidOperationException("Capacity temporarily unavailable.");
        world.Selected.PlayerAccess = false;
        TransferFeedback.UpdateCapacity();
        Check(TransferFeedback.CapacityText == "" && MyInventory.Transfers.Count == 0,
            "capacity: losing access still removes a warning when the capacity probe is unavailable");
        world = new World();
        world.Destination.Capacity = 0;
        TransferFeedback.WatchCapacity(world.Destination, new MyDefinitionId(Component, "SteelPlate"), false);
        world.Destination.AcceptsItem = false;
        TransferFeedback.UpdateCapacity();
        Check(TransferFeedback.CapacityText == "", "capacity: definite constraint rejection clears the obsolete full cause");

        foreach (var loss in new[] { "unlink", "target", "port", "character", "session", "context", "access", "owner", "inventory" })
        {
            world = new World();
            world.AddDepositItems();
            world.Destination.Capacity = 0;
            Tick(1);
            switch (loss)
            {
                case "unlink": LinkController.Disconnect(); break;
                case "target": GetTargetedBlock.selectedBlock = world.Other; break;
                case "port": GetTargetedBlock.selectedObject = new InventoryPort { Owner = world.PortOwner }; break;
                case "character": Sandbox.Game.World.MySession.Static.LocalCharacter = new MyCharacter(); break;
                case "session": Sandbox.Game.World.MySession.Static = new Sandbox.Game.World.MySession { LocalCharacter = world.Character }; break;
                case "context": Sandbox.Game.World.MySession.Static.WorldId = Guid.NewGuid(); break;
                case "access": world.Selected.PlayerAccess = false; break;
                case "owner": world.Selected.OwnerId = 99; break;
                case "inventory": world.Selected.Inventories[0] = new MyInventory { Owner = world.Selected }; break;
            }
            TransferFeedback.UpdateCapacity();
            Check(TransferFeedback.CapacityText == "", "capacity: " + loss + " loss clears the captured warning");
            Check(MyInventory.Transfers.Count == 0, "capacity: " + loss + " feedback polling never requests transfers");
        }

        world = new World();
        world.AddDepositItems();
        world.Destination.Capacity = 0;
        Tick(1);
        world.Destination.CapacityCheck = () => LinkController.Disconnect();
        TransferFeedback.UpdateCapacity();
        Check(TransferFeedback.CapacityText == "" && GetTargetedBlock.selectedBlock == null && MyInventory.Transfers.Count == 0,
            "capacity: unlink from an inventory probe cannot republish stale state or transfer items");
        world = new World();
        world.AddDepositItems();
        world.Destination.Capacity = 0;
        Tick(1);
        world.Destination.CapacityCheck = () => LinkController.SetUserPaused(true);
        TransferFeedback.UpdateCapacity();
        Check(TransferFeedback.CapacityText == "Linked container full." && LinkController.IsPaused && MyInventory.Transfers.Count == 0,
            "capacity: a reentrant personal pause preserves true capacity without allowing transfers");
        world.Destination.CapacityCheck = null;
        world.Selected.AccessCheck = () => LinkController.Disconnect();
        TransferFeedback.UpdateCapacity();
        Check(TransferFeedback.CapacityText == "" && MyInventory.Transfers.Count == 0,
            "capacity: access-probe context changes discard feedback without inspecting a stale destination");

        world = new World();
        var order = new List<string>();
        BoundsPreview.Disconnected = () => order.Add("range hidden");
        LanyardDiagnostics.Showing = _ => order.Add("link ended");
        try
        {
            LinkArea.Outside = LinkArea.BreakDistance + 1;
            Tick(1);
            Check(order.SequenceEqual(new[] { "range hidden", "link ended" }),
                "range: hard disconnect cancels the retained warning before reporting the ended link");
        }
        finally { BoundsPreview.Disconnected = null; LanyardDiagnostics.Showing = null; }
    }
}

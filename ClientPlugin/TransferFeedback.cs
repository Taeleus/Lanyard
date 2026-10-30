using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Lanyard;

// Requests are not acknowledgements. Only a subsequent observed inventory
// increase earns an arrival message; unresolved requests expire quietly.
internal static class TransferFeedback
{
    private sealed class Arrival
    {
        public MySession Session;
        public MyCharacter Character;
        public IMyTerminalBlock Block;
        public IMyUseObject Port;
        public MyInventory Inventory;
        public MyDefinitionId Item;
        public double Before;
        public string Name;
        public bool Starter, Returning;
        public long Expires;
    }

    private sealed class Capacity
    {
        public MySession Session;
        public MyCharacter Character;
        public IMyTerminalBlock Block;
        public IMyUseObject Port;
        public MyInventory Inventory;
        public MyDefinitionId Item;
        public bool Backpack;
        public bool Verified;
    }

    private static readonly List<Arrival> arrivals = new List<Arrival>();
    private static readonly List<Capacity> capacities = new List<Capacity>();
    private static readonly MyStringHash CharacterInventoryId = MyStringHash.GetOrCompute("Inventory");
    private static string text, lastNotice;
    private static string capacityText = "";
    private static bool updatingCapacity;
    private static int capacityGeneration;
    private static long expiresAt, nextNotice, lastNoticeAt;
    public static string Text => Stopwatch.GetTimestamp() < expiresAt ? text ?? "" : "";
    public static string CapacityText => capacityText;

    public static void Clear()
    {
        ClearTransient();
        capacities.Clear();
        capacityText = "";
        capacityGeneration++;
    }

    public static void ClearTransient()
    {
        arrivals.Clear();
        text = null;
        expiresAt = 0;
    }

    public static void WatchCapacity(MyInventory inventory, MyDefinitionId item, bool backpack)
    {
        if (inventory == null) return;
        var sameRole = 0;
        foreach (var pending in capacities)
        {
            if (pending.Backpack == backpack) sameRole++;
            if (ReferenceEquals(pending.Inventory, inventory) && pending.Item.Equals(item) && pending.Backpack == backpack) return;
        }
        if (sameRole >= 8) return;
        capacities.Add(new Capacity
        {
            Session = MySession.Static,
            Character = MySession.Static?.LocalCharacter,
            Block = GetTargetedBlock.selectedBlock,
            Port = GetTargetedBlock.selectedObject,
            Inventory = inventory,
            Item = item,
            Backpack = backpack
        });
        UpdateCapacity();
    }

    private static bool HasCapacityContext(Capacity pending)
    {
        var character = MySession.Static?.LocalCharacter;
        var block = GetTargetedBlock.selectedBlock;
        return LinkRecovery.HasCurrentContext && ReferenceEquals(pending.Session, MySession.Static) &&
            ReferenceEquals(pending.Character, character) && character != null && !character.IsDead && !character.Closed && !character.MarkedForClose &&
            ReferenceEquals(pending.Block, block) && block != null && !block.Closed && !block.MarkedForClose &&
            ReferenceEquals(pending.Port, GetTargetedBlock.selectedObject) && pending.Port?.Owner != null &&
            !pending.Port.Owner.Closed && !pending.Port.Owner.MarkedForClose &&
            pending.Inventory.Owner != null && !pending.Inventory.Owner.Closed && !pending.Inventory.Owner.MarkedForClose &&
            block.CubeGrid is MyCubeGrid grid && !grid.Closed && !grid.MarkedForClose;
    }

    private static bool HasCapacityAccess(Capacity pending)
    {
        if (!HasCapacityContext(pending) || !LinkController.HasAccess(pending.Block) || !HasCapacityContext(pending)) return false;
        var current = pending.Backpack ? (object)pending.Character.GetInventory(CharacterInventoryId) : pending.Block.GetInventory();
        return ReferenceEquals(current, pending.Inventory) && HasCapacityContext(pending);
    }

    public static void UpdateCapacity()
    {
        if (updatingCapacity) return;
        updatingCapacity = true;
        var generation = capacityGeneration;
        try
        {
            foreach (var pending in capacities.ToArray())
            {
                try
                {
                    if (!capacities.Contains(pending)) continue;
                    if (!HasCapacityAccess(pending)) { capacities.Remove(pending); continue; }
                    var accepts = pending.Inventory.CheckConstraint(pending.Item);
                    if (!accepts) { capacities.Remove(pending); continue; }
                    var room = (double)pending.Inventory.ComputeAmountThatFits(pending.Item);
                    if (!capacities.Contains(pending)) continue;
                    if (!HasCapacityAccess(pending)) { capacities.Remove(pending); continue; }
                    if (double.IsNaN(room) || double.IsInfinity(room)) continue;
                    if (pending.Backpack ? room >= 1 : room > 0) capacities.Remove(pending);
                    else pending.Verified = true;
                }
                catch
                {
                    // An unavailable capacity snapshot does not establish that room was made.
                    try { if (!HasCapacityAccess(pending)) capacities.Remove(pending); }
                    catch { capacities.Remove(pending); }
                }
            }
            if (generation != capacityGeneration) return;
            var backpack = false;
            var container = false;
            foreach (var pending in capacities)
            {
                if (!pending.Verified) continue;
                backpack |= pending.Backpack;
                container |= !pending.Backpack;
            }
            capacityText = backpack && container ? "Backpack full. Linked container full." :
                backpack ? "Backpack full." : container ? "Linked container full." : "";
        }
        finally { updatingCapacity = false; }
    }

    public static void CancelArrival(MyInventory inventory, MyDefinitionId item)
    {
        arrivals.RemoveAll(pending => ReferenceEquals(pending.Inventory, inventory) && pending.Item.Equals(item));
        if (arrivals.Count == 0) { text = null; expiresAt = 0; }
    }

    public static void Report(string message, bool notify = true)
    {
        text = LinkController.DisplayName(message, 104);
        var now = Stopwatch.GetTimestamp();
        expiresAt = now + 15L * Stopwatch.Frequency;
        if (!notify || now < nextNotice || (text == lastNotice && now < lastNoticeAt + 30L * Stopwatch.Frequency)) return;
        lastNotice = text;
        lastNoticeAt = now;
        nextNotice = now + 6L * Stopwatch.Frequency;
        try { MyAPIGateway.Utilities?.ShowNotification(text, 3500, "White"); }
        catch { } // Optional feedback must not interrupt inventory work.
    }

    public static void WatchArrival(MyInventory destination, MyDefinitionId item, string name,
        bool starter = false, bool returning = false)
    {
        try
        {
            if (destination == null) return;
            foreach (var pending in arrivals)
                if (ReferenceEquals(pending.Inventory, destination) && pending.Item.Equals(item)) return;
            if (arrivals.Count >= 32) return;
            arrivals.Add(new Arrival
            {
                Session = MySession.Static,
                Character = MySession.Static?.LocalCharacter,
                Block = GetTargetedBlock.selectedBlock,
                Port = GetTargetedBlock.selectedObject,
                Inventory = destination,
                Item = item,
                Before = (double)destination.GetItemAmount(item),
                Name = LinkController.DisplayName(string.IsNullOrEmpty(name) ? item.SubtypeName : name, 40),
                Starter = starter,
                Returning = returning,
                Expires = Stopwatch.GetTimestamp() + 5L * Stopwatch.Frequency
            });
            Report(returning ? "Material return requested." : starter
                ? "Starting component requested. Waiting for inventory update." : "Build-planner supply requested. Waiting for inventory update.", false);
        }
        catch { }
    }

    public static void WatchPlanner(MyInventory destination)
    {
        try
        {
            if (destination == null) return;
            var count = 0;
            foreach (var plan in MySession.Static.LocalCharacter.BuildPlanner)
            {
                if (plan?.Components == null) continue;
                foreach (var component in plan.Components)
                {
                    if (++count > 128) return;
                    if (component?.ComponentDefinition == null || component.Count <= 0) continue;
                    var id = component.ComponentDefinition.Id;
                    if (!destination.CheckConstraint(id)) { Report("Backpack does not accept a planned component."); continue; }
                    if (destination.ComputeAmountThatFits(id) < 1) { WatchCapacity(destination, id, true); continue; }
                    WatchArrival(destination, id, component.ComponentDefinition.DisplayNameText);
                }
            }
        }
        catch { }
    }

    public static void Update()
    {
        var now = Stopwatch.GetTimestamp();
        for (var i = arrivals.Count - 1; i >= 0; i--)
        {
            if (i >= arrivals.Count) continue;
            var pending = arrivals[i];
            try
            {
                if (!LinkRecovery.HasCurrentContext || !ReferenceEquals(pending.Session, MySession.Static) ||
                    !ReferenceEquals(pending.Character, MySession.Static?.LocalCharacter) ||
                    !ReferenceEquals(pending.Block, GetTargetedBlock.selectedBlock) ||
                    !ReferenceEquals(pending.Port, GetTargetedBlock.selectedObject) ||
                    pending.Inventory.Owner == null || pending.Inventory.Owner.Closed || pending.Inventory.Owner.MarkedForClose)
                {
                    arrivals.Remove(pending);
                    continue;
                }
                var amount = (double)pending.Inventory.GetItemAmount(pending.Item);
                if (!arrivals.Contains(pending) || !LinkRecovery.HasCurrentContext || LinkController.IsPaused ||
                    !ReferenceEquals(pending.Session, MySession.Static) || !ReferenceEquals(pending.Block, GetTargetedBlock.selectedBlock) ||
                    !ReferenceEquals(pending.Port, GetTargetedBlock.selectedObject) ||
                    !ReferenceEquals(pending.Character, MySession.Static?.LocalCharacter) ||
                    pending.Inventory.Owner == null || pending.Inventory.Owner.Closed || pending.Inventory.Owner.MarkedForClose)
                {
                    arrivals.Remove(pending);
                    continue;
                }
                if (amount > pending.Before)
                {
                    arrivals.Remove(pending);
                    Report(pending.Returning ? "Materials received by linked container." : pending.Name +
                        (pending.Starter ? " received — place again." : " received."), pending.Starter);
                }
                else if (now >= pending.Expires)
                {
                    arrivals.Remove(pending);
                    Report("No inventory update confirmed. Check capacity and available materials.", false);
                }
            }
            catch { arrivals.Remove(pending); }
        }
    }
}

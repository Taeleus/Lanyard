using System;
using System.Linq;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using VRage.Game.Entity;

internal static partial class Program
{
    private sealed class EnhancedWelder : MyWelder { }

    private static World PlannedWelder(MyEntity tool, string definition = "Welder4Item")
    {
        var world = new World();
        world.Equip(definition, tool);
        world.Character.BuildPlanner.Add(Plan("SteelPlate", 2));
        world.Destination.Add(Component, "SteelPlate", 5);
        PlacementResolver.Sources.Add(world.Destination);
        return world;
    }

    private static void WelderDispatchTests()
    {
        var inactive = new (string Name, MyEntity Tool)[]
        {
            ("holstered", null),
            ("ordinary weapon", new MyEntity()),
            ("closed welder", new MyWelder { Closed = true }),
            ("closing welder", new MyWelder { MarkedForClose = true })
        };
        foreach (var entry in inactive)
        {
            var world = PlannedWelder(entry.Tool);
            Tick(153);
            Check(PlacementResolver.Requests.Count == 0,
                "welder dispatch: " + entry.Name + " with stale definition performs no source lookup (observed " + PlacementResolver.Requests.Count + ")");
            Check(world.Character.Inventory.ConstraintChecks == 0 && world.Character.Inventory.CapacityChecks == 0,
                "welder dispatch: " + entry.Name + " performs no planner capacity probes");
            Check(world.Character.Inventory.AmountReads == 0 && world.Destination.AmountReads == 0 && TransferFeedback.Text == "",
                "welder dispatch: " + entry.Name + " performs no planner feedback or stock reads");
            Check(WithdrawRecorder.Calls.Count == 0 && MyInventory.Transfers.Count == 0,
                "welder dispatch: " + entry.Name + " sends no withdrawal or transfer");
            Check(GetTargetedBlock.selectedBlock == world.Selected && world.Character.BuildPlanner.Count == 1,
                "welder dispatch: " + entry.Name + " leaves the link and plan intact");
        }

        foreach (var tool in new MyWelder[] { new MyWelder(), new EnhancedWelder() })
        {
            var world = PlannedWelder(tool, tool is EnhancedWelder ? "CustomRepairTool" : "Welder4Item");
            var plan = world.Character.BuildPlanner.Single();
            Tick(51);
            Check(PlacementResolver.Requests.Count == 1 && world.Destination.AmountReads > 0,
                "welder dispatch: live " + tool.GetType().Name + " previews native accessible supplies");
            Check(world.Character.Inventory.ConstraintChecks > 0 && world.Character.Inventory.CapacityChecks > 0 && world.Character.Inventory.AmountReads > 0,
                "welder dispatch: live " + tool.GetType().Name + " observes planner capacity and arrival baseline");
            Check(WithdrawRecorder.Calls.Count == 1 && WithdrawRecorder.Calls[0].Owner == world.PortOwner &&
                WithdrawRecorder.Calls[0].Destination == world.Character.Inventory && WithdrawRecorder.Calls[0].ThirdArgument == null,
                "welder dispatch: live " + tool.GetType().Name + " preserves native withdrawal arguments");
            Check(world.Character.BuildPlanner.Single() == plan && MyInventory.Transfers.Count == 0,
                "welder dispatch: live " + tool.GetType().Name + " preserves planner ownership and transfer policy");
        }

        foreach (var stage in new[] { "source preview", "arrival baseline" })
        foreach (var change in new[] { "holster", "replace welder", "close welder", "mark welder for close" })
        {
            var tool = new MyWelder();
            var world = PlannedWelder(tool);
            Action changeTool = () =>
            {
                if (change == "holster") world.Character.EquippedTool = null;
                else if (change == "replace welder") world.Character.EquippedTool = new MyWelder();
                else if (change == "close welder") tool.Closed = true;
                else tool.MarkedForClose = true;
            };
            if (stage == "source preview") PlacementResolver.Resolving = changeTool;
            else world.Character.Inventory.AmountRead = changeTool;
            Tick(51);
            Check(PlacementResolver.Requests.Count == 1, "welder dispatch: " + change + " callback runs during " + stage);
            Check(WithdrawRecorder.Calls.Count == 0 && MyInventory.Transfers.Count == 0 && TransferFeedback.Text == "",
                "welder dispatch: " + change + " during " + stage + " cancels withdrawal and transient feedback");
        }
    }
}

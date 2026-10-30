using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace Lanyard;

// Counts remaining native planner requests against the same accessible sources
// used by normal withdrawals. It never requests items or mutates the planner.
internal static class PlannerAvailability
{
    private sealed class Need { public MyDefinitionId Item; public string Name; public long Count; }
    private static long nextRefresh;
    private static string summary;
    private static string supplyIssue;
    private static IMyTerminalBlock block;
    public static string SupplyIssue { get { _ = Summary; return supplyIssue; } }
    public static string Summary
    {
        get
        {
            try
            {
                if (!LinkRecovery.HasCurrentContext || !LinkController.ValidateExistingSelection())
                {
                    Clear();
                    return "Planner preview available with an active link.";
                }
                if (!ReferenceEquals(block, GetTargetedBlock.selectedBlock)) Clear();
                if (LinkController.IsPaused || GetTargetedBlock.selectedBlock == null)
                {
                    supplyIssue = null;
                    return "Planner preview available with an active link.";
                }
                if (Stopwatch.GetTimestamp() >= nextRefresh)
                {
                    nextRefresh = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
                    summary = ReadSummary();
                    block = GetTargetedBlock.selectedBlock;
                }
                return summary ?? "Planner preview unavailable.";
            }
            catch { Clear(); return "Planner preview unavailable."; }
        }
    }

    public static void Clear() { nextRefresh = 0; summary = null; supplyIssue = null; block = null; }

    private static string ReadSummary()
    {
        try
        {
            supplyIssue = null;
            if (!LinkController.CanInspectInventoryState()) return "Planner preview available with an active link.";
            var session = MySession.Static;
            var character = session?.LocalCharacter;
            var selected = GetTargetedBlock.selectedBlock;
            var port = GetTargetedBlock.selectedObject;
            if (character == null || selected == null || port?.Owner == null) return "Planner preview unavailable.";
            if (character.BuildPlanner.Count == 0) return "Build planner is empty.";
            if (Reflections.PlacementSources == null) return "Planner preview unavailable in this game version.";
            var needs = new Dictionary<MyDefinitionId, Need>();
            var scanned = 0;
            var limited = false;
            foreach (var plan in character.BuildPlanner)
            {
                if (plan?.Components == null) continue;
                foreach (var component in plan.Components)
                {
                    if (++scanned > 4096) { limited = true; break; }
                    if (component?.ComponentDefinition == null || component.Count <= 0) continue;
                    var id = component.ComponentDefinition.Id;
                    if (!needs.TryGetValue(id, out var need))
                    {
                        if (needs.Count >= 64) { limited = true; continue; }
                        needs.Add(id, need = new Need
                        {
                            Item = id,
                            Name = LinkController.DisplayName(component.ComponentDefinition.DisplayNameText ?? id.SubtypeName, 28)
                        });
                    }
                    need.Count += component.Count;
                }
                if (scanned > 4096) break;
            }
            if (needs.Count == 0) return "No remaining planner requests.";
            var output = new StringBuilder("Remaining requests / accessible stock\n");
            var shown = 0;
            foreach (var need in needs.Values)
            {
                if (shown++ >= 6) break;
                if (!Matches(session, character, selected, port)) return Aborted();
                var sources = new List<MyInventory>();
                Reflections.PlacementSources.Invoke(null, new object[] { character, need.Item, sources, (MyEntity)port.Owner, true });
                if (!Matches(session, character, selected, port) || !LinkController.CanInspectInventoryState())
                    return Aborted();
                double available = 0;
                var seen = new HashSet<MyInventory>();
                var sourceCount = 0;
                var rowLimited = false;
                foreach (var source in sources)
                {
                    if (++sourceCount > 4096) { limited = rowLimited = true; break; }
                    if (source == null || !seen.Add(source) || ReferenceEquals(source.Owner, character) ||
                        source.Owner == null || source.Owner.Closed || source.Owner.MarkedForClose) continue;
                    available += (double)source.GetItemAmount(need.Item);
                }
                if (!Matches(session, character, selected, port) || !LinkController.CanInspectInventoryState() ||
                    !Matches(session, character, selected, port)) return Aborted();
                var count = (long)Math.Min(long.MaxValue, Math.Max(0, Math.Floor(available)));
                var shortage = Math.Max(0, need.Count - count);
                if (!rowLimited && shortage > 0 && supplyIssue == null)
                    supplyIssue = count == 0 ? need.Name + " unavailable through linked port." : "Linked supplies are short of " + need.Name + ".";
                output.Append(need.Name).Append(": ").Append(need.Count).Append(" / ").Append(count)
                    .Append(shortage == 0 ? " — ready" : rowLimited ? " — preview limited" : " — short " + shortage).Append('\n');
            }
            if (needs.Count > 6) output.Append('+').Append(needs.Count - 6).Append(" more component types.\n");
            if (limited) output.Append("Large plan/network: preview limited.\n");
            if (!Matches(session, character, selected, port)) return Aborted();
            return output.ToString().TrimEnd();
        }
        catch { supplyIssue = null; return "Planner preview unavailable. Normal tool controls remain available."; }
    }

    private static string Aborted() { supplyIssue = null; return "Planner preview waiting for connection."; }

    private static bool Matches(MySession session, Sandbox.Game.Entities.Character.MyCharacter character,
        IMyTerminalBlock selected, VRage.Game.Entity.UseObject.IMyUseObject port) =>
        LinkRecovery.HasCurrentContext && ReferenceEquals(session, MySession.Static) &&
        ReferenceEquals(character, MySession.Static?.LocalCharacter) && ReferenceEquals(selected, GetTargetedBlock.selectedBlock) &&
        ReferenceEquals(port, GetTargetedBlock.selectedObject) && !LinkController.IsPaused;
}

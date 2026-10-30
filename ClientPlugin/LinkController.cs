using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.World;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Lanyard;

public static class LinkController
{
    static LinkController() { LanyardPreferences.Changed += PreferencesChanged; }

    private static void PreferencesChanged()
    {
        pendingPlacement.Clear();
        ClearGrinder();
        TransferFeedback.ClearTransient();
        PlannerAvailability.Clear();
        ticks = 0;
        NotifyStateChanged();
    }
    public static event Action StateChanged;
    private static bool previouslyBlocked;
    private static bool combatPaused;
    internal static bool IsCombatPaused => combatPaused;
    public static bool HasConnection => GetTargetedBlock.selectedBlock != null || LinkRecovery.IsPending;
    public static bool IsPaused => HasConnection && (LinkRecovery.IsPending || combatPaused || CombatLockout.IsBlocked || outOfRange || LanyardPreferences.Current.ManualPaused);
    public static string StateText
    {
        get
        {
            if (GetTargetedBlock.selectedBlock == null)
                return (LinkRecovery.IsPending ? "Waiting for saved container" : "Not linked") +
                    (CombatLockout.IsBlocked ? "; " + SafetyState : "");
            if (combatPaused || CombatLockout.IsBlocked)
                return SafetyState;
            if (outOfRange) return "Paused — outside working area";
            return LanyardPreferences.Current.ManualPaused ? "Paused by you" : "Active";
        }
    }
    private static string SafetyState
    {
        get
        {
            var seconds = CombatLockout.RemainingSeconds;
            return seconds > 0 ? "Cooldown — " + seconds / 60 + ":" + (seconds % 60).ToString("00") : "Paused — waiting for safety checks";
        }
    }
    public static string StatusText => GetTargetedBlock.selectedBlock == null
        ? (LinkRecovery.IsPending ? "Link saved. Waiting for its matching player and inventory port."
            : "Not linked. Aim at an inventory port with a tool to connect.") + (CombatLockout.IsBlocked ? "\n" + SafetyState + "." : "")
        : "Linked to " + DisplayName(GetTargetedBlock.selectedBlock.CustomName) + "\n" + StateText + ".";

    internal static string DisplayName(string value, int limit = 52)
    {
        value = (value ?? "Inventory").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return value.Length > limit ? value.Substring(0, limit - 1) + "…" : value;
    }

    public static void ToggleUserPause() => SetUserPaused(!LanyardPreferences.Current.ManualPaused);

    public static void SetUserPaused(bool paused)
    {
        if (LanyardPreferences.Current.ManualPaused == paused) return;
        LanyardPreferences.Change(settings => settings.ManualPaused = paused);
        pendingPlacement.Clear();
        ClearGrinder();
        TransferFeedback.ClearTransient();
        PlannerAvailability.Clear();
        ticks = 0;
        NotifyStateChanged();
        MyAPIGateway.Utilities?.ShowNotification(paused ? HasConnection ? "Lanyard paused by you. Link retained." : "Lanyard automatic transfers paused."
            : "Lanyard automatic transfers enabled. Safety checks still apply.", 4000, "White");
    }

    private static void NotifyStateChanged()
    {
        try { StateChanged?.Invoke(); }
        catch (Exception exception) { MyLog.Default.WriteLineAndConsole("Lanyard status update failed: " + exception); }
    }

    // Provisional review limits; these never alter game interaction distances.
    private static int enemyCheckTicks;

    private static readonly MyStringHash CharacterInventoryId = MyStringHash.GetOrCompute("Inventory");
    private static int ticks;
    private static bool outOfRange;
    private static MyCharacter linkedCharacter;
    private static readonly Dictionary<MyDefinitionId, long> pendingPlacement = new Dictionary<MyDefinitionId, long>();
    private static Func<long> grinderClock = Stopwatch.GetTimestamp;
    private static MySession grinderSession;
    private static MyAngleGrinder trackedGrinder;
    private static bool grinderWasShooting;
    private static long grinderGraceUntil;
    private static long nextGrinderDeposit;

    public static bool HasAccess(IMyTerminalBlock block)
    {
        var player = MySession.Static?.LocalHumanPlayer;
        if (player == null || block == null || block.Closed || block.MarkedForClose || !block.HasInventory)
            return false;
        var identity = player.Identity.IdentityId;
        if (identity == 0 || block.OwnerId == 0 || !block.HasPlayerAccess(identity))
            return false;
        if (block.OwnerId == identity)
            return true;
        var cube = block as MyCubeBlock;
        if (cube?.IDModule == null || cube.IDModule.ShareMode != MyOwnershipShareModeEnum.Faction)
            return false;
        var factions = MyAPIGateway.Session?.Factions;
        var ours = factions?.TryGetPlayerFaction(identity);
        var theirs = factions?.TryGetPlayerFaction(block.OwnerId);
        return ours != null && theirs != null && ours.FactionId == theirs.FactionId;
    }

    public static void Connected()
    {
        pendingPlacement.Clear();
        ClearGrinder();
        TransferFeedback.Clear();
        PlannerAvailability.Clear();
        BoundsPreview.LinkConnected();
        linkedCharacter = MySession.Static?.LocalCharacter;
        ticks = 0;
        enemyCheckTicks = 0;
        outOfRange = false;
        combatPaused = CombatLockout.IsBlocked;
        LinkRecovery.Remember();
        NotifyStateChanged();
    }

    public static void BeginSession() => LinkRecovery.BeginSession();
    public static void SuspendForWorldChange() => LinkRecovery.SuspendForWorldChange();

    internal static void Restored()
    {
        pendingPlacement.Clear();
        ClearGrinder();
        TransferFeedback.Clear();
        PlannerAvailability.Clear();
        BoundsPreview.LinkConnected();
        linkedCharacter = MySession.Static.LocalCharacter;
        ticks = 0;
        enemyCheckTicks = 29;
        outOfRange = false;
        combatPaused = true;
        NotifyStateChanged();
    }

    public static void Disconnect(string message = null)
    {
        var pending = LinkRecovery.IsPending;
        LinkRecovery.Forget();
        ClearLiveLink();
        if (message != null) LanyardDiagnostics.Show(message);
        if (pending) NotifyStateChanged();
    }

    internal static void ClearLiveLink()
    {
        pendingPlacement.Clear();
        ClearGrinder();
        TransferFeedback.Clear();
        PlannerAvailability.Clear();
        var changed = GetTargetedBlock.selectedBlock != null;
        if (GetTargetedBlock.selectedBlock != null) BoundsPreview.LinkDisconnected();
        LinkArea.ClearCache();
        GetTargetedBlock.ClearSelection();
        linkedCharacter = null;
        outOfRange = false;
        combatPaused = false;
        ticks = 0;
        enemyCheckTicks = 0;
        if (changed) NotifyStateChanged();
    }

    private static void SetCombatPaused(bool paused)
    {
        if (combatPaused == paused) return;
        combatPaused = paused;
        if (paused)
        {
            pendingPlacement.Clear();
            ClearGrinder();
            ticks = 0;
            TransferFeedback.ClearTransient();
            PlannerAvailability.Clear();
        }
        LinkRecovery.RememberPause();
        NotifyStateChanged();
        MyAPIGateway.Utilities?.ShowNotification(paused
            ? "Lanyard paused. Link retained."
            : outOfRange || LanyardPreferences.Current.ManualPaused ? "Lanyard is clear; transfers remain paused." : "Lanyard resumed.", 4000, "White");
    }

    private static void ClearGrinder()
    {
        grinderSession = null;
        trackedGrinder = null;
        grinderWasShooting = false;
        grinderGraceUntil = 0;
        nextGrinderDeposit = 0;
    }

    private static bool ObserveGrinder(bool scheduleImmediateDeposit = true)
    {
        if (!LanyardPreferences.Current.GrinderReturns || LanyardPreferences.Current.ManualPaused)
        {
            ClearGrinder();
            return false;
        }
        var session = MySession.Static;
        var character = session?.LocalCharacter;
        var grinder = character?.EquippedTool as MyAngleGrinder;
        if (character != linkedCharacter || grinder == null || grinder.Closed || grinder.MarkedForClose)
        {
            ClearGrinder();
            return false;
        }
        if (!ReferenceEquals(session, grinderSession) || !ReferenceEquals(grinder, trackedGrinder))
        {
            ClearGrinder();
            grinderSession = session;
            trackedGrinder = grinder;
        }
        var now = grinderClock();
        if (grinder.IsShooting)
        {
            if (!grinderWasShooting && scheduleImmediateDeposit) nextGrinderDeposit = 0;
            grinderWasShooting = true;
            grinderGraceUntil = 0;
            return true;
        }
        if (grinderWasShooting)
        {
            grinderWasShooting = false;
            grinderGraceUntil = now + Stopwatch.Frequency / 2;
            // An ongoing batch already flushes its remaining items after release.
            if (scheduleImmediateDeposit) nextGrinderDeposit = 0;
        }
        return now < grinderGraceUntil;
    }

    private static bool ValidateLink()
    {
        if (!LinkRecovery.HasCurrentContext)
        {
            SuspendForWorldChange();
            return false;
        }
        if (MySession.Static == null || MyAPIGateway.Session == null)
        {
            Disconnect();
            return false;
        }
        var character = MySession.Static?.LocalCharacter;
        var block = GetTargetedBlock.selectedBlock;
        if (block == null) return false;
        if (character == null || character != linkedCharacter || character.Closed || character.IsDead)
        {
            Disconnect("Lanyard disconnected: character changed or unavailable.");
            return false;
        }
        if (!HasAccess(block))
        {
            Disconnect("Lanyard disconnected: inventory unavailable or access revoked.");
            return false;
        }
        var portOwner = GetTargetedBlock.selectedObject?.Owner;
        if (portOwner == null || portOwner.Closed || portOwner.MarkedForClose)
        {
            Disconnect("Lanyard disconnected: inventory port unavailable.");
            return false;
        }
        var grid = block.CubeGrid as MyCubeGrid;
        if (grid == null || grid.Closed || grid.MarkedForClose)
        {
            Disconnect("Lanyard disconnected: grid unavailable.");
            return false;
        }
        CombatLockout.FollowGrid(grid);
        var distance = LinkArea.DistanceOutside(grid, character.PositionComp.GetPosition());
        if (distance > LinkArea.BreakDistance)
        {
            Disconnect("Link ended: more than 100 m outside the grid bounds.");
            return false;
        }
        var paused = distance > 0;
        if (paused != outOfRange)
        {
            outOfRange = paused;
            NotifyStateChanged();
            MyAPIGateway.Utilities?.ShowNotification(paused
                ? "Outside linked grid bounds. Transfers paused."
                : combatPaused || CombatLockout.IsBlocked ? "Back inside the working area. Lanyard remains paused."
                    : "Back inside the working area. Transfers resumed.", 4000, "White");
        }
        return true;
    }

    internal static bool ValidateExistingSelection() => ValidateLink();

    // Read-only previews use the same live context and safety guards as work.
    internal static bool CanInspectInventoryState()
    {
        CombatLockout.Update();
        return CanTransfer() && CheckEnemies();
    }

    private static bool CanTransfer()
    {
        if (!ValidateLink()) return false;
        // A clear timer alone is insufficient if an overlap is still present.
        if (combatPaused && !CombatLockout.IsBlocked && !CheckEnemies()) return false;
        SetCombatPaused(CombatLockout.IsBlocked);
        return !combatPaused && !outOfRange && !LanyardPreferences.Current.ManualPaused;
    }

    public static void Update()
    {
        TransferFeedback.UpdateCapacity();
        try
        {
            if (!LinkRecovery.Update()) return;
        }
        catch (Exception exception)
        {
            MyLog.Default?.WriteLineAndConsole("Lanyard recovery waiting: " + exception.Message);
            SuspendForWorldChange();
            return;
        }
        // Hard failures still end a retained link while combat has it paused.
        if (GetTargetedBlock.selectedBlock != null && !ValidateLink()) return;
        // Observe release outside bounds, but never renew a grinder tail during combat.
        if (!combatPaused) ObserveGrinder();
        CombatLockout.Update();
        var blocked = CombatLockout.IsBlocked;
        if (blocked != previouslyBlocked)
        {
            previouslyBlocked = blocked;
            NotifyStateChanged();
        }
        if (GetTargetedBlock.selectedBlock == null) return;
        // Continue refreshing hostile activity even while the combat timer is active.
        if (++enemyCheckTicks >= 30)
        {
            enemyCheckTicks = 0;
            if (!CheckEnemies()) return;
        }
        // Validate every update, even while waiting between transfer batches.
        if (!CanTransfer()) return;
        TransferFeedback.Update();
        var regularTransferDue = ++ticks >= 51;
        if (regularTransferDue) ticks = 0;
        try
        {
            if (ObserveGrinder())
            {
                var now = grinderClock();
                if (now < nextGrinderDeposit) return;
                if (!CheckEnemies()) return;
                enemyCheckTicks = 0;
                nextGrinderDeposit = now + Stopwatch.Frequency / 10;
                Deposit(false, trackedGrinder);
                return;
            }
            if (!regularTransferDue) return;
            if (!CheckEnemies()) return;
            enemyCheckTicks = 0;
            var character = MySession.Static.LocalCharacter;
            if (character.EquippedTool is MyWelder activeTool)
            {
                if (activeTool.Closed || activeTool.MarkedForClose || !LanyardPreferences.Current.WelderSupply) return;
                if (character.BuildPlanner.Count == 0 || !CanTransfer()) return;
                if (Reflections.Withdraw == null)
                {
                    Disconnect("Lanyard unavailable: game withdrawal method not found.");
                    return;
                }
                var activeBlock = GetTargetedBlock.selectedBlock;
                var activePort = GetTargetedBlock.selectedObject;
                var supplyIssue = PlannerAvailability.SupplyIssue;
                TransferFeedback.WatchPlanner(character.GetInventory(CharacterInventoryId) as MyInventory);
                if (!CanTransfer() || !LanyardPreferences.Current.WelderSupply || activeTool == null || activeTool.Closed || activeTool.MarkedForClose ||
                    !ReferenceEquals(character, MySession.Static?.LocalCharacter) || !ReferenceEquals(activeTool, character.EquippedTool) ||
                    !ReferenceEquals(activeBlock, GetTargetedBlock.selectedBlock) || !ReferenceEquals(activePort, GetTargetedBlock.selectedObject))
                { TransferFeedback.ClearTransient(); return; }
                Reflections.Withdraw.Invoke(null, new object[] {
                    (MyEntity)GetTargetedBlock.selectedObject.Owner,
                    character.GetInventory(CharacterInventoryId), null });
                TransferFeedback.Update();
                if (!string.IsNullOrEmpty(supplyIssue)) TransferFeedback.Report(supplyIssue);
            }
            else if (character.HandItemDefinition?.Id.SubtypeName is string tool && tool.IndexOf("Drill", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!LanyardPreferences.Current.DrillReturns || (LanyardPreferences.Current.DrillActiveOnly &&
                    !(character.EquippedTool is MyHandDrill drill && drill.IsShooting))) return;
                Deposit(true);
            }
        }
        catch (Exception exception)
        {
            MyLog.Default.WriteLineAndConsole("Lanyard transfer failed: " + exception);
            Disconnect("Lanyard disconnected: transfer failed. See game log.");
        }
    }

    public static void RequestPlacementComponent(MyCubeBlockDefinition definition, bool canStart)
    {
        if (!LanyardPreferences.Current.StartingComponents)
        {
            pendingPlacement.Clear();
            return;
        }
        if (definition?.Components == null || definition.Components.Length == 0 || definition.Components[0].Definition == null) return;
        var id = definition.Components[0].Definition.Id;
        if (canStart)
        {
            pendingPlacement.Remove(id);
            return;
        }
        try
        {
            if (!LinkRecovery.Update() || GetTargetedBlock.selectedBlock == null) return;
            CombatLockout.FollowGrid(GetTargetedBlock.selectedBlock.CubeGrid as MyCubeGrid);
            CombatLockout.Update();
            if (!CanTransfer() || !CheckEnemies()) return;
            var character = MySession.Static.LocalCharacter;
            var block = GetTargetedBlock.selectedBlock;
            var port = GetTargetedBlock.selectedObject;
            var destination = character.GetInventory(CharacterInventoryId) as MyInventory;
            if (destination == null || port?.Owner == null) return;
            if (destination.GetItemAmount(id) >= 1)
            {
                pendingPlacement.Remove(id);
                return;
            }
            if (pendingPlacement.TryGetValue(id, out var deadline) && Stopwatch.GetTimestamp() < deadline) return;
            if (Reflections.PlacementSources == null)
            {
                TransferFeedback.Report("Starting-component supply unavailable.");
                return;
            }
            if (!destination.CheckConstraint(id)) { TransferFeedback.Report("Backpack does not accept this component."); return; }
            if (destination.ComputeAmountThatFits(id) < 1) { TransferFeedback.WatchCapacity(destination, id, true); return; }

            var sources = new List<MyInventory>();
            Reflections.PlacementSources.Invoke(null, new object[] { destination.Owner, id, sources, (MyEntity)port.Owner, true });
            foreach (var source in sources)
            {
                if (source == null || ReferenceEquals(source, destination) || source.Owner == null ||
                    source.Owner.Closed || source.Owner.MarkedForClose || source.GetItemAmount(id) < 1) continue;
                CombatLockout.FollowGrid(GetTargetedBlock.selectedBlock?.CubeGrid as MyCubeGrid);
                CombatLockout.Update();
                if (!ReferenceEquals(character, MySession.Static?.LocalCharacter) ||
                    !ReferenceEquals(block, GetTargetedBlock.selectedBlock) || !ReferenceEquals(port, GetTargetedBlock.selectedObject) ||
                    !CanTransfer() || !CheckEnemies() || !LanyardPreferences.Current.StartingComponents) return;
                if (!ReferenceEquals(destination, character.GetInventory(CharacterInventoryId))) return;
                if (destination.GetItemAmount(id) >= 1)
                {
                    pendingPlacement.Remove(id);
                    return;
                }
                if (!destination.CheckConstraint(id)) return;
                if (destination.ComputeAmountThatFits(id) < 1) { TransferFeedback.WatchCapacity(destination, id, true); return; }
                if (pendingPlacement.TryGetValue(id, out deadline) && Stopwatch.GetTimestamp() < deadline) return;
                // Reserve before sending: hosting may complete the transfer synchronously.
                // Expiry permits a retry; it is not a server acknowledgement.
                pendingPlacement[id] = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3;
                TransferFeedback.WatchArrival(destination, id, definition.Components[0].Definition.DisplayNameText ?? id.SubtypeName, true);
                if (!ReferenceEquals(character, MySession.Static?.LocalCharacter) ||
                    !ReferenceEquals(block, GetTargetedBlock.selectedBlock) || !ReferenceEquals(port, GetTargetedBlock.selectedObject) ||
                    !CanTransfer() || !LanyardPreferences.Current.StartingComponents)
                { pendingPlacement.Remove(id); TransferFeedback.CancelArrival(destination, id); return; }
                MyInventory.TransferByPlanner(source, destination, id, MyItemFlags.None, 1, false);
                TransferFeedback.Update();
                return;
            }
            TransferFeedback.Report("Starting component unavailable through linked port.");
        }
        catch (Exception exception)
        {
            MyLog.Default.WriteLineAndConsole("Lanyard placement transfer failed: " + exception);
            Disconnect("Lanyard disconnected: transfer failed. See game log.");
        }
    }

    private static bool CheckEnemies()
    {
        var grid = GetTargetedBlock.selectedBlock?.CubeGrid as MyCubeGrid;
        if (LinkArea.CheckEnemies(grid, out var reason, out var hostileOverlap)) return true;
        if (hostileOverlap)
        {
            CombatLockout.FollowGrid(grid);
            CombatLockout.ObserveEnemyOverlap();
            SetCombatPaused(true);
            return false;
        }
        Disconnect(reason);
        return false;
    }
    private static void Deposit(bool oreOnly, MyAngleGrinder grinder = null)
    {
        var checkedGrid = GetTargetedBlock.selectedBlock?.CubeGrid as MyCubeGrid;
        var character = MySession.Static.LocalCharacter;
        var drillTool = oreOnly ? character.EquippedTool as MyEntity : null;
        var source = (MyInventory)character.GetInventory(CharacterInventoryId);
        // Transfers can mutate the inventory immediately when hosting a world.
        foreach (var item in source.GetItems().ToArray())
        {
            var type = item.Content.GetObjectId().TypeId.ToString();
            var accepted = type == "MyObjectBuilder_Ore" || (!oreOnly &&
                (type == "MyObjectBuilder_Ingot" || type == "MyObjectBuilder_Component"));
            if (!accepted) continue;
            if (!CanTransfer()) return;
            if (oreOnly && !CanReturnDrillOre(character, drillTool)) return;
            if (!oreOnly)
            {
                if (!ObserveGrinder(false) || !ReferenceEquals(grinder, trackedGrinder)) return;
            }
            var grid = GetTargetedBlock.selectedBlock?.CubeGrid as MyCubeGrid;
            if (!ReferenceEquals(grid, checkedGrid))
            {
                if (!CheckEnemies()) return;
                checkedGrid = grid;
            }
            var selected = GetTargetedBlock.selectedBlock;
            var port = GetTargetedBlock.selectedObject;
            var destination = (MyInventory)selected.GetInventory();
            if (!destination.CheckConstraint(item.Content.GetObjectId()))
            {
                TransferFeedback.Report("Linked container does not accept this material.");
                continue;
            }
            if (destination.ComputeAmountThatFits(item.Content.GetObjectId()) <= 0)
            {
                TransferFeedback.WatchCapacity(destination, item.Content.GetObjectId(), false);
                continue;
            }
            if (!CanTransfer() || (oreOnly ? !LanyardPreferences.Current.DrillReturns : !LanyardPreferences.Current.GrinderReturns)) return;
            TransferFeedback.WatchArrival(destination, item.Content.GetObjectId(), "Materials", false, true);
            if (!ReferenceEquals(selected, GetTargetedBlock.selectedBlock) || !ReferenceEquals(port, GetTargetedBlock.selectedObject) ||
                !ReferenceEquals(source, MySession.Static?.LocalCharacter?.GetInventory(CharacterInventoryId)) || !CanTransfer())
            { TransferFeedback.CancelArrival(destination, item.Content.GetObjectId()); return; }
            if (oreOnly ? !CanReturnDrillOre(character, drillTool) : !ObserveGrinder(false) || !ReferenceEquals(grinder, trackedGrinder))
            { TransferFeedback.CancelArrival(destination, item.Content.GetObjectId()); return; }
            MyInventory.TransferByPlanner(source, destination, item.Content.GetObjectId(),
                MyItemFlags.None, item.Amount);
            TransferFeedback.Update();
        }
    }

    private static bool CanReturnDrillOre(MyCharacter character, MyEntity tool) =>
        LanyardPreferences.Current.DrillReturns && ReferenceEquals(character, MySession.Static?.LocalCharacter) &&
        tool != null && !tool.Closed && !tool.MarkedForClose && ReferenceEquals(tool, character.EquippedTool) &&
        (!LanyardPreferences.Current.DrillActiveOnly || (tool is MyHandDrill drill && drill.IsShooting));
}

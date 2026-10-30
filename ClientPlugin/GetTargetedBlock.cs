using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Lanyard;

public static class GetTargetedBlock
{
    public static void ClearSelection()
    {
        var previous = selectedBlock;
        selectedBlock = null;
        selectedObject = null;
        // A renderer/scenario failure must not interrupt disconnect or world cleanup.
        try
        {
            if (previous != null && !previous.Closed && !previous.MarkedForClose && MySession.Static != null)
                MyVisualScriptLogicProvider.SetHighlightLocal(previous.Name, -1, 0,
                    Color.White, MySession.Static.LocalPlayerId, null);
        }
        catch (Exception exception)
        {
            MyLog.Default.WriteLineAndConsole("Lanyard highlight cleanup failed: " + exception);
        }
    }

    public static void GetPort()
    {
        bool connected = false;
        bool selecting = false;
        IMyTerminalBlock previousBlock = null;
        IMyUseObject previousPort = null;
        IMyTerminalBlock candidateBlock = null;
        IMyUseObject candidatePort = null;
        Sandbox.Game.Entities.Character.MyCharacter character = null;
        MySession session = null;
        Guid world = Guid.Empty;
        ulong server = 0, account = 0;
        long identity = 0;
        try
        {
            var utils = MyAPIGateway.Utilities;
            if (!LinkRecovery.Update()) return;
            if (selectedBlock != null && !LinkController.ValidateExistingSelection()) return;
            previousBlock = selectedBlock;
            previousPort = selectedObject;
            character = MySession.Static?.LocalCharacter;
            session = MySession.Static;
            world = session.WorldId;
            server = MyAPIGateway.Multiplayer?.ServerId ?? 0;
            account = session.LocalHumanPlayer?.Id.SteamId ?? 0;
            identity = session.LocalHumanPlayer?.Identity?.IdentityId ?? 0;
            if (selectedBlock != null) CombatLockout.FollowGrid(selectedBlock.CubeGrid as MyCubeGrid);
            CombatLockout.Update();
            if (CombatLockout.IsBlocked || (selectedBlock != null && LinkController.IsCombatPaused))
            {
                CombatLockout.ReportBlocked();
                return;
            }
            var caster = character?.EquippedTool?.Components?.Get<MyCasterComponent>();
            var block = caster?.HitBlock;
            if (block?.FatBlock is IMyTerminalBlock terminalBlock)
            {
                if (!LinkController.HasAccess(terminalBlock))
                {
                    utils.ShowNotification("Lanyard requires your own inventory or an accessible faction-shared inventory.", 5000, "Red");
                    return;
                }
                var grid = terminalBlock.CubeGrid as MyCubeGrid;
                if (grid == null || LinkArea.DistanceOutside(grid, character.PositionComp.GetPosition()) > LinkArea.BreakDistance)
                {
                    utils.ShowNotification("Move within 100 m of the grid bounds to connect.", 4000, "White");
                    return;
                }
                var objects = terminalBlock.Components.Get<MyUseObjectsComponentBase>();
                var usableObjects = new List<IMyUseObject>();
                objects?.GetInteractiveObjects(usableObjects);
                foreach (var usableObject in usableObjects)
                {
                    if (usableObject.PrimaryAction != UseActionEnum.OpenInventory || !ReferenceEquals(usableObject.Owner, terminalBlock)) continue;
                    if (!CombatLockout.CanConnect(grid))
                    {
                        CombatLockout.ReportBlocked();
                        return;
                    }
                    // Only create scan caches once we have a usable inventory port.
                    if (!LinkArea.CheckEnemies(grid, out var reason))
                    {
                        LanyardDiagnostics.Show(reason);
                        return;
                    }
                    // Access/model callbacks can change the world or selection.
                    // Never commit a result resolved against a stale character.
                    if (!SameContext(session, character, world, server, account, identity) ||
                        !ReferenceEquals(previousBlock, selectedBlock) || !ReferenceEquals(previousPort, selectedObject) ||
                        !LinkController.HasAccess(terminalBlock) || CombatLockout.IsBlocked) return;
                    if (!SameContext(session, character, world, server, account, identity) ||
                        !ReferenceEquals(previousBlock, selectedBlock) || !ReferenceEquals(previousPort, selectedObject)) return;
                    selecting = true;
                    candidateBlock = terminalBlock;
                    candidatePort = usableObject;
                    Select(terminalBlock, usableObject);
                    if (!SameContext(session, character, world, server, account, identity) ||
                        !ReferenceEquals(selectedBlock, terminalBlock) || !ReferenceEquals(selectedObject, usableObject) ||
                        !LinkController.HasAccess(terminalBlock)) return;
                    if (!SameContext(session, character, world, server, account, identity) ||
                        !ReferenceEquals(selectedBlock, terminalBlock) || !ReferenceEquals(selectedObject, usableObject) || CombatLockout.IsBlocked) return;
                    LinkController.Connected();
                    connected = true;
                    if (!ReferenceEquals(previousBlock, terminalBlock)) ClearHighlight(previousBlock);
                    utils.ShowNotification($"Linked to {terminalBlock.CustomName}", 4000, "White");
                    return;
                }
            }
            utils?.ShowNotification("Target a block with an inventory access port while holding a tool.", 4000, "Red");
        }
        catch (Exception exception)
        {
            MyLog.Default.WriteLineAndConsole("Lanyard port selection failed: " + exception);
            LanyardDiagnostics.Show("Lanyard unavailable. See game log.");
        }
        finally
        {
            try
            {
                if (!connected && selecting && ReferenceEquals(selectedBlock, candidateBlock) && ReferenceEquals(selectedObject, candidatePort))
                {
                    // Roll back a failed highlight/bind without erasing the old
                    // durable selection. A context change belongs to world recovery.
                    if (SameContext(session, character, world, server, account, identity))
                    {
                        ClearHighlight(selectedBlock);
                        if (!SameContext(session, character, world, server, account, identity))
                            LinkController.SuspendForWorldChange();
                        else
                        {
                            selectedBlock = previousBlock;
                            selectedObject = previousPort;
                            if (previousBlock != null)
                            {
                                try { Select(previousBlock, previousPort); }
                                catch (Exception exception) { MyLog.Default.WriteLineAndConsole("Lanyard previous highlight unavailable: " + exception.Message); }
                                if (SameContext(session, character, world, server, account, identity) &&
                                    ReferenceEquals(selectedBlock, previousBlock) && ReferenceEquals(selectedObject, previousPort) &&
                                    LinkController.ValidateExistingSelection() && SameContext(session, character, world, server, account, identity) &&
                                    ReferenceEquals(selectedBlock, previousBlock) && ReferenceEquals(selectedObject, previousPort)) LinkRecovery.Remember();
                                else LinkController.SuspendForWorldChange();
                            }
                            else LinkController.Disconnect();
                        }
                    }
                    else LinkController.SuspendForWorldChange();
                }
                // CanConnect temporarily observes the candidate. Keep the old live
                // anchor on failure while preserving any genuine observed cooldown.
                if (!connected && selectedBlock != null && LinkRecovery.HasCurrentContext)
                    CombatLockout.FollowGrid(selectedBlock.CubeGrid as MyCubeGrid);
            }
            catch (Exception exception)
            {
                MyLog.Default.WriteLineAndConsole("Lanyard selection rollback waiting: " + exception.Message);
                try { LinkController.SuspendForWorldChange(); }
                catch { }
            }
        }
    }

    private static bool SameContext(MySession session, Sandbox.Game.Entities.Character.MyCharacter character,
        Guid world, ulong server, ulong account, long identity) =>
        LinkRecovery.HasCurrentContext && ReferenceEquals(session, MySession.Static) && ReferenceEquals(character, session?.LocalCharacter) &&
        session.WorldId == world && (MyAPIGateway.Multiplayer?.ServerId ?? 0) == server &&
        (session.LocalHumanPlayer?.Id.SteamId ?? 0) == account && (session.LocalHumanPlayer?.Identity?.IdentityId ?? 0) == identity;

    private static void ClearHighlight(IMyTerminalBlock block)
    {
        if (block == null || block.Closed || block.MarkedForClose || MySession.Static == null) return;
        try { MyVisualScriptLogicProvider.SetHighlightLocal(block.Name, -1, 0, Color.White, MySession.Static.LocalPlayerId, null); }
        catch (Exception exception) { MyLog.Default.WriteLineAndConsole("Lanyard highlight cleanup failed: " + exception.Message); }
    }

    internal static void Select(IMyTerminalBlock block, IMyUseObject port)
    {
        selectedBlock = block;
        selectedObject = port;
        // Preserve scenario/visual-script entity names.
        if (string.IsNullOrEmpty(block.Name))
        {
            block.Name = block.EntityId.ToString();
            MyEntities.SetEntityName((MyEntity)block);
        }
        MyVisualScriptLogicProvider.SetHighlightLocal(block.Name, 2, 3,
            Color.ForestGreen, MySession.Static.LocalPlayerId, null);
    }

    public static IMyUseObject selectedObject;
    public static IMyTerminalBlock selectedBlock;
}

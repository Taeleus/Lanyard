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
        LinkController.Disconnect();
        bool connected = false;
        try
        {
            var utils = MyAPIGateway.Utilities;
            CombatLockout.Update();
            if (CombatLockout.IsBlocked)
            {
                CombatLockout.ReportBlocked();
                return;
            }
            var character = MySession.Static?.LocalCharacter;
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
                    if (usableObject.PrimaryAction != UseActionEnum.OpenInventory) continue;
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
                    selectedBlock = terminalBlock;
                    selectedObject = usableObject;
                    // Preserve scenario/visual-script entity names.
                    if (string.IsNullOrEmpty(terminalBlock.Name))
                    {
                        terminalBlock.Name = terminalBlock.EntityId.ToString();
                        MyEntities.SetEntityName((MyEntity)terminalBlock);
                    }
                    MyVisualScriptLogicProvider.SetHighlightLocal(terminalBlock.Name, 2, 3,
                        Color.ForestGreen, MySession.Static.LocalPlayerId, null);
                    LinkController.Connected();
                    connected = true;
                    utils.ShowNotification($"Linked to {terminalBlock.CustomName}", 4000, "White");
                    return;
                }
            }
            utils?.ShowNotification("Target a block with an inventory access port while holding a tool.", 4000, "Red");
        }
        catch (Exception exception)
        {
            connected = false;
            MyLog.Default.WriteLineAndConsole("Lanyard port selection failed: " + exception);
            LanyardDiagnostics.Show("Lanyard unavailable. See game log.");
        }
        finally
        {
            // Includes exceptions after a successful scan or partial selection.
            if (!connected) LinkController.Disconnect();
        }
    }

    public static IMyUseObject selectedObject;
    public static IMyTerminalBlock selectedBlock;
}

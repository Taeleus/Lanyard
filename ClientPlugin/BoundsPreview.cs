using System;
using System.Diagnostics;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Lanyard;

public static class BoundsPreview
{
    private static MyCubeGrid grid;
    private static BoundsAccess access;
    private static MySession previewSession;
    private static bool linkedPreview;

    private static long hideAt;
    private static MyCubeGrid rangeGrid;
    private static object rangeCharacter;
    private static object rangeBlock;
    private static MySession rangeSession;
    private static IMyHudNotification rangeNotification;
    private static long rangeGeneration;
    private static readonly RangeWarningState rangeWarning = new RangeWarningState(Stopwatch.Frequency);
    private static long nextRangeLog;
    public static string DistanceText
    {
        get
        {
            try
            {
                var block = GetTargetedBlock.selectedBlock;
                var character = MySession.Static?.LocalCharacter;
                if (block?.CubeGrid is not MyCubeGrid linkedGrid || character == null || character.IsDead ||
                    !LinkRecovery.HasCurrentContext || !LinkController.HasAccess(block)) return "";
                var distance = LinkArea.DistanceOutside(linkedGrid, character.PositionComp.GetPosition());
                if (double.IsNaN(distance) || double.IsInfinity(distance) || distance > LinkArea.BreakDistance) return "";
                return distance <= 0 ? "Inside the linked working area." :
                    Math.Ceiling(distance) + " m back to working area · " + Math.Ceiling(LinkArea.BreakDistance - distance) + " m until disconnect.";
            }
            catch { return ""; }
        }
    }
    private const float InnerLineWidth = 0.0006f;
    private const float OuterLineWidth = 0.0005f;

    private static void HideRangeWarning()
    {
        rangeGeneration++;
        var notification = rangeNotification;
        rangeNotification = null;
        try { notification?.Hide(); }
        catch { }
    }

    private static void ResetRangeWarning()
    {
        HideRangeWarning();
        rangeGrid = null;
        rangeCharacter = null;
        rangeBlock = null;
        rangeSession = null;
        rangeWarning.Reset();
    }

    public static void Clear()
    {
        ResetRangeWarning();
        access?.Dispose(); access = null; grid = null; hideAt = 0; previewSession = null; linkedPreview = false;
    }

    public static void LinkConnected() { ResetRangeWarning(); hideAt = 0; }

    public static void LinkDisconnected()
    {
        ResetRangeWarning();
        linkedPreview = false;
        if (grid != null)
            hideAt = Stopwatch.GetTimestamp() + 30L * Stopwatch.Frequency;
    }

    public static void ToggleLinked()
    {
        var block = GetTargetedBlock.selectedBlock;
        if (block?.CubeGrid is not MyCubeGrid linkedGrid || !LinkRecovery.HasCurrentContext || !LinkController.HasAccess(block))
        {
            MyAPIGateway.Utilities?.ShowNotification("Connect to an inventory before showing its bounds.", 4000, "White");
            return;
        }
        if (ReferenceEquals(grid, linkedGrid))
        {
            Clear();
            MyAPIGateway.Utilities?.ShowNotification("Lanyard bounds hidden.", 3000, "White");
            return;
        }
        Clear();
        Show(linkedGrid, true);
    }

    public static void Update()
    {
        try
        {
            var block = GetTargetedBlock.selectedBlock;
            var session = MySession.Static;
            var character = session?.LocalCharacter;
            if (block?.CubeGrid is not MyCubeGrid linkedGrid || character == null || character.IsDead ||
                character.Closed || character.MarkedForClose || linkedGrid.Closed || linkedGrid.MarkedForClose ||
                !LinkRecovery.HasCurrentContext || !LinkController.HasAccess(block))
            {
                ResetRangeWarning();
                return;
            }
            if (!ReferenceEquals(rangeGrid, linkedGrid) || !ReferenceEquals(rangeCharacter, character) ||
                !ReferenceEquals(rangeBlock, block) || !ReferenceEquals(rangeSession, session))
            {
                ResetRangeWarning();
                rangeGrid = linkedGrid;
                rangeCharacter = character;
                rangeBlock = block;
                rangeSession = session;
            }
            var distance = LinkArea.DistanceOutside(linkedGrid, character.PositionComp.GetPosition());
            var now = Stopwatch.GetTimestamp();
            if (!RangeWarningState.IsWarningRange(distance) || !LanyardShortcuts.GameplayReady) HideRangeWarning();
            if (!rangeWarning.Observe(distance, now, LanyardShortcuts.GameplayReady)) return;
            HideRangeWarning();
            var generation = rangeGeneration;
            var notification = MyAPIGateway.Utilities?.CreateNotification("Lanyard: " + Math.Ceiling(LinkArea.BreakDistance - distance) +
                " m until the link disconnects. Return toward the working area.", 5000, "White");
            if (notification == null) return;
            var hasAccess = LinkRecovery.HasCurrentContext && ReferenceEquals(block, GetTargetedBlock.selectedBlock) && LinkController.HasAccess(block);
            if (generation != rangeGeneration || !hasAccess || !ReferenceEquals(block, GetTargetedBlock.selectedBlock) ||
                !ReferenceEquals(session, MySession.Static) || !ReferenceEquals(character, MySession.Static?.LocalCharacter) ||
                !ReferenceEquals(linkedGrid, block.CubeGrid) || !LinkRecovery.HasCurrentContext)
            {
                try { notification.Hide(); } catch { }
                return;
            }
            rangeNotification = notification;
            notification.Show();
            hasAccess = LinkRecovery.HasCurrentContext && ReferenceEquals(block, GetTargetedBlock.selectedBlock) && LinkController.HasAccess(block);
            if (generation != rangeGeneration || !hasAccess || !ReferenceEquals(block, GetTargetedBlock.selectedBlock) ||
                !ReferenceEquals(session, MySession.Static) || !ReferenceEquals(character, MySession.Static?.LocalCharacter) ||
                !ReferenceEquals(linkedGrid, block.CubeGrid) || !LinkRecovery.HasCurrentContext)
            {
                HideRangeWarning();
                try { notification.Hide(); } catch { }
            }
        }
        catch (Exception exception)
        {
            HideRangeWarning();
            var now = Stopwatch.GetTimestamp();
            if (now < nextRangeLog) return;
            nextRangeLog = now + 10L * Stopwatch.Frequency;
            try { MyLog.Default?.WriteLineAndConsole("Lanyard range display unavailable: " + exception.Message); }
            catch { }
        }
    }

    public static void Toggle()
    {
        if (grid != null)
        {
            Clear();
            MyAPIGateway.Utilities?.ShowNotification("Lanyard bounds hidden.", 3000, "White");
            return;
        }
        var character = MySession.Static?.LocalCharacter;
        grid = character?.EquippedTool?.Components?.Get<MyCasterComponent>()?.HitBlock?.CubeGrid;
        if (grid == null && MyAPIGateway.Session?.Camera != null)
        {
            var camera = MyAPIGateway.Session.Camera.WorldMatrix;
            if (MyAPIGateway.Physics.CastRay(camera.Translation + camera.Forward * 0.5,
                camera.Translation + camera.Forward * 500, out var hit))
                grid = hit.HitEntity as MyCubeGrid;
        }
        if (grid == null)
        {
            MyAPIGateway.Utilities?.ShowNotification("Look at a grid within 500 m (or target it with a tool).", 4000, "White");
            return;
        }
        Show(grid);
    }

    private static void Show(MyCubeGrid target, bool followLink = false)
    {
        grid = target;
        previewSession = MySession.Static;
        linkedPreview = followLink;
        try
        {
            access = new BoundsAccess(grid);
            if (!access.IsAllowed())
            {
                Clear();
                MyAPIGateway.Utilities?.ShowNotification("Lanyard bounds unavailable.", 3000, "White");
                return;
            }
        }
        catch (Exception exception)
        {
            Clear();
            MyLog.Default.WriteLineAndConsole("Lanyard bounds access check failed: " + exception);
            return;
        }
        MyAPIGateway.Utilities?.ShowNotification(
            "Green: working bounds. Orange: +100 m box guide; corners extend beyond actual break boundary.",
            8000, "White");
    }

    public static void Draw()
    {
        if (grid == null) return;
        // A linked block can move to another grid after a split/merge. Follow
        // that exact selected block, while targeted previews keep their target.
        if (linkedPreview)
        {
            try
            {
                var block = GetTargetedBlock.selectedBlock;
                if (block?.CubeGrid is not MyCubeGrid current || !LinkRecovery.HasCurrentContext || !LinkController.HasAccess(block))
                {
                    Clear();
                    return;
                }
                if (!ReferenceEquals(grid, current))
                {
                    access?.Dispose();
                    access = null;
                    grid = current;
                    access = new BoundsAccess(current);
                    hideAt = 0;
                }
            }
            catch (Exception exception)
            {
                Clear();
                MyLog.Default?.WriteLineAndConsole("Lanyard linked bounds unavailable: " + exception.Message);
                return;
            }
        }
        if (hideAt != 0 && Stopwatch.GetTimestamp() >= hideAt)
        {
            Clear();
            return;
        }
        if (MyAPIGateway.Session == null || !ReferenceEquals(previewSession, MySession.Static) || grid.Closed || grid.MarkedForClose ||
            MySession.Static?.LocalCharacter == null || MySession.Static.LocalCharacter.IsDead)
        {
            Clear();
            return;
        }
        try
        {
            if (access == null || !access.IsAllowed())
            {
                Clear();
                return;
            }
            var matrix = grid.WorldMatrix;
            var local = grid.PositionComp.LocalAABB;
            var inner = new BoundingBoxD(local.Min, local.Max);
            var outer = new BoundingBoxD(inner.Min - new Vector3D(LinkArea.BreakDistance),
                inner.Max + new Vector3D(LinkArea.BreakDistance));
            var green = Color.LimeGreen;
            var orange = Color.Orange;
            var material = MyStringId.GetOrCompute("Square");
            MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref inner, ref green,
                MySimpleObjectRasterizer.Wireframe, 1, InnerLineWidth, lineMaterial: material);
            MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref outer, ref orange,
                MySimpleObjectRasterizer.Wireframe, 1, OuterLineWidth, lineMaterial: material);
        }
        catch (Exception exception)
        {
            Clear();
            MyLog.Default.WriteLineAndConsole("Lanyard bounds preview failed: " + exception);
        }
    }
}

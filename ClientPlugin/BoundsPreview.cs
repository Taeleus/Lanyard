using System;
using System.Diagnostics;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Lanyard;

public static class BoundsPreview
{
    private static MyCubeGrid grid;
    private static BoundsAccess access;

    private static long hideAt;
    // The renderer scales this value; keep both boxes at the same thin width.
    private const float LineWidth = 0.0005f;

    public static void Clear() { access?.Dispose(); access = null; grid = null; hideAt = 0; }

    public static void LinkConnected() { hideAt = 0; }

    public static void LinkDisconnected()
    {
        if (grid != null)
            hideAt = Stopwatch.GetTimestamp() + 30L * Stopwatch.Frequency;
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
        if (hideAt != 0 && Stopwatch.GetTimestamp() >= hideAt)
        {
            Clear();
            return;
        }
        if (MyAPIGateway.Session == null || grid.Closed || grid.MarkedForClose ||
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
                MySimpleObjectRasterizer.Wireframe, 1, LineWidth, lineMaterial: material);
            MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref outer, ref orange,
                MySimpleObjectRasterizer.Wireframe, 1, LineWidth, lineMaterial: material);
        }
        catch (Exception exception)
        {
            Clear();
            MyLog.Default.WriteLineAndConsole("Lanyard bounds preview failed: " + exception);
        }
    }
}
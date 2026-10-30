using System;
using System.Diagnostics;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.Graphics;
using VRage.Utils;
using VRageMath;

namespace Lanyard;

/// <summary>Own-connection status only; input focus is deliberately separate from rendering.</summary>
internal static class LanyardHud
{
    private static long nextFailureLog;

    public static void Clear()
    {
        try { LanyardHudPositionScreen.Close(); }
        catch (Exception exception)
        {
            try { MyLog.Default?.WriteLineAndConsole("Lanyard HUD cleanup failed: " + exception.Message); }
            catch { }
        }
        finally
        {
            nextFailureLog = 0;
        }
    }

    public static void Draw()
    {
        try
        {
            var settings = LanyardPreferences.Current;
            if (settings.HudMode == HudMode.Off || !LanyardShortcuts.RenderReady ||
                MyHud.MinimalHud || MyHud.IsHudMinimal || !MyHud.IsVisible || MyHud.CutsceneHud) return;
            if (settings.HudMode == HudMode.Paused && (!LinkController.HasConnection || !LinkController.IsPaused)) return;
            if (settings.HudMode == HudMode.Tools && !HasSupportedTool()) return;

            var name = GetTargetedBlock.selectedBlock?.CustomName;
            var title = string.IsNullOrEmpty(name) ? "Lanyard" : "Lanyard · " + name;
            var state = LinkController.StateText;
            var color = LinkController.IsPaused ? Color.Orange : LinkController.HasConnection ? Color.LimeGreen : Color.White;
            DrawAt(Position(settings), settings, title, state, TransferFeedback.CapacityText, color);
        }
        catch (Exception exception)
        {
            var now = Stopwatch.GetTimestamp();
            if (now < nextFailureLog) return;
            nextFailureLog = now + 10L * Stopwatch.Frequency;
            try { MyLog.Default?.WriteLineAndConsole("Lanyard HUD draw failed: " + exception.Message); }
            catch { }
        }
    }

    internal static Vector2 Position(LanyardSettings settings)
    {
        var size = Size(settings);
        var width = size.X;
        var height = size.Y;
        if (settings.HudCustomPosition) return Clamp(new Vector2(settings.HudX, settings.HudY), settings);
        var right = settings.HudPosition == HudPosition.TopRight || settings.HudPosition == HudPosition.BottomRight;
        var bottom = settings.HudPosition == HudPosition.BottomLeft || settings.HudPosition == HudPosition.BottomRight;
        return new Vector2(right ? .98f - width : .02f, bottom ? .98f - height : .02f);
    }

    internal static Vector2 Clamp(Vector2 position, LanyardSettings settings)
    {
        var size = Size(settings);
        return new Vector2(
            Math.Max(0, Math.Min(1f - size.X, float.IsNaN(position.X) || float.IsInfinity(position.X) ? .02f : position.X)),
            Math.Max(0, Math.Min(1f - size.Y, float.IsNaN(position.Y) || float.IsInfinity(position.Y) ? .02f : position.Y)));
    }

    // HUD positions are fullscreen-normalized. Native GUI coordinates are a
    // centered safe rectangle on wide screens, so preserve physical GUI size.
    internal static Vector2 Size(LanyardSettings settings) => FullscreenExtent(new Vector2(settings.HudWidth, settings.HudHeight));

    private static Vector2 FullscreenExtent(Vector2 guiSize)
    {
        var pixels = MyGuiManager.GetScreenSizeFromNormalizedSize(guiSize);
        var screen = MyGuiManager.GetSafeFullscreenRectangle();
        return new Vector2(pixels.X / Math.Max(1, screen.Width), pixels.Y / Math.Max(1, screen.Height));
    }

    internal static Vector2 CursorPosition()
    {
        var pixels = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(MyGuiManager.MouseCursorPosition);
        var screen = MyGuiManager.GetSafeFullscreenRectangle();
        return new Vector2((pixels.X - screen.Left) / Math.Max(1, screen.Width), (pixels.Y - screen.Top) / Math.Max(1, screen.Height));
    }

    internal static void DrawPreview(Vector2 position, LanyardSettings settings) =>
        DrawAt(position, settings, "Lanyard · HUD preview", "Drag this panel to move it.", "Capacity messages appear here.", Color.White);

    private static void DrawAt(Vector2 position, LanyardSettings settings, string title, string state, string capacity, Color color)
    {
        var scale = settings.HudScale;
        var size = Size(settings);
        var screen = MyGuiManager.GetSafeFullscreenRectangle();
        var spriteSize = new Vector2(size.X * screen.Width / Math.Max(1, screen.Width + 1), size.Y);
        var align = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP;
        MyGuiManager.DrawSpriteBatch("Textures\\GUI\\Blank.dds", position, spriteSize,
            WithOpacity(new Color(13, 24, 34, 210), settings.HudOpacity), align, useFullClientArea: true, waitTillLoaded: false);
        MyGuiManager.DrawString("White", LanyardPanel.Bounded(title, 45), position + FullscreenExtent(new Vector2(.009f, .005f)),
            scale * .8f, WithOpacity(Color.White, settings.HudOpacity), align, useFullClientArea: true, maxTextWidth: settings.HudWidth - .018f);
        MyGuiManager.DrawString("White", LanyardPanel.Bounded(state, 55), position + FullscreenExtent(new Vector2(.009f, .028f * scale / .8f)),
            scale * .73f, WithOpacity(color, settings.HudOpacity), align, useFullClientArea: true, maxTextWidth: settings.HudWidth - .018f);
        if (!string.IsNullOrEmpty(capacity))
            MyGuiManager.DrawString("White", LanyardPanel.Bounded(capacity, 64), position + FullscreenExtent(new Vector2(.009f, .054f * scale / .8f)),
                scale * .65f, WithOpacity(Color.Orange, settings.HudOpacity), align, useFullClientArea: true, maxTextWidth: settings.HudWidth - .018f);
    }

    private static Color WithOpacity(Color color, float opacity)
    {
        var value = float.IsNaN(opacity) || float.IsInfinity(opacity) ? 1f : Math.Max(.1f, Math.Min(1f, opacity));
        return new Color(color.R, color.G, color.B, (int)Math.Round(color.A * value));
    }

    private static bool HasSupportedTool()
    {
        var tool = MySession.Static?.LocalCharacter?.EquippedTool;
        return tool != null && !tool.Closed && !tool.MarkedForClose &&
            (tool is MyWelder || tool is MyAngleGrinder || tool is MyHandDrill || tool is MyBlockPlacerBase);
    }
}

using System;
using System.Text;
using Sandbox.Game.World;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace Lanyard;

/// <summary>A modal, local-only appearance preview. Only Save commits changes.</summary>
public sealed class LanyardHudPositionScreen : MyGuiScreenBase
{
    private static LanyardHudPositionScreen current;
    private readonly MySession session;
    private readonly object character;
    private readonly LanyardSettings preview;
    private readonly Action restoreFocus;
    private Vector2 position;
    private Vector2 dragOffset;
    private bool dragging;
    private bool closed;
    private readonly MyGuiControlButton scaleDown, scaleUp, opacityDown, opacityUp;

    private LanyardHudPositionScreen(Action restoreFocus)
        : base(new Vector2(.5f, .5f), null, null, false, null, 1f, 1f)
    {
        session = MySession.Static;
        character = session?.LocalCharacter;
        preview = LanyardPreferences.Current.Copy();
        this.restoreFocus = restoreFocus;
        position = LanyardHud.Position(preview);
        EnabledBackgroundFade = false;
        CloseButtonEnabled = false;
        DrawMouseCursor = true;
        m_canShareInput = false;
        scaleDown = AddButton("ScaleDown", -.245f, .295f, .105f, "-", () => ChangeAppearance(-.1f, 0));
        scaleUp = AddButton("ScaleUp", .245f, .295f, .105f, "+", () => ChangeAppearance(.1f, 0));
        opacityDown = AddButton("OpacityDown", -.245f, .36f, .105f, "-", () => ChangeAppearance(0, -.1f));
        opacityUp = AddButton("OpacityUp", .245f, .36f, .105f, "+", () => ChangeAppearance(0, .1f));
        AddButton("Save", -.115f, .43f, .21f, "Save appearance", Save);
        var cancel = AddButton("Cancel", .115f, .43f, .21f, "Cancel", () => CloseScreen());
        RefreshAppearanceControls();
        FocusedControl = cancel;
        Closed += (source, unloading) =>
        {
            closed = true;
            dragging = false;
            DisableControls();
            LanyardShortcuts.ClearTransient();
            if (ReferenceEquals(current, source)) current = null;
            if (!unloading && SameContext() && LanyardShortcuts.InputFocused)
                try { this.restoreFocus?.Invoke(); }
                catch (Exception exception) { LogFailure(exception); }
        };
    }

    public static void Open(Action restoreFocus = null)
    {
        if (current != null || MySession.Static?.LocalCharacter == null) return;
        LanyardShortcuts.ClearTransient();
        try
        {
            current = new LanyardHudPositionScreen(restoreFocus);
            MyGuiSandbox.AddScreen(current);
        }
        catch (Exception exception)
        {
            current = null;
            LogFailure(exception);
        }
    }

    public static void Close()
    {
        var screen = current;
        current = null;
        if (screen == null) return;
        screen.closed = true;
        screen.dragging = false;
        screen.DisableControls();
        try { screen.CloseScreenNow(true); }
        catch (Exception exception) { screen.RemoveFailedClose(exception); }
        finally { LanyardShortcuts.ClearTransient(); }
    }

    public override string GetFriendlyName() => "LanyardHudPositionScreen";

    public override bool CloseScreen(bool isUnloading = false)
    {
        closed = true;
        dragging = false;
        DisableControls();
        LanyardShortcuts.ClearTransient();
        return base.CloseScreen(isUnloading);
    }

    public override bool Update(bool hasFocus)
    {
        // Closing disables interaction immediately, but the native transition
        // still needs updates to fire Closed and release its modal input owner.
        if (closed) return UpdateClosing(hasFocus);
        if (!SameContext() || !LanyardShortcuts.InputFocused)
        {
            CloseScreen();
            return UpdateClosing(hasFocus);
        }
        if (!hasFocus) dragging = false;
        return base.Update(hasFocus);
    }

    private bool UpdateClosing(bool hasFocus)
    {
        try { return base.Update(hasFocus); }
        catch (Exception exception) { RemoveFailedClose(exception); return false; }
    }

    private void RemoveFailedClose(Exception exception)
    {
        closed = true;
        dragging = false;
        DisableControls();
        if (ReferenceEquals(current, this)) current = null;
        LogFailure(exception);
        try { MyScreenManager.RemoveScreen(this); }
        catch (Exception removalFailure) { LogFailure(removalFailure); }
    }

    public override void HandleInput(bool receivedFocusInThisUpdate)
    {
        if (closed || !SameContext() || !LanyardShortcuts.InputFocused) return;
        if (!ReferenceEquals(MyScreenManager.GetScreenWithFocus(), this)) { dragging = false; return; }
        var input = MyInput.Static;
        if (input == null) return;
        if (input.IsNewKeyPressed(MyKeys.Escape)) { CloseScreen(); return; }
        var mouse = LanyardHud.CursorPosition();
        if (dragging)
        {
            if (!input.IsLeftMousePressed()) { dragging = false; return; }
            position = LanyardHud.Clamp(mouse - dragOffset, preview);
            return;
        }
        var guiMouse = MyGuiManager.MouseCursorPosition;
        var trayPress = input.IsNewLeftMousePressed() && guiMouse.X >= .19f && guiMouse.X <= .81f &&
            guiMouse.Y >= .72f && guiMouse.Y <= .98f;
        base.HandleInput(receivedFocusInThisUpdate);
        if (closed || trayPress) return;
        var size = LanyardHud.Size(preview);
        if (input.IsNewLeftMousePressed() && mouse.X >= position.X && mouse.Y >= position.Y &&
            mouse.X <= position.X + size.X && mouse.Y <= position.Y + size.Y)
        {
            dragging = true;
            dragOffset = mouse - position;
        }
    }

    public override bool Draw()
    {
        if (closed || !SameContext()) return false;
        LanyardHud.DrawPreview(position, preview);
        MyGuiManager.DrawSpriteBatch("Textures\\GUI\\Blank.dds", new Vector2(.19f, .72f), new Vector2(.62f, .26f),
            new Color(13, 24, 34, 240), MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, waitTillLoaded: false);
        base.Draw();
        MyGuiManager.DrawString("White", "Drag the preview. Changes are saved only when you choose Save appearance.",
            new Vector2(.22f, .735f), .63f, Color.White, maxTextWidth: .56f);
        MyGuiManager.DrawString("White", "Size: " + Math.Round(preview.HudScale * 100) + "%",
            new Vector2(.4f, .782f), .7f, Color.White, maxTextWidth: .20f);
        MyGuiManager.DrawString("White", "Opacity: " + Math.Round(preview.HudOpacity * 100) + "%",
            new Vector2(.4f, .847f), .7f, Color.White, maxTextWidth: .20f);
        return true;
    }

    private bool SameContext() => ReferenceEquals(session, MySession.Static) && session != null && !session.IsUnloading &&
        ReferenceEquals(character, session.LocalCharacter) && session.LocalCharacter != null && !session.LocalCharacter.IsDead &&
        !session.LocalCharacter.Closed && !session.LocalCharacter.MarkedForClose;

    private void Save()
    {
        if (closed || !SameContext() || !LanyardShortcuts.InputFocused) return;
        // Other preferences may have changed while this preview was open.
        LanyardPreferences.Change(settings =>
        {
            settings.HudScale = preview.HudScale;
            settings.HudOpacity = preview.HudOpacity;
            var saved = LanyardHud.Clamp(position, settings);
            settings.HudCustomPosition = true;
            settings.HudX = saved.X;
            settings.HudY = saved.Y;
        });
        CloseScreen();
    }

    private void ChangeAppearance(float scale, float opacity)
    {
        if (closed || !SameContext() || !LanyardShortcuts.InputFocused) return;
        dragging = false;
        if (scale != 0) preview.HudScale = (float)Math.Round(preview.HudScale + scale, 2);
        if (opacity != 0) preview.HudOpacity = (float)Math.Round(preview.HudOpacity + opacity, 2);
        preview.Normalize();
        position = LanyardHud.Clamp(position, preview);
        RefreshAppearanceControls();
    }

    private void RefreshAppearanceControls()
    {
        scaleDown.Enabled = preview.HudScale > .601f;
        scaleUp.Enabled = preview.HudScale < 1.199f;
        opacityDown.Enabled = preview.HudOpacity > .101f;
        opacityUp.Enabled = preview.HudOpacity < .999f;
    }

    private MyGuiControlButton AddButton(string id, float x, float y, float width, string text, Action action)
    {
        var tooltip = id == "ScaleDown" ? "Reduce HUD size by 10 percentage points." : id == "ScaleUp" ? "Increase HUD size by 10 percentage points." :
            id == "OpacityDown" ? "Reduce HUD opacity by 10 percentage points." : id == "OpacityUp" ? "Increase HUD opacity by 10 percentage points." : text;
        var button = new MyGuiControlButton(new Vector2(x, y), size: new Vector2(width, .045f), text: new StringBuilder(text),
            textScale: .7f, toolTip: tooltip, onButtonClick: control =>
            {
                if (closed || !control.Enabled) return;
                try { action(); }
                catch (Exception exception) { LogFailure(exception); }
            }) { Name = "LanyardPosition" + id };
        LanyardPanel.StyleButton(button, new Vector2(width, .045f));
        Controls.Add(button);
        return button;
    }

    private void DisableControls()
    {
        foreach (var control in Controls)
            try { control.Enabled = false; }
            catch { }
    }

    private static void LogFailure(Exception exception)
    {
        try { MyLog.Default?.WriteLineAndConsole("Lanyard position editor unavailable: " + exception.Message); }
        catch { }
    }
}

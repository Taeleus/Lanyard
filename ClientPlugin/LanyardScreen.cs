using System;
using System.Collections.Generic;
using System.Text;
using Sandbox;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;

namespace Lanyard;

/// <summary>Native game controls; opening this screen does not add UI hooks.</summary>
public sealed class LanyardScreen : MyGuiScreenBase
{
    private static LanyardScreen current;
    private readonly List<MyGuiControlBase> focusOrder = new List<MyGuiControlBase>();
    private LanyardPanel panel;

    private LanyardScreen()
        : base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR,
            new Vector2(0.82f, 0.66f), false, null,
            MySandboxGame.Config.UIBkOpacity, MySandboxGame.Config.UIOpacity)
    {
        EnabledBackgroundFade = true;
        CloseButtonEnabled = true;
        RecreateControls(true);
        Closed += (source, isUnloading) =>
        {
            LinkController.StateChanged -= RefreshStatus;
            panel?.Dispose();
            if (ReferenceEquals(current, source)) current = null;
        };
    }

    public static void Open()
    {
        if (current != null) return;
        try
        {
            current = new LanyardScreen();
            MyGuiSandbox.AddScreen(current);
            LinkController.StateChanged += current.RefreshStatus;
            current.RefreshStatus();
        }
        catch (Exception exception)
        {
            if (current != null) LinkController.StateChanged -= current.RefreshStatus;
            current?.panel?.Dispose();
            current = null;
            LogFailure("open", exception);
        }
    }

    /// <summary>Immediately release the screen during session/plugin cleanup.</summary>
    public static void Close()
    {
        var screen = current;
        current = null;
        try
        {
            if (screen == null) return;
            LinkController.StateChanged -= screen.RefreshStatus;
            screen.panel?.Dispose();
            screen.CloseScreenNow(true);
        }
        catch (Exception exception) { LogFailure("close", exception); }
    }

    public override string GetFriendlyName() => "LanyardScreen";

    public override void RecreateControls(bool constructor)
    {
        base.RecreateControls(constructor);
        panel?.Dispose();
        focusOrder.Clear();
        AddCaption("Lanyard");
        panel = new LanyardPanel(Controls, this);
        AddButton(0f, 0.285f, "Close", () => CloseScreen(), "Return to the game.");
        FocusedControl = panel.FocusControl;
    }

    public override bool Update(bool hasFocus)
    {
        var result = base.Update(hasFocus);
        panel?.SetActive(hasFocus);
        if (hasFocus) panel?.RefreshStatus();
        var restore = hasFocus ? panel?.TakeFocusRequest() : null;
        if (restore != null) FocusedControl = restore;
        return result;
    }

    public override void HandleInput(bool receivedFocusInThisUpdate)
    {
        if (LanyardShortcuts.HandleCaptureEscape(this)) { panel?.RefreshStatus(); return; }
        base.HandleInput(receivedFocusInThisUpdate);
    }

    protected override bool HandleKeyboardActiveIndex(MyDirection direction, bool page, bool loop)
    {
        // Tab / Shift+Tab follow a stable reading order; arrows and D-pad use
        // the native screen's spatial navigation through these rows/columns.
        if (!page || (direction != MyDirection.Up && direction != MyDirection.Down))
            return base.HandleKeyboardActiveIndex(direction, page, loop);
        var order = new List<MyGuiControlBase>();
        if (panel != null)
            foreach (var control in panel.FocusOrder)
                if (control.Visible && control.Enabled) order.Add(control);
        foreach (var control in focusOrder)
            if (control.Visible && control.Enabled) order.Add(control);
        if (order.Count == 0) return false;
        var step = direction == MyDirection.Down ? 1 : -1;
        var index = order.IndexOf(FocusedControl);
        var next = index < 0 ? 0 : index + step;
        if (loop) next = (next + order.Count) % order.Count;
        if (next < 0 || next >= order.Count) return false;
        FocusedControl = order[next];
        return true;
    }

    private void RefreshStatus() => panel?.RefreshStatus();

    private MyGuiControlButton AddButton(float x, float y, string text, Action action, string tooltip)
    {
        var control = new MyGuiControlButton(new Vector2(x, y), size: new Vector2(0.205f, 0.045f),
            toolTip: tooltip, text: new StringBuilder(text), textScale: 0.72f,
            onButtonClick: button =>
            {
                try { action(); }
                catch (Exception exception) { LogFailure("action", exception); }
                RefreshStatus();
            });
        LanyardPanel.StyleButton(control, new Vector2(0.205f, 0.045f));
        Controls.Add(control);
        focusOrder.Add(control);
        return control;
    }

    private static void LogFailure(string operation, Exception exception)
    {
        try { MyLog.Default?.WriteLineAndConsole("Lanyard screen " + operation + " failed: " + exception); }
        catch { }
    }
}

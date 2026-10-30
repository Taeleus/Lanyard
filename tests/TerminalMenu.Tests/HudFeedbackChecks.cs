using System;
using System.Linq;
using Lanyard;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Input;
using VRageMath;

internal static class HudFeedbackChecks
{
    public static void Run(Action<bool, string> check)
    {
        void ClearDraw() { MyGuiManager.Text.Clear(); MyGuiManager.Backgrounds.Clear(); }
        MySession.Static = new MySession();
        LanyardHud.Clear();
        LanyardPreferences.Current = new LanyardSettings();
        LanyardShortcuts.RenderReady = true;
        LanyardShortcuts.InputFocused = true;
        MyHud.IsVisible = true;
        MyHud.MinimalHud = MyHud.IsHudMinimal = MyHud.CutsceneHud = false;
        ClearDraw();
        LanyardHud.Draw();
        check(MyGuiManager.Text.Count == 0, "fresh default-off HUD renders no overlay");
        var origin = LanyardHud.Position(LanyardPreferences.Current);
        check(origin.X == .02f && origin.Y == .02f, "top-left preset uses the actual top screen margin");
        LanyardPreferences.Current.HudMode = HudMode.Always;
        TransferFeedback.CapacityText = "Backpack full.";
        ClearDraw();
        LanyardHud.Draw();
        check(MyGuiManager.Text.Count == 3 && MyGuiManager.Text[2].Text == "Backpack full.", "persistent capacity has a dedicated HUD line");
        TransferFeedback.Text = "A different recent result.";
        ClearDraw();
        LanyardHud.Draw();
        check(MyGuiManager.Text[2].Text == "Backpack full.", "transient feedback cannot replace the persistent capacity line");
        TransferFeedback.CapacityText = "";
        ClearDraw();
        LanyardHud.Draw();
        check(MyGuiManager.Text.Count == 2, "capacity line clears when its captured condition clears");

        LanyardPreferences.Current.HudCustomPosition = true;
        LanyardPreferences.Current.HudX = .44f;
        LanyardPreferences.Current.HudY = .53f;
        var custom = LanyardHud.Position(LanyardPreferences.Current);
        check(custom.X == .44f && custom.Y == .53f, "HUD uses saved normalized custom position");
        var clamped = LanyardHud.Clamp(new Vector2(9, 9), LanyardPreferences.Current);
        check(clamped.X + LanyardPreferences.Current.HudWidth <= 1f && clamped.Y + LanyardPreferences.Current.HudHeight <= 1f,
            "drag layout clamps the whole three-line HUD to screen");
        var oldGui = MyGuiManager.GuiRectangle;
        var oldFullscreen = MyGuiManager.FullscreenRectangle;
        MyGuiManager.GuiRectangle = new Rectangle(1600, 0, 1920, 1440);
        MyGuiManager.FullscreenRectangle = new Rectangle(0, 0, 5120, 1440);
        LanyardPreferences.Current.HudCustomPosition = false;
        LanyardPreferences.Current.HudPosition = HudPosition.TopLeft;
        ClearDraw();
        LanyardHud.Draw();
        var wideBackground = MyGuiManager.Backgrounds.Single();
        check(wideBackground.Position.X == .02f && MyGuiManager.Text.All(text => text.FullClient),
            "ultrawide HUD uses full-client coordinates instead of the centered GUI rectangle");
        check(Math.Abs(wideBackground.Size.X * 5121f - LanyardPreferences.Current.HudWidth * 1921f) < .01f,
            "ultrawide HUD preserves its compact physical GUI-scaled width");
        MyGuiManager.MouseCursorPosition = new Vector2((512f - 1600f) / 1920f, .3f);
        var mappedCursor = LanyardHud.CursorPosition();
        check(Math.Abs(mappedCursor.X - .1f) < .0001f && Math.Abs(mappedCursor.Y - .3f) < .0001f,
            "native GUI cursor maps through pixels to fullscreen-normalized drag coordinates");
        var wideClamped = LanyardHud.Clamp(new Vector2(2, 2), LanyardPreferences.Current);
        var wideSize = LanyardHud.Size(LanyardPreferences.Current);
        check(wideClamped.X + wideSize.X <= 1f && wideClamped.Y + wideSize.Y <= 1f && wideClamped.X > .8f,
            "ultrawide drag can reach the true screen edge while clamping the whole panel");
        MyGuiManager.GuiRectangle = oldGui;
        MyGuiManager.FullscreenRectangle = oldFullscreen;
        CheckEditor(check);
        HudAppearanceChecks.Run(check);
        CheckImmediateStatus(check);

        var panelControls = new MyGuiControls();
        var panel = new LanyardPanel(panelControls);
        TransferFeedback.CapacityText = "Linked container full.";
        TransferFeedback.Text = "An old arrival.";
        panel.RefreshStatus();
        check(panelControls.OfType<MyGuiControlMultilineText>().Single(item => item.Name == "LanyardTransfer").Text.ToString() == "Linked container full.",
            "connection menu prioritizes persistent capacity when HUD is off");
        TransferFeedback.CapacityText = "";
        panelControls.OfType<MyGuiControlButton>().Single(item => item.Name == "LanyardViewDisplay").Click();
        LanyardPreferences.Change(settings =>
        {
            settings.ManualPaused = true; settings.HudMode = HudMode.Always;
            settings.HudCustomPosition = true; settings.HudX = .3f; settings.HudY = .4f;
            settings.LinkShortcut.Key = 76; settings.LinkShortcut.Control = true; settings.WelderSupply = false;
        });
        Calls.Actions.Clear();
        panelControls.OfType<MyGuiControlButton>().Single(item => item.Name == "LanyardResetDefaults").Click();
        check(LanyardPreferences.Current.HudMode == HudMode.Off &&
            !LanyardPreferences.Current.HudCustomPosition && !LanyardPreferences.Current.ManualPaused &&
            LanyardPreferences.Current.LinkShortcut.Key == 0 && LanyardPreferences.Current.WelderSupply,
            "native Reset defaults restores all preferences");
        check(Calls.Actions.Count == 0, "reset preferences does not invoke any connection or transfer action");
        panel.Dispose();
        LanyardHud.Clear();
    }

    private static void CheckEditor(Action<bool, string> check)
    {
        LanyardPreferences.Current = new LanyardSettings();
        MySession.Static = new MySession();
        LanyardShortcuts.InputFocused = true;
        MyInput.Static = new Input();
        LanyardHudPositionScreen.Open();
        var editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        MyGuiManager.Text.Clear();
        editor.Draw();
        check(MyGuiManager.Text.Any(item => item.Text == "Lanyard · HUD preview") &&
            MyGuiManager.Text.Any(item => item.Text == "Drag this panel to move it."),
            "drag editor immediately previews an off HUD with its movement instruction");
        Calls.Actions.Clear();
        MyGuiManager.MouseCursorPosition = new Vector2(.03f, .03f);
        MyInput.Static.Left = MyInput.Static.NewLeft = true;
        editor.HandleInput(true);
        MyInput.Static.NewLeft = false;
        MyGuiManager.MouseCursorPosition = new Vector2(.56f, .35f);
        editor.HandleInput(false);
        MyInput.Static.Left = false;
        editor.HandleInput(false);
        check(!LanyardPreferences.Current.HudCustomPosition && LanyardPreferences.Current.HudX == .02f, "drag and release do not silently commit preferences");
        check(Calls.Actions.Count == 0, "drag input does not invoke tool or shortcut actions");
        LanyardPreferences.Change(settings => settings.GrinderReturns = false);
        editor.Controls.OfType<MyGuiControlButton>().Single(item => item.Name == "LanyardPositionSave").Click();
        check(LanyardPreferences.Current.HudCustomPosition && Math.Abs(LanyardPreferences.Current.HudX - .55f) < .0001f &&
            Math.Abs(LanyardPreferences.Current.HudY - .34f) < .0001f, "Save commits the normalized dragged location");
        check(!LanyardPreferences.Current.GrinderReturns && LanyardPreferences.Current.HudMode == HudMode.Off, "Save preserves the latest independent preferences");

        var savedX = LanyardPreferences.Current.HudX;
        LanyardHudPositionScreen.Open();
        editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        MyGuiManager.MouseCursorPosition = new Vector2(savedX + .01f, LanyardPreferences.Current.HudY + .01f);
        MyInput.Static.Left = MyInput.Static.NewLeft = true;
        editor.HandleInput(true);
        MyInput.Static.NewLeft = false;
        MyGuiManager.MouseCursorPosition = new Vector2(3f, 3f);
        editor.HandleInput(false);
        MyGuiManager.Backgrounds.Clear();
        editor.Draw();
        var moved = MyGuiManager.Backgrounds[0];
        check(moved.Position.X + moved.Size.X <= 1f && moved.Position.Y + moved.Size.Y <= 1f, "actual editor dragging clamps the full panel");
        editor.Controls.OfType<MyGuiControlButton>().Single(item => item.Name == "LanyardPositionCancel").Click();
        check(LanyardPreferences.Current.HudX == savedX, "Cancel leaves the saved position unchanged");

        MyInput.Static.Left = MyInput.Static.NewLeft = false;
        LanyardHudPositionScreen.Open();
        editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        MyInput.Static.Escape = true;
        editor.HandleInput(false);
        check(LanyardPreferences.Current.HudX == savedX && editor.Controls.All(item => !item.Enabled), "Escape cancels without committing and neutralizes stale controls");
        MyInput.Static.Escape = false;
        LanyardHudPositionScreen.Open();
        editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        LanyardShortcuts.InputFocused = false;
        editor.Update(false);
        editor.Controls.OfType<MyGuiControlButton>().Single(item => item.Name == "LanyardPositionSave").Click();
        check(LanyardPreferences.Current.HudX == savedX && editor.Controls.All(item => !item.Enabled), "app focus loss cancels the editor without a stray save");
        LanyardShortcuts.InputFocused = true;
        LanyardHudPositionScreen.Open();
        editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        MySession.Static.LocalCharacter = new Character();
        editor.Update(true);
        check(editor.Controls.All(item => !item.Enabled), "character replacement cancels drag editing");
        LanyardHudPositionScreen.Open();
        editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        LanyardHud.Clear();
        check(editor.Controls.All(item => !item.Enabled), "HUD lifecycle cleanup closes the position editor");
        LanyardHudPositionScreen.Open();
        editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        MyGuiScreenBase.ThrowCloseNow = true;
        try { LanyardHud.Clear(); }
        finally { MyGuiScreenBase.ThrowCloseNow = false; }
        check(editor.Controls.All(item => !item.Enabled), "optional native close failure cannot abort HUD cleanup or keep callbacks live");
        LanyardHudPositionScreen.Open();
        check(!ReferenceEquals(editor, MyGuiSandbox.LastScreen), "failed native close does not retain the editor singleton");
        LanyardHud.Clear();
    }

    private static void CheckImmediateStatus(Action<bool, string> check)
    {
        try
        {
            MySession.Static = new MySession();
            LanyardPreferences.Current = new LanyardSettings { HudMode = HudMode.Always };
            LanyardShortcuts.RenderReady = true;
            LanyardHud.Clear();
            LinkController.StateText = "Active";
            MyGuiManager.Text.Clear();
            LanyardHud.Draw();
            check(MyGuiManager.Text[1].Text == "Active", "enabled HUD immediately shows connection status");
            LanyardShortcuts.GameplayReady = false;
            LanyardShortcuts.InputFocused = false;
            LinkController.StateText = "Paused by you";
            MyGuiManager.Text.Clear();
            LanyardHud.Draw();
            check(MyGuiManager.Text[1].Text == "Paused by you", "HUD continues showing current status without shortcut input focus");
            LanyardShortcuts.InputFocused = true;
            LanyardPreferences.Current.HudMode = HudMode.Off;
            MyGuiManager.Text.Clear();
            LanyardHud.Draw();
            check(MyGuiManager.Text.Count == 0, "disabling the HUD hides it immediately");
            LanyardPreferences.Current.HudMode = HudMode.Always;
            MyGuiManager.Text.Clear();
            LanyardHud.Draw();
            check(MyGuiManager.Text[1].Text == "Paused by you", "re-enabling the HUD immediately restores current status without an animation");
            MyHud.MinimalHud = true;
            MyGuiManager.Text.Clear();
            LanyardHud.Draw();
            check(MyGuiManager.Text.Count == 0, "hidden native HUD suppresses the status overlay");
            MyHud.MinimalHud = false;
        }
        finally { LanyardShortcuts.GameplayReady = true; LanyardShortcuts.InputFocused = true; }
    }
}

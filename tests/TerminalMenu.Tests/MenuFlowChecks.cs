using System;
using System.Linq;
using HarmonyLib;
using Lanyard;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Input;
using VRage.Utils;

internal static class MenuFlowChecks
{
    public static void Run(Action<bool, string> check)
    {
        MyGuiControlButton Button(MyGuiControls controls, string id) => controls.OfType<MyGuiControlButton>().Single(item => item.Name == "Lanyard" + id);
        LanyardHud.Clear(); LanyardScreen.Close();
        MySession.Static = new MySession();
        LanyardPreferences.Current = new LanyardSettings { HudScale = .6f };
        LanyardShortcuts.InputFocused = true;
        LanyardShortcuts.EscapeConsumed = false;
        MyInput.Static = new Input();
        LanyardScreen.Open();
        var screen = (LanyardScreen)MyGuiSandbox.LastScreen;
        Button(screen.Controls, "ViewDisplay").Click();
        var customize = Button(screen.Controls, "HudDrag");
        screen.FocusedControl = customize;
        customize.Click();
        var editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        check(editor.FocusedControl != null && editor.FocusedControl.Enabled && editor.FocusedControl.CanHaveFocus &&
            editor.FocusedControl.Name == "LanyardPositionCancel", "minimum-size editor initially focuses the enabled safe Cancel action");
        screen.Update(false);
        Button(editor.Controls, "PositionCancel").Click();
        screen.Update(true);
        check(ReferenceEquals(screen.FocusedControl, customize), "normal editor return restores standalone Customize HUD focus");
        customize.Click();
        editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        screen.Update(false);
        Button(editor.Controls, "PositionSave").Click();
        screen.Update(true);
        check(ReferenceEquals(screen.FocusedControl, customize), "saved appearance also returns focus to standalone Customize HUD");

        var navigate = AccessTools.Method(typeof(LanyardScreen), "HandleKeyboardActiveIndex");
        var visitedMode = false;
        var visitedPosition = false;
        var visitedCustomize = false;
        for (var index = 0; index < 18; index++)
        {
            navigate.Invoke(screen, new object[] { MyDirection.Down, true, true });
            check(screen.FocusedControl.Visible && screen.FocusedControl.Enabled, "Display Tab stays on enabled visible native controls");
            visitedMode |= screen.FocusedControl.Name == "LanyardHudMode";
            visitedPosition |= screen.FocusedControl.Name == "LanyardHudPosition";
            visitedCustomize |= ReferenceEquals(screen.FocusedControl, customize);
        }
        check(visitedMode && visitedPosition && visitedCustomize, "standalone keyboard order reaches both display selectors and appearance editing");
        customize.Click();
        editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        LanyardScreen.Close();
        var priorFocus = screen.FocusedControl;
        Button(editor.Controls, "PositionCancel").Click();
        screen.Update(true);
        check(ReferenceEquals(screen.FocusedControl, priorFocus) && screen.IsClosed, "closing a parent prevents editor focus restoration from reactivating it");

        TerminalMenu.Initialize();
        var terminal = new MyGuiScreenTerminal();
        terminal.Build();
        var page = terminal.Tabs.Pages.Single(item => item.Name == "PageLanyard");
        terminal.Tabs.SelectedPage = page.PageKey;
        Button(page.Controls, "ViewDisplay").Click();
        customize = Button(page.Controls, "HudDrag");
        terminal.FocusedControl = customize;
        customize.Click();
        editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        terminal.Update(false);
        Button(editor.Controls, "PositionCancel").Click();
        terminal.Update(true);
        check(ReferenceEquals(terminal.FocusedControl, customize), "normal editor return restores terminal Customize HUD focus");
        customize.Click();
        editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        terminal.Tabs.SelectedPage = 0;
        var nativeControl = new MyGuiControlBase { Enabled = true, Visible = true, CanHaveFocus = true };
        terminal.FocusedControl = nativeControl;
        Button(editor.Controls, "PositionCancel").Click();
        terminal.Update(true);
        check(ReferenceEquals(terminal.FocusedControl, nativeControl), "hidden terminal tab does not steal focus on editor return");
        terminal.Tabs.SelectedPage = page.PageKey;
        Button(page.Controls, "ViewDisplay").Click();
        customize = Button(page.Controls, "HudDrag");
        customize.Click();
        editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        TerminalMenu.Clear();
        terminal.FocusedControl = nativeControl;
        Button(editor.Controls, "PositionCancel").Click();
        terminal.Update(true);
        check(ReferenceEquals(terminal.FocusedControl, nativeControl), "disposed terminal panel cannot restore editor focus");
        TerminalMenu.Dispose();

        LanyardScreen.Open();
        screen = (LanyardScreen)MyGuiSandbox.LastScreen;
        MyInput.Static.Escape = true;
        LanyardShortcuts.EscapeConsumed = true;
        screen.HandleInput(false);
        check(!screen.IsClosed && screen.InputDispatches == 0 && ReferenceEquals(LanyardShortcuts.LastEscapeOwner, screen),
            "actual standalone host delegates consumed capture Escape before native close handling");
        LanyardShortcuts.EscapeConsumed = false;
        screen.HandleInput(false);
        check(screen.IsClosed && screen.InputDispatches == 1, "ordinary standalone Escape still reaches native close handling");
        MyInput.Static.Escape = false;
        TerminalMenu.Initialize();
        terminal = new MyGuiScreenTerminal();
        terminal.Build();
        page = terminal.Tabs.Pages.Single(item => item.Name == "PageLanyard");
        terminal.Tabs.SelectedPage = page.PageKey;
        MyInput.Static.Escape = true;
        LanyardShortcuts.EscapeConsumed = true;
        terminal.HandleInput(false);
        check(!terminal.IsClosed && terminal.InputDispatches == 0 && ReferenceEquals(LanyardShortcuts.LastEscapeOwner, terminal),
            "actual concrete terminal prefix delegates consumed capture Escape before native input");
        terminal.Tabs.SelectedPage = 0;
        terminal.HandleInput(false);
        check(terminal.IsClosed && terminal.InputDispatches == 1, "capture guard never swallows Escape on another terminal page");
        LanyardShortcuts.EscapeConsumed = false;
        MyInput.Static.Escape = false;
        TerminalMenu.Dispose();
        LanyardHud.Clear();
    }
}

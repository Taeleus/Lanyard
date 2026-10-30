using System;
using System.Linq;
using Lanyard;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Input;

internal static class EditorLifecycleChecks
{
    public static void Run(Action<bool, string> check)
    {
        try
        {
            foreach (var reason in new[] { "Save", "Cancel", "Escape", "Focus", "Context", "Teardown" })
            {
                LanyardHud.Clear();
                MyScreenManager.NativeReset();
                MyScreenManager.FaithfulClosing = true;
                MySession.Static = new MySession();
                MyInput.Static = new Input();
                LanyardShortcuts.InputFocused = true;
                LanyardShortcuts.EscapeConsumed = false;
                LanyardPreferences.Current = new LanyardSettings();
                var parent = new MyGuiScreenBase();
                MyGuiSandbox.AddScreen(parent);
                var callbacks = 0;
                LanyardHudPositionScreen.Open(() => callbacks++);
                var editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
                MyGuiControlButton Button(string id) => editor.Controls.OfType<MyGuiControlButton>().Single(item => item.Name == "LanyardPosition" + id);
                Button("ScaleUp").Click();
                var writes = LanyardPreferences.Writes;
                switch (reason)
                {
                    case "Save": Button("Save").Click(); break;
                    case "Cancel": Button("Cancel").Click(); break;
                    case "Escape": MyInput.Static.Escape = true; editor.HandleInput(false); MyInput.Static.Escape = false; break;
                    case "Focus": LanyardShortcuts.InputFocused = false; editor.Update(false); break;
                    case "Context": MySession.Static = new MySession(); editor.Update(true); break;
                    default: MySession.Static.IsUnloading = true; LanyardHud.Clear(); break;
                }
                var expectedWrites = writes + (reason == "Save" ? 1 : 0);
                Button("Save").Click();
                check(LanyardPreferences.Writes == expectedWrites, reason + ": a pending close cannot save twice or commit canceled appearance");
                check(editor.Controls.All(control => !control.Enabled), reason + ": pending close disables every editor input");
                if (reason != "Teardown")
                {
                    check(editor.NativeState == TestScreenState.Closing && editor.NativeClosedEvents == 0 &&
                        MyScreenManager.NativeContains(editor), reason + ": native close starts a transition without immediately removing the modal");
                    var updates = editor.NativeBaseUpdates;
                    MyScreenManager.NativeInput();
                    check(parent.InputDispatches == 0, reason + ": parent receives no input while modal close is pending");
                    MyScreenManager.NativeStep(100);
                    check(editor.NativeBaseUpdates > updates, reason + ": closing editor continues native base updates after close request");
                    check(editor.NativeState == TestScreenState.Closing && MyScreenManager.NativeContains(editor),
                        reason + ": manager never treats Update false as permission to remove the modal");
                    MyScreenManager.NativeStep(150);
                    check(editor.NativeState == TestScreenState.Closed && editor.NativeClosedEvents == 1 &&
                        MyScreenManager.NativeContains(editor), reason + ": native transition reaches Closed and queues later removal");
                }
                else
                    check(editor.NativeState == TestScreenState.Closed && editor.NativeClosedEvents == 1,
                        "teardown closes immediately through the native unload path");
                MyScreenManager.NativeStep(1);
                check(!MyScreenManager.NativeContains(editor) && ReferenceEquals(MyScreenManager.GetScreenWithFocus(), parent),
                    reason + ": the manager removes the closed modal and returns focus to its parent");
                MyScreenManager.NativeStep(200);
                LanyardShortcuts.InputFocused = true;
                MyScreenManager.NativeInput();
                check(parent.InputDispatches == 1 && parent.NativeState == TestScreenState.Opened,
                    reason + ": parent input works again after native removal and unhide updates");
                check(callbacks == (reason == "Save" || reason == "Cancel" || reason == "Escape" ? 1 : 0),
                    reason + ": close callback occurs once only for a valid normal return");
                Button("Save").Click();
                check(LanyardPreferences.Writes == expectedWrites, reason + ": removed modal callbacks cannot persist another change");
            }
            CheckCloseFailure(check, false);
            CheckCloseFailure(check, true);
            CheckActualParent(check);
        }
        finally
        {
            LanyardHud.Clear();
            LanyardShortcuts.InputFocused = true;
            MyInput.Static = new Input();
            MyScreenManager.NativeReset();
            LanyardPreferences.Current = new LanyardSettings();
        }
    }

    private static void CheckCloseFailure(Action<bool, string> check, bool teardown)
    {
        LanyardHud.Clear();
        MyScreenManager.NativeReset();
        MyScreenManager.FaithfulClosing = true;
        MySession.Static = new MySession();
        MyInput.Static = new Input();
        LanyardShortcuts.InputFocused = true;
        var parent = new MyGuiScreenBase();
        MyGuiSandbox.AddScreen(parent);
        LanyardHudPositionScreen.Open();
        var editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        MyGuiScreenBase.ThrowCloseNow = true;
        try
        {
            if (teardown) LanyardHud.Clear();
            else
            {
                editor.CloseScreen();
                MyScreenManager.NativeStep(250);
            }
        }
        finally { MyGuiScreenBase.ThrowCloseNow = false; }
        check(editor.Controls.All(control => !control.Enabled) && MyScreenManager.NativeContains(editor),
            "close failure: editor inputs are inert while native manager removal is queued");
        MyScreenManager.NativeStep(1);
        MyScreenManager.NativeStep(200);
        MyScreenManager.NativeInput();
        check(!MyScreenManager.NativeContains(editor) && parent.InputDispatches == 1,
            "close failure: " + (teardown ? "teardown" : "transition") + " failure releases modal ownership through queued removal");
        LanyardHudPositionScreen.Open();
        check(!ReferenceEquals(editor, MyGuiSandbox.LastScreen), "close failure: removed editor cannot retain the singleton or block reopening");
        LanyardHud.Clear();
        MyScreenManager.NativeStep(1);
    }

    private static void CheckActualParent(Action<bool, string> check)
    {
        LanyardHud.Clear();
        LanyardScreen.Close();
        MyScreenManager.NativeReset();
        MyScreenManager.FaithfulClosing = true;
        MySession.Static = new MySession();
        MyInput.Static = new Input();
        LanyardShortcuts.InputFocused = true;
        LanyardScreen.Open();
        var parent = (LanyardScreen)MyGuiSandbox.LastScreen;
        parent.Controls.OfType<MyGuiControlButton>().Single(button => button.Name == "LanyardViewDisplay").Click();
        var customize = parent.Controls.OfType<MyGuiControlButton>().Single(button => button.Name == "LanyardHudDrag");
        parent.FocusedControl = customize;
        customize.Click();
        var editor = (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        editor.Controls.OfType<MyGuiControlButton>().Single(button => button.Name == "LanyardPositionSave").Click();
        MyScreenManager.NativeStep(100);
        MyScreenManager.NativeStep(150);
        MyScreenManager.NativeStep(1);
        MyScreenManager.NativeStep(200);
        check(!MyScreenManager.NativeContains(editor) && ReferenceEquals(MyScreenManager.GetScreenWithFocus(), parent) &&
            ReferenceEquals(parent.FocusedControl, customize), "actual menu parent regains Customize focus after native close, removal and unhide");
        MyScreenManager.NativeInput();
        check(parent.InputDispatches == 1, "actual menu parent receives input after the editor is removed");
        MyInput.Static.Escape = true;
        MyScreenManager.NativeInput();
        MyInput.Static.Escape = false;
        MyScreenManager.NativeStep(250);
        MyScreenManager.NativeStep(1);
        check(!MyScreenManager.NativeContains(parent), "normal Escape can close the restored actual parent menu without an invisible modal remaining");
        LanyardScreen.Close();
    }
}

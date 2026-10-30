using System;
using System.Collections.Generic;
using Sandbox;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Input;
using VRage.Utils;

namespace Lanyard;

/// <summary>Optional local chords; the game's controls are never rebound or blocked.</summary>
internal static class LanyardShortcuts
{
    private static readonly List<MyKeys> pressed = new List<MyKeys>();
    private static ShortcutAction? capture;
    private static MyGuiScreenBase captureScreen;
    private static MySession captureSession;
    private static object captureCharacter;
    private static MyGuiScreenBase escapeOwner;
    private static MySession escapeSession;
    private static object escapeCharacter;
    private static bool waitForRelease;
    private static bool suppressUntilReleased = true;
    private static string captureText = "Choose Assign, then press Ctrl/Alt + a key or a function key. Escape cancels.";

    public static bool IsCapturing => capture.HasValue;
    public static ShortcutAction? CaptureAction => capture;
    public static string CaptureText => captureText;

    internal static bool InputFocused => MySandboxGame.Static?.IsActive == true &&
        MyVRage.Platform?.Windows?.Window?.IsActive == true;

    internal static bool RenderReady => MySandboxGame.IsGameReady &&
        MySession.Static != null && !MySession.Static.IsUnloading && MySession.Static.LocalCharacter != null &&
        !MySession.Static.LocalCharacter.IsDead && !MySession.Static.LocalCharacter.Closed &&
        !MyScreenManager.IsScreenOfTypeOpen("MyGuiScreenChat") &&
        !(MyScreenManager.GetScreenWithFocus()?.FocusedControl is MyGuiControlTextbox) &&
        ReferenceEquals(MyScreenManager.GetScreenWithFocus(), MyGuiScreenGamePlay.Static) &&
        MyGuiScreenGamePlay.Static != null && MyGuiScreenGamePlay.ActiveGameplayScreen == null;

    internal static bool GameplayReady => InputFocused && !MySandboxGame.IsPaused && RenderReady;

    public static void BeginCapture(ShortcutAction action)
    {
        if (!Enum.IsDefined(typeof(ShortcutAction), action)) return;
        ClearEscapeMarker();
        capture = action;
        captureScreen = MyScreenManager.GetScreenWithFocus();
        captureSession = MySession.Static;
        captureCharacter = captureSession?.LocalCharacter;
        waitForRelease = true;
        suppressUntilReleased = true;
        captureText = ActionName(action) + ": release held keys, then press Ctrl/Alt + a key or a function key. Escape cancels.";
    }

    public static void CancelCapture()
    {
        if (capture.HasValue) captureText = ActionName(capture.Value) + " binding canceled.";
        EndCapture();
    }

    private static void EndCapture()
    {
        capture = null;
        captureScreen = null;
        captureSession = null;
        captureCharacter = null;
        waitForRelease = false;
        suppressUntilReleased = true;
    }

    public static void ClearTransient()
    {
        CancelCapture();
        ClearEscapeMarker();
        pressed.Clear();
        captureText = "Choose Assign, then press Ctrl/Alt + a key or a function key. Escape cancels.";
    }

    public static void Clear(ShortcutAction action)
    {
        CancelCapture();
        LanyardPreferences.Change(settings => settings.SetBinding(action, new ShortcutBinding()));
        captureText = ActionName(action) + " shortcut cleared.";
    }

    /// <summary>Consume only the capture-cancel Escape, whichever input path runs first.</summary>
    public static bool HandleCaptureEscape(MyGuiScreenBase owner)
    {
        RefreshEscapeMarker();
        if (escapeOwner != null) return ReferenceEquals(owner, escapeOwner);
        var input = MyInput.Static;
        if (!capture.HasValue || !ReferenceEquals(owner, captureScreen) || !CaptureContextValid() ||
            input == null || !input.IsNewKeyPressed(MyKeys.Escape)) return false;
        var session = captureSession;
        var character = captureCharacter;
        CancelCapture();
        escapeOwner = owner;
        escapeSession = session;
        escapeCharacter = character;
        return true;
    }

    private static bool CaptureContextValid() => ReferenceEquals(captureSession, MySession.Static) &&
        ReferenceEquals(captureCharacter, captureSession?.LocalCharacter) && InputContextValid(captureScreen);

    private static bool InputContextValid(MyGuiScreenBase owner) => InputFocused && owner != null &&
        (owner is LanyardScreen || owner is MyGuiScreenTerminal) &&
        ReferenceEquals(owner, MyScreenManager.GetScreenWithFocus()) &&
        MySession.Static != null && !MySession.Static.IsUnloading && MySession.Static.LocalCharacter != null &&
        !MySession.Static.LocalCharacter.IsDead && !MySession.Static.LocalCharacter.Closed &&
        !MyScreenManager.IsScreenOfTypeOpen("MyGuiScreenChat") && !(owner.FocusedControl is MyGuiControlTextbox);

    private static void RefreshEscapeMarker()
    {
        if (escapeOwner != null && (MyInput.Static == null || !MyInput.Static.IsKeyPress(MyKeys.Escape) ||
            !ReferenceEquals(escapeSession, MySession.Static) ||
            !ReferenceEquals(escapeCharacter, escapeSession?.LocalCharacter) || !InputContextValid(escapeOwner))) ClearEscapeMarker();
    }

    private static void ClearEscapeMarker()
    {
        escapeOwner = null;
        escapeSession = null;
        escapeCharacter = null;
    }

    private static string ActionName(ShortcutAction action) => action == ShortcutAction.Link ? "Link targeted port" :
        action == ShortcutAction.Menu ? "Open menu" : action == ShortcutAction.Pause ? "Pause / resume" : "Toggle bounds";

    public static string BindingText(ShortcutAction action)
    {
        var binding = LanyardPreferences.Current.Binding(action);
        if (binding == null || binding.Key == 0) return "Unassigned";
        return (binding.Control ? "Ctrl+" : "") + (binding.Alt ? "Alt+" : "") +
            (binding.Shift ? "Shift+" : "") + ((MyKeys)binding.Key).ToString();
    }

    public static void Update()
    {
        try
        {
            var input = MyInput.Static;
            if (input == null) { ClearTransient(); return; }
            RefreshEscapeMarker();
            pressed.Clear();
            input.GetPressedKeys(pressed);
            var session = MySession.Static;
            if (!InputFocused || session == null || session.IsUnloading || session.LocalCharacter == null ||
                session.LocalCharacter.IsDead || session.LocalCharacter.Closed)
            {
                ClearTransient();
                return;
            }
            if (capture.HasValue)
            {
                if (!CaptureContextValid())
                {
                    CancelCapture();
                    return;
                }
                if (HandleCaptureEscape(captureScreen)) return;
                if (waitForRelease)
                {
                    if (pressed.Count == 0) waitForRelease = false;
                    return;
                }
                foreach (var key in pressed)
                {
                    if (!input.IsNewKeyPressed(key) || IsModifier(key)) continue;
                    var binding = new ShortcutBinding
                    {
                        Key = (int)key, Control = input.IsAnyCtrlKeyPressed(),
                        Alt = input.IsAnyAltKeyPressed(), Shift = input.IsAnyShiftKeyPressed()
                    };
                    if (!binding.IsValid || input.IsKeyPress(MyKeys.LeftWindows) || input.IsKeyPress(MyKeys.RightWindows))
                    {
                        captureText = "That key is reserved. Use Ctrl/Alt + a letter, digit or punctuation, or a function key.";
                        return;
                    }
                    foreach (ShortcutAction other in Enum.GetValues(typeof(ShortcutAction)))
                        if (other != capture.Value && binding.Matches(LanyardPreferences.Current.Binding(other)))
                        {
                            captureText = "Already assigned to " + other + ". Choose another key or clear that assignment.";
                            return;
                        }
                    var action = capture.Value;
                    LanyardPreferences.Change(settings => settings.SetBinding(action, binding));
                    EndCapture();
                    captureText = ActionName(action) + " shortcut: " + BindingText(action) + ".";
                    return;
                }
                return;
            }
            if (!GameplayReady) { suppressUntilReleased = true; return; }
            if (suppressUntilReleased)
            {
                if (pressed.Count == 0) suppressUntilReleased = false;
                return;
            }
            if (input.IsKeyPress(MyKeys.LeftWindows) || input.IsKeyPress(MyKeys.RightWindows)) return;
            foreach (ShortcutAction action in Enum.GetValues(typeof(ShortcutAction)))
            {
                var binding = LanyardPreferences.Current.Binding(action);
                if (binding.Key == 0 || !input.IsNewKeyPressed((MyKeys)binding.Key) ||
                    binding.Control != input.IsAnyCtrlKeyPressed() || binding.Alt != input.IsAnyAltKeyPressed() ||
                    binding.Shift != input.IsAnyShiftKeyPressed()) continue;
                switch (action)
                {
                    case ShortcutAction.Link: GetTargetedBlock.GetPort(); break;
                    case ShortcutAction.Menu: LanyardScreen.Open(); break;
                    case ShortcutAction.Pause: LinkController.ToggleUserPause(); break;
                    case ShortcutAction.Bounds:
                        if (LinkController.HasConnection) BoundsPreview.ToggleLinked(); else BoundsPreview.Toggle();
                        break;
                }
                return;
            }
        }
        catch (Exception exception)
        {
            ClearTransient();
            MyLog.Default?.WriteLineAndConsole("Lanyard shortcut input ignored: " + exception);
        }
    }

    private static bool IsModifier(MyKeys key) => key == MyKeys.Shift || key == MyKeys.LeftShift || key == MyKeys.RightShift ||
        key == MyKeys.Control || key == MyKeys.LeftControl || key == MyKeys.RightControl ||
        key == MyKeys.Alt || key == MyKeys.LeftAlt || key == MyKeys.RightAlt;
}

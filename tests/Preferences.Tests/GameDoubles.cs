using System.Collections.Generic;

namespace VRage.FileSystem { public static class MyFileSystem { public static string UserDataPath; } }
namespace VRage.Utils
{
    public sealed class MyLog
    {
        public static MyLog Default = new MyLog();
        public readonly List<string> Lines = new List<string>();
        public void WriteLineAndConsole(string text) => Lines.Add(text);
    }
}
namespace VRage
{
    public static class MyVRage { public static Platform Platform = new Platform(); }
    public sealed class Platform { public Windows Windows = new Windows(); }
    public sealed class Windows { public Window Window = new Window(); }
    public sealed class Window { public bool IsActive = true; }
}
namespace Sandbox
{
    public sealed class MySandboxGame
    {
        public static MySandboxGame Static = new MySandboxGame();
        public static bool IsGameReady = true;
        public static bool IsPaused;
        public bool IsActive = true;
    }
}
namespace Sandbox.Game.World
{
    public sealed class Character { public bool IsDead; public bool Closed; }
    public sealed class MySession
    {
        public static MySession Static;
        public bool IsUnloading;
        public Character LocalCharacter = new Character();
    }
}
namespace Sandbox.Graphics.GUI
{
    public class MyGuiControlTextbox { }
    public class MyGuiScreenBase
    {
        public object FocusedControl;
        public bool NativeClosed;
        public int NativeInputCalls;
        public virtual void HandleInput(bool receivedFocusInThisUpdate)
        {
            NativeInputCalls++;
            if (VRage.Input.MyInput.Static.IsNewKeyPressed(VRage.Input.MyKeys.Escape)) NativeClosed = true;
        }
    }
    public static class MyScreenManager
    {
        public static MyGuiScreenBase Focus;
        public static bool ChatOpen;
        public static MyGuiScreenBase GetScreenWithFocus() => Focus;
        public static bool IsScreenOfTypeOpen(string name) => ChatOpen && name == "MyGuiScreenChat";
    }
}
namespace Sandbox.Game.Gui
{
    public sealed class MyGuiScreenGamePlay : Sandbox.Graphics.GUI.MyGuiScreenBase
    {
        public static MyGuiScreenGamePlay Static = new MyGuiScreenGamePlay();
        public static Sandbox.Graphics.GUI.MyGuiScreenBase ActiveGameplayScreen;
    }
    public sealed class MyGuiScreenTerminal : Sandbox.Graphics.GUI.MyGuiScreenBase
    {
        public override void HandleInput(bool receivedFocusInThisUpdate)
        {
            if (!Lanyard.LanyardShortcuts.HandleCaptureEscape(this)) base.HandleInput(receivedFocusInThisUpdate);
        }
    }
}
namespace VRage.Input
{
    public enum MyKeys : byte
    {
        None = 0, Enter = 13, Shift = 16, Control = 17, Alt = 18, Escape = 27, Tab = 9,
        L = 76, M = 77, P = 80, B = 66, LeftWindows = 91, RightWindows = 92,
        F1 = 112, F4 = 115, LeftShift = 160, RightShift = 161,
        LeftControl = 162, RightControl = 163, LeftAlt = 164, RightAlt = 165
    }
    public static class MyInput { public static Input Static = new Input(); }
    public sealed class Input
    {
        private HashSet<MyKeys> keys = new HashSet<MyKeys>();
        private HashSet<MyKeys> previous = new HashSet<MyKeys>();
        public void Frame(params MyKeys[] next) { previous = keys; keys = new HashSet<MyKeys>(next); }
        public void GetPressedKeys(List<MyKeys> target) => target.AddRange(keys);
        public bool IsKeyPress(MyKeys key) => keys.Contains(key);
        public bool IsNewKeyPressed(MyKeys key) => keys.Contains(key) && !previous.Contains(key);
        public bool IsAnyCtrlKeyPressed() => keys.Contains(MyKeys.Control) || keys.Contains(MyKeys.LeftControl) || keys.Contains(MyKeys.RightControl);
        public bool IsAnyAltKeyPressed() => keys.Contains(MyKeys.Alt) || keys.Contains(MyKeys.LeftAlt) || keys.Contains(MyKeys.RightAlt);
        public bool IsAnyShiftKeyPressed() => keys.Contains(MyKeys.Shift) || keys.Contains(MyKeys.LeftShift) || keys.Contains(MyKeys.RightShift);
    }
}
namespace Lanyard
{
    public sealed class LanyardScreen : Sandbox.Graphics.GUI.MyGuiScreenBase
    {
        public static int Opens;
        public static void Open() { Opens++; Sandbox.Graphics.GUI.MyScreenManager.Focus = new LanyardScreen(); }
        public override void HandleInput(bool receivedFocusInThisUpdate)
        {
            if (!LanyardShortcuts.HandleCaptureEscape(this)) base.HandleInput(receivedFocusInThisUpdate);
        }
    }
    public static class GetTargetedBlock { public static int Links; public static void GetPort() => Links++; }
    public static class LinkController
    {
        public static bool HasConnection;
        public static int Pauses;
        public static void ToggleUserPause() => Pauses++;
    }
    public static class BoundsPreview
    {
        public static int Linked, Targeted;
        public static void ToggleLinked() => Linked++;
        public static void Toggle() => Targeted++;
    }
}

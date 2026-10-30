using System;
using System.Collections.Generic;

namespace Sandbox.ModAPI
{
    public delegate void MessageEnteredHandler(string message, ref bool sendToOthers);

    public sealed class MyAPIUtilities
    {
        public static readonly MyAPIUtilities Static = new MyAPIUtilities();
        public event MessageEnteredHandler MessageEntered;
        public bool Enter(string message, bool sendToOthers = true)
        {
            MessageEntered?.Invoke(message, ref sendToOthers);
            return sendToOthers;
        }
    }

    public static class MyAPIGateway
    {
        public static Utilities Utilities = new Utilities();
        public static GameSession Session = new GameSession();
        public static PhysicsApi Physics = new PhysicsApi();
    }
    public sealed class GameSession { public Camera Camera = new Camera(); }
    public sealed class Camera { public VRageMath.MatrixD WorldMatrix; }
    public sealed class Hit { public object HitEntity; }
    public sealed class PhysicsApi
    {
        public object Target;
        public bool CastRay(VRageMath.Vector3D from, VRageMath.Vector3D to, out Hit hit)
        { hit = new Hit { HitEntity = Target }; return Target != null; }
    }

    public sealed partial class Utilities
    {
        public readonly List<string> Notifications = new List<string>();
        public void ShowNotification(string text, int duration, string font) => Notifications.Add(text);
    }
}

namespace VRage.Game.Components
{
    public enum MyUpdateOrder { NoUpdate }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MySessionComponentDescriptor : Attribute
    {
        public MySessionComponentDescriptor(MyUpdateOrder order) { }
    }

    public abstract class MySessionComponentBase
    {
        public virtual void BeforeStart() { }
        protected virtual void UnloadData() { }
        public virtual void Draw() { }
    }
}

namespace Lanyard
{
    internal static class Calls
    {
        public static readonly List<string> Actions = new List<string>();
    }

    internal static class LinkController
    {
        public static event Action StateChanged;
        public static void Notify() => StateChanged?.Invoke();
        public static int StatusReads;
        public static string CurrentStatus = "No active link.";
        public static string StatusText { get { StatusReads++; return CurrentStatus; } }
        public static bool HasConnection = true;
        public static bool IsPaused;
        private static string currentState = "Active";
        public static string StateText { get { StatusReads++; return currentState; } set { currentState = value; } }
        public static bool Access = true;
        public static bool HasAccess(SelectedBlock block) => Access;
        public static void ToggleUserPause() { LanyardPreferences.Change(settings => settings.ManualPaused = !settings.ManualPaused); Calls.Actions.Add("pause"); }
        public static void SetUserPaused(bool value) { LanyardPreferences.Change(settings => settings.ManualPaused = value); Calls.Actions.Add(value ? "pause" : "resume"); }
        public static void Disconnect(string message = null) => Calls.Actions.Add("disconnect");
        public static void BeginSession() { }
        public static void SuspendForWorldChange() { }
    }

    internal static class LinkArea
    {
        public const double BreakDistance = 100;
        public static double DistanceOutside(Sandbox.Game.Entities.MyCubeGrid grid, VRageMath.Vector3D position) => grid.Distance;
    }
    internal static class LinkRecovery { public static bool HasCurrentContext = true; }

    internal static class CombatLockout
    {
        public static void Start() { }
        public static void Clear() { }
    }

    internal static class GetTargetedBlock
    {
        public static SelectedBlock selectedBlock = new SelectedBlock();
        public static void GetPort() => Calls.Actions.Add("link");
    }

    internal sealed class SelectedBlock
    {
        public string CustomName = "Cargo";
        public Sandbox.Game.Entities.MyCubeGrid CubeGrid = new Sandbox.Game.Entities.MyCubeGrid();
    }
    internal static class TransferFeedback { public static string Text = "No recent transfers."; public static string CapacityText = ""; }
    internal static class PlannerAvailability { public static string Summary = "Steel plate: ready"; }
    internal static class LanyardPreferences
    {
        public static LanyardSettings Current = new LanyardSettings();
        public static int Writes;
        public static void EnsureLoaded() { }
        public static void Change(Action<LanyardSettings> action) { Writes++; action(Current); Current.Normalize(); }
        public static void ResetDefaults() { Current = new LanyardSettings(); }
    }
    internal static class LanyardShortcuts
    {
        public static bool GameplayReady = true;
        public static bool RenderReady = true;
        public static bool InputFocused = true;
        public static bool IsCapturing;
        public static ShortcutAction? CaptureAction;
        public static int CancelCount;
        public static string CaptureText = "Press a key combination";
        public static void BeginCapture(ShortcutAction action) { IsCapturing = true; CaptureAction = action; }
        public static void CancelCapture() { IsCapturing = false; CaptureAction = null; CancelCount++; }
        public static void ClearTransient() => CancelCapture();
        public static void Clear(ShortcutAction action) { CurrentClear = action; LanyardPreferences.Current.SetBinding(action, new ShortcutBinding()); CancelCapture(); }
        public static ShortcutAction? CurrentClear;
        public static string BindingText(ShortcutAction action) => LanyardPreferences.Current.Binding(action).Key == 0 ? "Unassigned" : "Ctrl+L";
        public static bool EscapeConsumed;
        public static Sandbox.Graphics.GUI.MyGuiScreenBase LastEscapeOwner;
        public static bool HandleCaptureEscape(Sandbox.Graphics.GUI.MyGuiScreenBase owner)
        { LastEscapeOwner = owner; if (EscapeConsumed) CancelCapture(); return EscapeConsumed; }
    }
}

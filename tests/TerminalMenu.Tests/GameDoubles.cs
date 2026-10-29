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
    }

    public sealed class Utilities
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
        public static int StatusReads;
        public static string CurrentStatus = "No active link.";
        public static string StatusText { get { StatusReads++; return CurrentStatus; } }
        public static void Disconnect(string message = null) => Calls.Actions.Add("disconnect");
    }

    internal static class BoundsPreview
    {
        public static void Clear() => Calls.Actions.Add("bounds off");
        public static void Toggle() => Calls.Actions.Add("bounds");
        public static void Draw() { }
    }

    internal static class CombatLockout
    {
        public static void Start() { }
        public static void Clear() { }
    }

    internal static class LanyardScreen
    {
        public static void Open() => Calls.Actions.Add("menu");
        public static void Close() { }
    }

    internal static class GetTargetedBlock
    {
        public static void GetPort() => Calls.Actions.Add("link");
    }
}

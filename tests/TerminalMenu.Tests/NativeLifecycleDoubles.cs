using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Graphics.GUI
{
    // Opt-in lifecycle fixture preserves the older lightweight geometry tests.
    // It models native close delay, queued removal and parent unhide separately.
    public enum TestScreenState { Opened, Hidden, Closing, Closed, Unhiding }

    public partial class MyGuiScreenBase
    {
        public TestScreenState NativeState = TestScreenState.Opened;
        public int NativeTransitionStart, NativeBaseUpdates, NativeClosedEvents;
        internal bool AdvanceNativeTransition()
        {
            NativeBaseUpdates++;
            if (NativeState == TestScreenState.Closing && MyScreenManager.NativeTime - NativeTransitionStart >= 200)
                CloseScreenNow(false);
            else if (NativeState == TestScreenState.Unhiding && MyScreenManager.NativeTime - NativeTransitionStart >= 200)
                NativeState = TestScreenState.Opened;
            return NativeState != TestScreenState.Closed;
        }
    }

    public static partial class MyScreenManager
    {
        public static bool FaithfulClosing;
        public static int NativeTime;
        private static readonly List<MyGuiScreenBase> nativeScreens = new List<MyGuiScreenBase>();
        private static readonly List<MyGuiScreenBase> nativeRemovals = new List<MyGuiScreenBase>();
        public static bool NativeContains(MyGuiScreenBase screen) => nativeScreens.Contains(screen);
        public static void NativeRegister(MyGuiScreenBase screen)
        {
            if (screen.PreviousFocus != null) screen.PreviousFocus.NativeState = TestScreenState.Hidden;
            nativeScreens.Add(screen);
            screen.Closed += (closed, unloading) => RemoveScreen(closed);
        }
        public static void RemoveScreen(MyGuiScreenBase screen)
        {
            if (!nativeRemovals.Contains(screen)) nativeRemovals.Add(screen);
        }
        public static void NativeStep(int milliseconds)
        {
            NativeTime += milliseconds;
            foreach (var screen in nativeRemovals.ToArray())
            {
                nativeScreens.Remove(screen);
                if (ReferenceEquals(Focus, screen))
                {
                    Focus = screen.PreviousFocus;
                    while (Focus != null && !nativeScreens.Contains(Focus)) Focus = Focus.PreviousFocus;
                    if (Focus != null)
                    {
                        Focus.NativeState = TestScreenState.Unhiding;
                        Focus.NativeTransitionStart = NativeTime;
                    }
                }
            }
            nativeRemovals.Clear();
            // Like the native manager, ignore each child's Update return value.
            foreach (var screen in nativeScreens.ToArray()) screen.Update(ReferenceEquals(Focus, screen));
        }
        public static void NativeInput()
        {
            if (Focus?.NativeState == TestScreenState.Opened) Focus.HandleInput(false);
        }
        public static void NativeReset()
        {
            nativeScreens.Clear(); nativeRemovals.Clear(); Focus = null; NativeTime = 0; FaithfulClosing = false;
        }
    }
}

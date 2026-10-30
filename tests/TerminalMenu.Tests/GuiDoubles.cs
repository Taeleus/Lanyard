using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace VRageMath
{
    public struct Vector2
    {
        public float X, Y;
        public Vector2(float x, float y) { X = x; Y = y; }
        public static Vector2 operator +(Vector2 first, Vector2 second) => new Vector2(first.X + second.X, first.Y + second.Y);
        public static Vector2 operator -(Vector2 first, Vector2 second) => new Vector2(first.X - second.X, first.Y - second.Y);
    }
    public struct Color
    {
        public int R, G, B, A;
        public Color(int r, int g, int b, int a) { R = r; G = g; B = b; A = a; }
        public static readonly Color White = new Color(255, 255, 255, 255), Orange = new Color(255, 165, 0, 255), LimeGreen = new Color(50, 205, 50, 255);
    }
    public struct Rectangle
    {
        public int Left, Top, Width, Height;
        public Rectangle(int left, int top, int width, int height) { Left = left; Top = top; Width = width; Height = height; }
    }
}

namespace VRage.Game
{
    public enum MyGuiControlButtonStyleEnum { Small }
}

namespace VRage.Utils
{
    public enum MyGuiDrawAlignEnum { HORISONTAL_LEFT_AND_VERTICAL_TOP }
    public enum MyDirection { Up, Down, Left, Right }
    public sealed class MyLog
    {
        public static readonly MyLog Default = new MyLog();
        public readonly List<string> Lines = new List<string>();
        public void WriteLineAndConsole(string line) => Lines.Add(line);
    }
}

namespace Sandbox.Graphics.GUI
{
    public class MyGuiControlBase
    {
        public string Name;
        public bool Enabled = true;
        public bool Visible = true;
        public bool CanHaveFocus;
        public VRageMath.Vector2 Position, Size;
        public string Tooltip;
        public void SetToolTip(string text) => Tooltip = text;
    }

    public sealed class MyGuiControls : List<MyGuiControlBase>
    {
        public MyGuiControlBase GetControlByName(string name) => this.FirstOrDefault(control => control.Name == name);
    }

    public sealed class MyGuiControlMultilineText : MyGuiControlBase
    {
        public StringBuilder Text;
        public readonly float TextScale;
        public readonly bool Scrollable;
        public VRage.Utils.MyGuiDrawAlignEnum OriginAlign;
        public MyGuiControlMultilineText(VRageMath.Vector2 position, VRageMath.Vector2 size, string font,
            float textScale, StringBuilder contents, bool drawScrollbarV, bool drawScrollbarH,
            VRage.Utils.MyGuiDrawAlignEnum textBoxAlign)
        { Text = contents; Position = position; Size = size; TextScale = textScale; Scrollable = drawScrollbarV; }
    }

    public sealed class MyGuiControlButton : MyGuiControlBase
    {
        public sealed class StyleDefinition
        {
            public object NormalTexture, HighlightTexture, FocusTexture, ActiveTexture;
            public object NormalFont, HighlightFont, Padding, BackgroundColor;
            public object TextColor, TextColorHighlight, TextColorFocus, TextColorActive;
        }
        public static int GetVisualStyleCount;
        public static StyleDefinition GetVisualStyle(VRage.Game.MyGuiControlButtonStyleEnum style) { GetVisualStyleCount++; return new StyleDefinition(); }
        public StyleDefinition CustomStyle;
        public string Text;
        public bool Checked;
        private readonly Action<MyGuiControlButton> onClick;
        public MyGuiControlButton(VRageMath.Vector2 position, VRageMath.Vector2 size, string toolTip,
            StringBuilder text, float textScale, Action<MyGuiControlButton> onButtonClick)
        { Text = text.ToString(); onClick = onButtonClick; Position = position; Size = size; Tooltip = toolTip; CanHaveFocus = true; }
        // Deliberately allow stale callback invocation even after Enabled=false.
        public void Click() => onClick(this);
    }

    public sealed class MyGuiControlLabel : MyGuiControlBase
    {
        public MyGuiControlLabel(VRageMath.Vector2 position, string text, float textScale, string font,
            VRage.Utils.MyGuiDrawAlignEnum originAlign) { }
    }

    public sealed class MyGuiControlCheckbox : MyGuiControlBase
    {
        private bool value;
        public Action<MyGuiControlCheckbox> IsCheckedChanged;
        public bool IsChecked
        {
            get => value;
            set { if (this.value == value) return; this.value = value; IsCheckedChanged?.Invoke(this); }
        }
        public MyGuiControlCheckbox(VRageMath.Vector2 position, string toolTip)
        { Position = position; Tooltip = toolTip; Size = new VRageMath.Vector2(.03f, .04f); CanHaveFocus = true; }
        public void Click() => IsChecked = !IsChecked;
    }

    public sealed class MyGuiControlCombobox : MyGuiControlBase
    {
        public readonly List<(long Key, string Text)> Items = new List<(long, string)>();
        private long selected = -1;
        public int SelectionCalls, EventCount;
        public bool IsOpen;
        public event Action ItemSelected;
        public MyGuiControlCombobox(VRageMath.Vector2 position, VRageMath.Vector2 size, string toolTip, int openAreaItemsCount)
        { Position = position; Size = size; Tooltip = toolTip; CanHaveFocus = true; }
        public void AddItem(long key, string value, bool sort = true)
        { Items.Add((key, value)); if (sort) Items.Sort((a, b) => string.CompareOrdinal(a.Text, b.Text)); }
        public long GetSelectedKey() => selected;
        public void SelectItemByKey(long key, bool sendEvent = true)
        {
            SelectionCalls++;
            if (selected == key || !Items.Any(item => item.Key == key)) return;
            selected = key;
            if (sendEvent) { EventCount++; ItemSelected?.Invoke(); }
        }
        public void TriggerItemSelected() => ItemSelected?.Invoke();
    }

    public sealed class MyGuiControlSeparatorList : MyGuiControlBase
    {
        public void AddHorizontal(VRageMath.Vector2 start, float length) { }
    }

    public sealed class MyGuiControlTabPage : MyGuiControlBase
    {
        public readonly int PageKey;
        public StringBuilder Text;
        public float TextScale;
        public bool IsTabVisible = true;
        public readonly MyGuiControls Controls = new MyGuiControls();
        public MyGuiControlTabPage(int key) { PageKey = key; }
    }

    public sealed class PageView : IEnumerable<MyGuiControlTabPage>
    {
        private readonly Dictionary<int, MyGuiControlTabPage> pages;
        public PageView(Dictionary<int, MyGuiControlTabPage> source) { pages = source; }
        public int Count => pages.Count;
        public bool TryGetValue(int key, out MyGuiControlTabPage page) => pages.TryGetValue(key, out page);
        public IEnumerator<MyGuiControlTabPage> GetEnumerator() => pages.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class MyGuiControlTabControl : MyGuiControlBase
    {
        private readonly Dictionary<int, MyGuiControlTabPage> pages = new Dictionary<int, MyGuiControlTabPage>();
        private int selectedPage;
        public readonly MyGuiControls Controls = new MyGuiControls();
        public PageView Pages => new PageView(pages);
        public float TabButtonScale { get; set; } = 1f;
        public event Action OnPageChanged;
        public int HandlerCount => OnPageChanged?.GetInvocationList().Length ?? 0;
        public int SelectedPage
        {
            get => selectedPage;
            set
            {
                if (pages.TryGetValue(selectedPage, out var old)) old.Visible = false;
                selectedPage = value;
                OnPageChanged?.Invoke();
                if (pages.TryGetValue(value, out var current)) current.Visible = true;
            }
        }
        public MyGuiControlTabPage GetTabSubControl(int key)
        {
            if (!pages.TryGetValue(key, out var page))
            { page = new MyGuiControlTabPage(key); pages.Add(key, page); Controls.Add(page); }
            return page;
        }
        public void RecreatePages()
        {
            pages.Clear();
            foreach (MyGuiControlTabPage page in Controls) { pages.Add(page.PageKey, page); page.Visible = false; }
        }
    }

    public partial class MyGuiScreenBase
    {
        public static bool ThrowCloseNow;
        protected bool DrawMouseCursor, m_canShareInput;
        public bool EnabledBackgroundFade, CloseButtonEnabled;
        public readonly MyGuiControls Controls = new MyGuiControls();
        private MyGuiControlBase focused;
        public MyGuiControlBase FocusedControl { get => focused; set { if (value == null || value.CanHaveFocus && value.Enabled) focused = value; } }
        public MyGuiScreenBase PreviousFocus;
        public bool IsClosed;
        public int InputDispatches;
        public event Action<MyGuiScreenBase, bool> Closed;
        public int ClosedHandlerCount => Closed?.GetInvocationList().Length ?? 0;
        public MyGuiScreenBase() { }
        public MyGuiScreenBase(VRageMath.Vector2 position, object color, VRageMath.Vector2? size, bool topMost,
            object unused, float backgroundOpacity, float guiOpacity) { }
        public virtual string GetFriendlyName() => "Screen";
        public virtual void RecreateControls(bool constructor) => Controls.Clear();
        public virtual bool Update(bool hasFocus) => !MyScreenManager.FaithfulClosing || AdvanceNativeTransition();
        public virtual bool Draw() => true;
        public virtual void HandleInput(bool receivedFocusInThisUpdate)
        {
            if (MyScreenManager.FaithfulClosing && NativeState != TestScreenState.Opened) return;
            InputDispatches++; if (VRage.Input.MyInput.Static?.IsNewKeyPressed(VRage.Input.MyKeys.Escape) == true) CloseScreen();
        }
        protected virtual bool HandleKeyboardActiveIndex(VRage.Utils.MyDirection direction, bool page, bool loop) => false;
        public void CloseScreenNow(bool unload)
        {
            if (ThrowCloseNow) throw new InvalidOperationException("Native close failed");
            if (MyScreenManager.FaithfulClosing)
            {
                if (NativeState == TestScreenState.Closed) return;
                NativeState = TestScreenState.Closed;
                NativeClosedEvents++;
                IsClosed = true;
                Closed?.Invoke(this, unload);
                return;
            }
            IsClosed = true;
            if (ReferenceEquals(MyScreenManager.Focus, this)) MyScreenManager.Focus = PreviousFocus;
            Closed?.Invoke(this, unload);
        }
        public virtual bool CloseScreen(bool isUnloading = false)
        {
            if (MyScreenManager.FaithfulClosing)
            {
                if (NativeState == TestScreenState.Closing || NativeState == TestScreenState.Closed) return false;
                NativeState = TestScreenState.Closing;
                NativeTransitionStart = MyScreenManager.NativeTime;
                return true;
            }
            IsClosed = true;
            if (ReferenceEquals(MyScreenManager.Focus, this)) MyScreenManager.Focus = PreviousFocus;
            Closed?.Invoke(this, isUnloading); return true;
        }
        protected void AddCaption(string caption) { }
    }

    public static class MyGuiConstants { public static readonly object SCREEN_BACKGROUND_COLOR = new object(); }
    public static class MyGuiSandbox
    {
        public static MyGuiScreenBase LastScreen;
        public static void AddScreen(MyGuiScreenBase screen)
        {
            screen.PreviousFocus = MyScreenManager.Focus; LastScreen = screen; MyScreenManager.Focus = screen;
            if (MyScreenManager.FaithfulClosing) MyScreenManager.NativeRegister(screen);
            Lanyard.Calls.Actions.Add("menu");
        }
    }
    public static partial class MyScreenManager
    {
        public static MyGuiScreenBase Focus;
        public static MyGuiScreenBase GetScreenWithFocus() => Focus;
    }
}

namespace Sandbox.Game.Gui
{
    using Sandbox.Graphics.GUI;
    public sealed class MyGuiScreenTerminal : MyGuiScreenBase
    {
        public int[] Keys = Enumerable.Range(0, 7).ToArray();
        public MyGuiControlTabControl Tabs => (MyGuiControlTabControl)Controls.GetControlByName("TerminalTabs");
        public void Close() => CloseScreen();
        public void Build() { MyScreenManager.Focus = this; CreateTabs(); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CreateTabs()
        {
            Controls.Clear();
            var tabs = new MyGuiControlTabControl { Name = "TerminalTabs" };
            foreach (var key in Keys) tabs.GetTabSubControl(key).Name = "Native" + key;
            Controls.Add(tabs);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override bool Update(bool hasFocus) => true;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void HandleInput(bool receivedFocusInThisUpdate) => base.HandleInput(receivedFocusInThisUpdate);
    }
}

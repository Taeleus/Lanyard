using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace VRageMath
{
    public struct Vector2 { public Vector2(float x, float y) { } }
}

namespace VRage.Game
{
    public enum MyGuiControlButtonStyleEnum { Small }
}

namespace VRage.Utils
{
    public enum MyGuiDrawAlignEnum { HORISONTAL_LEFT_AND_VERTICAL_TOP }
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
        public bool Visible;
    }

    public sealed class MyGuiControls : List<MyGuiControlBase>
    {
        public MyGuiControlBase GetControlByName(string name) => this.FirstOrDefault(control => control.Name == name);
    }

    public sealed class MyGuiControlMultilineText : MyGuiControlBase
    {
        public StringBuilder Text;
        public VRage.Utils.MyGuiDrawAlignEnum OriginAlign;
        public MyGuiControlMultilineText(VRageMath.Vector2 position, VRageMath.Vector2 size, string font,
            float textScale, StringBuilder contents, bool drawScrollbarV, bool drawScrollbarH,
            VRage.Utils.MyGuiDrawAlignEnum textBoxAlign) { Text = contents; }
    }

    public sealed class MyGuiControlButton : MyGuiControlBase
    {
        public sealed class StyleDefinition
        {
            public object NormalTexture, HighlightTexture, FocusTexture, ActiveTexture;
            public object NormalFont, HighlightFont, Padding, BackgroundColor;
            public object TextColor, TextColorHighlight, TextColorFocus, TextColorActive;
        }
        public static StyleDefinition GetVisualStyle(VRage.Game.MyGuiControlButtonStyleEnum style) => new StyleDefinition();
        public StyleDefinition CustomStyle;
        public VRageMath.Vector2 Size;
        public readonly StringBuilder Text;
        private readonly Action<MyGuiControlButton> onClick;
        public MyGuiControlButton(VRageMath.Vector2 position, VRageMath.Vector2 size, string toolTip,
            StringBuilder text, float textScale, Action<MyGuiControlButton> onButtonClick)
        { Text = text; onClick = onButtonClick; }
        // Deliberately allow stale callback invocation even after Enabled=false.
        public void Click() => onClick(this);
    }

    public sealed class MyGuiControlLabel : MyGuiControlBase
    {
        public MyGuiControlLabel(VRageMath.Vector2 position, string text, float textScale, string font,
            VRage.Utils.MyGuiDrawAlignEnum originAlign) { }
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

    public class MyGuiScreenBase
    {
        public readonly MyGuiControls Controls = new MyGuiControls();
        public MyGuiControlBase FocusedControl;
        public event Action<MyGuiScreenBase, bool> Closed;
        public int ClosedHandlerCount => Closed?.GetInvocationList().Length ?? 0;
        public void Close() => Closed?.Invoke(this, false);
    }
}

namespace Sandbox.Game.Gui
{
    using Sandbox.Graphics.GUI;
    public sealed class MyGuiScreenTerminal : MyGuiScreenBase
    {
        public int[] Keys = Enumerable.Range(0, 7).ToArray();
        public MyGuiControlTabControl Tabs => (MyGuiControlTabControl)Controls.GetControlByName("TerminalTabs");
        public void Build() => CreateTabs();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CreateTabs()
        {
            Controls.Clear();
            var tabs = new MyGuiControlTabControl { Name = "TerminalTabs" };
            foreach (var key in Keys) tabs.GetTabSubControl(key).Name = "Native" + key;
            Controls.Add(tabs);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool Update(bool hasFocus) => true;
    }
}

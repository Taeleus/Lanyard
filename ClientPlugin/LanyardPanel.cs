using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Lanyard;

/// <summary>Native task views shared by the terminal and standalone menu.</summary>
internal sealed class LanyardPanel : IDisposable
{
    private enum View { Connection, Tools, Display, Shortcuts }
    private static class ButtonStyleCache
    {
        internal static readonly MyGuiControlButton.StyleDefinition Value = CreateButtonStyle();
        static ButtonStyleCache() { }
    }
    private readonly List<MyGuiControlBase> focusOrder = new List<MyGuiControlBase>();
    private readonly List<MyGuiControlButton> navigation = new List<MyGuiControlButton>();
    private readonly List<MyGuiControlBase>[] views = { new List<MyGuiControlBase>(), new List<MyGuiControlBase>(), new List<MyGuiControlBase>(), new List<MyGuiControlBase>() };
    private readonly Dictionary<string, MyGuiControlButton> buttons = new Dictionary<string, MyGuiControlButton>();
    private readonly Dictionary<string, MyGuiControlMultilineText> texts = new Dictionary<string, MyGuiControlMultilineText>();
    private readonly Dictionary<string, MyGuiControlCheckbox> checks = new Dictionary<string, MyGuiControlCheckbox>();
    private readonly MyGuiControls controls;
    private readonly MyGuiScreenBase owner;
    private readonly MyGuiControlCombobox hudMode, hudPosition;
    private View selectedView;
    private bool disposed;
    private bool active = true;
    private bool refreshing;
    private bool restoreCustomizationFocus;

    public MyGuiControlBase FocusControl => selectedView == View.Connection
        ? buttons["Link"].Enabled ? buttons["Link"] : buttons["Unlink"].Enabled ? buttons["Unlink"] : navigation[0]
        : navigation[(int)selectedView];
    public IReadOnlyList<MyGuiControlBase> FocusOrder => focusOrder;

    public LanyardPanel(MyGuiControls controls, MyGuiScreenBase owner = null)
    {
        this.controls = controls;
        this.owner = owner;
        LanyardPreferences.EnsureLoaded();
        var names = new[] { "Connection", "Tools", "Display", "Shortcuts" };
        for (var index = 0; index < names.Length; index++)
        {
            var view = (View)index;
            navigation.Add(AddButton(null, "View" + names[index], -.2745f + index * .183f, -.235f, .171f,
                names[index], () => SelectView(view), "Open " + names[index].ToLowerInvariant() + " controls."));
        }

        AddText(View.Connection, "Status", -.177f, .028f, .82f);
        AddText(View.Connection, "Container", -.141f, .027f, .64f);
        AddButton(View.Connection, "Link", -.205f, -.078f, .31f, "Connect", GetTargetedBlock.GetPort,
            "Aim at an inventory port with a tool, then connect.");
        AddButton(View.Connection, "Pause", .075f, -.078f, .20f, "Pause", LinkController.ToggleUserPause,
            "Pause transfers while keeping your selected inventory.");
        AddButton(View.Connection, "Unlink", .285f, -.078f, .14f, "Disconnect", () => LinkController.Disconnect(),
            "Forget the current or saved connection so you can choose another inventory.");
        AddText(View.Connection, "LinkHint", -.043f, .038f, .62f);
        AddButton(View.Connection, "LinkedBounds", -.185f, .029f, .35f, "Toggle working area", BoundsPreview.ToggleLinked,
            "Show or hide your linked inventory's working area.");
        AddButton(View.Connection, "TargetBounds", .185f, .029f, .35f, "Toggle aimed grid", BoundsPreview.Toggle,
            "Show or hide the bounds preview for the grid you are looking at.");
        AddText(View.Connection, "Transfer", .066f, .042f, .65f);
        AddLabel(View.Connection, -.36f, .12f, "Build planner", .72f);
        AddText(View.Connection, "Planner", .148f, .075f, .63f, scroll: true);
        AddText(View.Connection, "Distance", .234f, .023f, .61f);

        AddText(View.Tools, "ToolsHint", -.174f, .035f, .66f, "Choose which automatic functions Lanyard uses.");
        AddCheck(View.Tools, "Welder", -.105f, "Welder supply", value => LanyardPreferences.Change(settings => settings.WelderSupply = value),
            "Withdraw components listed in your build planner.");
        AddCheck(View.Tools, "Grinder", -.035f, "Grinder returns", value => LanyardPreferences.Change(settings => settings.GrinderReturns = value),
            "Return ore, ingots and components while grinding.");
        AddCheck(View.Tools, "Drill", .035f, "Drill returns", value => LanyardPreferences.Change(settings => settings.DrillReturns = value),
            "Return ore from your backpack.");
        AddCheck(View.Tools, "DrillActive", .093f, "Only while drilling", value => LanyardPreferences.Change(settings => settings.DrillActiveOnly = value),
            "With Drill returns enabled, return ore only while actively drilling.", indent: .035f);
        AddCheck(View.Tools, "Starter", .168f, "Starting components", value => LanyardPreferences.Change(settings => settings.StartingComponents = value),
            "Enable or disable starting-component supply.");
        AddText(View.Tools, "ToolsHelp", .215f, .042f, .61f);

        AddLabel(View.Display, -.36f, -.162f, "HUD visibility", .66f);
        hudMode = AddCombo("HudMode", -.149f, "Choose when the HUD appears.", key =>
            LanyardPreferences.Change(settings => settings.HudMode = (HudMode)key));
        hudMode.AddItem(0, "Off", sort: false);
        hudMode.AddItem(1, "With tools", sort: false);
        hudMode.AddItem(2, "Only when paused", sort: false);
        hudMode.AddItem(3, "Always during gameplay", sort: false);
        AddLabel(View.Display, -.36f, -.099f, "Position", .66f);
        hudPosition = AddCombo("HudPosition", -.086f, "A corner preset replaces custom positioning. Custom position opens the appearance preview.", key =>
        {
            if (key == 4) { CustomizeHud(); return; }
            LanyardPreferences.Change(settings => { settings.HudPosition = (HudPosition)key; settings.HudCustomPosition = false; });
        });
        hudPosition.AddItem(0, "Top left", sort: false);
        hudPosition.AddItem(1, "Top right", sort: false);
        hudPosition.AddItem(2, "Bottom left", sort: false);
        hudPosition.AddItem(3, "Bottom right", sort: false);
        hudPosition.AddItem(4, "Custom position…", sort: false);
        AddButton(View.Display, "HudDrag", -.16f, -.011f, .40f, "Customize HUD…", CustomizeHud,
            "Preview position, size and opacity, then Save appearance or Cancel.");
        AddText(View.Display, "Appearance", -.032f, .05f, .61f, x: .075f, width: .285f);
        AddText(View.Display, "DisplayHelp", .055f, .055f, .61f,
            "Settings save automatically. Customize HUD previews changes until Save.");
        var separator = new MyGuiControlSeparatorList();
        separator.AddHorizontal(new Vector2(-.36f, .147f), .72f);
        controls.Add(separator);
        views[(int)View.Display].Add(separator);
        AddButton(View.Display, "ResetDefaults", 0f, .184f, .32f, "Reset all settings", () =>
        {
            LanyardShortcuts.ClearTransient();
            LanyardPreferences.ResetDefaults();
        }, "Restore tools, HUD appearance, manual pause and shortcuts. Keep the connection and combat cooldown.");
        AddText(View.Display, "ResetScope", .216f, .042f, .61f,
            "Restores tools, HUD, pause and shortcuts.\nYour connection and combat cooldown are kept.");

        AddText(View.Shortcuts, "ShortcutHint", -.174f, .035f, .66f, "Choose an action, then press Ctrl/Alt + a key or a function key.");
        AddShortcut(ShortcutAction.Link, "Connect / change", -.102f);
        AddShortcut(ShortcutAction.Menu, "Open menu", -.033f);
        AddShortcut(ShortcutAction.Pause, "Pause / resume", .036f);
        AddShortcut(ShortcutAction.Bounds, "Toggle working area", .105f);
        AddText(View.Shortcuts, "Capture", .151f, .056f, .61f, x: -.36f, width: .45f);
        AddButton(View.Shortcuts, "CancelBinding", .23f, .177f, .25f, "Cancel binding", LanyardShortcuts.CancelCapture,
            "Keep the current shortcut and stop waiting for a new binding.");
        AddText(View.Shortcuts, "ShortcutsSave", .23f, .025f, .61f, "Shortcuts save automatically. Escape cancels a waiting binding.");
        SelectView(View.Connection);
    }

    public void SetActive(bool value)
    {
        if (active && !value) LanyardShortcuts.CancelCapture();
        active = value;
    }

    public MyGuiControlBase TakeFocusRequest()
    {
        if (!restoreCustomizationFocus || !active || disposed || selectedView != View.Display ||
            (owner != null && !ReferenceEquals(MyScreenManager.GetScreenWithFocus(), owner))) return null;
        restoreCustomizationFocus = false;
        return buttons["HudDrag"].Visible && buttons["HudDrag"].Enabled ? buttons["HudDrag"] : null;
    }

    private void CustomizeHud()
    {
        LanyardShortcuts.CancelCapture();
        LanyardHudPositionScreen.Open(() =>
        {
            if (!disposed && selectedView == View.Display) restoreCustomizationFocus = true;
        });
    }

    private void SelectView(View view)
    {
        LanyardShortcuts.CancelCapture();
        restoreCustomizationFocus = false;
        selectedView = view;
        focusOrder.Clear();
        for (var index = 0; index < navigation.Count; index++)
        {
            navigation[index].Checked = index == (int)view;
            focusOrder.Add(navigation[index]);
            foreach (var control in views[index])
            {
                control.Visible = index == (int)view;
                control.Enabled = index == (int)view;
                if (control is MyGuiControlCombobox combo && index != (int)view) combo.IsOpen = false;
            }
        }
        foreach (var control in views[(int)view])
            if (control.CanHaveFocus) focusOrder.Add(control);
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        if (disposed || !active || refreshing) return;
        refreshing = true;
        try
        {
            switch (selectedView)
            {
                case View.Connection:
                    var block = GetTargetedBlock.selectedBlock;
                    var pending = LinkController.HasConnection && block == null;
                    SetText("Status", pending ? "Restoring saved connection" : Bounded(LinkController.StateText, 105));
                    SetText("Container", block != null ? "Inventory: " + Bounded(block.CustomName, 72) :
                        pending ? Bounded(LinkController.StateText, 105) : "Connect your tools to an inventory port.");
                    buttons["Link"].Text = pending ? "Restoring…" : block != null ? "Change connection" : "Connect";
                    buttons["Link"].Enabled = !pending;
                    var manual = LanyardPreferences.Current.ManualPaused;
                    buttons["Pause"].Text = manual ? "Resume" : "Pause";
                    buttons["Pause"].SetToolTip(manual ? "Resume your automatic transfers when range, access and combat checks allow." :
                        "Pause automatic transfers while keeping the selected inventory.");
                    buttons["Pause"].Enabled = LinkController.HasConnection;
                    buttons["Unlink"].Enabled = LinkController.HasConnection;
                    buttons["LinkedBounds"].Enabled = block != null;
                    SetText("LinkHint", pending ? "Waiting for the matching player and inventory port. Disconnect to choose another." :
                        manual ? "Paused by you. Resume keeps all normal range, access and combat checks." :
                        block == null ? "Aim at an inventory port with a tool equipped, then connect." :
                        "A failed change keeps your current connection.");
                    SetText("Transfer", Bounded(string.IsNullOrEmpty(TransferFeedback.CapacityText) ? TransferFeedback.Text : TransferFeedback.CapacityText, 180));
                    SetText("Planner", PlannerAvailability.Summary ?? "Add blocks to your build planner to check materials.");
                    SetText("Distance", BoundsPreview.DistanceText);
                    break;
                case View.Tools:
                    var settings = LanyardPreferences.Current;
                    checks["Welder"].IsChecked = settings.WelderSupply;
                    checks["Grinder"].IsChecked = settings.GrinderReturns;
                    checks["Drill"].IsChecked = settings.DrillReturns;
                    checks["DrillActive"].IsChecked = settings.DrillActiveOnly;
                    checks["DrillActive"].Enabled = settings.DrillReturns;
                    checks["Starter"].IsChecked = settings.StartingComponents;
                    SetText("ToolsHelp", settings.DrillReturns ? "Settings save automatically. Normal link restrictions always apply." :
                        "Enable Drill returns to use Only while drilling. Settings save automatically.");
                    break;
                case View.Display:
                    var display = LanyardPreferences.Current;
                    RefreshCombo(hudMode, (long)display.HudMode);
                    RefreshCombo(hudPosition, display.HudCustomPosition ? 4 : (long)display.HudPosition);
                    SetText("Appearance", "Size " + Math.Round(display.HudScale * 100) + "% · Opacity " + Math.Round(display.HudOpacity * 100) + "%\n" +
                        (display.HudCustomPosition ? "Custom position" : PositionName(display.HudPosition)));
                    break;
                case View.Shortcuts:
                    foreach (ShortcutAction action in Enum.GetValues(typeof(ShortcutAction)))
                    {
                        var assigned = LanyardPreferences.Current.Binding(action).Key != 0;
                        buttons["Bind" + action].Text = LanyardShortcuts.CaptureAction == action ? "Press keys…" :
                            assigned ? LanyardShortcuts.BindingText(action) : "Assign…";
                        buttons["Bind" + action].Checked = LanyardShortcuts.CaptureAction == action;
                        buttons["Clear" + action].Enabled = assigned && !LanyardShortcuts.IsCapturing;
                    }
                    buttons["CancelBinding"].Enabled = LanyardShortcuts.IsCapturing;
                    SetText("Capture", LanyardShortcuts.CaptureText);
                    break;
            }
        }
        catch (Exception exception) { LogFailure("status refresh", exception); }
        finally { refreshing = false; }
    }

    private static void RefreshCombo(MyGuiControlCombobox combo, long key)
    {
        if (!combo.IsOpen && combo.GetSelectedKey() != key) combo.SelectItemByKey(key, sendEvent: false);
    }

    private static string PositionName(HudPosition value) => value == HudPosition.TopLeft ? "Top left" : value == HudPosition.TopRight ? "Top right" : value == HudPosition.BottomLeft ? "Bottom left" : "Bottom right";
    private void SetText(string id, string value)
    {
        var text = value ?? "";
        if (texts[id].Text.ToString() != text) texts[id].Text = new StringBuilder(text);
    }

    internal static string Bounded(string value, int maximum)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var text = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return text.Length <= maximum ? text : text.Substring(0, maximum - 1) + "…";
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        LanyardShortcuts.CancelCapture();
        foreach (var view in views)
            foreach (var control in view) control.Enabled = false;
        foreach (var button in navigation) button.Enabled = false;
    }

    private void AddCheck(View view, string id, float y, string title, Action<bool> write, string tooltip, float indent = 0)
    {
        var control = new MyGuiControlCheckbox(new Vector2(-.335f + indent, y), toolTip: tooltip) { Name = "Lanyard" + id };
        control.IsCheckedChanged += checkbox =>
        {
            if (refreshing || disposed || !active || !checkbox.Visible || !checkbox.Enabled) return;
            try { write(checkbox.IsChecked); }
            catch (Exception exception) { LogFailure("preference", exception); }
            RefreshStatus();
        };
        controls.Add(control);
        checks.Add(id, control);
        views[(int)view].Add(control);
        AddLabel(view, -.30f + indent, y - .012f, title, .67f);
    }

    private MyGuiControlCombobox AddCombo(string id, float y, string tooltip, Action<long> select)
    {
        var control = new MyGuiControlCombobox(new Vector2(.105f, y), new Vector2(.50f, .04f), toolTip: tooltip,
            openAreaItemsCount: 5) { Name = "Lanyard" + id };
        control.ItemSelected += () =>
        {
            if (refreshing || disposed || !active || !control.Visible || !control.Enabled) return;
            try { select(control.GetSelectedKey()); }
            catch (Exception exception) { LogFailure("selection", exception); }
            RefreshStatus();
        };
        controls.Add(control);
        views[(int)View.Display].Add(control);
        return control;
    }

    private void AddShortcut(ShortcutAction action, string title, float y)
    {
        AddLabel(View.Shortcuts, -.36f, y - .012f, title, .65f);
        AddButton(View.Shortcuts, "Bind" + action, .075f, y, .32f, "Assign…", () => LanyardShortcuts.BeginCapture(action), "Choose a key combination for " + title.ToLowerInvariant() + ".");
        AddButton(View.Shortcuts, "Clear" + action, .303f, y, .105f, "Clear", () => LanyardShortcuts.Clear(action), "Remove this assigned shortcut.");
    }

    private MyGuiControlButton AddButton(View? view, string id, float x, float y, float width, string text, Action action, string tooltip)
    {
        var control = new MyGuiControlButton(new Vector2(x, y), size: new Vector2(width, .044f),
            toolTip: tooltip, text: new StringBuilder(text), textScale: .66f,
            onButtonClick: button =>
            {
                if (disposed || !active || !button.Enabled || !button.Visible) return;
                try { action(); }
                catch (Exception exception) { LogFailure("action", exception); }
                RefreshStatus();
            }) { Name = "Lanyard" + id };
        StyleButton(control, new Vector2(width, .044f));
        controls.Add(control);
        buttons.Add(id, control);
        if (view.HasValue) views[(int)view.Value].Add(control);
        return control;
    }

    internal static void StyleButton(MyGuiControlButton control, Vector2 size)
    {
        control.CustomStyle = ButtonStyleCache.Value;
        control.Size = size;
    }

    private static MyGuiControlButton.StyleDefinition CreateButtonStyle()
    {
        var native = MyGuiControlButton.GetVisualStyle(MyGuiControlButtonStyleEnum.Small);
        return new MyGuiControlButton.StyleDefinition
        {
            NormalTexture = native.NormalTexture, HighlightTexture = native.HighlightTexture,
            FocusTexture = native.FocusTexture, ActiveTexture = native.ActiveTexture,
            NormalFont = native.NormalFont, HighlightFont = native.HighlightFont,
            Padding = native.Padding, BackgroundColor = native.BackgroundColor,
            TextColor = native.TextColor, TextColorHighlight = native.TextColorHighlight,
            TextColorFocus = native.TextColorFocus, TextColorActive = native.TextColorActive
        };
    }

    private void AddLabel(View view, float x, float y, string text, float scale)
    {
        var control = new MyGuiControlLabel(new Vector2(x, y), text: text, textScale: scale, font: "Blue",
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP);
        controls.Add(control);
        views[(int)view].Add(control);
    }

    private void AddText(View view, string id, float y, float height, float scale, string text = "", bool scroll = false, float x = -.36f, float width = .72f)
    {
        var control = new MyGuiControlMultilineText(new Vector2(x, y), new Vector2(width, height), font: "White", textScale: scale,
            contents: new StringBuilder(text), drawScrollbarV: scroll, drawScrollbarH: false,
            textBoxAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP)
        { OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, Name = "Lanyard" + id, CanHaveFocus = scroll };
        controls.Add(control);
        texts.Add(id, control);
        views[(int)view].Add(control);
    }

    private static void LogFailure(string operation, Exception exception)
    {
        try { MyLog.Default?.WriteLineAndConsole("Lanyard panel " + operation + " failed: " + exception); }
        catch { }
    }
}

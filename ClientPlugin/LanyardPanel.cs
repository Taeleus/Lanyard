using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Lanyard;

/// <summary>The same connection controls are used by the terminal and chat menu.</summary>
internal sealed class LanyardPanel : IDisposable
{
    private static readonly MyGuiControlButton.StyleDefinition buttonStyle = CreateButtonStyle();
    private readonly List<MyGuiControlBase> focusOrder = new List<MyGuiControlBase>();
    private readonly MyGuiControlMultilineText status;
    private string displayedStatus;
    private bool disposed;

    public MyGuiControlButton FocusControl { get; }
    public IReadOnlyList<MyGuiControlBase> FocusOrder => focusOrder;

    public LanyardPanel(MyGuiControls controls)
    {
        AddLabel(controls, -0.36f, -0.225f, "Connection", 0.78f);
        status = AddText(controls, new Vector2(-0.36f, -0.19f), new Vector2(0.72f, 0.065f), "", 0.78f);
        FocusControl = AddButton(controls, -0.215f, 0.29f, "Link targeted port", GetTargetedBlock.GetPort,
            "Aim at your specific inventory port while holding a tool, then link it.");
        AddButton(controls, 0.04f, 0.18f, "Unlink", () => LinkController.Disconnect(), "End the current inventory link.");
        AddButton(controls, 0.255f, 0.21f, "Toggle bounds", BoundsPreview.Toggle,
            "Show or hide the targeted grid's link bounds.");
        AddText(controls, new Vector2(-0.36f, -0.044f), new Vector2(0.72f, 0.03f),
            "Aim at an inventory access port with a tool equipped.", 0.66f);

        var separators = new MyGuiControlSeparatorList();
        separators.AddHorizontal(new Vector2(-0.36f, 0.006f), 0.72f);
        controls.Add(separators);
        AddLabel(controls, -0.36f, 0.025f, "Tools", 0.75f);

        AddLabel(controls, -0.36f, 0.06f, "Welder", 0.7f);
        AddText(controls, new Vector2(-0.36f, 0.088f), new Vector2(0.34f, 0.04f),
            "Withdraws components listed\nin your build planner.", 0.66f);
        AddLabel(controls, 0.02f, 0.06f, "Grinder", 0.7f);
        AddText(controls, new Vector2(0.02f, 0.088f), new Vector2(0.34f, 0.04f),
            "Returns ore, ingots and components\nto your linked inventory.", 0.66f);

        AddLabel(controls, -0.36f, 0.14f, "Drill", 0.7f);
        AddText(controls, new Vector2(-0.36f, 0.168f), new Vector2(0.34f, 0.047f),
            "Deposits ore while the drill\nis equipped.", 0.66f);

        AddText(controls, new Vector2(-0.36f, 0.23f), new Vector2(0.72f, 0.022f),
            "Grid bounds, ownership and the combat cooldown always apply.", 0.64f);
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        if (disposed) return;
        try
        {
            var text = LinkController.StatusText ?? "No active link.";
            if (text == displayedStatus) return;
            displayedStatus = text;
            status.Text = new StringBuilder(text);
        }
        catch (Exception exception) { LogFailure("status refresh", exception); }
    }

    public void Dispose()
    {
        disposed = true;
        foreach (var control in focusOrder) control.Enabled = false;
    }

    private MyGuiControlButton AddButton(MyGuiControls controls, float x, float width, string text, Action action, string tooltip)
    {
        var control = new MyGuiControlButton(new Vector2(x, -0.085f), size: new Vector2(width, 0.045f),
            toolTip: tooltip, text: new StringBuilder(text), textScale: 0.72f,
            onButtonClick: button =>
            {
                if (disposed) return;
                try { action(); }
                catch (Exception exception) { LogFailure("action", exception); }
                RefreshStatus();
            });
        StyleButton(control, new Vector2(width, 0.045f));
        controls.Add(control);
        focusOrder.Add(control);
        return control;
    }

    internal static void StyleButton(MyGuiControlButton control, Vector2 size)
    {
        control.CustomStyle = buttonStyle;
        control.Size = size;
    }

    private static MyGuiControlButton.StyleDefinition CreateButtonStyle()
    {
        // The native Small skin stretches; Default has a fixed texture size.
        // Keep every native interaction state, allowing only the layout to set size.
        var native = MyGuiControlButton.GetVisualStyle(MyGuiControlButtonStyleEnum.Small);
        return new MyGuiControlButton.StyleDefinition
        {
            NormalTexture = native.NormalTexture,
            HighlightTexture = native.HighlightTexture,
            FocusTexture = native.FocusTexture,
            ActiveTexture = native.ActiveTexture,
            NormalFont = native.NormalFont,
            HighlightFont = native.HighlightFont,
            Padding = native.Padding,
            BackgroundColor = native.BackgroundColor,
            TextColor = native.TextColor,
            TextColorHighlight = native.TextColorHighlight,
            TextColorFocus = native.TextColorFocus,
            TextColorActive = native.TextColorActive
        };
    }

    private static void AddLabel(MyGuiControls controls, float x, float y, string text, float scale)
    {
        controls.Add(new MyGuiControlLabel(new Vector2(x, y), text: text, textScale: scale, font: "Blue",
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP));
    }

    private static MyGuiControlMultilineText AddText(MyGuiControls controls, Vector2 position, Vector2 size, string text, float scale)
    {
        var control = new MyGuiControlMultilineText(position, size, font: "White", textScale: scale,
            contents: new StringBuilder(text), drawScrollbarV: false, drawScrollbarH: false,
            textBoxAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP)
        {
            OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP
        };
        controls.Add(control);
        return control;
    }

    private static void LogFailure(string operation, Exception exception)
    {
        try { MyLog.Default?.WriteLineAndConsole("Lanyard panel " + operation + " failed: " + exception); }
        catch { }
    }
}

using System;

namespace Lanyard;

internal enum HudMode { Off, Tools, Paused, Always }
internal enum HudPosition { TopLeft, TopRight, BottomLeft, BottomRight }
internal enum ShortcutAction { Link, Menu, Pause, Bounds }

internal sealed class ShortcutBinding
{
    public int Key { get; set; }
    public bool Control { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }

    public ShortcutBinding Copy() => new ShortcutBinding { Key = Key, Control = Control, Alt = Alt, Shift = Shift };
    public bool Matches(ShortcutBinding other) => other != null && Key != 0 && Key == other.Key &&
        Control == other.Control && Alt == other.Alt && Shift == other.Shift;

    // Limit bindings to ordinary keyboard keys. Navigation, text-entry controls,
    // OS keys, and system combinations remain reserved. Modifiers avoid most
    // accidental overlaps with the game's ordinary letter/digit controls.
    public bool IsValid => Key == 0 ||
        (((Key >= 48 && Key <= 57) || (Key >= 65 && Key <= 90) || (Key >= 96 && Key <= 111) ||
          (Key >= 112 && Key <= 123) || (Key >= 186 && Key <= 192) || (Key >= 219 && Key <= 222)) &&
         ((Key >= 112 && Key <= 123) || Control || Alt) && !(Alt && Key == 115));
}

internal sealed class LanyardSettings
{
    public bool WelderSupply { get; set; } = true;
    public bool GrinderReturns { get; set; } = true;
    public bool DrillReturns { get; set; } = true;
    public bool StartingComponents { get; set; } = true;
    public bool DrillActiveOnly { get; set; }
    public bool ManualPaused { get; set; }
    public HudMode HudMode { get; set; } = HudMode.Off;
    public HudPosition HudPosition { get; set; } = HudPosition.TopLeft;
    public float HudScale { get; set; } = .8f;
    public float HudOpacity { get; set; } = 1f;
    public bool HudCustomPosition { get; set; }
    public float HudX { get; set; } = .02f;
    public float HudY { get; set; } = .02f;
    public float HudWidth => .285f * HudScale / .8f;
    public float HudHeight => .087f * HudScale / .8f;
    public ShortcutBinding LinkShortcut { get; set; } = new ShortcutBinding();
    public ShortcutBinding MenuShortcut { get; set; } = new ShortcutBinding();
    public ShortcutBinding PauseShortcut { get; set; } = new ShortcutBinding();
    public ShortcutBinding BoundsShortcut { get; set; } = new ShortcutBinding();

    public LanyardSettings Copy() => new LanyardSettings
    {
        WelderSupply = WelderSupply, GrinderReturns = GrinderReturns, DrillReturns = DrillReturns,
        StartingComponents = StartingComponents, DrillActiveOnly = DrillActiveOnly, ManualPaused = ManualPaused,
        HudMode = HudMode, HudPosition = HudPosition, HudScale = HudScale, HudOpacity = HudOpacity,
        HudCustomPosition = HudCustomPosition, HudX = HudX, HudY = HudY,
        LinkShortcut = LinkShortcut?.Copy(), MenuShortcut = MenuShortcut?.Copy(),
        PauseShortcut = PauseShortcut?.Copy(), BoundsShortcut = BoundsShortcut?.Copy()
    };

    public ShortcutBinding Binding(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.Link: return LinkShortcut;
            case ShortcutAction.Menu: return MenuShortcut;
            case ShortcutAction.Pause: return PauseShortcut;
            case ShortcutAction.Bounds: return BoundsShortcut;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    public void SetBinding(ShortcutAction action, ShortcutBinding binding)
    {
        switch (action)
        {
            case ShortcutAction.Link: LinkShortcut = binding; break;
            case ShortcutAction.Menu: MenuShortcut = binding; break;
            case ShortcutAction.Pause: PauseShortcut = binding; break;
            case ShortcutAction.Bounds: BoundsShortcut = binding; break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    public void Normalize()
    {
        if (!Enum.IsDefined(typeof(HudMode), HudMode)) HudMode = HudMode.Off;
        if (!Enum.IsDefined(typeof(HudPosition), HudPosition)) HudPosition = HudPosition.TopLeft;
        HudScale = float.IsNaN(HudScale) || float.IsInfinity(HudScale) ? .8f : Math.Max(.6f, Math.Min(1.2f, HudScale));
        HudOpacity = float.IsNaN(HudOpacity) || float.IsInfinity(HudOpacity) ? 1f : Math.Max(.1f, Math.Min(1f, HudOpacity));
        // Screen aspect and physical HUD extent are resolved by the renderer.
        HudX = Coordinate(HudX, 1f);
        HudY = Coordinate(HudY, 1f);
        foreach (ShortcutAction action in Enum.GetValues(typeof(ShortcutAction)))
        {
            var binding = Binding(action);
            if (binding == null || !binding.IsValid) SetBinding(action, new ShortcutBinding());
            else if (binding.Key == 0) SetBinding(action, new ShortcutBinding());
        }
        // Retain one deterministic assignment if a hand-edited file duplicates a chord.
        foreach (ShortcutAction action in Enum.GetValues(typeof(ShortcutAction)))
            foreach (ShortcutAction prior in Enum.GetValues(typeof(ShortcutAction)))
                if (prior < action && Binding(action).Matches(Binding(prior)))
                    SetBinding(action, new ShortcutBinding());
    }

    private static float Coordinate(float value, float maximum) => float.IsNaN(value) || float.IsInfinity(value)
        ? .02f : Math.Max(0f, Math.Min(maximum, value));
}

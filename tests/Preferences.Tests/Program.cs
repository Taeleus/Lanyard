using System;
using System.IO;
using System.Linq;
using System.Xml;
using Lanyard;
using Sandbox;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.FileSystem;
using VRage.Input;

internal static class Program
{
    private static int assertions;
    private static string stateRoot;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("FAIL: " + message);
    }

    private static void Main()
    {
        var testRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-state"));
        stateRoot = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stateRoot);
        MyFileSystem.UserDataPath = stateRoot;
        try
        {
            Store();
            Shortcuts();
            CaptureEscapeOwnership();
            Console.WriteLine("PASS: " + assertions + " preferences and shortcut assertions");
        }
        finally
        {
            var resolved = Path.GetFullPath(stateRoot);
            if (!resolved.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test cleanup escaped the test directory.");
            Directory.Delete(resolved, true);
        }
    }

    private static void Store()
    {
        var path = Path.Combine(stateRoot, "preferences.xml");
        var defaults = LanyardPreferences.Load(path);
        Check(defaults.WelderSupply && defaults.GrinderReturns && defaults.DrillReturns && defaults.StartingComponents, "existing automation enabled by default");
        Check(!defaults.ManualPaused && !defaults.DrillActiveOnly && defaults.HudMode == HudMode.Off && defaults.HudScale == .8f, "fresh HUD defaults off with normal pause and drill settings");
        Check(!defaults.HudCustomPosition && defaults.HudX == .02f && defaults.HudY == .02f, "top-left preset is the default position");
        Check(defaults.HudOpacity == 1f, "fresh HUD opacity preserves native alpha at full opacity");
        Check(Enum.GetValues(typeof(ShortcutAction)).Cast<ShortcutAction>().All(a => defaults.Binding(a).Key == 0), "shortcuts unassigned by default");
        var settings = new LanyardSettings
        {
            WelderSupply = false, GrinderReturns = false, DrillReturns = false, StartingComponents = false,
            DrillActiveOnly = true, ManualPaused = true, HudMode = HudMode.Paused, HudPosition = HudPosition.BottomRight, HudScale = 1.1f,
            HudCustomPosition = true, HudX = .333f, HudY = .444f, HudOpacity = .45f,
            LinkShortcut = new ShortcutBinding { Key = 76, Control = true },
            MenuShortcut = new ShortcutBinding { Key = 77, Alt = true },
            PauseShortcut = new ShortcutBinding { Key = 80, Control = true, Alt = true },
            BoundsShortcut = new ShortcutBinding { Key = 112, Shift = true }
        };
        LanyardPreferences.Save(path, settings);
        var roundtrip = LanyardPreferences.Load(path);
        Check(!roundtrip.WelderSupply && !roundtrip.GrinderReturns && !roundtrip.DrillReturns && !roundtrip.StartingComponents, "tool flags round trip");
        Check(roundtrip.DrillActiveOnly && roundtrip.ManualPaused, "manual pause and drill mode round trip");
        Check(roundtrip.HudMode == HudMode.Paused && roundtrip.HudPosition == HudPosition.BottomRight && roundtrip.HudScale == 1.1f, "HUD round trip");
        Check(roundtrip.HudCustomPosition && roundtrip.HudX == .333f && roundtrip.HudY == .444f,
            "custom normalized position survives a fresh load");
        Check(roundtrip.HudOpacity == .45f, "opacity survives a fresh preference load");
        foreach (ShortcutAction action in Enum.GetValues(typeof(ShortcutAction))) Check(roundtrip.Binding(action).Matches(settings.Binding(action)), action + " chord round trip");
        foreach (var legacyValue in new[] { "true", "false" })
        {
            var legacyPath = Path.Combine(stateRoot, "legacy-" + legacyValue + ".xml");
            var legacy = new XmlDocument();
            legacy.Load(path);
            legacy.DocumentElement.SetAttribute("BootAnimation", legacyValue);
            legacy.Save(legacyPath);
            var bytes = File.ReadAllBytes(legacyPath);
            var compatible = LanyardPreferences.Load(legacyPath);
            Check(!compatible.WelderSupply && !compatible.GrinderReturns && !compatible.DrillReturns && !compatible.StartingComponents &&
                compatible.DrillActiveOnly && compatible.ManualPaused, "obsolete display attribute preserves every automation preference: " + legacyValue);
            Check(compatible.HudMode == HudMode.Paused && compatible.HudPosition == HudPosition.BottomRight && compatible.HudScale == 1.1f &&
                compatible.HudOpacity == .45f && compatible.HudCustomPosition && compatible.HudX == .333f && compatible.HudY == .444f,
                "obsolete display attribute preserves HUD mode, appearance and position: " + legacyValue);
            foreach (ShortcutAction action in Enum.GetValues(typeof(ShortcutAction)))
                Check(compatible.Binding(action).Matches(settings.Binding(action)), "obsolete display attribute preserves " + action + " shortcut: " + legacyValue);
            Check(File.ReadAllBytes(legacyPath).SequenceEqual(bytes), "loading an older preference file leaves its bytes untouched: " + legacyValue);
            LanyardPreferences.Save(legacyPath, compatible);
            Check(!File.ReadAllText(legacyPath).Contains("BootAnimation") && LanyardPreferences.Load(legacyPath).HudMode == HudMode.Paused,
                "ordinary preference save drops the obsolete attribute without resetting visibility: " + legacyValue);
        }
        settings.HudMode = HudMode.Off;
        LanyardPreferences.Save(path, settings);
        Check(LanyardPreferences.Load(path).HudMode == HudMode.Off, "atomic replace updates existing file");
        var prior = File.ReadAllText(path);
        bool blocked = false;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { settings.ManualPaused = false; LanyardPreferences.Save(path, settings); }
            catch (IOException) { blocked = true; }
        }
        Check(blocked && File.ReadAllText(path) == prior, "failed atomic replace preserves old settings");
        Check(Directory.GetFiles(stateRoot, "*.tmp").Length == 0, "failed write cleans only its temporary file");
        File.WriteAllText(path, "<LanyardPreferences Version='1' WelderSupply='nonsense' ManualPaused='True' HudMode='999' HudPosition='-1' HudScale='NaN'><Shortcuts><Link Key='76' Control='True'/><Menu Key='76' Control='True'/><Pause Key='115' Alt='True'/><Bounds Key='91'/></Shortcuts></LanyardPreferences>");
        var invalid = LanyardPreferences.Load(path);
        Check(invalid.WelderSupply && invalid.ManualPaused, "invalid bool defaults without discarding valid independent fields");
        Check(invalid.HudMode == HudMode.Off && invalid.HudPosition == HudPosition.TopLeft && invalid.HudScale == .8f, "invalid enum and NaN normalized");
        File.WriteAllText(path, "<LanyardPreferences Version='1' HudMode='Tools' HudPosition='BottomLeft' HudScale='1.1'/>");
        var migrated = LanyardPreferences.Load(path);
        Check(migrated.HudMode == HudMode.Tools && migrated.HudPosition == HudPosition.BottomLeft && migrated.HudScale == 1.1f &&
            !migrated.HudCustomPosition, "older saved intentional HUD mode and preset survive added optional fields");
        Check(migrated.HudOpacity == 1f, "old preference files keep native alpha when the opacity attribute is absent");
        foreach (var input in new[] { (Text: "NaN", Expected: 1f), (Text: "Infinity", Expected: 1f), (Text: "-5", Expected: .1f),
            (Text: "5", Expected: 1f), (Text: "unreadable", Expected: 1f) })
        {
            File.WriteAllText(path, "<LanyardPreferences Version='1' HudOpacity='" + input.Text + "'/>");
            Check(LanyardPreferences.Load(path).HudOpacity == input.Expected, "invalid opacity normalizes safely: " + input.Text);
        }
        File.WriteAllText(path, "<LanyardPreferences Version='1' HudCustomPosition='true' HudX='NaN' HudY='Infinity'/>");
        var badPosition = LanyardPreferences.Load(path);
        Check(badPosition.HudX == .02f && badPosition.HudY == .02f && badPosition.HudMode == HudMode.Off, "non-finite saved position falls back safely");
        Check(invalid.LinkShortcut.Key == 76 && invalid.MenuShortcut.Key == 0, "duplicate chord retains only first assignment");
        Check(invalid.PauseShortcut.Key == 0 && invalid.BoundsShortcut.Key == 0, "system chords rejected on disk");
        File.WriteAllText(path, "<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///never-read'>]><LanyardPreferences Version='1' ManualPaused='True'>&a;</LanyardPreferences>");
        Check(!LanyardPreferences.Load(path).ManualPaused, "DTD rejected");
        File.WriteAllText(path, new string('x', 32769));
        Check(LanyardPreferences.Load(path).WelderSupply, "oversized file ignored");
        File.WriteAllText(path, "<LanyardPreferences Version='2' ManualPaused='True'/>");
        Check(!LanyardPreferences.Load(path).ManualPaused, "future schema ignored safely");
        File.WriteAllText(path, "<invalid");
        Check(LanyardPreferences.Load(path).GrinderReturns, "malformed XML ignored");
        settings.HudScale = 100;
        settings.HudOpacity = -100;
        LanyardPreferences.Save(path, settings);
        Check(LanyardPreferences.Load(path).HudScale == 1.2f && settings.HudScale == 100, "save normalizes a copy");
        Check(LanyardPreferences.Load(path).HudOpacity == .1f && settings.HudOpacity == -100, "opacity save normalizes a copy without mutating the caller");
        settings.HudScale = -100;
        settings.Normalize();
        Check(settings.HudScale == .6f, "small HUD clamped");
        settings.HudX = settings.HudY = 2f;
        settings.Normalize();
        Check(settings.HudX <= 1f && settings.HudY <= 1f, "saved normalized coordinates remain bounded before display-specific clamping");
        LanyardPreferences.EnsureLoaded();
        bool original = LanyardPreferences.Current.ManualPaused;
        try { LanyardPreferences.Change(value => { value.ManualPaused = !original; throw new InvalidOperationException("test callback"); }); }
        catch (InvalidOperationException) { }
        Check(LanyardPreferences.Current.ManualPaused == original, "throwing preference edit does not partially mutate live state");
        Action failedListener = () => throw new InvalidOperationException("test listener");
        LanyardPreferences.Changed += failedListener;
        LanyardPreferences.Change(value => value.ManualPaused = true);
        LanyardPreferences.Changed -= failedListener;
        Check(LanyardPreferences.Current.ManualPaused && File.Exists(Path.Combine(stateRoot, "Storage", "Lanyard.Preferences.v1.xml")), "preference change persists even if view listener fails");
        LanyardPreferences.Change(value =>
        {
            value.WelderSupply = value.GrinderReturns = value.DrillReturns = value.StartingComponents = false;
            value.DrillActiveOnly = value.ManualPaused = value.HudCustomPosition = true;
            value.HudMode = HudMode.Always; value.HudX = .3f; value.HudY = .4f; value.HudScale = 1.2f;
            value.LinkShortcut = new ShortcutBinding { Key = 76, Control = true };
            value.HudOpacity = .3f;
        });
        LinkController.HasConnection = true;
        var priorPauses = LinkController.Pauses;
        LanyardPreferences.ResetDefaults();
        var reset = LanyardPreferences.Current;
        Check(reset.WelderSupply && reset.GrinderReturns && reset.DrillReturns && reset.StartingComponents &&
            !reset.DrillActiveOnly && !reset.ManualPaused, "reset restores every automation preference");
        Check(reset.HudMode == HudMode.Off && reset.HudPosition == HudPosition.TopLeft && reset.HudScale == .8f &&
            !reset.HudCustomPosition && reset.HudX == .02f && reset.HudY == .02f && reset.HudOpacity == 1f,
            "reset restores all display preferences including opacity");
        Check(Enum.GetValues(typeof(ShortcutAction)).Cast<ShortcutAction>().All(action => reset.Binding(action).Key == 0), "reset removes every shortcut binding");
        Check(LinkController.HasConnection && LinkController.Pauses == priorPauses, "reset does not call connection or combat operations");
        Check(LanyardPreferences.Load(Path.Combine(stateRoot, "Storage", "Lanyard.Preferences.v1.xml")).HudMode == HudMode.Off,
            "reset defaults is persisted for the next process");
    }

    private static void Step(params MyKeys[] keys) { MyInput.Static.Frame(keys); LanyardShortcuts.Update(); }
    private static void Ready()
    {
        MySandboxGame.Static.IsActive = true;
        MySandboxGame.IsGameReady = true;
        MySandboxGame.IsPaused = false;
        MyVRage.Platform.Windows.Window.IsActive = true;
        MySession.Static = new MySession();
        MyScreenManager.Focus = MyGuiScreenGamePlay.Static;
        MyScreenManager.ChatOpen = false;
        MyGuiScreenGamePlay.Static.FocusedControl = null;
        MyGuiScreenGamePlay.ActiveGameplayScreen = null;
        GetTargetedBlock.Links = LanyardScreen.Opens = LinkController.Pauses = BoundsPreview.Linked = BoundsPreview.Targeted = 0;
        LinkController.HasConnection = false;
        LanyardShortcuts.ClearTransient();
        Step();
    }
    private static void Begin(ShortcutAction action)
    {
        MyScreenManager.Focus = new LanyardScreen();
        LanyardShortcuts.BeginCapture(action);
        Step();
    }
    private static void Shortcuts()
    {
        Ready();
        LanyardPreferences.Change(settings =>
        {
            foreach (ShortcutAction action in Enum.GetValues(typeof(ShortcutAction))) settings.SetBinding(action, new ShortcutBinding());
        });
        Begin(ShortcutAction.Link);
        Step(MyKeys.LeftControl, MyKeys.L);
        Check(!LanyardShortcuts.IsCapturing && LanyardPreferences.Current.LinkShortcut.Key == 76 && LanyardPreferences.Current.LinkShortcut.Control, "capture saves actual chord");
        Check(GetTargetedBlock.Links == 0, "captured chord never dispatches action");
        Check(LanyardShortcuts.BindingText(ShortcutAction.Link) == "Ctrl+L", "binding label readable");
        Begin(ShortcutAction.Menu);
        Step(MyKeys.LeftControl, MyKeys.L);
        Check(LanyardShortcuts.IsCapturing && LanyardShortcuts.CaptureText.Contains("Already assigned"), "duplicate capture refused without stealing action");
        Step(); Step(MyKeys.LeftAlt, MyKeys.M);
        Check(!LanyardShortcuts.IsCapturing && LanyardPreferences.Current.MenuShortcut.Alt, "alternate chord accepted after duplicate");
        Begin(ShortcutAction.Pause);
        Step(MyKeys.P);
        Check(LanyardShortcuts.IsCapturing && LanyardPreferences.Current.PauseShortcut.Key == 0, "unmodified letter reserved");
        Step(); Step(MyKeys.LeftAlt, MyKeys.F4);
        Check(LanyardShortcuts.IsCapturing && LanyardPreferences.Current.PauseShortcut.Key == 0, "Alt+F4 reserved");
        Step(); Step(MyKeys.LeftWindows, MyKeys.LeftControl, MyKeys.P);
        Check(LanyardShortcuts.IsCapturing, "OS key combination rejected");
        Step(); Step(MyKeys.LeftControl);
        Check(LanyardShortcuts.IsCapturing, "modifier alone ignored");
        Step(); Step(MyKeys.LeftControl, MyKeys.P);
        Check(!LanyardShortcuts.IsCapturing && LanyardPreferences.Current.PauseShortcut.Key == 80, "pause chord saved");
        Begin(ShortcutAction.Bounds);
        Step(MyKeys.Escape);
        Check(!LanyardShortcuts.IsCapturing && LanyardPreferences.Current.BoundsShortcut.Key == 0, "escape cancels capture");
        Begin(ShortcutAction.Bounds);
        Step(MyKeys.F1);
        Check(LanyardPreferences.Current.BoundsShortcut.Key == 112, "unmodified function key supported");
        Ready();
        Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 1, "link chord dispatches during gameplay");
        Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 1, "held chord does not repeat");
        Step(); Step(MyKeys.LeftControl, MyKeys.LeftShift, MyKeys.L);
        Check(GetTargetedBlock.Links == 1, "modifiers must match exactly");
        Step(); Step(MyKeys.LeftControl, MyKeys.P);
        Check(LinkController.Pauses == 1, "pause chord dispatches through controller");
        Step(); Step(MyKeys.F1);
        Check(BoundsPreview.Targeted == 1, "unlinked bounds shortcut uses targeted preview");
        Step(); LinkController.HasConnection = true; Step(MyKeys.F1);
        Check(BoundsPreview.Linked == 1, "linked bounds shortcut uses connection grid");
        Step(); MyScreenManager.Focus = new MyGuiScreenBase(); Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 1, "chat and other screens suppress gameplay shortcuts");
        MyScreenManager.Focus = MyGuiScreenGamePlay.Static; Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 1, "held key from closed screen does not leak into gameplay");
        Step(); Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 2, "shortcut rearmed after release");
        Step(); MySandboxGame.Static.IsActive = false; Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 2, "inactive game suppresses shortcut");
        Check(LanyardShortcuts.RenderReady && !LanyardShortcuts.GameplayReady, "background gameplay stays render-ready while input remains disabled");
        MySandboxGame.Static.IsActive = true; Step(); MyVRage.Platform.Windows.Window.IsActive = false; Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 2, "inactive window suppresses shortcut");
        Check(LanyardShortcuts.RenderReady && !LanyardShortcuts.GameplayReady, "background window renders without dispatching shortcuts");
        MyVRage.Platform.Windows.Window.IsActive = true; Step(); MySession.Static.LocalCharacter.IsDead = true; Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 2, "dead character suppresses shortcut");
        MySession.Static.LocalCharacter.IsDead = false; Step(); MySession.Static.IsUnloading = true; Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 2, "unloading session suppresses shortcut");
        Ready(); MySandboxGame.IsPaused = true; Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 0, "paused game suppresses shortcut");
        MySandboxGame.IsPaused = false; Step(); MyGuiScreenGamePlay.ActiveGameplayScreen = new MyGuiScreenBase(); Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 0, "active overlay suppresses shortcut");
        Ready(); MyScreenManager.ChatOpen = true; Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 0, "open native chat suppresses shortcut even if gameplay remains focused");
        Ready(); MyGuiScreenGamePlay.Static.FocusedControl = new MyGuiControlTextbox(); Step(MyKeys.LeftControl, MyKeys.L);
        Check(GetTargetedBlock.Links == 0, "gameplay textbox suppresses shortcut");
        Ready(); Step(MyKeys.LeftAlt, MyKeys.M);
        Check(LanyardScreen.Opens == 1, "menu shortcut opens native screen");
        Step(); Step(MyKeys.LeftControl, MyKeys.P);
        Check(LinkController.Pauses == 0, "gameplay actions suppressed in opened menu");
        Begin(ShortcutAction.Link); MyScreenManager.Focus = new MyGuiScreenBase(); Step(MyKeys.LeftControl, MyKeys.B);
        Check(!LanyardShortcuts.IsCapturing && LanyardPreferences.Current.LinkShortcut.Key == 76, "changing focused screen cancels capture");
        Begin(ShortcutAction.Link); MySession.Static = new MySession(); Step(MyKeys.LeftControl, MyKeys.B);
        Check(!LanyardShortcuts.IsCapturing && LanyardPreferences.Current.LinkShortcut.Key == 76, "session handoff cancels capture");
        Begin(ShortcutAction.Link); MyScreenManager.Focus.FocusedControl = new MyGuiControlTextbox(); Step(MyKeys.LeftControl, MyKeys.B);
        Check(!LanyardShortcuts.IsCapturing && LanyardPreferences.Current.LinkShortcut.Key == 76, "textbox focus cancels capture");
        Begin(ShortcutAction.Link); MySandboxGame.Static.IsActive = false; Step(MyKeys.LeftControl, MyKeys.B);
        Check(!LanyardShortcuts.IsCapturing && LanyardPreferences.Current.LinkShortcut.Key == 76, "capture cancelled on application focus loss");
        MySandboxGame.Static.IsActive = true;
        LanyardShortcuts.Clear(ShortcutAction.Link);
        Check(LanyardShortcuts.BindingText(ShortcutAction.Link) == "Unassigned", "clear removes saved shortcut");
    }

    private static void CaptureEscapeOwnership()
    {
        foreach (var terminal in new[] { false, true })
            foreach (var updateFirst in new[] { false, true })
            {
                Ready();
                var owner = terminal ? (MyGuiScreenBase)new MyGuiScreenTerminal() : new LanyardScreen();
                MyScreenManager.Focus = owner;
                LanyardShortcuts.BeginCapture(ShortcutAction.Menu);
                Check(LanyardShortcuts.CaptureAction == ShortcutAction.Menu, "capture exposes its active action");
                // No release update yet: Escape must also cancel the initial held-key gate.
                MyInput.Static.Frame(MyKeys.Escape);
                if (updateFirst) { LanyardShortcuts.Update(); owner.HandleInput(false); }
                else { owner.HandleInput(false); LanyardShortcuts.Update(); }
                Check(!LanyardShortcuts.IsCapturing && LanyardShortcuts.CaptureAction == null && !owner.NativeClosed && owner.NativeInputCalls == 0,
                    (terminal ? "terminal" : "standalone") + " capture Escape is consumed in " + (updateFirst ? "update-first" : "input-first") + " order");
                Check(LanyardShortcuts.CaptureText.Contains("canceled") && LanyardShortcuts.CaptureText.Contains("Open menu"),
                    "capture cancellation keeps action-specific feedback");
                MyInput.Static.Frame(MyKeys.Escape);
                owner.HandleInput(false);
                LanyardShortcuts.Update();
                Check(!owner.NativeClosed && owner.NativeInputCalls == 0, "held capture-cancel Escape remains consumed only through its press");
                MyInput.Static.Frame();
                owner.HandleInput(false);
                LanyardShortcuts.Update();
                MyInput.Static.Frame(MyKeys.Escape);
                owner.HandleInput(false);
                Check(owner.NativeClosed, "a new Escape after release closes its owner normally");
            }

        Ready();
        var original = new LanyardScreen();
        MyScreenManager.Focus = original;
        LanyardShortcuts.BeginCapture(ShortcutAction.Link);
        Step(); Step(MyKeys.Escape);
        var other = new LanyardScreen();
        Check(!LanyardShortcuts.HandleCaptureEscape(other) && LanyardShortcuts.HandleCaptureEscape(original),
            "capture-cancel marker belongs only to its original owner");
        MyScreenManager.Focus = other;
        other.HandleInput(false);
        Check(other.NativeClosed, "focus change never consumes Escape on a different owner");

        Ready();
        original = new LanyardScreen();
        MyScreenManager.Focus = original;
        LanyardShortcuts.BeginCapture(ShortcutAction.Pause);
        Step(); Step(MyKeys.Escape);
        MySession.Static = new MySession();
        Check(!LanyardShortcuts.HandleCaptureEscape(original), "session change releases consumed Escape ownership");
        LanyardShortcuts.BeginCapture(ShortcutAction.Pause);
        Step(); Step(MyKeys.Escape);
        MySession.Static.LocalCharacter = new Character();
        Check(!LanyardShortcuts.HandleCaptureEscape(original), "character change releases consumed Escape ownership");
        LanyardShortcuts.BeginCapture(ShortcutAction.Pause);
        Step(); Step(MyKeys.Escape);
        MySandboxGame.Static.IsActive = false;
        Check(!LanyardShortcuts.HandleCaptureEscape(original), "application focus loss releases consumed Escape ownership");
        MySandboxGame.Static.IsActive = true;
        LanyardShortcuts.ClearTransient();

        Ready();
        original = new LanyardScreen();
        MyScreenManager.Focus = original;
        LanyardShortcuts.BeginCapture(ShortcutAction.Menu);
        Step(); Step(MyKeys.LeftAlt, MyKeys.M);
        Check(!LanyardShortcuts.IsCapturing && LanyardShortcuts.CaptureText.Contains("Open menu shortcut:") &&
            LanyardShortcuts.CaptureText.Contains("Alt+M"), "completed capture keeps its success feedback after ending");
        var completed = LanyardShortcuts.CaptureText;
        LanyardShortcuts.CancelCapture();
        Check(LanyardShortcuts.CaptureText == completed, "inactive refresh cancellation does not discard success feedback");
        LanyardShortcuts.ClearTransient();
        Check(!LanyardShortcuts.CaptureText.Contains("Alt+M"), "lifecycle cleanup clears old capture feedback");
    }
}

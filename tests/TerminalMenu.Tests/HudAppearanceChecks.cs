using System;
using System.Linq;
using Lanyard;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Input;
using VRageMath;

internal static class HudAppearanceChecks
{
    public static void Run(Action<bool, string> check)
    {
        LanyardHudPositionScreen Open(LanyardSettings settings)
        {
            LanyardHud.Clear();
            LanyardPreferences.Current = settings;
            MySession.Static = new MySession();
            MyInput.Static = new Input();
            LanyardShortcuts.InputFocused = true;
            LanyardShortcuts.RenderReady = true;
            LanyardHudPositionScreen.Open();
            return (LanyardHudPositionScreen)MyGuiSandbox.LastScreen;
        }
        MyGuiControlButton Button(LanyardHudPositionScreen editor, string name) =>
            editor.Controls.OfType<MyGuiControlButton>().Single(item => item.Name == "LanyardPosition" + name);
        void ClearDraw() { MyGuiManager.Text.Clear(); MyGuiManager.Backgrounds.Clear(); }
        var editor = Open(new LanyardSettings());
        var trayButtons = editor.Controls.OfType<MyGuiControlButton>().ToArray();
        check(trayButtons.All(button => button.Position.X - button.Size.X / 2 >= -.31f && button.Position.X + button.Size.X / 2 <= .31f &&
            button.Position.Y - button.Size.Y / 2 >= .22f && button.Position.Y + button.Size.Y / 2 <= .48f),
            "appearance controls fit inside the native editor tray");
        for (var i = 0; i < trayButtons.Length; i++)
            for (var j = i + 1; j < trayButtons.Length; j++)
                check(Math.Abs(trayButtons[i].Position.X - trayButtons[j].Position.X) >= (trayButtons[i].Size.X + trayButtons[j].Size.X) / 2 ||
                    Math.Abs(trayButtons[i].Position.Y - trayButtons[j].Position.Y) >= (trayButtons[i].Size.Y + trayButtons[j].Size.Y) / 2,
                    "editor appearance controls do not overlap: " + trayButtons[i].Name + "/" + trayButtons[j].Name);
        Button(editor, "ScaleUp").Click(); Button(editor, "ScaleUp").Click();
        for (var i = 0; i < 5; i++) Button(editor, "OpacityDown").Click();
        ClearDraw(); editor.Draw();
        check(LanyardPreferences.Current.HudScale == .8f && LanyardPreferences.Current.HudOpacity == 1f,
            "editor scale and opacity remain preview-only before Save");
        check(MyGuiManager.Text.Any(item => item.Text == "Size: 100%") && MyGuiManager.Text.Any(item => item.Text == "Opacity: 50%"),
            "editor controls display live size and opacity percentages");
        var preview = MyGuiManager.Backgrounds[0];
        check(preview.Color.A == 105 && preview.Color.R == 13 && preview.Color.G == 24 && preview.Color.B == 34,
            "live preview multiplies panel alpha while preserving its RGB color");
        check(MyGuiManager.Text.Where(item => item.FullClient).All(item => item.Color.A == 128),
            "live opacity reaches every preview text line");
        check(Math.Abs(preview.Size.X - .35625f) < .001f, "preview resizes immediately at edited scale");
        check(MyGuiManager.Backgrounds[1].Color.A == 240 && MyGuiManager.Text.Where(item => !item.FullClient).All(item => item.Color.A == 255),
            "appearance controls remain readable independently of preview opacity");
        LanyardPreferences.Change(settings =>
        {
            settings.GrinderReturns = false;
            settings.MenuShortcut = new ShortcutBinding { Key = 77, Control = true };
        });
        Button(editor, "Save").Click();
        check(Math.Abs(LanyardPreferences.Current.HudScale - 1f) < .001f && Math.Abs(LanyardPreferences.Current.HudOpacity - .5f) < .001f &&
            LanyardPreferences.Current.HudCustomPosition, "Save commits position, scale and opacity together");
        check(!LanyardPreferences.Current.GrinderReturns &&
            LanyardPreferences.Current.MenuShortcut.Key == 77 && LanyardPreferences.Current.HudMode == HudMode.Off,
            "appearance Save preserves the latest unrelated preferences");

        editor = Open(new LanyardSettings { HudScale = .6f, HudCustomPosition = true, HudX = .71f, HudY = .91f });
        for (var i = 0; i < 12; i++) Button(editor, "ScaleUp").Click();
        for (var i = 0; i < 20; i++) Button(editor, "OpacityDown").Click();
        ClearDraw(); editor.Draw();
        var edge = MyGuiManager.Backgrounds[0];
        check(edge.Position.X + edge.Size.X <= 1f && edge.Position.Y + edge.Size.Y <= 1f &&
            edge.Position.X < .71f && edge.Position.Y < .91f, "growing a preview reclamps its entire panel at the screen edge");
        check(!Button(editor, "ScaleUp").Enabled && !Button(editor, "OpacityDown").Enabled &&
            MyGuiManager.Text.Any(item => item.Text == "Size: 120%") && MyGuiManager.Text.Any(item => item.Text == "Opacity: 10%"),
            "live appearance controls stop at supported maximum size and minimum opacity");
        Button(editor, "Save").Click();
        check(LanyardPreferences.Current.HudScale == 1.2f && LanyardPreferences.Current.HudOpacity == .1f &&
            LanyardPreferences.Current.HudX < .71f && LanyardPreferences.Current.HudY < .91f,
            "combined Save clamps location using the edited scale");
        editor = Open(LanyardPreferences.Current.Copy());
        for (var i = 0; i < 20; i++) { Button(editor, "ScaleDown").Click(); Button(editor, "OpacityUp").Click(); }
        check(!Button(editor, "ScaleDown").Enabled && !Button(editor, "OpacityUp").Enabled,
            "appearance controls also stop at minimum size and full opacity");
        Button(editor, "Cancel").Click();

        foreach (var reason in new[] { "Cancel", "Escape", "Focus", "Context", "Cleanup" })
        {
            editor = Open(new LanyardSettings { HudCustomPosition = true, HudX = .2f, HudY = .2f, HudScale = .8f, HudOpacity = .75f });
            Button(editor, "ScaleUp").Click(); Button(editor, "OpacityDown").Click();
            MyGuiManager.MouseCursorPosition = new Vector2(.21f, .21f);
            MyInput.Static.Left = MyInput.Static.NewLeft = true;
            editor.HandleInput(true);
            MyInput.Static.NewLeft = false;
            MyGuiManager.MouseCursorPosition = new Vector2(.45f, .45f);
            editor.HandleInput(false);
            MyInput.Static.Left = false;
            editor.HandleInput(false);
            switch (reason)
            {
                case "Cancel": Button(editor, "Cancel").Click(); break;
                case "Escape": MyInput.Static.Escape = true; editor.HandleInput(false); break;
                case "Focus": LanyardShortcuts.InputFocused = false; editor.Update(false); break;
                case "Context": MySession.Static = new MySession(); editor.Update(true); break;
                default: LanyardHud.Clear(); break;
            }
            check(LanyardPreferences.Current.HudX == .2f && LanyardPreferences.Current.HudY == .2f &&
                LanyardPreferences.Current.HudScale == .8f && LanyardPreferences.Current.HudOpacity == .75f,
                reason + " discards combined position, scale and opacity edits");
            Button(editor, "Save").Click();
            check(LanyardPreferences.Current.HudScale == .8f && LanyardPreferences.Current.HudOpacity == .75f,
                reason + " neutralizes retained Save callbacks");
        }

        editor = Open(new LanyardSettings { HudScale = 1.2f, HudCustomPosition = true, HudX = .2f, HudY = .75f });
        Button(editor, "ScaleDown").Click();
        MyGuiManager.MouseCursorPosition = new Vector2(.255f, .795f);
        MyInput.Static.Left = MyInput.Static.NewLeft = true;
        editor.HandleInput(true);
        MyInput.Static.NewLeft = false;
        MyGuiManager.MouseCursorPosition = new Vector2(.50f, .45f);
        editor.HandleInput(false);
        ClearDraw(); editor.Draw();
        check(MyGuiManager.Backgrounds[0].Position.X == .2f && MyGuiManager.Backgrounds[0].Position.Y == .75f,
            "a control-tray click cannot also drag the HUD underneath it");
        MyInput.Static.Left = false;
        editor.HandleInput(false);
        Button(editor, "Cancel").Click();

        LanyardPreferences.Current = new LanyardSettings { HudMode = HudMode.Always, HudOpacity = .5f };
        MyHud.IsVisible = true; MyHud.MinimalHud = MyHud.IsHudMinimal = MyHud.CutsceneHud = false;
        TransferFeedback.CapacityText = "Backpack full.";
        LinkController.HasConnection = true; LinkController.IsPaused = true;
        ClearDraw(); LanyardHud.Draw();
        check(MyGuiManager.Backgrounds.Single().Color.A == 105 && MyGuiManager.Text.All(item => item.Color.A == 128),
            "normal HUD applies opacity to background, status and persistent capacity");
        check(MyGuiManager.Text[1].Color.R == Color.Orange.R && MyGuiManager.Text[1].Color.G == Color.Orange.G &&
            MyGuiManager.Text[1].Color.B == Color.Orange.B, "HUD opacity preserves the paused status RGB color");
        LanyardHud.Clear();
        ClearDraw(); LanyardHud.Draw();
        check(MyGuiManager.Text[1].Text == LinkController.StateText && MyGuiManager.Text.All(item => item.Color.A == 128),
            "reopened HUD shows current status and capacity at the saved opacity");
        TransferFeedback.CapacityText = "";
        LanyardHud.Clear();
    }
}

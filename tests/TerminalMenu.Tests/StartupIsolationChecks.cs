using System;
using System.Linq;
using Lanyard;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;

internal static class StartupIsolationChecks
{
    // Run before any native menu/editor is constructed in this process.
    public static void Run(Action<bool, string> check)
    {
        check(MyGuiControlButton.GetVisualStyleCount == 0, "cold HUD fixture has not requested a native menu button style");
        LanyardPanel.Bounded("Cold HUD title", 45);
        check(MyGuiControlButton.GetVisualStyleCount == 0, "pure text bounding does not initialize the native button style cache");

        var settings = LanyardPreferences.Current;
        var writes = LanyardPreferences.Writes;
        try
        {
            MySession.Static = new MySession();
            LanyardShortcuts.RenderReady = true;
            MyHud.MinimalHud = MyHud.IsHudMinimal = MyHud.CutsceneHud = false;
            MyHud.IsVisible = true;
            LanyardPreferences.Current = new LanyardSettings { HudMode = HudMode.Always };
            LanyardHud.Clear();
            MyGuiManager.Text.Clear();
            MyGuiManager.Backgrounds.Clear();
            MyGuiManager.SpriteWaitFlags.Clear();
            LanyardHud.Draw();
            check(MyGuiManager.Backgrounds.Count == 1 && MyGuiManager.Text.Count >= 2,
                "first normal HUD draw renders its background and connection text");
            check(MyGuiControlButton.GetVisualStyleCount == 0,
                "first normal HUD draw does not request a native menu button style");
            check(MyGuiManager.SpriteWaitFlags.Count == 1 && !MyGuiManager.SpriteWaitFlags[0],
                "first gameplay HUD background never requests synchronous texture loading");

            var status = MyGuiManager.Text.Select(text => text.Text).ToArray();
            LanyardHud.Clear();
            MyGuiManager.Text.Clear();
            MyGuiManager.Backgrounds.Clear();
            MyGuiManager.SpriteWaitFlags.Clear();
            LanyardHud.Draw();
            check(MyGuiManager.Text.Select(text => text.Text).SequenceEqual(status) && MyGuiManager.Text[1].Text == LinkController.StateText,
                "reopened HUD immediately shows real connection status without a startup animation");
            check(MyGuiControlButton.GetVisualStyleCount == 0 && MyGuiManager.SpriteWaitFlags.Count == 1 && !MyGuiManager.SpriteWaitFlags[0],
                "reopened HUD also avoids menu styling and synchronous texture loading");
            check(LanyardPreferences.Writes == writes && new LanyardSettings().HudMode == HudMode.Off,
                "cold HUD draws never write preferences or change the fresh HUD Off default");

            using (var first = new LanyardPanel(new MyGuiControls()))
                check(MyGuiControlButton.GetVisualStyleCount == 1, "opening a menu initializes its native button style once");
            using (var second = new LanyardPanel(new MyGuiControls()))
                check(MyGuiControlButton.GetVisualStyleCount == 1, "subsequent menus reuse the native button style cache");
        }
        finally
        {
            LanyardHud.Clear();
            LanyardPreferences.Current = settings;
            MyGuiManager.Text.Clear();
            MyGuiManager.Backgrounds.Clear();
            MyGuiManager.SpriteWaitFlags.Clear();
        }
    }
}

using System;
using Lanyard;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;

internal static class HudToolVisibilityChecks
{
    private sealed class EnhancedWelder : MyWelder { }
    private sealed class WelderNamedWeapon : Tool { }

    public static void Run(Action<bool, string> check)
    {
        var previousSession = MySession.Static;
        var previousSettings = LanyardPreferences.Current;
        var previousRender = LanyardShortcuts.RenderReady;
        var previousGameplay = LanyardShortcuts.GameplayReady;
        var previousFocus = LanyardShortcuts.InputFocused;
        var previousMinimal = MyHud.MinimalHud;
        var previousIsMinimal = MyHud.IsHudMinimal;
        var previousVisible = MyHud.IsVisible;
        var previousCutscene = MyHud.CutsceneHud;
        var previousConnection = LinkController.HasConnection;
        var previousPause = LinkController.IsPaused;
        var previousState = LinkController.StateText;
        var previousBlock = GetTargetedBlock.selectedBlock;
        var previousCapacity = TransferFeedback.CapacityText;
        var writes = LanyardPreferences.Writes;
        var styles = MyGuiControlButton.GetVisualStyleCount;

        void Draws(bool visible, string reason)
        {
            MyGuiManager.Text.Clear();
            MyGuiManager.Backgrounds.Clear();
            LanyardHud.Draw();
            check(visible
                ? MyGuiManager.Backgrounds.Count == 1 && MyGuiManager.Text.Count == 2
                : MyGuiManager.Backgrounds.Count == 0 && MyGuiManager.Text.Count == 0, reason);
        }

        try
        {
            MySession.Static = new MySession();
            LanyardPreferences.Current = new LanyardSettings { HudMode = HudMode.Tools };
            LanyardShortcuts.RenderReady = LanyardShortcuts.GameplayReady = LanyardShortcuts.InputFocused = true;
            MyHud.MinimalHud = MyHud.IsHudMinimal = MyHud.CutsceneHud = false;
            MyHud.IsVisible = true;
            LinkController.HasConnection = true;
            LinkController.IsPaused = false;
            LinkController.StateText = "Active";
            GetTargetedBlock.selectedBlock = new SelectedBlock { CustomName = "Visibility fixture" };
            TransferFeedback.CapacityText = "";
            var character = MySession.Static.LocalCharacter;

            character.EquippedTool = new MyWelder();
            character.HandItemDefinition.Id.SubtypeName = "Welder";
            Draws(true, "Tools HUD appears with a live equipped welder");
            character.EquippedTool = null;
            Draws(false, "holstering hides Tools HUD immediately despite the retained welder definition");

            foreach (var staleName in new[] { "Welder2Item", "AngleGrinder4Item", "HandDrill3Item" })
            {
                character.HandItemDefinition.Id.SubtypeName = staleName;
                character.EquippedTool = null;
                Draws(false, "no equipped entity cannot qualify through stale " + staleName);
                character.EquippedTool = new MyAutomaticRifleGun();
                Draws(false, "an equipped pistol or rifle cannot qualify through stale " + staleName);
                character.EquippedTool = new Tool();
                Draws(false, "an unrelated equipped entity cannot qualify through stale " + staleName);
            }
            character.EquippedTool = new WelderNamedWeapon();
            character.HandItemDefinition.Id.SubtypeName = "DecorativeWelderGrinderDrillWeapon";
            Draws(false, "tool-like entity and definition names do not qualify an unrelated weapon");

            var supported = new (string Name, Func<Tool> Create)[]
            {
                ("welder", () => new MyWelder()),
                ("grinder", () => new MyAngleGrinder()),
                ("drill", () => new MyHandDrill()),
                ("block placer", () => new MyBlockPlacerBase())
            };
            foreach (var entry in supported)
            {
                character.EquippedTool = entry.Create();
                character.HandItemDefinition = null;
                Draws(true, "actual equipped " + entry.Name + " does not need a hand-item definition");
                character.HandItemDefinition = new HandItem();
                character.HandItemDefinition.Id.SubtypeName = "Rifle";
                Draws(true, "actual equipped " + entry.Name + " is recognized despite an unrelated definition");
                character.EquippedTool.Closed = true;
                Draws(false, "closed " + entry.Name + " cannot keep Tools HUD visible");
                character.EquippedTool.Closed = false;
                character.EquippedTool.MarkedForClose = true;
                Draws(false, "closing " + entry.Name + " cannot keep Tools HUD visible");
            }

            character.EquippedTool = new EnhancedWelder();
            Draws(true, "a live supported subclass remains eligible without name matching or active use");
            character.EquippedTool = null;
            Draws(false, "a subsequent holster is not cached as an equipped tool");
            character.EquippedTool = new MyHandDrill();
            Draws(true, "equipping a drill immediately restores Tools HUD");
            character.EquippedTool = new MyAutomaticRifleGun();
            Draws(false, "switching directly from a tool to a weapon immediately hides Tools HUD");
            character.EquippedTool = new MyBlockPlacerBase();
            Draws(true, "equipping the block placer immediately restores Tools HUD");

            LanyardPreferences.Current.HudMode = HudMode.Off;
            Draws(false, "Off still hides HUD with a live supported tool equipped");
            LanyardPreferences.Current.HudMode = HudMode.Always;
            character.EquippedTool = null;
            Draws(true, "Always remains visible while holstered");
            character.EquippedTool = new MyAutomaticRifleGun();
            Draws(true, "Always remains visible with an ordinary weapon");
            character.EquippedTool = new MyWelder { Closed = true, MarkedForClose = true };
            Draws(true, "Always does not depend on the supported-tool lifecycle filter");

            LanyardPreferences.Current.HudMode = HudMode.Paused;
            character.EquippedTool = null;
            Draws(false, "Paused remains hidden while the link is active");
            LinkController.IsPaused = true;
            Draws(true, "Paused shows a retained paused connection while holstered");
            LinkController.HasConnection = false;
            Draws(false, "Paused remains hidden without a retained connection");

            LinkController.HasConnection = true;
            LinkController.IsPaused = false;
            LanyardPreferences.Current.HudMode = HudMode.Tools;
            character.EquippedTool = new MyWelder();
            LanyardShortcuts.GameplayReady = LanyardShortcuts.InputFocused = false;
            Draws(true, "a visible background game still renders its equipped-tool HUD without input focus");
            LanyardShortcuts.RenderReady = false;
            Draws(false, "the native menu/chat/session render guard still suppresses Tools HUD");
            LanyardShortcuts.RenderReady = true;
            MyHud.IsVisible = false;
            Draws(false, "native hidden HUD still suppresses supported tools");
            MyHud.IsVisible = true;
            MyHud.MinimalHud = true;
            Draws(false, "minimal HUD still suppresses supported tools");
            MyHud.MinimalHud = false;
            MyHud.IsHudMinimal = true;
            Draws(false, "native minimal HUD mode still suppresses supported tools");
            MyHud.IsHudMinimal = false;
            MyHud.CutsceneHud = true;
            Draws(false, "cutscene HUD still suppresses supported tools");
            check(LanyardPreferences.Writes == writes, "equip, holster and visibility draws never write preferences");
            check(MyGuiControlButton.GetVisualStyleCount == styles, "equip and holster draws never initialize native menu styling");
        }
        finally
        {
            MySession.Static = previousSession;
            LanyardPreferences.Current = previousSettings;
            LanyardShortcuts.RenderReady = previousRender;
            LanyardShortcuts.GameplayReady = previousGameplay;
            LanyardShortcuts.InputFocused = previousFocus;
            MyHud.MinimalHud = previousMinimal;
            MyHud.IsHudMinimal = previousIsMinimal;
            MyHud.IsVisible = previousVisible;
            MyHud.CutsceneHud = previousCutscene;
            LinkController.HasConnection = previousConnection;
            LinkController.IsPaused = previousPause;
            LinkController.StateText = previousState;
            GetTargetedBlock.selectedBlock = previousBlock;
            TransferFeedback.CapacityText = previousCapacity;
            MyGuiManager.Text.Clear();
            MyGuiManager.Backgrounds.Clear();
        }
    }
}

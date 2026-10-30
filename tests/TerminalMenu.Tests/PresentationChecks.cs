using System;
using System.Linq;
using HarmonyLib;
using Lanyard;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Utils;

internal static class PresentationChecks
{
    public static void Run(Action<bool, string> check)
    {
        LanyardPreferences.Current = new LanyardSettings();
        LinkController.HasConnection = true;
        GetTargetedBlock.selectedBlock = new SelectedBlock();
        var controls = new MyGuiControls();
        var panel = new LanyardPanel(controls);
        MyGuiControlButton Button(string id) => controls.OfType<MyGuiControlButton>().Single(item => item.Name == "Lanyard" + id);
        MyGuiControlCheckbox Checkbox(string id) => controls.OfType<MyGuiControlCheckbox>().Single(item => item.Name == "Lanyard" + id);
        MyGuiControlCombobox Combo(string id) => controls.OfType<MyGuiControlCombobox>().Single(item => item.Name == "Lanyard" + id);
        MyGuiControlMultilineText Text(string id) => controls.OfType<MyGuiControlMultilineText>().Single(item => item.Name == "Lanyard" + id);
        void Geometry(string view)
        {
            var inputs = controls.Where(item => item.Visible && item.CanHaveFocus && item is not MyGuiControlMultilineText).ToArray();
            check(inputs.All(item => item.Position.X - item.Size.X / 2 >= -.361f && item.Position.X + item.Size.X / 2 <= .361f &&
                item.Position.Y - item.Size.Y / 2 >= -.26f && item.Position.Y + item.Size.Y / 2 <= .26f), view + " inputs stay within shared panel bounds");
            for (var i = 0; i < inputs.Length; i++)
                for (var j = i + 1; j < inputs.Length; j++)
                    check(Math.Abs(inputs[i].Position.X - inputs[j].Position.X) >= (inputs[i].Size.X + inputs[j].Size.X) / 2 ||
                        Math.Abs(inputs[i].Position.Y - inputs[j].Position.Y) >= (inputs[i].Size.Y + inputs[j].Size.Y) / 2,
                        view + " visible inputs do not overlap: " + inputs[i].Name + "/" + inputs[j].Name);
            foreach (var text in controls.OfType<MyGuiControlMultilineText>().Where(item => item.Visible))
            {
                check(text.Position.X >= -.361f && text.Position.X + text.Size.X <= .361f && text.Position.Y >= -.26f &&
                    text.Position.Y + text.Size.Y <= .26f, view + " text area fits the shared panel: " + text.Name);
                check(inputs.All(input => text.Position.X >= input.Position.X + input.Size.X / 2 ||
                    text.Position.X + text.Size.X <= input.Position.X - input.Size.X / 2 ||
                    text.Position.Y >= input.Position.Y + input.Size.Y / 2 ||
                    text.Position.Y + text.Size.Y <= input.Position.Y - input.Size.Y / 2),
                    view + " text area does not overlap a visible input: " + text.Name);
            }
            check(panel.FocusOrder.All(item => item.Visible), view + " keyboard order contains only visible controls");
        }
        Geometry("Connection");
        check(Text("Planner").Scrollable && Text("Planner").CanHaveFocus && panel.FocusOrder.Contains(Text("Planner")),
            "planner summary supports native scrollbar and keyboard focus");
        GetTargetedBlock.selectedBlock.CustomName = "Cargo\n" + new string('x', 500);
        TransferFeedback.Text = new string('y', 500);
        PlannerAvailability.Summary = "Steel plate: ready\nMotor: short 3";
        LinkController.StateText = "Cooldown — 1:24";
        panel.RefreshStatus();
        check(Text("Container").Text.Length <= 83 && !Text("Container").Text.ToString().Contains('\n') &&
            Text("Transfer").Text.Length <= 180, "inventory name and recent feedback are bounded independently of state");
        check(Text("Planner").Text.ToString().Contains("\nMotor") && Text("Status").Text.ToString() == "Cooldown — 1:24",
            "planner line breaks and primary cooldown remain readable");
        check(Button("Link").Text == "Change connection" && Button("Link").Size.X > Button("Pause").Size.X, "change connection has primary action hierarchy");
        LanyardPreferences.Current.ManualPaused = true;
        panel.RefreshStatus();
        check(Button("Pause").Text == "Resume" && Button("Pause").Tooltip.StartsWith("Resume") && Text("LinkHint").Text.ToString().Contains("Paused by you"),
            "manual pause stays visible during safety cooldown and Resume explains its action");
        LanyardPreferences.Current.ManualPaused = false;
        LinkController.HasConnection = false; GetTargetedBlock.selectedBlock = null;
        panel.RefreshStatus();
        check(!Button("Pause").Enabled && !Button("Unlink").Enabled && !Button("LinkedBounds").Enabled && Button("Link").Text == "Connect",
            "disconnected state presents the enabled primary Connect action");
        LinkController.HasConnection = true;
        panel.RefreshStatus();
        check(!Button("Link").Enabled && Button("Unlink").Enabled && Text("Status").Text.ToString().Contains("Restoring") &&
            Text("LinkHint").Text.ToString().Contains("Disconnect to choose another"), "pending recovery explains its disabled primary action and offers a recovery path");
        check(ReferenceEquals(panel.FocusControl, Button("Unlink")), "pending recovery gives initial focus to an enabled recovery action");
        GetTargetedBlock.selectedBlock = new SelectedBlock();

        Button("ViewTools").Click(); Geometry("Tools");
        check(Checkbox("Welder").IsChecked && Checkbox("Starter").IsChecked, "tool defaults use native checked settings");
        var writes = LanyardPreferences.Writes;
        LanyardPreferences.Current.WelderSupply = false;
        panel.RefreshStatus();
        check(!Checkbox("Welder").IsChecked && LanyardPreferences.Writes == writes, "programmatic checkbox refresh never writes preferences");
        Checkbox("Welder").Click(); Checkbox("Grinder").Click(); Checkbox("Starter").Click();
        check(LanyardPreferences.Current.WelderSupply && !LanyardPreferences.Current.GrinderReturns &&
            !LanyardPreferences.Current.StartingComponents, "native checkbox actions edit only their own tool setting");
        Checkbox("Drill").Click();
        check(!Checkbox("DrillActive").Enabled && Text("ToolsHelp").Text.ToString().Contains("Enable Drill returns"), "disabled drill child includes the recovery instruction");
        Checkbox("DrillActive").Click();
        check(!LanyardPreferences.Current.DrillActiveOnly, "disabled checkbox callbacks do not mutate preferences");
        Checkbox("Drill").Click(); Checkbox("DrillActive").Click();
        check(LanyardPreferences.Current.DrillActiveOnly && Checkbox("DrillActive").Position.Y > Checkbox("Drill").Position.Y &&
            Checkbox("DrillActive").Position.Y < Checkbox("Starter").Position.Y && Checkbox("DrillActive").Position.X > Checkbox("Drill").Position.X,
            "drill-only child is adjacent, indented and independently configurable");
        check(panel.FocusOrder.Contains(Checkbox("Welder")) && panel.FocusOrder.Contains(Checkbox("DrillActive")), "checkbox rows participate in keyboard reading order");

        Button("ViewDisplay").Click(); Geometry("Display");
        var mode = Combo("HudMode"); var position = Combo("HudPosition");
        check(mode.Items.Select(item => item.Text).SequenceEqual(new[] { "Off", "With tools", "Only when paused", "Always during gameplay" }),
            "HUD visibility choices appear in deliberate native order");
        writes = LanyardPreferences.Writes;
        var calls = mode.SelectionCalls;
        for (var i = 0; i < 5; i++) panel.RefreshStatus();
        check(writes == LanyardPreferences.Writes && calls == mode.SelectionCalls && mode.EventCount == 0, "stable combo refresh neither selects again nor sends preference events");
        mode.IsOpen = true;
        LanyardPreferences.Current.HudMode = HudMode.Always;
        panel.RefreshStatus();
        check(mode.IsOpen && mode.GetSelectedKey() == 0 && mode.SelectionCalls == calls, "refresh does not reset an open native dropdown");
        mode.IsOpen = false; panel.RefreshStatus();
        check(mode.GetSelectedKey() == 3 && LanyardPreferences.Writes == writes, "closed combo refresh reconciles without writing");
        mode.SelectItemByKey(1);
        position.SelectItemByKey(1);
        check(LanyardPreferences.Current.HudMode == HudMode.Tools && LanyardPreferences.Current.HudPosition == HudPosition.TopRight &&
            !LanyardPreferences.Current.HudCustomPosition, "explicit native selectors persist selected values");
        check(!controls.Any(item => item.Name == "LanyardHudBoot") && panel.FocusOrder.Contains(mode) && panel.FocusOrder.Contains(position),
            "Display retains keyboard selectors without a removed startup-animation checkbox");
        check(Button("HudDrag").Text == "Customize HUD…" && Text("Appearance").Text.ToString().Contains("Opacity") &&
            Button("ResetDefaults").Text == "Reset all settings" && Text("ResetScope").Text.ToString().Contains("connection and combat"),
            "customization and global reset communicate their full scopes");
        check(!controls.Any(item => item.Name == "LanyardHudSmaller" || item.Name == "LanyardHudLarger"), "Display has one appearance editing workflow");

        Button("ViewShortcuts").Click(); Geometry("Shortcuts");
        check(!Button("ClearLink").Enabled && Button("BindLink").Text == "Assign…" && !Button("CancelBinding").Enabled, "unassigned shortcuts offer Assign and disable pointless actions");
        Button("BindLink").Click();
        check(LanyardShortcuts.IsCapturing && Button("BindLink").Text == "Press keys…" && Button("BindLink").Checked &&
            Button("CancelBinding").Enabled, "active shortcut row visibly owns capture");
        Button("CancelBinding").Click();
        check(!LanyardShortcuts.IsCapturing && Button("BindLink").Text == "Assign…", "explicit Cancel binding returns to the unchanged assignment");
        LanyardShortcuts.CaptureText = "Link shortcut saved.";
        panel.RefreshStatus();
        check(Text("Capture").Text.ToString() == "Link shortcut saved.", "capture completion feedback remains visible after capture ends");
        Button("BindMenu").Click(); panel.SetActive(false);
        check(!LanyardShortcuts.IsCapturing, "hiding the whole panel cancels capture");
        panel.SetActive(true); Button("BindMenu").Click(); panel.Dispose();
        check(!LanyardShortcuts.IsCapturing && controls.Where(item => item.CanHaveFocus).All(item => !item.Enabled), "disposal neutralizes all native preference widgets");
        writes = LanyardPreferences.Writes; Calls.Actions.Clear();
        foreach (var button in controls.OfType<MyGuiControlButton>()) button.Click();
        Checkbox("Welder").Click(); mode.TriggerItemSelected();
        check(Calls.Actions.Count == 0 && LanyardPreferences.Writes == writes, "disposed retained callbacks cannot trigger actions or preference writes");
        CheckScreen(check);
        CheckHud(check);
        HudFeedbackChecks.Run(check);
        MenuFlowChecks.Run(check);
        var range = new RangeWarningState(1000);
        check(!range.Observe(79, 0, true) && !range.Observe(90, 0, false), "range warning waits for near range and gameplay focus");
        check(range.Observe(90, 0, true) && !range.Observe(99, 30000, true), "range warning remains quiet within one excursion");
        check(!range.Observe(70, 5000, true) && !range.Observe(90, 6000, true) && range.Observe(90, 15000, true), "range rearming preserves hysteresis and time throttle");
    }

    private static void CheckScreen(Action<bool, string> check)
    {
        LanyardScreen.Close();
        LanyardScreen.Open();
        var screen = (LanyardScreen)MyGuiSandbox.LastScreen;
        var status = screen.Controls.OfType<MyGuiControlMultilineText>().Single(item => item.Name == "LanyardStatus");
        LinkController.StateText = "Cooldown — 0:59";
        screen.Update(true);
        check(status.Text.ToString() == "Cooldown — 0:59", "standalone menu refreshes countdown every focused update");
        LinkController.StateText = "Cooldown — 0:58";
        screen.Update(true);
        check(status.Text.ToString() == "Cooldown — 0:58", "standalone countdown does not depend on transition events");
        var navigation = AccessTools.Method(typeof(LanyardScreen), "HandleKeyboardActiveIndex");
        var planner = screen.Controls.OfType<MyGuiControlMultilineText>().Single(item => item.Name == "LanyardPlanner");
        var visitedPlanner = false;
        for (var i = 0; i < 16; i++)
        {
            navigation.Invoke(screen, new object[] { MyDirection.Down, true, true });
            check(screen.FocusedControl.Visible && screen.FocusedControl.Enabled, "standalone Tab skips disabled or hidden controls");
            visitedPlanner |= ReferenceEquals(screen.FocusedControl, planner);
        }
        check(visitedPlanner, "standalone Tab reaches the planner scroll area");
        screen.Controls.OfType<MyGuiControlButton>().Single(item => item.Name == "LanyardViewShortcuts").Click();
        screen.Controls.OfType<MyGuiControlButton>().Single(item => item.Name == "LanyardBindLink").Click();
        screen.Update(false);
        check(!LanyardShortcuts.IsCapturing, "standalone focus loss cancels capture");
        screen.Update(true);
        LanyardScreen.Close();
        check(screen.Controls.OfType<MyGuiControlButton>().Where(item => item.Name != null).All(item => !item.Enabled),
            "standalone cleanup neutralizes view controls");
    }

    private static void CheckHud(Action<bool, string> check)
    {
        void Reset()
        {
            MyGuiManager.Text.Clear();
            MyGuiManager.Backgrounds.Clear();
        }
        void Hidden(string reason)
        {
            Reset();
            LanyardHud.Draw();
            check(MyGuiManager.Text.Count == 0 && MyGuiManager.Backgrounds.Count == 0, reason);
        }
        LanyardPreferences.Current = new LanyardSettings();
        LanyardPreferences.Current.HudMode = HudMode.Tools;
        LanyardShortcuts.GameplayReady = true;
        LanyardShortcuts.RenderReady = true;
        MyHud.MinimalHud = MyHud.IsHudMinimal = MyHud.CutsceneHud = false;
        MyHud.IsVisible = true;
        LinkController.HasConnection = true;
        LinkController.IsPaused = false;
        LinkController.StateText = "Active";
        GetTargetedBlock.selectedBlock = new SelectedBlock { CustomName = new string('z', 400) + "\nRemote name" };
        MySession.Static.LocalCharacter.EquippedTool = new Sandbox.Game.Weapons.MyWelder();
        Reset();
        LanyardHud.Draw();
        check(MyGuiManager.Text.Count == 2 && MyGuiManager.Backgrounds.Count == 1 &&
            MyGuiManager.Text[0].Text.Length <= 45 && !MyGuiManager.Text[0].Text.Contains('\n'), "HUD shows bounded own-container name and status");
        check(MyGuiManager.Text[1].Color.Equals(VRageMath.Color.LimeGreen), "active linked HUD uses green status");
        LinkController.HasConnection = false;
        GetTargetedBlock.selectedBlock = null;
        Reset();
        LanyardHud.Draw();
        check(MyGuiManager.Text[1].Color.Equals(VRageMath.Color.White), "unlinked HUD uses neutral status");
        LanyardPreferences.Current.HudMode = HudMode.Paused;
        Hidden("paused-only HUD hides when there is no retained link");
        LinkController.HasConnection = true;
        LinkController.IsPaused = true;
        Reset();
        LanyardHud.Draw();
        check(MyGuiManager.Text.Count == 2 && MyGuiManager.Text[1].Color.Equals(VRageMath.Color.Orange), "retained pause is visible in amber");
        LanyardPreferences.Current.HudMode = HudMode.Off;
        Hidden("off mode draws no overlay");
        LanyardPreferences.Current.HudMode = HudMode.Tools;
        MySession.Static.LocalCharacter.EquippedTool = new Sandbox.Game.Weapons.MyAutomaticRifleGun();
        Hidden("tools mode excludes unrelated equipment");
        MySession.Static.LocalCharacter.EquippedTool = new Sandbox.Game.Weapons.MyBlockPlacerBase();
        Reset();
        LanyardHud.Draw();
        check(MyGuiManager.Text.Count == 2, "tools mode includes block placement");
        MySession.Static.LocalCharacter.EquippedTool = null;
        LanyardPreferences.Current.HudMode = HudMode.Always;
        LanyardShortcuts.GameplayReady = false;
        Reset();
        LanyardHud.Draw();
        check(MyGuiManager.Text.Count == 2, "HUD rendering does not require shortcut input focus");
        LanyardShortcuts.RenderReady = false;
        Hidden("HUD respects gameplay, chat and session rendering guard");
        LanyardShortcuts.RenderReady = true;
        LanyardShortcuts.GameplayReady = true;
        MyHud.MinimalHud = true;
        Hidden("HUD follows native minimal HUD");
        MyHud.MinimalHud = false;
        MyHud.IsHudMinimal = true;
        Hidden("HUD follows hidden HUD state");
        MyHud.IsHudMinimal = false;
        MyHud.CutsceneHud = true;
        Hidden("HUD is suppressed in cutscenes");
        MyHud.CutsceneHud = false;
        MyHud.IsVisible = false;
        Hidden("HUD follows native visibility");
        MyHud.IsVisible = true;
        LanyardPreferences.Current.HudScale = 1.2f;
        foreach (HudPosition position in Enum.GetValues(typeof(HudPosition)))
        {
            LanyardPreferences.Current.HudPosition = position;
            Reset();
            LanyardHud.Draw();
            var background = MyGuiManager.Backgrounds.Single();
            check(background.Position.X >= 0 && background.Position.Y >= 0 && background.Position.X + background.Size.X < 1 &&
                background.Position.Y + background.Size.Y < 1 && MyGuiManager.Text.All(item => item.MaximumWidth > 0),
                "largest HUD remains on screen at " + position);
        }
    }
}

using System;
using System.Diagnostics;
using HarmonyLib;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;

internal static class BoundsChecks
{
    public static void Run(Action<bool, string> check)
    {
        BoundsPreview.Clear();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Session = new GameSession();
        MySession.Static = new MySession();
        LinkRecovery.HasCurrentContext = true;
        LinkController.Access = true;
        LanyardShortcuts.GameplayReady = true;
        var target = new MyCubeGrid();
        var linked = new MyCubeGrid();
        MySession.Static.LocalCharacter.EquippedTool.Components.Caster.HitBlock.CubeGrid = target;
        GetTargetedBlock.selectedBlock = new SelectedBlock { CubeGrid = linked };
        BoundsPreview.Toggle();
        MySimpleObjectDraw.DrawnGrids.Clear();
        MySimpleObjectDraw.Widths.Clear();
        BoundsPreview.Draw();
        check(MySimpleObjectDraw.DrawnGrids.Count == 2 && MySimpleObjectDraw.DrawnGrids.TrueForAll(id => id == target.WorldMatrix.Id),
            "targeted preview draws the aimed grid");
        check(Math.Abs(MySimpleObjectDraw.Widths[0] - 0.0006f) < 0.000001f &&
            Math.Abs(MySimpleObjectDraw.Widths[1] - 0.0005f) < 0.000001f,
            "bounds: inner green line is twenty percent stronger while orange guide stays unchanged");
        BoundsPreview.ToggleLinked();
        MySimpleObjectDraw.DrawnGrids.Clear();
        BoundsPreview.Draw();
        check(MySimpleObjectDraw.DrawnGrids.Count == 2 && MySimpleObjectDraw.DrawnGrids.TrueForAll(id => id == linked.WorldMatrix.Id),
            "linked preview switches to the selected grid despite aiming elsewhere");
        check(BoundsAccess.Active == 1, "switching preview releases the old bounds observer");
        var moved = new MyCubeGrid();
        GetTargetedBlock.selectedBlock.CubeGrid = moved;
        linked.Closed = true;
        MySimpleObjectDraw.DrawnGrids.Clear();
        BoundsPreview.Draw();
        check(MySimpleObjectDraw.DrawnGrids.Count == 2 && MySimpleObjectDraw.DrawnGrids.TrueForAll(id => id == moved.WorldMatrix.Id) && BoundsAccess.Active == 1,
            "linked preview follows a selected block after split/merge even when its former grid closes");
        linked.Closed = false;
        GetTargetedBlock.selectedBlock = new SelectedBlock { CubeGrid = linked };
        MySimpleObjectDraw.DrawnGrids.Clear();
        BoundsPreview.Draw();
        check(MySimpleObjectDraw.DrawnGrids.Count == 2 && MySimpleObjectDraw.DrawnGrids.TrueForAll(id => id == linked.WorldMatrix.Id) && BoundsAccess.Active == 1,
            "linked preview follows a successful selected-container replacement");
        BoundsPreview.ToggleLinked();
        check(BoundsAccess.Active == 0, "toggling linked preview again releases its observer");
        GetTargetedBlock.selectedBlock = null;
        BoundsPreview.ToggleLinked();
        check(BoundsAccess.Active == 0, "linked preview requires a live selected block");
        GetTargetedBlock.selectedBlock = new SelectedBlock { CubeGrid = linked };
        LinkRecovery.HasCurrentContext = false;
        BoundsPreview.ToggleLinked();
        check(BoundsAccess.Active == 0, "linked preview rejects a stale world context");
        LinkRecovery.HasCurrentContext = true;
        LinkController.Access = false;
        BoundsPreview.ToggleLinked();
        check(BoundsAccess.Active == 0, "linked preview checks current container access");
        LinkController.Access = true;
        linked.AllowBounds = false;
        BoundsPreview.ToggleLinked();
        check(BoundsAccess.Active == 0, "linked preview retains the existing native bounds access policy");
        linked.AllowBounds = true;
        linked.Distance = 0;
        check(BoundsPreview.DistanceText.Contains("Inside"), "distance display identifies the usable area");
        linked.Distance = 57.3;
        check(BoundsPreview.DistanceText.Contains("58 m back") && BoundsPreview.DistanceText.Contains("43 m until"),
            "distance display uses distance to the actual bounds and remaining break distance");
        linked.Distance = 101;
        check(BoundsPreview.DistanceText == "", "ended range does not display an active distance");
        linked.Distance = double.NaN;
        check(BoundsPreview.DistanceText == "", "invalid geometry produces no distance claim");
        linked.Distance = 90;
        MySession.Static.LocalCharacter.IsDead = true;
        check(BoundsPreview.DistanceText == "", "dead character produces no distance claim");
        MySession.Static.LocalCharacter.IsDead = false;
        LinkRecovery.HasCurrentContext = false;
        check(BoundsPreview.DistanceText == "", "stale context produces no distance claim");
        LinkRecovery.HasCurrentContext = true;

        var warning = (RangeWarningState)AccessTools.Field(typeof(BoundsPreview), "rangeWarning").GetValue(null);
        AccessTools.Field(typeof(RangeWarningState), "hasWarned").SetValue(warning, false);
        MyAPIGateway.Utilities.Notifications.Clear();
        linked.Distance = 79;
        BoundsPreview.Update();
        check(MyAPIGateway.Utilities.Notifications.Count == 0, "actual bounds update waits before warning");
        linked.Distance = 80;
        BoundsPreview.Update();
        check(MyAPIGateway.Utilities.Notifications.Count == 1 && MyAPIGateway.Utilities.Notifications[0].Contains("20 m"),
            "actual bounds update warns before the 100 m break using actual remaining distance");
        linked.Distance = 99;
        BoundsPreview.Update();
        BoundsPreview.Update();
        check(MyAPIGateway.Utilities.Notifications.Count == 1, "actual bounds update suppresses repeated edge notices");
        linked.Distance = 70;
        BoundsPreview.Update();
        linked.Distance = 90;
        BoundsPreview.Update();
        check(MyAPIGateway.Utilities.Notifications.Count == 1, "actual bounds update throttles quick repeat excursions");
        AccessTools.Field(typeof(RangeWarningState), "lastWarning").SetValue(warning, Stopwatch.GetTimestamp() - 16L * Stopwatch.Frequency);
        BoundsPreview.Update();
        check(MyAPIGateway.Utilities.Notifications.Count == 2, "actual bounds update allows a later rearmed warning");
        LinkRecovery.HasCurrentContext = false;
        BoundsPreview.Update();
        check(AccessTools.Field(typeof(BoundsPreview), "rangeGrid").GetValue(null) == null &&
            AccessTools.Field(typeof(BoundsPreview), "rangeCharacter").GetValue(null) == null,
            "context change releases range tracking references");
        LinkRecovery.HasCurrentContext = true;
        MySession.Static.LocalCharacter = new Character();
        AccessTools.Field(typeof(RangeWarningState), "lastWarning").SetValue(warning, Stopwatch.GetTimestamp() - 16L * Stopwatch.Frequency);
        BoundsPreview.Update();
        check(MyAPIGateway.Utilities.Notifications.Count == 3 &&
            ReferenceEquals(AccessTools.Field(typeof(BoundsPreview), "rangeCharacter").GetValue(null), MySession.Static.LocalCharacter),
            "character change starts fresh range tracking without keeping the old character");
        BoundsPreview.ToggleLinked();
        check(BoundsAccess.Active == 1, "restored context can show linked bounds");
        MySession.Static = new MySession();
        MySimpleObjectDraw.DrawnGrids.Clear();
        BoundsPreview.Draw();
        check(BoundsAccess.Active == 0 && MySimpleObjectDraw.DrawnGrids.Count == 0, "preview cannot draw an old world after session replacement");
        MySession.Static.LocalCharacter.EquippedTool.Components.Caster.HitBlock.CubeGrid = target;
        BoundsPreview.Toggle();
        target.AllowBounds = false;
        BoundsPreview.Draw();
        check(BoundsAccess.Active == 0, "preview drops its observer when native bounds access is lost");
        target.AllowBounds = true;
        BoundsPreview.Toggle();
        GetTargetedBlock.selectedBlock = new SelectedBlock { CubeGrid = moved };
        MySimpleObjectDraw.DrawnGrids.Clear();
        BoundsPreview.Draw();
        check(MySimpleObjectDraw.DrawnGrids.Count == 2 && MySimpleObjectDraw.DrawnGrids.TrueForAll(id => id == target.WorldMatrix.Id),
            "targeted preview keeps the aimed target when the selected container changes");
        MySession.Static.LocalCharacter.IsDead = true;
        BoundsPreview.Draw();
        check(BoundsAccess.Active == 0, "preview releases references on character death");
        MySession.Static.LocalCharacter.IsDead = false;
        BoundsPreview.Toggle();
        BoundsPreview.Clear();
        BoundsPreview.Clear();
        check(BoundsAccess.Active == 0 && AccessTools.Field(typeof(BoundsPreview), "previewSession").GetValue(null) == null,
            "repeated cleanup releases native preview state");
        RetainedRangeWarningChecks(check);
    }

    private static RetainedNotification VisibleWarning(out MyCubeGrid linked, Action<Utilities> configure = null)
    {
        BoundsPreview.Clear();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Session = new GameSession();
        MySession.Static = new MySession();
        LinkRecovery.HasCurrentContext = true;
        LinkController.Access = true;
        LanyardShortcuts.GameplayReady = true;
        linked = new MyCubeGrid { Distance = 85 };
        GetTargetedBlock.selectedBlock = new SelectedBlock { CubeGrid = linked };
        var warning = (RangeWarningState)AccessTools.Field(typeof(BoundsPreview), "rangeWarning").GetValue(null);
        AccessTools.Field(typeof(RangeWarningState), "hasWarned").SetValue(warning, false);
        configure?.Invoke(MyAPIGateway.Utilities);
        BoundsPreview.Update();
        var retained = MyAPIGateway.Utilities.Retained;
        return retained.Count == 0 ? null : retained[retained.Count - 1];
    }

    private static void RetainedRangeWarningChecks(Action<bool, string> check)
    {
        var notification = VisibleWarning(out var linked);
        check(notification != null && notification.Visible && notification.Shows == 1,
            "range: warning uses a retained native notification handle");
        linked.Distance = 0;
        BoundsPreview.Update();
        check(!notification.Visible && notification.Hides == 1,
            "range: returning to safe bounds hides the native warning immediately");

        foreach (var change in new[] { "break", "unlink", "replace link", "target", "access", "context", "character", "session", "closed character", "closed grid", "hidden gameplay", "cleanup" })
        {
            notification = VisibleWarning(out linked);
            switch (change)
            {
                case "break": linked.Distance = LinkArea.BreakDistance + 1; break;
                case "unlink": BoundsPreview.LinkDisconnected(); break;
                case "replace link": BoundsPreview.LinkConnected(); break;
                case "target": GetTargetedBlock.selectedBlock = new SelectedBlock { CubeGrid = linked }; break;
                case "access": LinkController.Access = false; break;
                case "context": LinkRecovery.HasCurrentContext = false; break;
                case "character": MySession.Static.LocalCharacter = new Character(); break;
                case "session": MySession.Static = new MySession(); break;
                case "closed character": MySession.Static.LocalCharacter.Closed = true; break;
                case "closed grid": linked.Closed = true; break;
                case "hidden gameplay": LanyardShortcuts.GameplayReady = false; break;
                case "cleanup": BoundsPreview.Clear(); break;
            }
            BoundsPreview.Update();
            check(!notification.Visible && notification.Hides >= 1,
                "range: " + change + " cancels the previous native warning");
        }

        notification = VisibleWarning(out linked, utilities => utilities.OnCreate = _ =>
        {
            GetTargetedBlock.selectedBlock = null;
            BoundsPreview.LinkDisconnected();
        });
        check(notification.Shows == 0 && !notification.Visible && notification.Hides >= 1,
            "range: a link break during native creation cannot publish a stale warning");
        notification = VisibleWarning(out linked, utilities => utilities.OnShow = _ =>
        {
            GetTargetedBlock.selectedBlock = null;
            BoundsPreview.LinkDisconnected();
        });
        check(notification.Shows == 1 && !notification.Visible && notification.Hides >= 1 &&
            AccessTools.Field(typeof(BoundsPreview), "rangeNotification").GetValue(null) == null,
            "range: a link break during native Show is rechecked and actively hidden");
        notification = VisibleWarning(out linked, utilities => utilities.ThrowCreate = true);
        check(notification == null && GetTargetedBlock.selectedBlock?.CubeGrid == linked,
            "range: optional notification creation failure leaves the link intact");
        notification = VisibleWarning(out linked, utilities => utilities.ThrowShow = true);
        check(notification != null && !notification.Visible && notification.Hides >= 1 && GetTargetedBlock.selectedBlock?.CubeGrid == linked,
            "range: optional native Show failure is hidden without changing the link");
        notification = VisibleWarning(out linked, utilities => utilities.ThrowHide = true);
        BoundsPreview.LinkDisconnected();
        check(!notification.Visible && AccessTools.Field(typeof(BoundsPreview), "rangeNotification").GetValue(null) == null,
            "range: native Hide failure cannot interrupt link-disconnect cleanup");
        BoundsPreview.Clear();
    }
}

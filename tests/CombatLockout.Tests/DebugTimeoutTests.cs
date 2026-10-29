using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Lanyard;
using VRage.Game.ModAPI;

internal static class DebugTimeoutTests
{
    private static bool Contains(string text, string expected)
    {
        return text != null && text.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static MyCubeGrid World()
    {
        CombatLockout.Clear();
        ((CombatCooldown)typeof(CombatLockout).GetField("cooldown", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Multiplayer = new Multiplayer();
        MyAPIGateway.GridGroups = new GridGroups();
        MyAPIGateway.Session = new BoundsSession();
        MySession.Static = new MySession();
        CoreSystems.Session.I = new CoreSystems.Session();
        VanillaPlusFramework.VanillaPlusFramework.I = null;
        VanillaPlusFramework.BeamLogic.Beams.I = null;
        MyEntities.Entities.Clear();
        var grid = new MyCubeGrid { DisplayName = "Debug grid" };
        MyEntities.Add(grid);
        return grid;
    }

    private static void ObserveQuiet()
    {
        ((CombatCooldown)typeof(CombatLockout).GetField("cooldown", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
        var grids = (IDictionary)typeof(CombatLockout).GetField("grids", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (var activity in grids.Values)
        {
            activity.GetType().GetProperty("Until").SetValue(activity, 0L);
            activity.GetType().GetProperty("ObservationUntil").SetValue(activity, 0L);
        }
        MyAPIGateway.Utilities.Files.Clear();
    }

    public static void Run(Action<bool, string> check)
    {
        check(CombatLockout.CooldownSeconds == 120, "release feedback: configured duration is two minutes");
        var grid = World();
        CombatLockout.Start();
        check(!CombatLockout.CanConnect(grid), "release feedback: unknown-grid observation still prevents connection");
        check(Contains(CombatLockout.BlockReason, "New grid observation"),
            "release feedback: a fresh grid has an observation cause rather than a damage cause");
        check(Contains(CombatLockout.BlockReason, "Debug grid"), "release feedback: observation reason identifies the selected grid");
        CombatLockout.ReportBlocked();
        check(MyAPIGateway.Utilities.ChatMessages.Count == 0,
            "release feedback: blocked connection does not emit a diagnostic chat message");
        check(MyAPIGateway.Utilities.Notifications.Count == 1 &&
            MyAPIGateway.Utilities.Notifications[0] == "Lanyard unavailable.",
            "release feedback: blocked connection uses the generic HUD notification");
        check(!Contains(MyAPIGateway.Utilities.Notifications[0], "Debug grid") &&
            !Contains(MyAPIGateway.Utilities.Notifications[0], "remaining"),
            "release feedback: HUD does not reveal diagnostic grid identity or countdown");
        check(VRage.Utils.MyLog.Default.Lines.Exists(line => Contains(line, CombatLockout.BlockReason)),
            "release feedback: detailed trigger provenance remains in the game log");

        MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName] = DateTime.UtcNow.AddMinutes(10).Ticks.ToString();
        var restored = new CombatCooldownStore().Load();
        check(restored <= Stopwatch.GetTimestamp() + CombatLockout.CooldownSeconds * Stopwatch.Frequency &&
            restored > Stopwatch.GetTimestamp() + (CombatLockout.CooldownSeconds - 1) * Stopwatch.Frequency,
            "release feedback: an old saved cooldown is clamped to the configured two-minute maximum");

        ObserveQuiet(); CombatLockout.CanConnect(grid);
        grid.Damage(1);
        check(Contains(CombatLockout.BlockReason, "damage") && Contains(CombatLockout.BlockReason, "Debug grid"),
            "release feedback: structural damage reason includes trigger and grid");
        var until = ((CombatCooldown)typeof(CombatLockout).GetField("cooldown", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Deadline;
        check(until <= Stopwatch.GetTimestamp() + CombatLockout.CooldownSeconds * Stopwatch.Frequency &&
            until > Stopwatch.GetTimestamp() + (CombatLockout.CooldownSeconds - 1) * Stopwatch.Frequency,
            "release feedback: actual structural timeout is two minutes");
        var reason = CombatLockout.BlockReason;
        CombatLockout.Update();
        check(CombatLockout.BlockReason == reason,
            "release feedback: group refresh does not replace a damage cause with a generic reason");

        grid = World();
        var connector = new MyShipConnector(); grid.Add(connector);
        var slim = new MySlimBlock { CubeGrid = grid, FatBlock = connector };
        CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        grid.Damage(slim, 1); grid.Damage(slim, 1);
        check(Contains(CombatLockout.BlockReason, "connector"),
            "release feedback: repeated connector damage has a distinguishable reason");

        grid = World();
        var core = new FakeWeaponCore(); core.RespondOnRequest();
        var coreGun = new CoreGun(); grid.Add(coreGun); core.Add(coreGun, 3);
        CombatLockout.Start(); CombatLockout.CanConnect(grid);
        check(Contains(CombatLockout.BlockReason, "WeaponCore weapon observation started"),
            "release feedback: newly observed WeaponCore weapons have a distinct initialization cause");
        ObserveQuiet(); CombatLockout.CanConnect(grid);
        core.Event(coreGun, 3, 1, true);
        check(Contains(CombatLockout.BlockReason, "firing event") && Contains(CombatLockout.BlockReason, "part 3") &&
            Contains(CombatLockout.BlockReason, "Debug grid"),
            "release feedback: WeaponCore event identifies the firing part and selected grid");
        ObserveQuiet(); CombatLockout.CanConnect(grid);
        core.State(coreGun, 3).Shots++;
        CombatLockout.Update();
        check(Contains(CombatLockout.BlockReason, "shot counter changed"),
            "release feedback: WeaponCore between-update shot counter has its own reason");

        grid = World(); CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        var other = new MyCubeGrid { DisplayName = "Unrelated grid" }; MyEntities.Add(other);
        other.Damage(1);
        check(!CombatLockout.IsBlocked && MyAPIGateway.Utilities.ChatMessages.Count == 0,
            "release feedback: unrelated combat does not emit an early-warning chat message");

        grid = World(); CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        grid.RemoveSlim();
        check(Contains(CombatLockout.BlockReason, "Block removal") && Contains(CombatLockout.BlockReason, "Debug grid"),
            "release feedback: outright removal identifies its own source and grid");

        grid = World();
        MySession.Static.Mods.Add(new VRage.Game.MyObjectBuilder_Checkpoint.ModItem { PublishedFileId = 2815514917 });
        CombatLockout.Start();
        check(Contains(CombatLockout.BlockReason, "waiting for WeaponCore API"),
            "release feedback: missing required API has an exact readiness cause");
        check(Contains(CombatLockout.BlockMessage, "condition to clear") && !Contains(CombatLockout.BlockMessage, "remaining"),
            "release feedback: an unavailable API is not mislabeled as an expiring cooldown");

        grid = World(); CombatLockout.Start(); ObserveQuiet(); CombatLockout.CanConnect(grid);
        MyAPIGateway.GridGroups.Throw = true;
        CombatLockout.Update();
        check(Contains(CombatLockout.BlockReason, "Simulated group lookup failure"),
            "release feedback: monitoring failures expose the underlying exception message");

        grid = World();
        MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName] = DateTime.UtcNow.AddMinutes(10).Ticks.ToString();
        CombatLockout.Start();
        check(Contains(CombatLockout.BlockReason, "Restored cooldown"),
            "release feedback: persisted state is identified when its original trigger is unavailable");
        CombatLockout.Clear();
    }
}

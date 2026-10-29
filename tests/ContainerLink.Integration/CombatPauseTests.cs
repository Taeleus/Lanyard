using System;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;

internal static partial class Program
{
    private static void CombatPauseTests()
    {
        PausedToolPaths();
        HardFailuresDuringPause();
        BoundsAndEnemyPause();
        PauseClearsWorkState();
        PauseStatusChanges();
    }

    private static void PausedToolPaths()
    {
        foreach (var tool in new[] { "grinder", "drill", "welder", "placement" })
        {
            var world = tool == "placement" ? PlacementWorld() : new World();
            if (tool == "drill") world.Equip("HandDrillItem", new MyHandDrill());
            if (tool == "welder")
            {
                world.Equip("WelderItem", new MyWelder());
                world.Character.BuildPlanner.Add(new object());
            }
            if (tool != "placement") world.AddDepositItems();
            var port = GetTargetedBlock.selectedObject;
            CombatLockout.IsBlocked = true;
            Tick(60);
            if (tool == "placement") RequestPlacement();
            Check(GetTargetedBlock.selectedBlock == world.Selected && ReferenceEquals(GetTargetedBlock.selectedObject, port),
                "combat pause: " + tool + " retains the exact selected container and port");
            Check(MyInventory.Transfers.Count == 0 && WithdrawRecorder.Calls.Count == 0 && PlacementResolver.Requests.Count == 0,
                "combat pause: " + tool + " issues no inventory or planner requests");
            Check(LinkController.StatusText.Contains(world.Selected.CustomName) &&
                LinkController.StatusText.IndexOf("paused", StringComparison.OrdinalIgnoreCase) >= 0,
                "combat pause: menu status identifies the retained connection as paused");
            CombatLockout.IsBlocked = false;
            Tick(51);
            if (tool == "placement")
            {
                Check(MyInventory.Transfers.Count == 0, "combat resume: old placement intent is not replayed automatically");
                RequestPlacement();
            }
            var expectedTransfers = tool == "grinder" ? 3 : tool == "welder" ? 0 : 1;
            Check(MyInventory.Transfers.Count == expectedTransfers && WithdrawRecorder.Calls.Count == (tool == "welder" ? 1 : 0),
                "combat resume: " + tool + " uses the same link once the cooldown is clear");
        }
    }

    private static void HardFailuresDuringPause()
    {
        var failures = new (string Name, Action<World> Revoke)[]
        {
            ("access revoked", world => world.Selected.PlayerAccess = false),
            ("unowned target", world => world.Selected.OwnerId = 0),
            ("hostile target owner", world => world.Selected.OwnerId = 99),
            ("closed target", world => world.Selected.Closed = true),
            ("closing target", world => world.Selected.MarkedForClose = true),
            ("inventory removed", world => world.Selected.HasInventory = false),
            ("missing selected port", world => GetTargetedBlock.selectedObject = null),
            ("closed selected port", world => world.PortOwner.Closed = true),
            ("closing selected port", world => world.PortOwner.MarkedForClose = true),
            ("closed grid", world => world.Grid.Closed = true),
            ("closing grid", world => world.Grid.MarkedForClose = true),
            ("past break distance", world => LinkArea.Outside = 100.001),
            ("dead character", world => world.Character.IsDead = true),
            ("closed character", world => world.Character.Closed = true),
            ("changed character", world => MySession.Static.LocalCharacter = new MyCharacter()),
            ("missing character", world => MySession.Static.LocalCharacter = null),
            ("missing player", world => MySession.Static.LocalHumanPlayer = null),
            ("missing game session", world => MySession.Static = null),
            ("missing API session", world => MyAPIGateway.Session = null)
        };
        foreach (var failure in failures)
            foreach (var placementPath in new[] { false, true })
            {
                var world = new World();
                CombatLockout.IsBlocked = true;
                Tick(1);
                failure.Revoke(world);
                if (placementPath) RequestPlacement();
                else Tick(1);
                Check(GetTargetedBlock.selectedBlock == null && GetTargetedBlock.selectedObject == null &&
                    MyInventory.Transfers.Count == 0 && WithdrawRecorder.Calls.Count == 0,
                    "paused hard failure: " + failure.Name + " disconnects on " + (placementPath ? "placement guard" : "update"));
            }
    }

    private static void BoundsAndEnemyPause()
    {
        var world = new World();
        world.AddDepositItems();
        var port = GetTargetedBlock.selectedObject;
        CombatLockout.IsBlocked = true;
        LinkArea.Outside = 100;
        Tick(1);
        CombatLockout.IsBlocked = false;
        Tick(60);
        Check(MyInventory.Transfers.Count == 0 && GetTargetedBlock.selectedBlock == world.Selected &&
            ReferenceEquals(GetTargetedBlock.selectedObject, port), "combat resume: exact break distance remains a bounds pause");
        LinkArea.Outside = 0;
        Tick(1);
        Check(MyInventory.Transfers.Count == 3, "combat resume: active grinder resumes after cooldown and bounds both clear");

        world = new World();
        world.AddDepositItems();
        port = GetTargetedBlock.selectedObject;
        LinkArea.NoEnemies = false;
        Tick(1);
        var observed = CombatLockout.EnemyOverlapObservations;
        Tick(90);
        Check(CombatLockout.EnemyOverlapObservations >= observed + 3 && MyInventory.Transfers.Count == 0 &&
            GetTargetedBlock.selectedBlock == world.Selected && ReferenceEquals(GetTargetedBlock.selectedObject, port),
            "enemy pause: normal periodic scans continue refreshing activity while blocked");
        observed = CombatLockout.EnemyOverlapObservations;
        CombatLockout.IsBlocked = false;
        Tick(1);
        Check(CombatLockout.IsBlocked && CombatLockout.EnemyOverlapObservations > observed && MyInventory.Transfers.Count == 0,
            "enemy pause: an expired timer cannot resume through a continuing hostile overlap");
        LinkArea.NoEnemies = true;
        observed = CombatLockout.EnemyOverlapObservations;
        Tick(60);
        Check(CombatLockout.EnemyOverlapObservations == observed && MyInventory.Transfers.Count == 0,
            "enemy pause: clear scans do not erase the remaining cooldown");
        CombatLockout.IsBlocked = false;
        Tick(1);
        Check(MyInventory.Transfers.Count == 3 && GetTargetedBlock.selectedBlock == world.Selected,
            "enemy resume: same selected inventory works after overlap and cooldown clear");

        world = new World();
        CombatLockout.IsBlocked = true;
        Tick(1);
        LinkArea.EnemyScanFails = true;
        Tick(30);
        Check(GetTargetedBlock.selectedBlock == null && CombatLockout.EnemyOverlapObservations == 0,
            "enemy scan failure: unavailable scan disconnects while paused and does not manufacture hostile activity");
    }

    private static void PauseClearsWorkState()
    {
        var world = PlacementWorld();
        MyInventory.DeferTransfers = true;
        RequestPlacement();
        Check(PendingPlacement.Count == 1, "pause state: placement request is pending before combat");
        CombatLockout.IsBlocked = true;
        RequestPlacement();
        Check(PendingPlacement.Count == 0 && MyInventory.Transfers.Count == 1 && GetTargetedBlock.selectedBlock == world.Selected,
            "pause state: entering combat clears pending placement without dropping the selected link");
        CombatLockout.IsBlocked = false;
        RequestPlacement();
        Check(MyInventory.Transfers.Count == 2, "pause state: a new placement attempt after resume has no stale throttle");

        world = new World();
        Tick(1);
        world.Grinder.IsShooting = false;
        AdvanceGrinder(0);
        CombatLockout.IsBlocked = true;
        AdvanceGrinder(0.01);
        world.Character.Inventory.Add(Component, "SteelPlate");
        CombatLockout.IsBlocked = false;
        AdvanceGrinder(0.01);
        Check(MyInventory.Transfers.Count == 0 && world.Character.Inventory.Items.Count == 1,
            "pause state: old grinder grace is cancelled rather than replayed on early resume");
        world.Grinder.IsShooting = true;
        Tick(1);
        Check(MyInventory.Transfers.Count == 1, "pause state: fresh active grinding resumes normally");
    }

    private static void PauseStatusChanges()
    {
        var world = new World();
        var changes = 0;
        Action changed = () => changes++;
        LinkController.StateChanged += changed;
        try
        {
            CombatLockout.IsBlocked = true;
            Tick(1);
            Check(changes > 0 && LinkController.IsCombatPaused && LinkController.StatusText.Contains(world.Selected.CustomName),
                "pause status: controller raises state change while retaining the named connection");
            var pausedChanges = changes;
            CombatLockout.IsBlocked = false;
            Tick(1);
            Check(changes > pausedChanges && !LinkController.IsCombatPaused &&
                LinkController.StatusText.IndexOf("paused", StringComparison.OrdinalIgnoreCase) < 0,
                "pause status: resumed controller raises state change and clears paused menu text");
        }
        finally { LinkController.StateChanged -= changed; }
    }
}

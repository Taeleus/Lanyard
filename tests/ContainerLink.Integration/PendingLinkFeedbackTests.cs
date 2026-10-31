using System;
using System.IO;
using System.Linq;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRageRender.Import;

internal static partial class Program
{
    private static bool IsPendingLinkHint(string message) => message.Contains("Saved link pending") &&
        message.Contains("/ly off") && message.Contains("then /ly");

    private static MyCubeBlock AimAtNewRecoveryContainer()
    {
        var target = new MyCubeBlock { CubeGrid = new MyCubeGrid(), CustomName = "New container" };
        target.Inventories[0].Owner = target;
        var ports = new MyUseObjectsComponentBase();
        ports.Ports.Add(new InventoryPort { Owner = target });
        target.Components.Set(ports);
        MySession.Static.LocalCharacter.EquippedTool.Components.Set(new MyCasterComponent
            { HitBlock = new TargetedBlock { FatBlock = target } });
        return target;
    }

    private static void PendingLinkFeedbackTests()
    {
        foreach (var unavailable in new[] { "missing entity", "missing exact port" })
        using (var fixture = new RecoveryWorld())
        {
            var commands = new RecoveryCommands();
            commands.BeforeStart();
            try
            {
                fixture.Rejoin(publish: unavailable != "missing entity");
                if (unavailable == "missing exact port")
                {
                    fixture.Ports.Ports.Clear();
                    fixture.Ports.Ports.Add(new InventoryPort { Owner = fixture.Replica,
                        Dummy = new MyModelDummy { Name = "OtherPort" } });
                }
                var candidate = AimAtNewRecoveryContainer();
                Tick(1);
                var session = MySession.Static;
                var saved = File.ReadAllText(fixture.StateFile);
                CombatLockout.IsBlocked = true;
                CombatLockout.RemainingSeconds = 63;
                Check(LinkRecovery.IsPending && MyAPIGateway.Utilities.Messages.Count == 0,
                    "pending feedback: automatic recovery waits silently for " + unavailable);
                Check(!MyAPIUtilities.Static.Enter("/ly"), "pending feedback: explicit /ly remains local");
                Check(MyAPIGateway.Utilities.Messages.Count == 1 && IsPendingLinkHint(MyAPIGateway.Utilities.Messages.Single()),
                    "pending feedback: " + unavailable + " gives one actionable hint on explicit link (observed " + MyAPIGateway.Utilities.Messages.Count + ")");
                Check(!MyAPIUtilities.Static.Enter("/lanyard"), "pending feedback: full command alias remains local");
                GetTargetedBlock.GetPort();
                Check(MyAPIGateway.Utilities.Messages.Count == 3 && MyAPIGateway.Utilities.Messages.All(IsPendingLinkHint),
                    "pending feedback: command aliases and shared Link action each give exactly one hint");
                Tick(120);
                Check(MyAPIGateway.Utilities.Messages.Count == 3, "pending feedback: automatic retries never repeat explicit guidance");
                Check(ReferenceEquals(session, MySession.Static) && LinkRecovery.IsPending && GetTargetedBlock.selectedBlock == null &&
                    File.ReadAllText(fixture.StateFile) == saved,
                    "pending feedback: attempts retain context and the exact durable saved link");
                Check(CombatLockout.IsBlocked && CombatLockout.RemainingSeconds == 63 &&
                    MyInventory.Transfers.Count == 0 && WithdrawRecorder.Calls.Count == 0,
                    "pending feedback: attempts preserve cooldown and issue no transfers");
                Check(candidate.AccessChecks == 0 && candidate.Components.Get<MyUseObjectsComponentBase>().Resolutions == 0,
                    "pending feedback: pending attempts never inspect or replace the aimed candidate");

                Check(!MyAPIUtilities.Static.Enter("/ly off") && !LinkRecovery.IsPending && fixture.Store.Load(42) == null,
                    "pending feedback: explicit off cancels recovery and durably forgets the link");
                MyAPIGateway.Utilities.Messages.Clear();
                Check(!MyAPIUtilities.Static.Enter("/ly") && GetTargetedBlock.selectedBlock == null && CombatLockout.IsBlocked,
                    "pending feedback: forgetting the saved link cannot bypass the existing cooldown");
                Check(MyAPIGateway.Utilities.Messages.All(message => !IsPendingLinkHint(message)),
                    "pending feedback: cancelled recovery does not claim a saved link is pending");
                CombatLockout.IsBlocked = false;
                CombatLockout.RemainingSeconds = 0;
                Check(!MyAPIUtilities.Static.Enter("/ly") && ReferenceEquals(GetTargetedBlock.selectedBlock, candidate) &&
                    fixture.Store.Load(42)?.BlockId == candidate.EntityId,
                    "pending feedback: after safety clears, off then link selects and saves the new container");
                Check(MyAPIGateway.Utilities.Messages.Count == 1 && MyAPIGateway.Utilities.Messages[0].StartsWith("Linked to "),
                    "pending feedback: ordinary linking shows success without a false pending hint");
                fixture.Publish();
                Tick(40);
                Check(ReferenceEquals(GetTargetedBlock.selectedBlock, candidate),
                    "pending feedback: a late old container never replaces the explicitly chosen new link");
            }
            finally { commands.EndSession(); }
        }

        foreach (var unavailable in new[] { "session", "character", "player" })
        using (var fixture = new RecoveryWorld())
        {
            var commands = new RecoveryCommands();
            commands.BeforeStart();
            try
            {
                MyAPIUtilities.Static.Enter("/ly off");
                if (unavailable == "session") MySession.Static = null;
                else if (unavailable == "character") MySession.Static.LocalCharacter = null;
                else MySession.Static.LocalHumanPlayer = null;
                MyAPIGateway.Utilities.Messages.Clear();
                Check(!MyAPIUtilities.Static.Enter("/ly"), "pending feedback: unavailable " + unavailable + " command remains local");
                Check(MyAPIGateway.Utilities.Messages.Count == 1 && MyAPIGateway.Utilities.Messages[0].Contains("not ready") &&
                    !MyAPIGateway.Utilities.Messages[0].Contains("Saved link") && !MyAPIGateway.Utilities.Messages[0].Contains("/ly off"),
                    "pending feedback: unavailable " + unavailable + " gets accurate non-pending guidance");
                Tick(60);
                Check(MyAPIGateway.Utilities.Messages.Count == 1 && !LinkRecovery.IsPending && GetTargetedBlock.selectedBlock == null,
                    "pending feedback: unavailable " + unavailable + " automatic ticks do not repeat guidance or create a link");
            }
            finally { commands.EndSession(); }
        }

        foreach (var notifications in new[] { "missing", "throwing" })
        using (var fixture = new RecoveryWorld())
        {
            fixture.Rejoin(publish: false);
            Tick(1);
            var saved = File.ReadAllText(fixture.StateFile);
            var utility = MyAPIGateway.Utilities;
            if (notifications == "missing") MyAPIGateway.Utilities = null;
            else utility.ThrowNotification = true;
            GetTargetedBlock.GetPort();
            MyAPIGateway.Utilities = utility;
            utility.ThrowNotification = false;
            Tick(60);
            Check(LinkRecovery.IsPending && GetTargetedBlock.selectedBlock == null && File.ReadAllText(fixture.StateFile) == saved,
                "pending feedback: " + notifications + " notifications cannot erase or restore the saved link");
            Check(utility.Messages.Count == 0 && MyInventory.Transfers.Count == 0,
                "pending feedback: " + notifications + " notifications create no later retry spam or transfers");
        }

        using (var fixture = new RecoveryWorld())
        {
            fixture.Rejoin();
            GetTargetedBlock.GetPort();
            Check(ReferenceEquals(GetTargetedBlock.selectedBlock, fixture.Replica) &&
                MyAPIGateway.Utilities.Messages.All(message => !IsPendingLinkHint(message)),
                "pending feedback: recovery completed during an explicit attempt follows the normal link path");
        }
    }
}

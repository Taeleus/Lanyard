using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Lanyard;

internal static class LinkRecovery
{
    private static readonly LinkConnectionStore store = new LinkConnectionStore();
    private static LinkConnectionState saved;
    private static MySession session;
    private static ulong account;
    private static long identity;
    private static ulong server;
    private static Guid world;
    private static bool monitorStarted;
    private static bool resumeApplied;
    private static bool recoveryDisabled;
    private static int retryTicks;
    private static long nextFailureLog;

    public static bool IsPending => saved != null && GetTargetedBlock.selectedBlock == null && !recoveryDisabled;
    public static bool HasCurrentContext => session != null && ReferenceEquals(session, MySession.Static) &&
        !session.IsUnloading && MyAPIGateway.Session != null && world == session.WorldId &&
        server == (MyAPIGateway.Multiplayer?.ServerId ?? 0) &&
        TryPrincipal(session, out var currentAccount, out var currentIdentity) && currentAccount == account && currentIdentity == identity;

    public static void BeginSession()
    {
        SuspendForWorldChange();
    }

    public static void SuspendForWorldChange()
    {
        if (monitorStarted)
        {
            RememberPause();
            // Detach observers before teardown can look like fresh damage.
            CombatLockout.Clear();
        }
        monitorStarted = false;
        resumeApplied = false;
        LinkController.ClearLiveLink();
        BoundsPreview.Clear();
        session = null;
        account = 0;
        identity = 0;
        server = 0;
        world = Guid.Empty;
        retryTicks = 0;
    }

    public static bool Update()
    {
        try { return UpdateCore(); }
        catch (Exception exception)
        {
            // A failed highlight or component callback must not leave a half-bound
            // selection that the controller mistakes for a permanently invalid link.
            try { LinkController.ClearLiveLink(); }
            catch { }
            var now = Stopwatch.GetTimestamp();
            if (now >= nextFailureLog)
            {
                nextFailureLog = now + Stopwatch.Frequency * 5;
                MyLog.Default?.WriteLineAndConsole("Lanyard recovery is waiting: " + exception.Message);
            }
            return false;
        }
    }

    private static bool UpdateCore()
    {
        var current = MySession.Static;
        if (current == null || current.IsUnloading || MyAPIGateway.Session == null ||
            !TryPrincipal(current, out var currentAccount, out var currentIdentity))
        {
            if (session != null || monitorStarted || GetTargetedBlock.selectedBlock != null) SuspendForWorldChange();
            return false;
        }
        var currentServer = MyAPIGateway.Multiplayer?.ServerId ?? 0;
        if (session != null && (!ReferenceEquals(session, current) || server != currentServer || world != current.WorldId ||
                                account != currentAccount || identity != currentIdentity))
            SuspendForWorldChange();
        if (session == null)
        {
            session = current;
            account = currentAccount;
            identity = currentIdentity;
            server = currentServer;
            world = current.WorldId;
            saved = recoveryDisabled ? null : store.Load(account);
        }
        if (!monitorStarted)
        {
            if (current.LocalCharacter == null) return false;
            if (current.LocalCharacter.IsDead && saved?.IdentityId == identity) Forget();
            var resume = saved?.IdentityId == identity ? saved.Resume : null;
            CombatLockout.Start(resume);
            monitorStarted = true;
            resumeApplied = resume != null;
        }
        // Identity IDs are world-scoped. Keep an origin record dormant on another
        // principal so returning to its original context can still restore it.
        if (!IsPending || saved.IdentityId != identity) return true;

        // Keep observing genuine activity while the exact target is still replicating.
        var character = current.LocalCharacter;
        CombatLockout.Update();
        if (!HasCurrentContext || character == null || !ReferenceEquals(character, current.LocalCharacter)) return false;
        if (character.IsDead)
        {
            Forget();
            return true;
        }
        if (retryTicks > 0) { retryTicks--; return false; }
        retryTicks = 29;
        var result = FindTarget(saved, character, out var block, out var port);
        // Denial callbacks can change worlds too; never erase the origin intent
        // based on a result obtained from a context that has already gone away.
        if (!HasCurrentContext || !ReferenceEquals(character, session.LocalCharacter)) return false;
        if (result == RestoreResult.Pending) return false;
        if (result == RestoreResult.Invalid)
        {
            Forget();
            return true;
        }
        // Native callbacks can replace context while access is being checked.
        if (character.IsDead) return false;
        if (block.OwnerId != saved.OwnerId || (block as MyCubeBlock)?.BlockDefinition?.Id.ToString() != saved.Definition ||
            !ReferenceEquals(port.Owner, block) || port.Dummy?.Name != saved.DummyName) return false;
        GetTargetedBlock.Select(block, port);
        LinkController.Restored();
        RememberPause();
        return true;
    }

    public static void Remember()
    {
        var current = MySession.Static;
        var block = GetTargetedBlock.selectedBlock as MyCubeBlock;
        var port = GetTargetedBlock.selectedObject;
        if (current == null || !TryPrincipal(current, out var currentAccount, out var currentIdentity)) return;
        session = current;
        account = currentAccount;
        identity = currentIdentity;
        server = MyAPIGateway.Multiplayer?.ServerId ?? 0;
        world = current.WorldId;
        monitorStarted = true;
        if (block == null || !ReferenceEquals(port?.Owner, block) || string.IsNullOrEmpty(port.Dummy?.Name)) return;
        saved = new LinkConnectionState
        {
            AccountId = account, IdentityId = identity, BlockId = block.EntityId,
            Definition = block.BlockDefinition.Id.ToString(), OwnerId = block.OwnerId,
            DummyName = port.Dummy.Name, OriginWorldId = world,
            Paused = LinkController.IsCombatPaused || CombatLockout.IsBlocked,
            Resume = CombatLockout.CaptureResumeState()
        };
        Save();
    }

    public static void RememberPause()
    {
        if (saved == null || !monitorStarted || saved.IdentityId != identity) return;
        saved.Paused = GetTargetedBlock.selectedBlock == null
            ? saved.Paused || CombatLockout.IsBlocked : LinkController.IsCombatPaused || CombatLockout.IsBlocked;
        saved.Resume = CombatLockout.CaptureResumeState();
        Save();
    }

    public static void Forget()
    {
        var savedAccount = saved?.AccountId ?? account;
        if (savedAccount == 0 && MySession.Static != null) TryPrincipal(MySession.Static, out savedAccount, out _);
        saved = null;
        retryTicks = 0;
        try { if (savedAccount != 0) store.Forget(savedAccount); }
        catch (Exception exception)
        {
            recoveryDisabled = true;
            LogFailure("forget", exception);
        }
        if (resumeApplied && monitorStarted && session != null && !session.IsUnloading && ReferenceEquals(session, MySession.Static))
        {
            CombatLockout.Start();
            resumeApplied = false;
        }
    }

    private static void Save()
    {
        try
        {
            store.Save(saved);
            recoveryDisabled = false;
        }
        catch (Exception exception)
        {
            recoveryDisabled = true;
            LogFailure("save", exception);
        }
    }

    private static bool TryPrincipal(MySession current, out ulong currentAccount, out long currentIdentity)
    {
        var player = current.LocalHumanPlayer;
        currentAccount = player?.Id.SteamId ?? 0;
        currentIdentity = player?.Identity?.IdentityId ?? 0;
        return currentAccount != 0 && currentIdentity != 0;
    }

    private enum RestoreResult { Pending, Invalid, Ready }

    private static RestoreResult FindTarget(LinkConnectionState state, MyCharacter character, out IMyTerminalBlock block, out IMyUseObject port)
    {
        block = null;
        port = null;
        if (!MyEntities.TryGetEntityById(state.BlockId, out MyEntity entity)) return RestoreResult.Pending;
        if (entity.Closed || entity.MarkedForClose) return RestoreResult.Pending;
        var cube = entity as MyCubeBlock;
        block = cube as IMyTerminalBlock;
        if (block == null || cube.OwnerId != state.OwnerId ||
            cube.BlockDefinition?.Id.ToString() != state.Definition) return RestoreResult.Invalid;
        if (!LinkController.HasAccess(block)) return RestoreResult.Invalid;
        if (!HasCurrentContext || !ReferenceEquals(character, session.LocalCharacter) ||
            cube.OwnerId != state.OwnerId || cube.BlockDefinition?.Id.ToString() != state.Definition)
            return RestoreResult.Pending;
        var grid = cube.CubeGrid;
        if (grid == null || grid.Closed || grid.MarkedForClose) return RestoreResult.Pending;
        if (LinkArea.DistanceOutside(grid, character.PositionComp.GetPosition()) > LinkArea.BreakDistance)
            return RestoreResult.Invalid;
        // Anchor verified history before waiting for the port's components. This
        // records activity across another hop without permitting any transfers.
        CombatLockout.FollowGrid(grid);
        var objects = cube.Components.Get<MyUseObjectsComponentBase>();
        if (objects == null) return RestoreResult.Pending;
        var ports = new List<IMyUseObject>();
        objects.GetInteractiveObjects(ports);
        if (ports.Count == 0) return RestoreResult.Pending;
        foreach (var candidate in ports)
        {
            if (candidate.PrimaryAction != UseActionEnum.OpenInventory || candidate.Dummy?.Name != state.DummyName ||
                !ReferenceEquals(candidate.Owner, cube)) continue;
            if (port != null) return RestoreResult.Invalid;
            port = candidate;
        }
        return port == null ? RestoreResult.Pending : RestoreResult.Ready;
    }

    private static void LogFailure(string operation, Exception exception)
    {
        MyLog.Default?.WriteLineAndConsole("Lanyard saved link " + operation + " failed; automatic recovery is disabled: " + exception.Message);
    }
}

using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;

internal static class CombatResumeTests
{
    private static CombatCooldown Timer => (CombatCooldown)typeof(CombatLockout)
        .GetField("cooldown", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    private static CombatResumeState KnownState(out long gridId, out long weaponId)
    {
        CombatLockout.Clear();
        Timer.Clear();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Multiplayer = new Multiplayer();
        MyAPIGateway.GridGroups = new GridGroups();
        MyAPIGateway.Session = new BoundsSession();
        MySession.Static = new MySession();
        CoreSystems.Session.I = new CoreSystems.Session();
        VanillaPlusFramework.VanillaPlusFramework.I = null;
        VanillaPlusFramework.BeamLogic.Beams.I = null;
        MyEntities.Entities.Clear();
        var grid = new MyCubeGrid();
        var gun = new CoreGun();
        grid.Add(gun);
        MyEntities.Add(grid);
        var provider = new FakeWeaponCore();
        provider.Add(gun, 0);
        provider.RespondOnRequest();
        CombatLockout.Start();
        var activities = (IDictionary)typeof(CombatLockout)
            .GetField("grids", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        foreach (var activity in activities.Values)
        {
            activity.GetType().GetProperty("Until").SetValue(activity, 0L);
        }
        Timer.Clear();
        MyAPIGateway.Utilities.Files.Clear();
        if (!CombatLockout.CanConnect(grid)) throw new InvalidOperationException("Known combat fixture must be observed and quiet.");
        gridId = grid.EntityId;
        weaponId = gun.EntityId;
        return CombatLockout.CaptureResumeState();
    }

    private static MyCubeGrid Rebind(CombatResumeState state, long gridId, long weaponId,
        out CoreGun gun, out FakeWeaponCore provider, bool firing = false, bool mapUnavailable = false)
    {
        CombatLockout.Clear();
        MySession.Static = new MySession();
        MyAPIGateway.GridGroups = new GridGroups();
        MyEntities.Entities.Clear();
        var grid = new MyCubeGrid { EntityId = gridId };
        gun = new CoreGun { EntityId = weaponId };
        grid.Add(gun);
        MyEntities.Add(grid);
        provider = new FakeWeaponCore();
        provider.MapUnavailable = mapUnavailable;
        provider.Add(gun, 0);
        provider.State(gun, 0).Shooting = firing;
        provider.RespondOnRequest();
        CombatLockout.Start(state);
        return grid;
    }

    public static void Run(Action<bool, string> check)
    {
        try
        {
            var state = KnownState(out var gridId, out var weaponId);
            check(state.GridIds.Contains(gridId) && state.WeaponIds.Contains(weaponId),
                "combat resume: snapshot includes the observed construct and identified weapon");
            var grid = Rebind(state, gridId, weaponId, out var gun, out var provider);
            check(CombatLockout.CanConnect(grid), "combat resume: replicated known IDs do not restart synthetic quiet warmup");

            state = KnownState(out gridId, out weaponId);
            grid = Rebind(state, gridId + 1000000, weaponId, out gun, out provider);
            check(CombatLockout.CanConnect(grid), "combat resume: a quiet unknown grid requires no invented observation cooldown");
            state = KnownState(out gridId, out weaponId);
            grid = Rebind(state, gridId, weaponId + 1000000, out gun, out provider);
            check(CombatLockout.CanConnect(grid), "combat resume: an idle unknown weapon requires no invented observation cooldown");

            state = KnownState(out gridId, out weaponId);
            grid = Rebind(state, gridId, weaponId, out gun, out provider, true);
            check(!CombatLockout.CanConnect(grid), "combat resume: current firing on a known weapon still starts combat restriction");
            state = KnownState(out gridId, out weaponId);
            grid = Rebind(state, gridId, weaponId, out gun, out provider);
            CombatLockout.CanConnect(grid);
            provider.State(gun, 0).Shots++;
            CombatLockout.Update();
            check(CombatLockout.IsBlocked, "combat resume: a new shot counter change on a known weapon is still observed");
            state = KnownState(out gridId, out weaponId);
            grid = Rebind(state, gridId, weaponId, out gun, out provider);
            CombatLockout.CanConnect(grid);
            grid.Damage(1);
            check(CombatLockout.IsBlocked, "combat resume: current grid damage is never hidden by saved observation history");

            state = KnownState(out gridId, out weaponId);
            grid = Rebind(state, gridId, weaponId, out gun, out provider, mapUnavailable: true);
            check(!CombatLockout.CanConnect(grid), "combat resume: saved history cannot bypass an unavailable weapon map");
            state = CombatLockout.CaptureResumeState();
            check(state.WeaponIds.Contains(weaponId), "combat resume: restored known weapon history survives a pending map during another handoff");
            grid = Rebind(state, gridId, weaponId, out gun, out provider);
            check(CombatLockout.CanConnect(grid), "combat resume: known weapon avoids a fresh warmup once its map is ready after repeated handoff");

            state = KnownState(out gridId, out weaponId);
            for (var handoff = 0; handoff < 3; handoff++)
            {
                CombatLockout.Clear();
                MyEntities.Entities.Clear();
                MySession.Static = new MySession();
                CombatLockout.Start(state);
                state = CombatLockout.CaptureResumeState();
                check(state.GridIds.Contains(gridId) && state.WeaponIds.Contains(weaponId),
                    "combat resume: repeated handoff before replication retains unconsumed known IDs");
            }
            grid = Rebind(state, gridId, weaponId, out gun, out provider);
            check(CombatLockout.CanConnect(grid), "combat resume: delayed known replicas remain eligible after repeated handoff");

            foreach (var firing in new[] { false, true })
            {
                state = KnownState(out gridId, out weaponId);
                grid = Rebind(state, gridId, weaponId, out gun, out provider);
                var beforeActivity = Stopwatch.GetTimestamp();
                if (firing) { provider.State(gun, 0).Shots++; CombatLockout.Update(); }
                else grid.Damage(1);
                check(CombatLockout.IsBlocked && Timer.Deadline >= beforeActivity + 120 * Stopwatch.Frequency,
                    "combat resume: known pending grid " + (firing ? "fire" : "damage") + " enters the persistent cooldown before any anchor is selected");
                state = CombatLockout.CaptureResumeState();
                check(state.GridIds.Contains(gridId), "combat resume: consumed known grid history survives while its exact container is pending");
                grid = Rebind(state, gridId, weaponId, out gun, out provider);
                check(CombatLockout.IsBlocked && !CombatLockout.CanConnect(grid) &&
                    Timer.Deadline >= beforeActivity + 119 * Stopwatch.Frequency,
                    "combat resume: a second hop preserves actual activity observed during pending replication");
            }

            state = KnownState(out gridId, out weaponId);
            var detachedId = gridId + 10000000;
            state.GridIds = state.GridIds.Concat(new[] { detachedId }).ToArray();
            grid = Rebind(state, gridId, weaponId, out gun, out provider);
            var detached = new MyCubeGrid { EntityId = detachedId };
            MyEntities.Add(detached);
            CombatLockout.CanConnect(grid);
            detached.Damage(1);
            check(!CombatLockout.IsBlocked, "combat resume: a detached saved grid does not affect the current selected construct");
            check(!CombatLockout.CaptureResumeState().GridIds.Contains(detachedId),
                "combat resume: anchored snapshots do not retain detached known members");

            state = KnownState(out gridId, out weaponId);
            state.GridIds = new long[CombatResumeState.MaxIds + 1];
            state.GridIds[0] = gridId;
            grid = Rebind(state, gridId, weaponId, out gun, out provider);
            check(CombatLockout.CanConnect(grid), "combat resume: oversized history hints are ignored without inventing combat");
            state = KnownState(out gridId, out weaponId);
            state.GridIds = null;
            state.WeaponIds = null;
            grid = Rebind(state, gridId, weaponId, out gun, out provider);
            check(CombatLockout.CanConnect(grid), "combat resume: absent history arrays do not impose a quiet-grid timer");
        }
        finally
        {
            CombatLockout.Clear();
            Timer.Clear();
            MyAPIGateway.Utilities.Files.Clear();
        }
    }
}

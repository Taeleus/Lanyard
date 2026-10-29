using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Lanyard;

// Observes replicated activity, not nearby enemies. Damage history is collected
// before a target is selected so reconnecting to a recently hit grid cannot
// discard that history. Only the selected mechanical/logical construct affects the lockout.
internal static class CombatLockout
{
    private static bool pollingEffects;
    public const int CooldownSeconds = 120;
    private static readonly CombatCooldown cooldown = new CombatCooldown(CooldownSeconds * Stopwatch.Frequency);
    private static readonly Dictionary<MyCubeGrid, GridActivity> grids = new Dictionary<MyCubeGrid, GridActivity>();
    private static readonly List<IMyCubeGrid> group = new List<IMyCubeGrid>();
    private static readonly HashSet<MyCubeGrid> relevant = new HashSet<MyCubeGrid>();
    private static MyCubeGrid anchor;
    private static WeaponActivity handWeapon;
    private static bool started;
    private static CombatCooldownStore store;
    private static WeaponCoreMonitor weaponCore;
    private static NonDamageEffectMonitor effects;
    private static WeaponCoreMonitor.Weapon coreHandWeapon;
    private static MyEntity coreHandEntity;
    private static bool groupUnavailable;
    private static MySession monitoredSession;
    private static ulong monitoredServer;
    private static bool failed;

    private static string cooldownReason;
    private static string failureReason;
    private static string groupReason;

    private static bool Unavailable => !started || failed || groupUnavailable || effects?.IsBlocking == true ||
        (weaponCore != null && (weaponCore.Failed || (weaponCore.Required && !weaponCore.Ready)));

    public static bool IsBlocked => Unavailable || cooldown.IsBlocked(Stopwatch.GetTimestamp());

    public static string BlockReason
    {
        get
        {
            if (!started) return "combat monitor not started";
            if (failed) return "monitoring failure: " + failureReason;
            if (groupUnavailable) return groupReason ?? "construct observer unavailable";
            if (weaponCore?.Failed == true) return "WeaponCore observer failed: " + weaponCore.FailureReason;
            if (weaponCore?.Required == true && !weaponCore.Ready) return "waiting for WeaponCore API";
            if (effects?.IsBlocking == true) return effects.BlockingReason;
            return cooldown.IsBlocked(Stopwatch.GetTimestamp()) ? cooldownReason ?? "combat activity" : null;
        }
    }

    public static string BlockMessage
    {
        get
        {
            var reason = BlockReason;
            if (reason == null) return "Lanyard available.";
            if (Unavailable) return "Lanyard blocked: " + reason + ". Waiting for this condition to clear.";
            var seconds = Math.Max(0, Math.Ceiling((double)(cooldown.Deadline - Stopwatch.GetTimestamp()) / Stopwatch.Frequency));
            return "Lanyard cooldown: " + reason + "; " + seconds + "s remaining (" + CooldownSeconds + "s after latest activity).";
        }
    }

    public static void ReportBlocked()
    {
        MyLog.Default.WriteLineAndConsole("Lanyard lockout: " + BlockMessage);
        LanyardDiagnostics.Show("Lanyard unavailable.");
    }

    private static string DescribeGrid(MyCubeGrid grid)
    {
        if (grid == null) return "grid unavailable";
        return "grid '" + grid.DisplayName + "' #" + grid.EntityId;
    }

    private static string DescribeBlock(MyCubeBlock block)
    {
        return block == null ? "armor or unresolved fat block" : "block " + block.BlockDefinition?.Id + " #" + block.EntityId;
    }

    public static void Start()
    {
        Clear();
        started = true;
        failed = false;
        failureReason = null;
        monitoredSession = MySession.Static;
        monitoredServer = MyAPIGateway.Multiplayer?.ServerId ?? 0;
        try
        {
            store = new CombatCooldownStore();
            IncludeDeadline(store.Load(), "Restored cooldown; original cause unavailable");
            bool expectWeaponCore = false;
            bool expectVanillaPlus = false;
            if (MySession.Static?.Mods != null)
                foreach (var mod in MySession.Static.Mods)
                {
                    // SDX2 Core/Weapons, and current/legacy public WeaponCore IDs.
                    if (mod.PublishedFileId == 2815514917 || mod.PublishedFileId == 3580645761)
                        expectWeaponCore = true;
                    if (mod.PublishedFileId == 3154371364 || mod.PublishedFileId == 1918681825 || mod.PublishedFileId == 3149625043)
                        expectWeaponCore = true;
                    if (mod.PublishedFileId == 2915780227 || mod.PublishedFileId == 2937540732)
                        expectVanillaPlus = true;
                }
            weaponCore = new WeaponCoreMonitor(CoreDamage);
            weaponCore.Start(expectWeaponCore);
            effects = new NonDamageEffectMonitor(CoreEffect, EmpPulse, expectVanillaPlus, expectWeaponCore);
            MyEntities.OnEntityAdd += EntityAdded;
            MyEntities.OnEntityRemove += EntityRemoved;
            foreach (var entity in MyEntities.GetEntities()) EntityAdded(entity);
            Update();
        }
        catch (Exception exception) { Fail(exception); }
    }

    public static void Clear()
    {
        try { store?.Save(cooldown.Deadline, true); }
        catch (Exception exception) { Fail(exception); }
        started = false;
        MyEntities.OnEntityAdd -= EntityAdded;
        MyEntities.OnEntityRemove -= EntityRemoved;
        foreach (var activity in grids.Values) activity.Dispose();
        grids.Clear();
        coreHandWeapon?.Dispose();
        coreHandWeapon = null;
        coreHandEntity = null;
        effects?.Dispose();
        effects = null;
        weaponCore?.Dispose();
        weaponCore = null;
        store = null;
        handWeapon = null;
        anchor = null;
        group.Clear();
        relevant.Clear();
        // Do not clear the timer when unloading: Nexus transfers/reconnects
        // must not provide a way to discard recent combat activity.
        groupUnavailable = false;
        started = false;
        failed = false;
    }

    public static void Update()
    {
        if (!started) return;
        if (monitoredSession != MySession.Static || monitoredServer != (MyAPIGateway.Multiplayer?.ServerId ?? 0))
        {
            // Seamless reconnects can replace the world/server without the
            // usual session-component lifecycle. Rebind without losing time.
            Start();
            return;
        }
        if (failed) return;
        try
        {
            weaponCore.Update();
            pollingEffects = true;
            try { effects.Update(weaponCore.Ready ? weaponCore.ProviderAssembly : null); }
            finally { pollingEffects = false; }
            var character = MySession.Static?.LocalCharacter;
            // MyGunBase excludes engineer tools and drills, which also expose
            // IsShooting but must not count as weapon fire.
            // Final-poll the previous weapon before a tool switch/respawn can
            // discard a single shot that happened between plugin updates.
            if (handWeapon != null) IncludeDeadline(handWeapon.Poll(), "Vanilla personal weapon fire");
            var weapon = character?.CurrentWeapon as IMyGunObject<MyGunBase>;
            if (weapon == null) handWeapon = null;
            else
            {
                if (handWeapon == null || handWeapon.Gun != weapon) handWeapon = new WeaponActivity(weapon);
                IncludeDeadline(handWeapon.Poll(), "Vanilla personal weapon fire");
            }

            var handEntity = character?.CurrentWeapon as MyEntity;
            if (handEntity != coreHandEntity)
            {
                coreHandWeapon?.Dispose();
                coreHandEntity = handEntity;
                coreHandWeapon = handEntity == null ? null : weaponCore.Watch(handEntity, null, reason => ObserveActivity(reason + "; personal weapon"));
            }
            coreHandWeapon?.Poll();
            foreach (var activity in grids.Values) activity.PollWeapons();
            RefreshGroup();
        }
        catch (Exception exception) { Fail(exception); }
    }

    public static bool CanConnect(MyCubeGrid grid)
    {
        Update();
        if (!started || failed) return false;
        try
        {
            // Changing targets does not clear the player-wide cooldown.
            FollowGrid(grid);
            RefreshGroup();
            return !IsBlocked;
        }
        catch (Exception exception)
        {
            Fail(exception);
            return false;
        }
    }

    // A selected container survives grid splits and merges. Its live grid must
    // remain the combat anchor even when the original grid is removed.
    public static void FollowGrid(MyCubeGrid grid)
    {
        if (!started || failed || ReferenceEquals(anchor, grid)) return;
        try
        {
            PreserveGroupActivity();
            anchor = grid;
            RefreshGroup();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void PreserveGroupActivity()
    {
        foreach (var previous in new List<MyCubeGrid>(relevant))
            if (grids.TryGetValue(previous, out var activity))
            {
                activity.PollWeapons();
                IncludeDeadline(activity.Until, activity.Reason);
                IncludeDeadline(activity.ObservationUntil, activity.ObservationReason);
            }
    }

    private static void RefreshGroup()
    {
        group.Clear();
        relevant.Clear();
        groupUnavailable = coreHandWeapon?.Uncertain == true;
        groupReason = groupUnavailable ? "Personal WeaponCore observer: " + coreHandWeapon.UnavailableReason : null;
        if (anchor == null || anchor.Closed || anchor.MarkedForClose) return;
        MyAPIGateway.GridGroups.GetGroup(anchor, GridLinkTypeEnum.Mechanical, group);
        // Build-planner sources can cross locked connectors. Monitor that same
        // logical construct, including a docked combat ship supplied by a station.
        MyAPIGateway.GridGroups.GetGroup(anchor, GridLinkTypeEnum.Logical, group);
        relevant.Add(anchor);
        foreach (var member in group)
            if (member is MyCubeGrid grid && !grid.Closed && !grid.MarkedForClose) relevant.Add(grid);
        foreach (var grid in relevant)
        {
            if (grids.TryGetValue(grid, out var activity))
            {
                IncludeDeadline(activity.Until, activity.Reason);
                IncludeDeadline(activity.ObservationUntil, activity.ObservationReason);
                if (activity.Uncertain)
                {
                    groupUnavailable = true;
                    groupReason = activity.UnavailableReason + "; " + DescribeGrid(grid);
                }
            }
            else
            {
                groupUnavailable = true;
                groupReason = "Grid activity observer not registered; " + DescribeGrid(grid);
            }
        }
    }

    private static void IncludeDeadline(long deadline, string reason = null)
    {
        if (deadline > cooldown.Deadline) cooldownReason = reason ?? "combat activity";
        cooldown.Include(deadline);
        try { if (started) store?.Save(cooldown.Deadline); }
        catch (Exception exception) { Fail(exception); }
    }

    private static void ObserveActivity(string reason)
    {
        if (started) IncludeDeadline(Stopwatch.GetTimestamp() + CooldownSeconds * Stopwatch.Frequency, reason);
    }

    private static void EmpPulse(Vector3D position, float radius, MyEntity owner, bool friendlyFire)
    {
        if (!started) return;
        var ownerGrid = owner as MyCubeGrid ?? (owner as MyCubeBlock)?.CubeGrid;
        foreach (var entry in grids)
        {
            var grid = entry.Key;
            if (grid.Closed || grid.MarkedForClose) continue;
            if (!friendlyFire && ownerGrid != null && (ownerGrid == grid ||
                MyAPIGateway.GridGroups.HasConnection(ownerGrid, grid, GridLinkTypeEnum.Mechanical))) continue;
            entry.Value.EmpAt(position, radius);
        }
    }

    private static void CoreDamage(object target, float amount)
    {
        if (amount <= 0 || !started) return;
        var slim = target as MySlimBlock;
        var cube = target as MyCubeBlock;
        var grid = slim?.CubeGrid ?? cube?.CubeGrid ?? target as MyCubeGrid;
        if (grid != null && grids.TryGetValue(grid, out var activity))
            activity.RecordDamage(cube ?? slim?.FatBlock, ConnectorDamageSource.WeaponCore, amount);
    }

    private static void CoreEffect(object target)
    {
        if (!started) return;
        var grid = (target as MySlimBlock)?.CubeGrid ?? (target as MyCubeBlock)?.CubeGrid ?? target as MyCubeGrid;
        if (grid != null && grids.TryGetValue(grid, out var activity))
        {
            var deadline = Stopwatch.GetTimestamp() + CooldownSeconds * Stopwatch.Frequency;
            if (deadline > activity.Until)
                activity.Record(deadline, "EMP/jump-inhibition effect; " + DescribeGrid(grid) + "; " + DescribeBlock(target as MyCubeBlock ?? (target as MySlimBlock)?.FatBlock), !pollingEffects);
        }
    }

    internal static void OwnBlockDismantled(MySlimBlock block)
    {
        if (started && !failed && grids.TryGetValue(block.CubeGrid, out var activity))
            activity.OwnDismantled.Add(block);
    }

    private static void EntityAdded(MyEntity entity)
    {
        if (failed || !(entity is MyCubeGrid grid) || grid.Closed || grid.MarkedForClose || grids.ContainsKey(grid)) return;
        try { grids.Add(grid, new GridActivity(grid)); }
        catch (Exception exception) { Fail(exception); }
    }

    private static void EntityRemoved(MyEntity entity)
    {
        if (ReferenceEquals(entity, MySession.Static?.LocalCharacter?.CurrentWeapon) ||
            ReferenceEquals(entity, coreHandEntity))
        {
            try
            {
                // WC rifles may fire without updating vanilla LastShootTime.
                // Even a complete equip/fire/holster between our updates must
                // start their normal conservative observation cooldown.
                var observed = ReferenceEquals(entity, coreHandEntity) ? coreHandWeapon : null;
                observed?.Poll();
                if (observed?.IsObserved != true)
                    weaponCore?.ObserveRemovedHandheld(entity,
                        (entity as IMyGunBaseUser)?.PhysicalItemId, reason => ObserveActivity(reason + "; personal weapon"));
            }
            catch (Exception exception) { Fail(exception); }
        }
        // UnequipWeapon removes the gun entity before clearing CurrentWeapon.
        // This also catches equip/fire/holster entirely between our updates.
        if (entity is IMyGunObject<MyGunBase> gun &&
            (ReferenceEquals(entity, MySession.Static?.LocalCharacter?.CurrentWeapon) ||
             ReferenceEquals(gun, handWeapon?.Gun)))
        {
            try { IncludeDeadline(new WeaponActivity(gun).Poll(), "Vanilla personal weapon fire observed during holster/removal"); }
            catch (Exception exception) { Fail(exception); }
        }
        if (!(entity is MyCubeGrid grid) || !grids.TryGetValue(grid, out var activity)) return;
        activity.Dispose();
        grids.Remove(grid);
        relevant.Remove(grid);
    }

    private static void ConnectionsChanged(MyCubeGrid changed, GridLinkTypeEnum type)
    {
        if (!started || failed || anchor == null ||
            (type != GridLinkTypeEnum.Mechanical && type != GridLinkTypeEnum.Logical)) return;
        try
        {
            // Snapshot the old construct before replacing membership. A gun
            // can fire and undock between plugin updates; sample its final
            // shot while it still belongs to the previously observed interval.
            PreserveGroupActivity();
            RefreshGroup();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void Fail(Exception exception)
    {
        if (!failed) MyLog.Default.WriteLineAndConsole("Lanyard activity monitoring failed: " + exception);
        failureReason = exception.GetType().Name + ": " + exception.Message;
        failed = true; // A failed monitor must not silently allow transfers.
    }

    private sealed class GridActivity : IDisposable
    {
        private readonly MyCubeGrid grid;
        private readonly ConnectorDamageWindow connectorDamage = new ConnectorDamageWindow(Stopwatch.Frequency);
        private bool hasConnectorIntegrityFrame;
        private int lastConnectorIntegrityFrame;
        private readonly Dictionary<MyCubeBlock, WeaponActivity> weapons = new Dictionary<MyCubeBlock, WeaponActivity>();
        private readonly Dictionary<MyCubeBlock, WeaponCoreMonitor.Weapon> coreWeapons = new Dictionary<MyCubeBlock, WeaponCoreMonitor.Weapon>();
        private readonly HashSet<MyCubeBlock> functionalBlocks = new HashSet<MyCubeBlock>();
        private bool coreDiscovered;
        public bool Uncertain
        {
            get
            {
                foreach (var weapon in coreWeapons.Values) if (weapon.Uncertain) return true;
                return false;
            }
        }
        public string UnavailableReason
        {
            get
            {
                foreach (var weapon in coreWeapons)
                    if (weapon.Value.Uncertain) return "WeaponCore observer: " + weapon.Value.UnavailableReason + "; " + DescribeBlock(weapon.Key);
                return null;
            }
        }
        public string Reason { get; private set; }
        public string ObservationReason { get; private set; }
        public long Until { get; private set; }
        public long ObservationUntil { get; private set; }
        public readonly HashSet<MySlimBlock> OwnDismantled = new HashSet<MySlimBlock>();

        public GridActivity(MyCubeGrid grid)
        {
            this.grid = grid;
            // Replication/reconnects and splits do not carry past damage
            // events. Require a full observed quiet interval before trusting
            // any newly observed grid. This remains private until selection.
            ObservationUntil = Stopwatch.GetTimestamp() + CooldownSeconds * Stopwatch.Frequency;
            ObservationReason = "New grid observation; " + DescribeGrid(grid);
            try
            {
                grid.OnGridBlockDamaged += Damaged;
                // A block destroyed outright can arrive as a removal without an
                // intermediate integrity update. Conservatively count removals.
                grid.OnBlockRemoved += Removed;
                grid.OnFatBlockAdded += Added;
                grid.OnFatBlockRemoved += FatRemoved;
                grid.OnGridMerge += Merged;
                grid.OnConnectionChangeCompleted += ConnectionsChanged;
                foreach (var block in grid.GetFatBlocks()) Added(block);
            }
            catch { Dispose(); throw; }
        }

        public void EmpAt(Vector3D position, float radius)
        {
            // Called only for an observed EMP detonation/beam endpoint. Check
            // actual functional block bounds, never enemy proximity/radar.
            foreach (var block in functionalBlocks)
                if (block is IMyFunctionalBlock && !block.Closed && !block.MarkedForClose &&
                    block.PositionComp.WorldAABB.Distance(position) <= radius)
                {
                    Record(Stopwatch.GetTimestamp() + CooldownSeconds * Stopwatch.Frequency, "Observed EMP pulse; " + DescribeGrid(grid) + "; " + DescribeBlock(block));
                    return;
                }
        }

        public void Record(long deadline, string reason = null, bool refreshGroup = true)
        {
            if (deadline <= 0) return;
            if (deadline > Until) { Until = deadline; Reason = reason ?? "Grid activity; " + DescribeGrid(grid); }
            // Polled EWAR is batched; Update refreshes the construct after all
            // grids have recorded their history. Event callbacks stay immediate.
            if (!refreshGroup) return;
            // Damage/effect callbacks can arrive between plugin updates.
            // Resolve the current connector/mechanical construct at the event.
            try
            {
                RefreshGroup();
                if (relevant.Contains(grid)) IncludeDeadline(Until, Reason);
            }
            catch (Exception exception) { Fail(exception); }
        }

        private void Merged(MyCubeGrid survivor, MyCubeGrid removed)
        {
            if (survivor != grid) return;
            // The source can be disposed before this event. Restart the quiet
            // observation interval instead of losing its unselected history.
            ObservationUntil = Stopwatch.GetTimestamp() + CooldownSeconds * Stopwatch.Frequency;
            ObservationReason = "Grid merge observation; " + DescribeGrid(grid);
            try { RefreshGroup(); }
            catch (Exception exception) { Fail(exception); }
        }

        public void RecordDamage(MyCubeBlock block, ConnectorDamageSource source, float amount = 0)
        {
            var now = Stopwatch.GetTimestamp();
            if (block is MyShipConnector)
            {
                // One damage operation may lower several component layers.
                // Their integrity callbacks in one frame are still one hit.
                if (source == ConnectorDamageSource.ReplicatedIntegrity && MySession.Static != null)
                {
                    int frame = MySession.Static.GameplayFrameCounter;
                    if (hasConnectorIntegrityFrame && frame == lastConnectorIntegrityFrame) return;
                    hasConnectorIntegrityFrame = true;
                    lastConnectorIntegrityFrame = frame;
                }
                if (!connectorDamage.Observe(source, now)) return;
            }
            Record(now + CooldownSeconds * Stopwatch.Frequency, (block is MyShipConnector ? "Repeated connector damage" : "Grid damage") + " (" + source + ", amount " + amount + "); " + DescribeGrid(grid) + "; " + DescribeBlock(block));
        }
        private void Damaged(IMySlimBlock block, float amount, MyHitInfo? hit, long attacker)
        {
            // Replicated integrity losses use a negative delta; direct damage
            // uses a positive amount. Neither supplies an enemy proximity signal.
            var slim = block as MySlimBlock;
            if (amount == 0 || (amount < 0 && OwnGrindingPatches.AppliesTo(slim))) return;
            if (slim != null) OwnDismantled.Remove(slim);
            RecordDamage(slim?.FatBlock, amount < 0
                ? ConnectorDamageSource.ReplicatedIntegrity : ConnectorDamageSource.NativeDamage, amount);
        }

        private void Removed(MySlimBlock block)
        {
            // Connectors and advanced rotors also notify about a live peer
            // block when their conveyor topology changes. That is not damage.
            if (block == null || block.CubeGrid != grid || grid.CubeBlocks.Contains(block)) return;
            if (OwnDismantled.Remove(block) && block.IsFullyDismounted && block.BuildIntegrity <= 0) return;
            if (block.IsMovedBySplit || grid.IsBlockTrasferInProgress) return;
            Record(Stopwatch.GetTimestamp() + CooldownSeconds * Stopwatch.Frequency, "Block removal; " + DescribeGrid(grid) + "; " + DescribeBlock(block?.FatBlock));
        }

        private void Added(MyCubeBlock block)
        {
            if (block == null || block.CubeGrid != grid) return;
            if (block is IMyGunObject<MyGunBase> gun) weapons[block] = new WeaponActivity(gun);
            if (block is IMyFunctionalBlock) functionalBlocks.Add(block);
            WatchCore(block);
        }

        private void WatchCore(MyCubeBlock block)
        {
            if (weaponCore.IsCandidate(block.BlockDefinition.Id) && !coreWeapons.ContainsKey(block))
                coreWeapons.Add(block, weaponCore.Watch(block, block.BlockDefinition.Id,
                    reason => Record(Stopwatch.GetTimestamp() + CooldownSeconds * Stopwatch.Frequency, reason + "; " + DescribeGrid(grid) + "; " + DescribeBlock(block))));
        }

        private void FatRemoved(MyCubeBlock block)
        {
            if (block == null || block.CubeGrid != grid) return;
            if (block.SlimBlock != null && grid.CubeBlocks.Contains(block.SlimBlock)) return;
            // The game closes a fat block before raising removal callbacks.
            // A mod gun may no longer expose usable state at this point.
            try
            {
                if (weapons.TryGetValue(block, out var gun)) Record(gun.Poll(), "Vanilla weapon fire during removal; " + DescribeGrid(grid) + "; " + DescribeBlock(block));
            }
            catch (Exception exception) { Fail(exception); }
            finally
            {
                weapons.Remove(block);
                functionalBlocks.Remove(block);
                if (coreWeapons.TryGetValue(block, out var weapon))
                {
                    coreWeapons.Remove(block);
                    weapon.Dispose();
                }
            }
        }

        public void PollWeapons()
        {
            if (grid.Closed || grid.MarkedForClose) return;
            if (weaponCore.Ready && !coreDiscovered)
            {
                // One discovery pass when the optional API becomes ready.
                // Later blocks are classified by Added, never by a world scan.
                foreach (var block in grid.GetFatBlocks()) WatchCore(block);
                coreDiscovered = true;
            }
            foreach (var entry in coreWeapons)
                if (!entry.Key.Closed && !entry.Key.MarkedForClose) entry.Value.Poll();
            foreach (var entry in weapons)
            {
                if (entry.Key.Closed || entry.Key.MarkedForClose) continue;
                var deadline = entry.Value.Poll();
                if (deadline > 0)
                    Record(deadline, "Vanilla weapon fire; " + DescribeGrid(grid) + "; " + DescribeBlock(entry.Key));
            }
        }

        public void Dispose()
        {
            grid.OnGridBlockDamaged -= Damaged;
            grid.OnBlockRemoved -= Removed;
            grid.OnFatBlockAdded -= Added;
            grid.OnFatBlockRemoved -= FatRemoved;
            grid.OnGridMerge -= Merged;
            grid.OnConnectionChangeCompleted -= ConnectionsChanged;
            weapons.Clear();
            foreach (var weapon in coreWeapons.Values) weapon.Dispose();
            coreWeapons.Clear();
            functionalBlocks.Clear();
            OwnDismantled.Clear();
        }
    }

    private sealed class WeaponActivity
    {
        public readonly IMyGunObject<MyGunBase> Gun;
        private DateTime lastShot;
        public WeaponActivity(IMyGunObject<MyGunBase> gun) { Gun = gun; }

        public long Poll()
        {
            var now = Stopwatch.GetTimestamp();
            var deadline = Gun.IsShooting ? now + CooldownSeconds * Stopwatch.Frequency : 0;
            var shot = Gun.GunBase.LastShootTime;
            if (shot == lastShot) return deadline;
            lastShot = shot;
            // LastShootTime catches single shots between plugin updates and
            // recent fire before a port was selected. Convert once to a
            // monotonic deadline; never run the cooldown on the wall clock.
            var age = (DateTime.UtcNow - shot).TotalSeconds;
            if (age >= 0 && age < CooldownSeconds)
                deadline = Math.Max(deadline, now + (long)((CooldownSeconds - age) * Stopwatch.Frequency));
            return deadline;
        }
    }
}

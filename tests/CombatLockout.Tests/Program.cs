using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Lanyard;
using VRage.Game.ModAPI;

internal class Gun : MyCubeBlock, IMyGunObject<MyGunBase>
{
    public bool IsShooting { get; set; }
    public MyGunBase GunBase { get; } = new MyGunBase();
}
internal class Tool : MyCubeBlock, IMyGunObject<MyDeviceBase>
{
    public bool IsShooting { get; set; } = true;
    public MyDeviceBase GunBase { get; } = new MyDeviceBase();
}
internal static class Program
{
    private static int assertions;
    private static void Check(bool pass, string message)
    {
        assertions++;
        if (!pass) throw new Exception(message);
    }
    private static MyCubeGrid World()
    {
        // Each World is a new isolated storage fixture, not an in-process
        // handoff. Finish any recovered prior fixture's pending suspension.
        typeof(CombatLockout).GetField("nextStorageRetry", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 0L);
        CombatLockout.Clear();
        ResetTimer();
        MyAPIGateway.Utilities = new Utilities();
        MyAPIGateway.Multiplayer = new Multiplayer();
        CoreSystems.Session.I = new CoreSystems.Session();
        VanillaPlusFramework.VanillaPlusFramework.I = null;
        VanillaPlusFramework.BeamLogic.Beams.I = null;
        VRage.Utils.MyLog.Default.Errors = 0;
        MyEntities.Entities.Clear();
        MyAPIGateway.GridGroups = new GridGroups();
        MySession.Static = new MySession();
        var grid = new MyCubeGrid();
        grid.BigOwners.Add(1);
        MyEntities.Add(grid);
        return grid;
    }
    private static long Deadline()
    {
        var timer = (CombatCooldown)typeof(CombatLockout).GetField("cooldown", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        return (long)typeof(CombatCooldown).GetField("until", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(timer);
    }
    private static void ResetTimer()
    {
        ((CombatCooldown)typeof(CombatLockout).GetField("cooldown", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Clear();
    }
    private static void ExpireObservedCombat()
    {
        ResetTimer();
        var dictionary = (System.Collections.IDictionary)typeof(CombatLockout).GetField("grids", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        foreach (var activity in dictionary.Values) { activity.GetType().GetProperty("Until").SetValue(activity, 0L); }
        MyAPIGateway.Utilities.Files.Clear();
    }
    private static void TestBoundsAccess()
    {
        MySession.Static = new MySession();
        MyAPIGateway.Session = new BoundsSession();
        var factions = MyAPIGateway.Session.Factions;
        factions.Members[1] = new BoundsFaction { FactionId = 10 };
        factions.Members[2] = new BoundsFaction { FactionId = 10 };
        factions.Members[3] = new BoundsFaction { FactionId = 20 };
        var grid = new MyCubeGrid();
        int before = grid.Subscribers;
        using (var access = new BoundsAccess(grid))
        {
            Check(!access.IsAllowed(), "bounds: empty grid denied");
            grid.Add(new MyCubeBlock());
            Check(!access.IsAllowed(), "bounds: armor-only grid denied");
            var own = new MyCubeBlock { OwnerId = 1, IDModule = new OwnershipModule() };
            grid.Add(own);
            Check(access.IsAllowed(), "bounds: personally owned grid allowed, armor excluded");
            var other = new MyCubeBlock { IDModule = new OwnershipModule() };
            grid.Add(other);
            Check(!access.IsAllowed(), "bounds: unowned blocks count; tie denied");
            other.OwnerId = 3;
            Check(!access.IsAllowed(), "bounds: enemy ownership denied");
            own.OwnerId = 0;
            other.OwnerId = 0;
            Check(!access.IsAllowed(), "bounds: entirely unowned grid denied");
            own.OwnerId = 1;
            other.OwnerId = 2;
            other.IDModule.ShareMode = VRage.Game.MyOwnershipShareModeEnum.All;
            Check(!access.IsAllowed(), "bounds: Share All does not qualify");
            other.IDModule.ShareMode = VRage.Game.MyOwnershipShareModeEnum.Faction;
            Check(access.IsAllowed(), "bounds: own plus faction-shared majority allowed");
            own.OwnerId = 2;
            own.IDModule.ShareMode = VRage.Game.MyOwnershipShareModeEnum.Faction;
            Check(access.IsAllowed(), "bounds: wholly faction-shared grid allowed");
            factions.Members.Remove(1);
            Check(!access.IsAllowed(), "bounds: leaving faction immediately revokes access");
            factions.Members[1] = new BoundsFaction { FactionId = 10 };
            Check(access.IsAllowed(), "bounds: faction membership restored live");
            other.IDModule.ShareMode = VRage.Game.MyOwnershipShareModeEnum.None;
            Check(!access.IsAllowed(), "bounds: sharing removal immediately revokes access");
            grid.Remove(other);
            Check(access.IsAllowed(), "bounds: structure removal updates majority");
            own.OwnerId = 3;
            Check(!access.IsAllowed(), "bounds: other faction sharing denied");
            own.OwnerId = 1;
            MySession.Static.LocalHumanPlayer.Identity.IdentityId = 0;
            Check(!access.IsAllowed(), "bounds: missing local identity denied");
            MySession.Static.LocalHumanPlayer.Identity.IdentityId = 1;
            grid.MarkedForClose = true;
            Check(!access.IsAllowed(), "bounds: closing grid denied");
        }
        Check(grid.Subscribers == before, "bounds: structure observers disposed");
    }
    // Start normal observation; idle observers do not create synthetic combat.
    private static void StartObserved()
    {
        CombatLockout.Start();
    }
    public static void Main(string[] args)
    {
        CooldownTestStorage.Initialize();
        try { Run(args); }
        finally { CooldownTestStorage.Cleanup(); }
    }
    private static void Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--storage-followup-probe")
        {
            CooldownStorageProbe.Run();
            return;
        }
        if (args.Length > 0 && args[0] == "--effects-only")
        {
            OwnGrindingPatches.Initialize();
            FrameworkAuditTests.Run(Check);
            EffectTests();
            Console.WriteLine($"PASS: {assertions} effect assertions.");
            return;
        }
        TopologyNotificationTests.Run(Check);
        DebugTimeoutTests.Run(Check);
        ConnectorDamageTests.Run(Check);
        RemovalCleanupTests.Run(Check);
        CombatAuditTests.Run(Check);
        GridAnchorTransitionTests.Run(Check);
        FrameworkAuditTests.Run(Check);
        WeaponCoreCleanupTests.Run(Check);
        WeaponCoreRegistrationTests.Run(Check);
        EnemyOverlapCooldownTests.Run(Check);
        CooldownRoundTripTests.Run(Check);
        CooldownAtomicSaveTests.Run(Check);
        CooldownWriteRecoveryTests.Run(Check);
        CombatResumeTests.Run(Check);
        QuietReadinessTests.Run(Check);
        GridOwnershipCacheTests.Run(Check);
        OwnershipRelationTests.Run(Check);
        TestBoundsAccess();
        OwnGrindingPatches.Initialize();
        FriendlyGrindingTests.Run(Check);
        var timer = new CombatCooldown(120);
        Check(!timer.IsBlocked(0), "fresh timer");
        timer.Observe(100);
        Check(timer.IsBlocked(219), "blocked just before expiry");
        Check(!timer.IsBlocked(220), "unblocked at exact expiry");
        timer.Observe(150);
        Check(timer.IsBlocked(269), "further activity extends cooldown");
        timer.Observe(110);
        Check(timer.IsBlocked(269), "older activity cannot shorten cooldown");
        timer.Include(500);
        Check(timer.IsBlocked(499) && !timer.IsBlocked(500), "imported group deadline");
        timer.Clear();
        Check(!timer.IsBlocked(0), "session reset clears timer");

        var grid = World();
        Check(CombatLockout.IsBlocked, "unstarted monitor fails closed");
        StartObserved();
        Check(CombatLockout.CanConnect(grid), "quiet grid can connect");
        grid.Damage(0);
        Check(!CombatLockout.IsBlocked, "zero damage ignored");
        grid.Damage(10);
        Check(CombatLockout.IsBlocked, "positive damage blocks immediately");
        Check(Deadline() > Stopwatch.GetTimestamp() + (CombatLockout.CooldownSeconds - 1) * Stopwatch.Frequency, "full configured cooldown");
        Check(!CombatLockout.CanConnect(new MyCubeGrid()), "changing targets cannot clear lockout");
        var replacement = new Character();
        MySession.Static.LocalCharacter = replacement;
        CombatLockout.Update();
        Check(CombatLockout.IsBlocked, "respawn cannot clear cooldown");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        grid.Damage(-0.01f);
        Check(CombatLockout.IsBlocked, "negative replicated integrity delta blocks");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        grid.RemoveSlim();
        Check(CombatLockout.IsBlocked, "outright block removal blocks");

        grid = World(); StartObserved();
        var other = new MyCubeGrid(); MyEntities.Add(other);
        Check(CombatLockout.CanConnect(grid), "quiet selected grid");
        other.Damage(5);
        Check(!CombatLockout.IsBlocked, "unrelated damage does not signal nearby activity");
        Check(!CombatLockout.CanConnect(other), "damage before target selection is remembered");

        grid = World(); var subgrid = new MyCubeGrid(); MyEntities.Add(subgrid);
        MyAPIGateway.GridGroups.Groups[grid] = new List<IMyCubeGrid> { grid, subgrid };
        StartObserved(); CombatLockout.CanConnect(grid); subgrid.Damage(1);
        Check(CombatLockout.IsBlocked, "mechanical subgrid damage blocks");

        grid = World(); subgrid = new MyCubeGrid(); MyEntities.Add(subgrid);
        StartObserved(); CombatLockout.CanConnect(grid); subgrid.Damage(1);
        Check(!CombatLockout.IsBlocked, "detached grid initially unrelated");
        MyAPIGateway.GridGroups.Groups[grid] = new List<IMyCubeGrid> { grid, subgrid };
        CombatLockout.Update();
        Check(CombatLockout.IsBlocked, "newly attached subgrid brings recent damage history");

        grid = World(); StartObserved();
        var health = MySession.Static.LocalCharacter.StatComp.Health;
        health.Change(100, 90); Check(!CombatLockout.IsBlocked, "healing does not lock out");
        health.Change(99, 100); Check(!CombatLockout.IsBlocked, "character damage does not create a timeout");
        MySession.Static.LocalCharacter = new Character(); CombatLockout.Update();
        Check(health.Subscribers == 0, "old character health handler detached");

        grid = World(); var gun = new Gun { IsShooting = true }; grid.Add(gun);
        StartObserved();
        Check(!CombatLockout.IsBlocked, "unselected firing ship does not signal proximity");
        Check(!CombatLockout.CanConnect(grid), "active ship weapon fire blocks connection");
        var firstDeadline = Deadline(); CombatLockout.Update();
        Check(Deadline() > firstDeadline, "sustained fire extends cooldown");

        grid = World(); gun = new Gun(); grid.Add(gun); StartObserved(); CombatLockout.CanConnect(grid);
        gun.GunBase.LastShootTime = DateTime.UtcNow; CombatLockout.Update();
        Check(CombatLockout.IsBlocked, "single shot between updates blocks even when IsShooting is false");
        firstDeadline = Deadline(); gun.GunBase.LastShootTime = DateTime.UtcNow; CombatLockout.Update();
        Check(Deadline() > firstDeadline, "later single shot extends cooldown");

        grid = World(); gun = new Gun(); gun.GunBase.LastShootTime = DateTime.UtcNow.AddSeconds(-5); grid.Add(gun);
        StartObserved(); Check(!CombatLockout.CanConnect(grid), "recent fire before selection blocks");
        Check(Deadline() < Stopwatch.GetTimestamp() + (CombatLockout.CooldownSeconds - 4) * Stopwatch.Frequency, "recent fire imports remaining duration");

        grid = World(); gun = new Gun(); gun.GunBase.LastShootTime = DateTime.UtcNow.AddMinutes(-3); grid.Add(gun);
        StartObserved(); Check(CombatLockout.CanConnect(grid), "old fire does not block");

        grid = World(); grid.Add(new Tool()); MySession.Static.LocalCharacter.CurrentWeapon = new Tool();
        StartObserved(); Check(CombatLockout.CanConnect(grid), "working tools do not count as weapon fire");

        grid = World(); MySession.Static.LocalCharacter.CurrentWeapon = new Gun { IsShooting = true };
        StartObserved(); Check(CombatLockout.IsBlocked, "personal weapon fire blocks without link");

        grid = World(); gun = new Gun(); grid.Add(gun); MySession.Static.LocalCharacter.CurrentWeapon = gun;
        StartObserved(); gun.GunBase.LastShootTime = DateTime.UtcNow; CombatLockout.Update();
        Check(CombatLockout.IsBlocked, "personal single-shot timestamp blocks");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        gun = new Gun { IsShooting = true }; grid.Add(gun); CombatLockout.Update();
        Check(CombatLockout.IsBlocked, "newly built gun monitored");

        grid = World(); gun = new Gun(); grid.Add(gun); StartObserved(); CombatLockout.CanConnect(grid);
        grid.Remove(gun); gun.IsShooting = true; CombatLockout.Update();
        Check(!CombatLockout.IsBlocked, "removed gun no longer polled");

        grid = World(); StartObserved();
        Check(grid.Subscribers == 7, "grid events registered once");
        MyEntities.Remove(grid); Check(grid.Subscribers == 0, "removed grid handlers detached");
        MyEntities.Add(grid); Check(grid.Subscribers == 7, "replicated grid can be tracked again");
        health = MySession.Static.LocalCharacter.StatComp.Health;
        CombatLockout.Clear();
        Check(grid.Subscribers == 0 && health.Subscribers == 0 && MyEntities.Subscribers == 0, "session teardown detaches all handlers");
        StartObserved(); Check(CombatLockout.CanConnect(grid), "new session starts cleanly");
        Check(grid.Subscribers == 7 && health.Subscribers == 0 && MyEntities.Subscribers == 2, "session restart does not duplicate handlers");

        MyAPIGateway.GridGroups.Throw = true; CombatLockout.Update();
        Check(CombatLockout.IsBlocked && !CombatLockout.CanConnect(grid), "monitor failure rejects transfer and connection");
        Check(VRage.Utils.MyLog.Default.Errors == 1, "monitor failure logs once");
        CombatLockout.Clear();

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        var block = new MySlimBlock { CubeGrid = grid, OwnerId = 1 };
        block.DecreaseMountLevel(10, identityId: 1);
        Check(!CombatLockout.IsBlocked && block.Integrity == 90 && block.BuildIntegrity == 90, "own grinding allowed and original executes");
        block.DecreaseMountLevel(90, identityId: 1); grid.RemoveSlim(block);
        Check(!CombatLockout.IsBlocked, "own grinding through final block removal allowed");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        block = new MySlimBlock { CubeGrid = grid }; block.DecreaseMountLevel(10, identityId: 1);
        Check(!CombatLockout.IsBlocked, "own grinding on unowned armor of owned grid allowed");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        block = new MySlimBlock { CubeGrid = grid, OwnerId = 1 };
        block.SetIntegrity(80, 80, null, 1);
        Check(!CombatLockout.IsBlocked, "replicated own grinding allowed");
        block.SetIntegrity(0, 0, null, 1); grid.RemoveSlim(block);
        Check(!CombatLockout.IsBlocked, "replicated own final dismantling allowed");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        block = new MySlimBlock { CubeGrid = grid, OwnerId = 1 };
        block.SetIntegrity(100, 80, null, 1);
        Check(CombatLockout.IsBlocked, "matching identity without decreasing build level does not exempt damage");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        block = new MySlimBlock { CubeGrid = grid, OwnerId = 1 };
        block.DecreaseMountLevel(10, identityId: 2);
        Check(CombatLockout.IsBlocked, "another player grinding owned block still blocks");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        block = new MySlimBlock { CubeGrid = grid, OwnerId = 1 };
        block.SetIntegrity(80, 80, null, 2);
        Check(CombatLockout.IsBlocked, "replicated other-player grinding still blocks");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        block = new MySlimBlock { CubeGrid = grid, OwnerId = 2 };
        block.DecreaseMountLevel(10, identityId: 1);
        Check(CombatLockout.IsBlocked, "enemy-owned block on owned mixed grid has no exemption");

        grid = World(); grid.BigOwners.Clear(); grid.BigOwners.Add(2); StartObserved(); CombatLockout.CanConnect(grid);
        block = new MySlimBlock { CubeGrid = grid }; block.DecreaseMountLevel(10, identityId: 1);
        Check(CombatLockout.IsBlocked, "grinding armor on somebody else's grid has no exemption");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        block = new MySlimBlock { CubeGrid = grid, OwnerId = 1 };
        block.DecreaseMountLevel(10, identityId: 1); grid.Damage(block, -0.1f);
        Check(CombatLockout.IsBlocked, "incoming replicated damage after grinding is not exempt");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        block = new MySlimBlock { CubeGrid = grid, OwnerId = 1 };
        var sameBlock = block;
        block.DuringGrinding = () => grid.Damage(sameBlock, 10);
        block.DecreaseMountLevel(10, identityId: 1);
        Check(CombatLockout.IsBlocked, "direct combat damage during own grinding still counts");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        block = new MySlimBlock { CubeGrid = grid, OwnerId = 1, ThrowDuringGrinding = true };
        bool threw = false;
        try { block.DecreaseMountLevel(10, identityId: 1); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "grinding observer does not suppress vanilla exceptions");
        Check(!OwnGrindingPatches.AppliesTo(block), "grinding scope restored after exception");
        grid.Damage(block, -0.1f); Check(CombatLockout.IsBlocked, "damage still monitored after grinding exception");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        block = new MySlimBlock { CubeGrid = grid, OwnerId = 1 };
        block.DecreaseMountLevel(10, identityId: 1); grid.RemoveSlim(block);
        Check(CombatLockout.IsBlocked, "unexplained removal after partial grinding still counts");

        Sdx2Tests();
        EffectTests();
        PollingOptimizationTests();
        WeaponDefinitionClassificationTests();
        CombatLockout.Clear();
        OwnGrindingPatches.Dispose();
        Console.WriteLine($"PASS: {assertions} combat-lockout assertions (production sources and real Harmony patches with game API doubles).");
    }

    private static void Sdx2Tests()
    {
        var grid = World();
        MySession.Static.Mods.Add(new VRage.Game.MyObjectBuilder_Checkpoint.ModItem { PublishedFileId = 3154371364 });
        StartObserved(); Check(CombatLockout.IsBlocked, "expected WC without ready API fails closed");
        var wc = new FakeWeaponCore(); wc.Respond(); CombatLockout.Update();
        Check(CombatLockout.CanConnect(grid), "late API response enables quiet non-weapon grid");

        grid = World(); wc = new FakeWeaponCore(); wc.RespondOnRequest();
        var coreGun = new CoreGun(); grid.Add(coreGun); wc.Add(coreGun, 0, 3);
        StartObserved(); Check(CombatLockout.CanConnect(grid), "idle newly identified WC weapon is immediately ready");
        ExpireObservedCombat(); CombatLockout.Update(); Check(CombatLockout.CanConnect(grid), "quiet observed WC construct becomes usable");
        wc.Event(coreGun, 3, 2, true); Check(!CombatLockout.IsBlocked, "WC tracking alone is not a combat detector");
        wc.Event(coreGun, 3, 1, true); Check(CombatLockout.IsBlocked, "sorter-based WC firing event blocks immediately");
        ExpireObservedCombat(); wc.Event(coreGun, 0, 8, true);
        Check(CombatLockout.IsBlocked, "WC railgun prefire charge blocks");
        ExpireObservedCombat(); wc.State(coreGun, 0).Shooting = true; CombatLockout.Update();
        Check(CombatLockout.IsBlocked, "WC sustained fire polling blocks");
        var before = Deadline(); CombatLockout.Update(); Check(Deadline() > before, "WC sustained defensive fire extends cooldown");
        wc.State(coreGun, 0).Shooting = false; ExpireObservedCombat();
        wc.State(coreGun, 3).Shots++; CombatLockout.Update();
        Check(CombatLockout.IsBlocked, "WC single shot between updates is detected on nonzero part ID");
        ExpireObservedCombat(); wc.Hit(new MySlimBlock { CubeGrid = grid }, 10);
        Check(CombatLockout.IsBlocked, "WC projectile damage to selected ship blocks");
        ExpireObservedCombat(); wc.Hit(MySession.Static.LocalCharacter, 1);
        Check(!CombatLockout.IsBlocked, "WC projectile damage to character does not create a timeout");
        ExpireObservedCombat(); wc.Hit(new MySlimBlock { CubeGrid = grid }, 0);
        Check(!CombatLockout.IsBlocked, "zero WC hit damage is ignored");
        var unrelated = new MyCubeGrid(); MyEntities.Add(unrelated);
        wc.Hit(new MySlimBlock { CubeGrid = unrelated }, 5);
        Check(!CombatLockout.IsBlocked, "unrelated WC hit does not signal nearby enemies");
        Check(!CombatLockout.CanConnect(unrelated), "WC damage before port selection is remembered");
        CombatLockout.Clear();
        Check(wc.ObserverCount == 0 && !wc.DamageRegistered && MyAPIGateway.Utilities.Handlers.Count == 0, "WC observers, damage handler and message callback detached");

        grid = World(); wc = new FakeWeaponCore(); wc.RespondOnRequest();
        coreGun = new CoreGun(); grid.Add(coreGun); StartObserved();
        Check(!CombatLockout.CanConnect(grid), "known WC weapon with late component initialization rejects link");
        wc.Add(coreGun, 0); CombatLockout.Update(); ExpireObservedCombat(); CombatLockout.Update();
        Check(CombatLockout.CanConnect(grid), "late WC component registration recovers without reloading");
        wc.ThrowPolling = true; CombatLockout.Update(); Check(CombatLockout.IsBlocked, "broken selected WC observer fails closed");
        wc.ThrowPolling = false; CombatLockout.Update(); Check(CombatLockout.CanConnect(grid), "transient WC observer failure can recover");

        grid = World(); wc = new FakeWeaponCore(); wc.RespondOnRequest();
        var support = new CoreGun(); support.BlockDefinition.Id = new VRage.Game.MyDefinitionId("SupportComponent");
        grid.Add(support); wc.Add(support, 0); wc.SupportEntities.Add(support);
        StartObserved(); Check(CombatLockout.CanConnect(grid), "non-weapon WC support component does not block link");

        grid = World(); wc = new FakeWeaponCore(); wc.RespondOnRequest();
        coreGun = new CoreGun(); grid.Add(coreGun); wc.Add(coreGun, 0); StartObserved(); CombatLockout.CanConnect(grid);
        wc.ThrowCleanup = true; CombatLockout.Clear();
        Check(MyAPIGateway.Utilities.Handlers.Count == 0 && grid.Subscribers == 0, "WC-first unload still detaches local message and grid handlers");

        grid = World(); wc = new FakeWeaponCore(); wc.RespondOnRequest();
        var hand = new CoreHand(); wc.Add(hand, 0); MySession.Static.LocalCharacter.CurrentWeapon = hand;
        StartObserved(); ExpireObservedCombat(); CombatLockout.Update();
        Check(CombatLockout.CanConnect(grid), "quiet WC handheld weapon is immediately available");
        wc.Event(hand, 0, 1, true); Check(CombatLockout.IsBlocked, "WC handheld fire triggers player cooldown");

        grid = World(); wc = new FakeWeaponCore(); wc.RespondOnRequest(); coreGun = new CoreGun(); grid.Add(coreGun); wc.Add(coreGun, 0);
        StartObserved(); CombatLockout.CanConnect(grid); ExpireObservedCombat();
        grid.Remove(coreGun); Check(wc.ObserverCount == 0, "removed WC gun unsubscribed");
        wc.Event(coreGun, 0, 1, true); Check(!CombatLockout.IsBlocked, "removed WC gun cannot trigger stale callbacks");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        var docked = new MyCubeGrid(); MyEntities.Add(docked);
        MyAPIGateway.GridGroups.LogicalGroups[grid] = new List<IMyCubeGrid> { grid, docked };
        CombatLockout.Update(); docked.Damage(10);
        Check(CombatLockout.IsBlocked, "connector-docked damage blocks supplying station link");

        grid = World(); wc = new FakeWeaponCore(); wc.RespondOnRequest();
        MySession.Static.Mods.Add(new VRage.Game.MyObjectBuilder_Checkpoint.ModItem { PublishedFileId = 2815514917 });
        grid.Speed = 500; StartObserved();
        Check(CombatLockout.CanConnect(grid), "SDX2 has no speed restriction");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid); grid.Damage(10);
        Check(MyAPIGateway.Utilities.Files.ContainsKey(CombatCooldownStore.FileName), "combat deadline persisted immediately");
        CombatLockout.Clear(); StartObserved();
        Check(!CombatLockout.CanConnect(grid), "Nexus/session transition preserves cooldown");
        MyAPIGateway.Multiplayer.ServerId++;
        CombatLockout.Update();
        Check(!CombatLockout.CanConnect(grid) && grid.Subscribers == 7 && MyAPIGateway.Utilities.Handlers.Count == 1,
            "server replacement rebinds monitoring without clearing cooldown or duplicating handlers");
        MySession.Static = new MySession();
        CombatLockout.Update();
        Check(!CombatLockout.CanConnect(grid) && grid.Subscribers == 7,
            "session replacement rebinds monitoring and retains cooldown");
        CombatLockout.Clear(); ResetTimer(); StartObserved();
        Check(!CombatLockout.CanConnect(grid), "persisted cooldown survives simulated process restart");
        Check(Deadline() <= Stopwatch.GetTimestamp() + (CombatLockout.CooldownSeconds + 1) * Stopwatch.Frequency, "restored timer clamps to conservative maximum");
        CombatLockout.Clear(); ResetTimer();
        MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName] = "2:0";
        StartObserved(); Check(CombatLockout.CanConnect(grid), "expired persisted cooldown permits use");
        CombatLockout.Clear(); ResetTimer(); MyAPIGateway.Utilities.Files[CombatCooldownStore.FileName] = "truncated";
        StartObserved(); Check(!CombatLockout.CanConnect(grid), "corrupt deadline starts conservative cooldown");

        grid = World(); StartObserved(); CombatLockout.CanConnect(grid);
        grid.Damage(1); var writes = MyAPIGateway.Utilities.Writes;
        for (int i = 0; i < 100; i++) grid.Damage(1);
        Check(MyAPIGateway.Utilities.Writes == writes, "sustained activity uses write-ahead rather than per-hit disk writes");

        grid = World(); MyAPIGateway.Utilities.FailWrites = true; StartObserved(); CombatLockout.CanConnect(grid);
        grid.Damage(10); Check(CombatLockout.IsBlocked, "persistence write failure fails closed without propagating into damage event");
        MyAPIGateway.Utilities.FailWrites = false;

        grid = World(); MyAPIGateway.Utilities.FailReads = true; StartObserved();
        Check(CombatLockout.IsBlocked, "persistence read failure fails closed");
        MyAPIGateway.Utilities.FailReads = false;

        grid = World(); StartObserved();
        var invalid = new Dictionary<string, Delegate> { ["GetCoreWeapons"] = new Action(() => { }) };
        MyAPIGateway.Utilities.Broadcast(invalid);
        Check(CombatLockout.IsBlocked, "incompatible WC API fails closed");
    }

    private static int CoreWatcherCount()
    {
        var dictionary = (System.Collections.IDictionary)typeof(CombatLockout).GetField("grids", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        int count = 0;
        foreach (var activity in dictionary.Values)
            count += ((System.Collections.IDictionary)activity.GetType().GetField("coreWeapons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(activity)).Count;
        return count;
    }

    private static void PollingOptimizationTests()
    {
        var grid = World();
        for (int i = 0; i < 5000; i++) grid.Add(new FunctionalBlock());
        StartObserved();
        Check(CoreWatcherCount() == 0, "polling: vanilla worlds create no WC block watchers");
        var wc = new FakeWeaponCore();
        var gun = new CoreGun(); grid.Add(gun); wc.Add(gun, 0);
        var remote = new MyCubeGrid(); var remoteGun = new CoreGun(); remote.Add(remoteGun); wc.Add(remoteGun, 3); MyEntities.Add(remote);
        Check(CoreWatcherCount() == 0, "polling: blocks added before the API create no WC watchers");
        wc.Respond(); CombatLockout.Update();
        Check(CoreWatcherCount() == 2 && wc.ObserverCount == 2, "polling: late API discovers weapons on selected and unselected grids");
        Check(wc.HasCalls == 2 && wc.MapCalls == 2, "polling: 5000 ordinary functional blocks never query WC components or maps");
        ExpireObservedCombat(); CombatLockout.CanConnect(grid);
        int hasCalls = wc.HasCalls, mapCalls = wc.MapCalls, shotCalls = wc.ShotCalls;
        for (int i = 0; i < 20; i++) CombatLockout.Update();
        Check(wc.HasCalls == hasCalls && wc.MapCalls == mapCalls, "polling: steady-state ordinary blocks and initialized guns need no classification probes");
        Check(wc.ShotCalls == shotCalls + 40, "polling: both real weapons still check shot counters every update without a selected remote link");
        wc.State(remoteGun, 3).Shots++; CombatLockout.Update();
        Check(!CombatLockout.IsBlocked, "polling: remote gunfire is not a nearby-enemy signal");
        Check(!CombatLockout.CanConnect(remote), "polling: remote single-shot history survives switching targets");
        ExpireObservedCombat(); CombatLockout.CanConnect(grid);
        var added = new CoreGun(); wc.Add(added, 7); grid.Add(added); CombatLockout.Update();
        Check(CoreWatcherCount() == 3 && wc.ObserverCount == 3 && !CombatLockout.IsBlocked, "polling: idle post-API weapon additions remain ready");
        ExpireObservedCombat(); wc.Event(added, 7, 8, true);
        Check(CombatLockout.IsBlocked, "polling: newly added WC weapon charging callback remains immediate");
        grid.Remove(added);
        Check(CoreWatcherCount() == 2 && wc.ObserverCount == 2, "polling: filtered watcher removal still detaches callbacks");
        CombatLockout.Clear();
        Check(wc.ObserverCount == 0, "polling: filtered world teardown detaches all observers");

        grid = World(); wc = new FakeWeaponCore { MapUnavailable = true }; wc.RespondOnRequest();
        gun = new CoreGun(); grid.Add(gun); wc.Add(gun, 0); StartObserved(); CombatLockout.CanConnect(grid);
        for (int i = 0; i < 20; i++) CombatLockout.Update();
        Check(CombatLockout.IsBlocked && wc.MapInstances.Count == 1, "polling: pending maps reuse scratch storage and stay fail-closed");
        wc.MapUnavailable = false; CombatLockout.Update();
        Check(wc.ObserverCount == 1 && !CombatLockout.IsBlocked, "polling: late idle map initialization registers observers without a cooldown");
        ExpireObservedCombat();
        Check(CombatLockout.CanConnect(grid), "polling: initialized late-map weapon recovers normally");

        grid = World(); wc = new FakeWeaponCore(); wc.RespondOnRequest();
        foreach (var name in new[] { "CoreSupport", "CoreUpgrade" })
        {
            var module = new FunctionalBlock(); module.BlockDefinition.Id = new VRage.Game.MyDefinitionId(name);
            wc.ExtraDefinitions.Add(module.BlockDefinition.Id); wc.Add(module, 0); wc.SupportEntities.Add(module); grid.Add(module);
        }
        StartObserved(); CombatLockout.CanConnect(grid);
        for (int i = 0; i < 20; i++) CombatLockout.Update();
        Check(CoreWatcherCount() == 0 && wc.HasCalls == 0 && wc.MapCalls == 0 && wc.ObserverCount == 0,
            "polling: definition-listed support and upgrade cores need no weapon observer or map");
        Check(CombatLockout.CanConnect(grid), "polling: support and upgrade cores do not indefinitely deny linking");
        foreach (var block in grid.CubeBlocks)
        {
            CoreSystems.Session.I.ActiveEwarCubes[block.FatBlock.EntityId] = new object();
            CombatLockout.Update();
            Check(CombatLockout.IsBlocked, "polling: EMP observation still covers " + block.FatBlock.BlockDefinition.Id.Value);
            CoreSystems.Session.I.ActiveEwarCubes.Clear(); ExpireObservedCombat(); CombatLockout.Update();
            Check(CombatLockout.CanConnect(grid), "polling: cleared module EMP releases after cooldown");
        }

        grid = EffectWorld(false); wc = new FakeWeaponCore(); wc.RespondOnRequest();
        for (int i = 0; i < 500; i++)
        {
            var block = new FunctionalBlock(); grid.Add(block);
            CoreSystems.Session.I.ActiveEwarCubes[block.EntityId] = new object();
            CoreSystems.Session.I.EffectedCubes[block.EntityId] = new object();
        }
        StartObserved(); ExpireObservedCombat(); CombatLockout.CanConnect(grid);
        int groupCalls = MyAPIGateway.GridGroups.Calls;
        CombatLockout.Update();
        Check(CombatLockout.IsBlocked, "polling: EWAR on non-weapon blocks still blocks link");
        Check(MyAPIGateway.GridGroups.Calls == groupCalls + 2, "polling: 1000 EWAR entries perform one mechanical/logical construct refresh per update");
        long previous = Deadline(); CombatLockout.Update();
        Check(Deadline() > previous, "polling: continuing batched EWAR extends the cooldown every update");
        var other = new MyCubeGrid(); var otherBlock = new FunctionalBlock(); other.Add(otherBlock); MyEntities.Add(other);
        CoreSystems.Session.I.ActiveEwarCubes.Clear(); CoreSystems.Session.I.EffectedCubes.Clear(); ExpireObservedCombat();
        CoreSystems.Session.I.ActiveEwarCubes[otherBlock.EntityId] = new object(); CombatLockout.Update();
        Check(!CombatLockout.IsBlocked, "polling: remote EWAR does not alert the selected grid");
        CoreSystems.Session.I.ActiveEwarCubes.Clear();
        Check(!CombatLockout.CanConnect(other), "polling: remote batched EWAR history prevents switching-target bypass");
        CoreSystems.Session.I.ActiveEwarCubes.Clear(); ExpireObservedCombat();
        Check(CombatLockout.CanConnect(other), "polling: cleared EWAR releases after cooldown");
        CombatLockout.Clear();

        // EMP pulse coverage must not depend on WC watcher membership.
        grid = EffectWorld(); var functional = new FunctionalBlock(); grid.Add(functional); StartObserved(); CombatLockout.CanConnect(grid);
        Check(CoreWatcherCount() == 0, "polling: Vanilla+ ordinary functional block needs no WC watcher");
        var missile = new VanillaPlusFramework.Missiles.VPFMissile(default, new VanillaPlusFramework.Utilities.EmpStats());
        missile.Close(); Check(CombatLockout.IsBlocked, "polling: EMP pulse on a non-weapon functional block still blocks");
        ExpireObservedCombat(); grid.Remove(functional); ExpireObservedCombat();
        missile = new VanillaPlusFramework.Missiles.VPFMissile(default, new VanillaPlusFramework.Utilities.EmpStats());
        missile.Close(); Check(!CombatLockout.IsBlocked, "polling: removed functional blocks leave no stale EMP coverage");
        CombatLockout.Clear();
    }

    private static void WeaponDefinitionClassificationTests()
    {
        foreach (var kind in new[] { "Static", "Turret" })
        {
            var grid = World(); var wc = new FakeWeaponCore(); wc.RespondOnRequest();
            var gun = new CoreGun(); gun.BlockDefinition.Id = new VRage.Game.MyDefinitionId("Only" + kind);
            (kind == "Static" ? wc.StaticDefinitions : wc.TurretDefinitions).Add(gun.BlockDefinition.Id);
            grid.Add(gun);
            StartObserved();
            Check(!CombatLockout.CanConnect(grid), "classification: " + kind + " definition without core remains unavailable");
            wc.Add(gun, 7); wc.MapUnavailable = true; CombatLockout.Update(); ExpireObservedCombat();
            Check(!CombatLockout.CanConnect(grid), "classification: " + kind + " definition without weapon map remains unavailable");
            wc.MapUnavailable = false; CombatLockout.Update();
            Check(wc.ObserverCount == 1 && !CombatLockout.IsBlocked, "classification: " + kind + " idle late initialization is immediately ready");
            ExpireObservedCombat();
            Check(CombatLockout.CanConnect(grid), "classification: " + kind + " quiet initialized weapon allows linking");
            wc.Event(gun, 7, 8, true);
            Check(CombatLockout.IsBlocked, "classification: " + kind + " charging callback blocks immediately");
            ExpireObservedCombat(); wc.Event(gun, 7, 1, true);
            Check(CombatLockout.IsBlocked, "classification: " + kind + " firing callback blocks immediately");
            ExpireObservedCombat(); wc.State(gun, 7).Shooting = true; CombatLockout.Update();
            Check(CombatLockout.IsBlocked, "classification: " + kind + " firing poll blocks");
            wc.State(gun, 7).Shooting = false; ExpireObservedCombat(); wc.State(gun, 7).Shots++; CombatLockout.Update();
            Check(CombatLockout.IsBlocked, "classification: " + kind + " single-shot counter still blocks");
        }

        var rifleGrid = World(); var provider = new FakeWeaponCore(); provider.RespondOnRequest();
        var rifle = new CoreRifle(); provider.RifleDefinitions.Add(rifle.PhysicalItemId);
        MySession.Static.LocalCharacter.CurrentWeapon = rifle;
        StartObserved();
        Check(!CombatLockout.CanConnect(rifleGrid), "classification: rifle-only definition without core remains unavailable");
        provider.Add(rifle, 0); CombatLockout.Update();
        Check(provider.ObserverCount == 1 && !CombatLockout.IsBlocked, "classification: idle rifle late initialization is immediately ready");
        ExpireObservedCombat();
        Check(CombatLockout.CanConnect(rifleGrid), "classification: quiet initialized rifle allows linking");
        provider.Event(rifle, 0, 8, true);
        Check(CombatLockout.IsBlocked, "classification: rifle charging callback remains active");
        ExpireObservedCombat(); provider.Event(rifle, 0, 1, true);
        Check(CombatLockout.IsBlocked, "classification: rifle firing callback remains active");
        ExpireObservedCombat(); provider.State(rifle, 0).Shots++; CombatLockout.Update();
        Check(CombatLockout.IsBlocked, "classification: rifle single-shot counter remains active");
        ExpireObservedCombat();
        var removedRifle = new CoreRifle { Closed = true };
        MySession.Static.LocalCharacter.CurrentWeapon = removedRifle;
        MyEntities.RemoveEntity(removedRifle); MySession.Static.LocalCharacter.CurrentWeapon = null;
        Check(!CombatLockout.IsBlocked, "classification: a closed rifle definition alone invents no recent firing evidence");

        foreach (var method in new[] { "GetCoreStaticLaunchers", "GetCoreTurrets", "GetCoreRifles" })
        {
            World(); provider = new FakeWeaponCore { MissingMethod = method }; provider.RespondOnRequest(); StartObserved();
            Check(CombatLockout.IsBlocked && !provider.DamageRegistered, "classification: missing " + method + " fails closed");
            World(); provider = new FakeWeaponCore { IncompatibleMethod = method }; provider.RespondOnRequest(); StartObserved();
            Check(CombatLockout.IsBlocked && !provider.DamageRegistered, "classification: incompatible " + method + " fails closed");
        }
    }

    private static MyCubeGrid EffectWorld(bool plus = true)
    {
        var grid = World();
        if (plus)
        {
            MySession.Static.Mods.Add(new VRage.Game.MyObjectBuilder_Checkpoint.ModItem { PublishedFileId = 2915780227 });
            VanillaPlusFramework.VanillaPlusFramework.I = new VanillaPlusFramework.VanillaPlusFramework();
            VanillaPlusFramework.BeamLogic.Beams.I = new VanillaPlusFramework.BeamLogic.Beams();
        }
        return grid;
    }

    private static void EffectTests()
    {
        var grid = EffectWorld(false); var block = new FunctionalBlock(); grid.Add(block);
        StartObserved(); Check(CombatLockout.CanConnect(grid), "absent electronic-warfare frameworks leave vanilla usable");
        block.Enabled = false; block.JumpCharge = 0; CombatLockout.Update();
        Check(!CombatLockout.IsBlocked, "normal block switches and jump charge consumption are not combat signals");

        grid = EffectWorld(); block = new FunctionalBlock(); grid.Add(block);
        StartObserved(); Check(CombatLockout.CanConnect(grid), "Vanilla+ optional observer binds without a warmup lockout");
        block.Enabled = false; block.JumpCharge = 0; CombatLockout.Update();
        Check(!CombatLockout.IsBlocked, "ordinary switches/jump use remain allowed with Vanilla+ installed");
        block.Enabled = true;
        var missile = new VanillaPlusFramework.Missiles.VPFMissile(default, new VanillaPlusFramework.Utilities.EmpStats());
        missile.Close(); Check(CombatLockout.IsBlocked && missile.ClosedForTest, "client-observed EMP missile detonation locks out and preserves original method");
        ExpireObservedCombat(); missile.Close(); Check(!CombatLockout.IsBlocked, "duplicate missile close does not produce another EMP signal");

        missile = new VanillaPlusFramework.Missiles.VPFMissile(new VRageMath.Vector3D(100, 0, 0), new VanillaPlusFramework.Utilities.EmpStats());
        missile.Close(); Check(!CombatLockout.IsBlocked, "EMP detonation beyond functional block radius is not a proximity detector");
        missile = new VanillaPlusFramework.Missiles.VPFMissile(default, new VanillaPlusFramework.Utilities.EmpStats()) { IsDisarmed = true };
        missile.Close(); Check(!CombatLockout.IsBlocked, "disarmed EMP missile ignored");
        missile = new VanillaPlusFramework.Missiles.VPFMissile(default, new VanillaPlusFramework.Utilities.EmpStats { EMP_TimeDisabled = 0 });
        missile.Close(); Check(!CombatLockout.IsBlocked, "zero-duration EMP ignored");

        missile = new VanillaPlusFramework.Missiles.VPFMissile(default, new VanillaPlusFramework.Utilities.EmpStats(), grid);
        missile.Close(); Check(!CombatLockout.IsBlocked, "friendly-fire-disabled EMP excludes launching construct");
        missile = new VanillaPlusFramework.Missiles.VPFMissile(default, new VanillaPlusFramework.Utilities.EmpStats { EMP_AllowFriendlyFire = true }, grid);
        missile.Close(); Check(CombatLockout.IsBlocked, "friendly-fire-enabled EMP still locks out own construct");

        ExpireObservedCombat(); var before = VanillaPlusFramework.Utilities.FrameworkUtilities.JumpHits;
        VanillaPlusFramework.Utilities.FrameworkUtilities.JDI_Hit(grid, 100, true, null, false);
        Check(CombatLockout.IsBlocked && VanillaPlusFramework.Utilities.FrameworkUtilities.JumpHits == before + 1,
            "jump-inhibition hit locks out without altering original operation");
        ExpireObservedCombat(); VanillaPlusFramework.Utilities.FrameworkUtilities.JDI_Hit(grid, 0, true, null, false);
        Check(!CombatLockout.IsBlocked, "zero-strength jump-inhibition ignored");
        VanillaPlusFramework.Utilities.FrameworkUtilities.JDI_Hit(grid, 100, true, grid, false);
        Check(!CombatLockout.IsBlocked, "JDI friendly-fire guard respected");
        var other = new MyCubeGrid(); MyEntities.Add(other);
        VanillaPlusFramework.Utilities.FrameworkUtilities.JDI_Hit(other, 100, true, null, false);
        Check(!CombatLockout.IsBlocked, "unrelated grid JDI hit does not trigger link warning");

        var beam = new VanillaPlusFramework.BeamLogic.BeamDefinition { EMP_Stats = new VanillaPlusFramework.Utilities.EmpStats() };
        VanillaPlusFramework.BeamLogic.Beams.I.GenerateBeam(default, default, null, ref beam, other);
        Check(CombatLockout.IsBlocked, "client-observed EMP beam endpoint locks out");
        ExpireObservedCombat(); beam.EMP_Stats = null;
        VanillaPlusFramework.BeamLogic.Beams.I.GenerateBeam(default, default, null, ref beam, other);
        Check(!CombatLockout.IsBlocked, "non-EMP beam does not cause electronic-warfare lockout");
        beam.EMP_Stats = new VanillaPlusFramework.Utilities.EmpStats(); beam.Throw = true; bool threw = false;
        try { VanillaPlusFramework.BeamLogic.Beams.I.GenerateBeam(default, default, null, ref beam, other); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw && !CombatLockout.IsBlocked, "beam observer preserves exception and does not invent a hit");
        beam.Throw = false; beam.EMP_Stats = null;
        VanillaPlusFramework.BeamLogic.Beams.I.GenerateBeam(default, default, null, ref beam, other);
        Check(!CombatLockout.IsBlocked, "beam context restored after exception");

        MyAPIGateway.Multiplayer.IsServer = true;
        VanillaPlusFramework.Utilities.FrameworkUtilities.ApplyEMP(block, 60);
        Check(CombatLockout.IsBlocked && !block.Enabled, "host EMP disable observed without changing effect");
        ExpireObservedCombat(); CombatLockout.Update();
        Check(CombatLockout.IsBlocked, "active host EMP state refreshes cooldown");
        var callbackList = VanillaPlusFramework.BeamLogic.Beams.I.OnBeamEndList;
        CombatLockout.Clear(); Check(callbackList.Count == 0, "optional beam callback detached on unload");
        ResetTimer(); MyAPIGateway.Utilities.Files.Clear();
        missile = new VanillaPlusFramework.Missiles.VPFMissile(default, new VanillaPlusFramework.Utilities.EmpStats());
        missile.Close(); Check(!DeadlineActive(), "optional missile patches removed on unload");

        grid = EffectWorld(false); var wc = new FakeWeaponCore(); wc.RespondOnRequest(); block = new FunctionalBlock(); grid.Add(block);
        StartObserved(); CombatLockout.CanConnect(grid);
        CoreSystems.Session.I.ActiveEwarCubes[block.EntityId] = new object(); CombatLockout.Update();
        Check(CombatLockout.IsBlocked, "WeaponCore synchronized client EMP/jump-null state locks out without damage");
        ExpireObservedCombat(); CombatLockout.Update(); Check(CombatLockout.IsBlocked, "continuing synchronized effect refreshes cooldown");
        CoreSystems.Session.I.ActiveEwarCubes.Clear(); ExpireObservedCombat(); CombatLockout.Update();
        Check(CombatLockout.CanConnect(grid), "effect removal allows cooldown expiration");
        var remote = new MyCubeGrid(); var remoteBlock = new FunctionalBlock(); remote.Add(remoteBlock); MyEntities.Add(remote);
        CoreSystems.Session.I.ActiveEwarCubes[remoteBlock.EntityId] = new object(); CombatLockout.Update();
        Check(!CombatLockout.IsBlocked, "synchronized effects on unrelated grids do not reveal nearby combat");
        CoreSystems.Session.I.ActiveEwarCubes.Clear();
        CoreSystems.Session.I.EffectedCubes[block.EntityId] = new object(); CombatLockout.Update();
        Check(CombatLockout.IsBlocked, "WeaponCore server-side electronic warfare state observed");

        grid = EffectWorld(false); MySession.Static.Mods.Add(new VRage.Game.MyObjectBuilder_Checkpoint.ModItem { PublishedFileId = 2915780227 });
        StartObserved(); Check(CombatLockout.IsBlocked, "present but uninitialized Vanilla+ effect integration fails closed");

        grid = EffectWorld(); wc = new FakeWeaponCore(); wc.RespondOnRequest();
        MySession.Static.Mods.Add(new VRage.Game.MyObjectBuilder_Checkpoint.ModItem { PublishedFileId = 3154371364 });
        VanillaPlusFramework.VanillaPlusFramework.I = null; VanillaPlusFramework.BeamLogic.Beams.I = null;
        StartObserved(); Check(CombatLockout.CanConnect(grid), "Vanilla+ disabled by WeaponCore does not block otherwise healthy WC world");
    }
    private static bool DeadlineActive() { return Deadline() > Stopwatch.GetTimestamp(); }
}

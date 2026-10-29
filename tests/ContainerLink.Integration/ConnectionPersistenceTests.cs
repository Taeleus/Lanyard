using System;
using System.IO;
using System.Linq;
using Lanyard;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.Entity.UseObject;
using VRageRender.Import;

internal static partial class Program
{
    private sealed class RecoveryWorld : IDisposable
    {
        private readonly string previousPath = MyFileSystem.UserDataPath;
        public readonly string StorageRoot = Path.Combine(AppContext.BaseDirectory, "test-state", Guid.NewGuid().ToString("N"));
        public readonly World Original;
        public readonly MySession OriginSession;
        public readonly LinkConnectionStore Store = new LinkConnectionStore();
        public readonly LinkConnectionState State;
        public MyCubeBlock Replica;
        public MyCubeGrid ReplicaGrid;
        public InventoryPort ExactPort;
        public MyUseObjectsComponentBase Ports;
        public string StateFile => Path.Combine(StorageRoot, "Storage", "Lanyard.LinkConnection.v1.42.xml");

        public RecoveryWorld()
        {
            MyFileSystem.UserDataPath = StorageRoot;
            Original = new World(true);
            OriginSession = MySession.Static;
            var originalPorts = new MyUseObjectsComponentBase();
            originalPorts.Ports.Add(GetTargetedBlock.selectedObject);
            Original.Selected.Components.Set(originalPorts);
            MyEntities.Entities[Original.Selected.EntityId] = Original.Selected;
            CombatLockout.ResumeToCapture = new CombatResumeState { GridIds = new[] { Original.Grid.EntityId } };
            LinkRecovery.Remember();
            State = Store.Load(42);
            if (State == null) throw new InvalidOperationException("Recovery fixture must save its exact selected port.");
        }

        public void Suspend()
        {
            LinkController.SuspendForWorldChange();
            MyEntities.Entities.Clear();
            MySession.Static = null;
        }

        public void Rejoin(bool publish = true, bool character = true, ulong account = 42, long identity = 1, Guid? world = null)
        {
            Suspend();
            MySession.Static = new MySession { LocalCharacter = character ? new MyCharacter() : null,
                WorldId = world ?? Guid.NewGuid() };
            MySession.Static.LocalHumanPlayer.Id.SteamId = account;
            MySession.Static.LocalHumanPlayer.Identity.IdentityId = identity;
            if (MySession.Static.LocalCharacter != null)
            {
                MySession.Static.LocalCharacter.Inventory.Owner = MySession.Static.LocalCharacter;
                MySession.Static.LocalCharacter.EquippedTool = new MyAngleGrinder();
                MySession.Static.LocalCharacter.HandItemDefinition = new HandItemDefinition
                    { Id = new MyDefinitionId("MyObjectBuilder_PhysicalGunObject", "AngleGrinderItem") };
            }
            MyAPIGateway.Session = new GatewaySession();
            MyAPIGateway.Multiplayer = new Multiplayer { ServerId = MyAPIGateway.Multiplayer.ServerId + 1 };
            ReplicaGrid = new MyCubeGrid { EntityId = Original.Grid.EntityId };
            Replica = new MyCubeBlock { EntityId = Original.Selected.EntityId, OwnerId = Original.Selected.OwnerId, CubeGrid = ReplicaGrid };
            Replica.BlockDefinition.Id = Original.Selected.BlockDefinition.Id;
            Replica.Inventories[0].Owner = Replica;
            ExactPort = new InventoryPort { Owner = Replica, Dummy = new MyModelDummy { Name = State.DummyName } };
            Ports = new MyUseObjectsComponentBase();
            Ports.Ports.Add(ExactPort);
            Replica.Components.Set(Ports);
            if (publish) Publish();
        }

        public void Publish() { MyEntities.Entities[Replica.EntityId] = Replica; }

        public void Dispose()
        {
            try { LinkController.Disconnect(); LinkController.SuspendForWorldChange(); }
            finally
            {
                MyFileSystem.UserDataPath = previousPath;
                MyEntities.Entities.Clear();
                var allowed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-state")) + Path.DirectorySeparatorChar;
                var target = Path.GetFullPath(StorageRoot);
                if (!target.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup target.");
                if (Directory.Exists(target)) Directory.Delete(target, true);
            }
        }
    }

    private static void ConnectionPersistenceTests()
    {
        ConnectionStoreBoundaries();
        DelayedExactPortRecovery();
        RecoveryIsolationAndTombstones();
        RecoveryCallbacksAndPendingHops();
        RecoveryNativeLifecycle();
    }

    private static void ConnectionStoreBoundaries()
    {
        using (var fixture = new RecoveryWorld())
        {
            var saved = fixture.Store.Load(42);
            Check(saved.BlockId == fixture.Original.Selected.EntityId && saved.OwnerId == 1 && saved.DummyName == "Inventory" &&
                saved.Resume.GridIds.SequenceEqual(new[] { fixture.Original.Grid.EntityId }), "saved link: actual store roundtrips the exact fingerprint and observer history");
            File.WriteAllText(fixture.StateFile + ".interrupted.tmp", "<incomplete");
            Check(fixture.Store.Load(42)?.BlockId == saved.BlockId, "saved link: an interrupted temporary write is never treated as a committed connection");
            using (var locked = new FileStream(fixture.StateFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                saved.DummyName = "Changed";
                var failed = false;
                try { fixture.Store.Save(saved); } catch (IOException) { failed = true; }
                Check(failed, "saved link: unavailable atomic replacement reports write failure");
            }
            Check(fixture.Store.Load(42).DummyName == "Inventory", "saved link: failed replacement leaves the prior complete record intact");
            var anotherAccount = Path.Combine(Path.GetDirectoryName(fixture.StateFile), "Lanyard.LinkConnection.v1.99.xml");
            File.Copy(fixture.StateFile, anotherAccount);
            Check(fixture.Store.Load(99) == null, "saved link: an account-mismatched record is rejected");
            fixture.Store.Forget(42);
            Check(fixture.Store.Load(42) == null && File.ReadAllText(fixture.StateFile).Contains("Unlinked=\"true\""),
                "saved link: unlink writes a durable tombstone instead of falling back to a stale record");
            foreach (var corrupt in new[] { "", "<LanyardLink", "<Wrong />", "<!DOCTYPE LanyardLink [<!ENTITY x SYSTEM 'file:///invalid'>]><LanyardLink Version='1' />" })
            {
                File.WriteAllText(fixture.StateFile, corrupt);
                Check(fixture.Store.Load(42) == null, "saved link: incomplete, invalid or DTD-bearing data cannot restore a target");
            }
        }
    }

    private static void DelayedExactPortRecovery()
    {
        using (var fixture = new RecoveryWorld())
        {
            fixture.Rejoin(publish: false, character: false);
            Tick(40);
            Check(GetTargetedBlock.selectedBlock == null && MyInventory.Transfers.Count == 0,
                "rejoin: absent character and entity cannot issue transfers");
            MySession.Static.LocalCharacter = new MyCharacter();
            Tick(40);
            Check(LinkRecovery.IsPending && GetTargetedBlock.selectedBlock == null && fixture.Store.Load(42) != null,
                "rejoin: missing target replication retains a pending descriptor");
            fixture.Publish();
            fixture.Ports.Ports.Clear();
            var decoy = new InventoryPort { Owner = fixture.Replica, Dummy = new MyModelDummy { Name = "OtherPort" } };
            fixture.Ports.Ports.Add(decoy);
            Tick(60);
            Check(LinkRecovery.IsPending && GetTargetedBlock.selectedBlock == null,
                "rejoin: a loaded decoy port cannot substitute for the exact saved dummy");
            fixture.Ports.Ports.Add(fixture.ExactPort);
            Tick(30);
            Check(ReferenceEquals(GetTargetedBlock.selectedBlock, fixture.Replica) && ReferenceEquals(GetTargetedBlock.selectedObject, fixture.ExactPort),
                "rejoin: newly replicated exact port restores the new entity instance");
            Check(MySession.Static.WorldId != fixture.State.OriginWorldId && fixture.Replica.EntityId == fixture.State.BlockId,
                "rejoin: an ID-preserving world handoff is allowed only through the saved fingerprint");
            Check(CombatLockout.StartedResume.GridIds.Contains(fixture.State.Resume.GridIds.Single()),
                "rejoin: validated observer history is passed to the actual resume entry point");
        }

        foreach (var transient in new[] { "entity", "grid" })
        {
            using (var fixture = new RecoveryWorld())
            {
                fixture.Rejoin();
                if (transient == "entity") fixture.Replica.MarkedForClose = true;
                else fixture.ReplicaGrid.MarkedForClose = true;
                Tick(30);
                Check(LinkRecovery.IsPending && GetTargetedBlock.selectedBlock == null && fixture.Store.Load(42) != null,
                    "rejoin: temporarily closing replicated " + transient + " remains pending without forgetting");
                fixture.Replica.MarkedForClose = false;
                fixture.ReplicaGrid.MarkedForClose = false;
                Tick(30);
                Check(ReferenceEquals(GetTargetedBlock.selectedBlock, fixture.Replica),
                    "rejoin: stable replacement " + transient + " restores the exact saved target");
            }
        }

        using (var fixture = new RecoveryWorld())
        {
            CombatLockout.IsBlocked = true;
            fixture.Rejoin();
            MySession.Static.LocalCharacter.Inventory.Add(Component, "SteelPlate");
            MySession.Static.LocalCharacter.EquippedTool = new MyAngleGrinder { IsShooting = true };
            Tick(60);
            Check(ReferenceEquals(GetTargetedBlock.selectedObject, fixture.ExactPort) && LinkController.IsCombatPaused && MyInventory.Transfers.Count == 0,
                "rejoin: restored link remains paused throughout the existing combat restriction");
            CombatLockout.IsBlocked = false;
            Tick(1);
            Check(MyInventory.Transfers.Count == 1 && MyInventory.Transfers[0].Destination == fixture.Replica.Inventories[0],
                "rejoin: current active tool resumes through the same selected inventory after cooldown clears");
        }
    }

    private static void RecoveryIsolationAndTombstones()
    {
        using (var fixture = new RecoveryWorld())
        {
            fixture.Rejoin(publish: false);
            Tick(1);
            LinkController.Disconnect();
            fixture.Publish();
            Tick(60);
            Check(GetTargetedBlock.selectedBlock == null && !LinkRecovery.IsPending && fixture.Store.Load(42) == null,
                "rejoin tombstone: manual unlink cancels a still-pending replicated target");
        }
        using (var fixture = new RecoveryWorld())
        {
            fixture.Rejoin(identity: 2);
            Tick(40);
            Check(GetTargetedBlock.selectedBlock == null && CombatLockout.StartedResume == null && fixture.Store.Load(42).IdentityId == 1,
                "rejoin isolation: foreign principal receives no link or observer exemption and does not erase the origin record");
            CombatLockout.IsBlocked = true;
            GetTargetedBlock.GetPort();
            Check(fixture.Store.Load(42).IdentityId == 1, "rejoin isolation: blocked manual connection attempt cannot erase a dormant origin record");
            CombatLockout.IsBlocked = false;
            fixture.Rejoin(identity: 1, world: fixture.State.OriginWorldId);
            Tick(30);
            Check(ReferenceEquals(GetTargetedBlock.selectedBlock, fixture.Replica), "rejoin isolation: returning to the origin principal restores the saved target");
            fixture.Rejoin(account: 99);
            Tick(30);
            Check(GetTargetedBlock.selectedBlock == null && fixture.Store.Load(42) != null && CombatLockout.StartedResume == null,
                "rejoin isolation: another account cannot inherit the prior connection or history");
        }

        foreach (var failure in new[] { "owner", "definition", "duplicate port", "access", "range", "death", "manual unlink" })
        {
            using (var fixture = new RecoveryWorld())
            {
                fixture.Rejoin();
                if (failure == "owner") fixture.Replica.OwnerId = 2;
                else if (failure == "definition") fixture.Replica.BlockDefinition.Id = new MyDefinitionId("MyObjectBuilder_CargoContainer", "Different");
                else if (failure == "duplicate port") fixture.Ports.Ports.Add(new InventoryPort { Owner = fixture.Replica, Dummy = new MyModelDummy { Name = fixture.State.DummyName } });
                else
                {
                    Tick(30);
                    if (failure == "access") fixture.Replica.PlayerAccess = false;
                    if (failure == "range") LinkArea.Outside = 101;
                    if (failure == "death") MySession.Static.LocalCharacter.IsDead = true;
                    if (failure == "manual unlink") LinkController.Disconnect();
                }
                Tick(30);
                Check(GetTargetedBlock.selectedBlock == null && fixture.Store.Load(42) == null,
                    "rejoin tombstone: " + failure + " prevents restoration or forgets the invalid restored target");
                LinkArea.Outside = 0;
                fixture.Rejoin();
                Tick(30);
                Check(GetTargetedBlock.selectedBlock == null && !LinkRecovery.IsPending,
                    "rejoin tombstone: " + failure + " cannot resurrect on a later session");
            }
        }
    }

    private static void RecoveryCallbacksAndPendingHops()
    {
        using (var fixture = new RecoveryWorld())
        {
            fixture.Rejoin();
            fixture.Ports.Throw = true;
            Tick(120);
            Check(LinkRecovery.IsPending && GetTargetedBlock.selectedBlock == null && fixture.Ports.Resolutions <= 4,
                "rejoin callbacks: unresolved native ports retry at a bounded cadence without transfers");
            Check(VRage.Utils.MyLog.Default.Messages.Count(line => line.Contains("recovery is waiting")) <= 1,
                "rejoin callbacks: repeated native errors are logged at a bounded rate");
            fixture.Ports.Throw = false;
            Tick(30);
            Check(ReferenceEquals(GetTargetedBlock.selectedBlock, fixture.Replica), "rejoin callbacks: native ports recover after transient exceptions");
        }
        foreach (var change in new[] { "session cleared", "character replaced", "principal changed" })
        {
            using (var fixture = new RecoveryWorld())
            {
                fixture.Rejoin();
                fixture.Replica.AccessCheck = () =>
                {
                    fixture.Replica.AccessCheck = null;
                    if (change == "session cleared") MySession.Static = null;
                    if (change == "character replaced") MySession.Static.LocalCharacter = new MyCharacter();
                    if (change == "principal changed") MySession.Static.LocalHumanPlayer.Identity.IdentityId = 2;
                };
                Tick(1);
                Check(GetTargetedBlock.selectedBlock == null && fixture.Store.Load(42)?.BlockId == fixture.State.BlockId,
                    "rejoin callbacks: " + change + " during validation cannot bind a stale target or forget the origin");
            }
        }
        using (var fixture = new RecoveryWorld())
        {
            fixture.Rejoin();
            MyVisualScriptLogicProvider.ThrowHighlightOnce = true;
            Tick(1);
            Check(GetTargetedBlock.selectedBlock == null && LinkRecovery.IsPending && fixture.Store.Load(42) != null,
                "rejoin callbacks: a partial native highlight failure rolls back live selection without forgetting it");
            Tick(30);
            Check(ReferenceEquals(GetTargetedBlock.selectedObject, fixture.ExactPort), "rejoin callbacks: the exact target recovers after a partial selection failure");
        }
        using (var fixture = new RecoveryWorld())
        {
            fixture.Rejoin();
            fixture.Ports.Ports.Clear();
            Tick(1);
            Check(LinkRecovery.IsPending && CombatLockout.FollowedGrid == fixture.ReplicaGrid,
                "rejoin pending: validated container anchors combat monitoring before its port is ready");
            CombatLockout.IsBlocked = true;
            fixture.Rejoin();
            Tick(30);
            Check(GetTargetedBlock.selectedBlock == fixture.Replica && LinkController.IsCombatPaused && MyInventory.Transfers.Count == 0,
                "rejoin pending: another hop retains observed combat restriction until the exact port restores paused");
        }
    }

    private sealed class RecoveryCommands : Commands { public void EndSession() => UnloadData(); }

    private static void RecoveryNativeLifecycle()
    {
        using (var fixture = new RecoveryWorld())
        {
            var plugin = new ClientPlugin.Plugin();
            var commands = new RecoveryCommands();
            plugin.Init(null);
            try
            {
                Check(MySession.UnloadingSubscribers == 1, "rejoin lifecycle: actual plugin subscribes the early native unload hook");
                MySession.RaiseUnloading();
                Check(GetTargetedBlock.selectedBlock == null && fixture.Store.Load(42) != null,
                    "rejoin lifecycle: native unloading saves continuity before clearing live references");
                commands.EndSession();
                Check(fixture.Store.Load(42) != null, "rejoin lifecycle: actual session component teardown preserves the saved connection");
                fixture.Rejoin();
                commands.BeforeStart();
                Tick(30);
                Check(ReferenceEquals(GetTargetedBlock.selectedBlock, fixture.Replica),
                    "rejoin lifecycle: actual session start restores through the production controller");
                Check(!MyAPIUtilities.Static.Enter("/ly off") && fixture.Store.Load(42) == null && GetTargetedBlock.selectedBlock == null,
                    "rejoin lifecycle: actual manual unlink command writes a durable tombstone");
            }
            finally { commands.EndSession(); plugin.Dispose(); }
            Check(MySession.UnloadingSubscribers == 0, "rejoin lifecycle: plugin disposal releases its native unload hook");
        }
    }
}

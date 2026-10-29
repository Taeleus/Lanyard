// Minimal contracts from the inspected framework versions. No real mod code is
// included or required by this test executable or the production plugin.
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;
using BeamEnd = System.Action<string, VRage.Ref<VRage.MyTuple<VRageMath.Vector3D, VRageMath.Vector3D, VRageMath.Vector3D?>>, VRage.ModAPI.IMyEntity, VRage.Ref<VRage.MyTuple<float, float, float, float, float>>, int, VRage.Game.Entity.MyEntity>;

internal class FunctionalBlock : MyCubeBlock, IMyFunctionalBlock
{
    public bool Enabled { get; set; } = true;
    public float JumpCharge = 100;
}
namespace CoreSystems
{
    internal class Session
    {
        internal static Session I = new Session();
        internal readonly Dictionary<long, object> EffectedCubes = new Dictionary<long, object>();
        internal readonly ConcurrentDictionary<long, object> ActiveEwarCubes = new ConcurrentDictionary<long, object>();
    }
}
namespace VanillaPlusFramework
{
    public class VanillaPlusFramework
    {
        public static VanillaPlusFramework I;
        public bool Disabled;
        public Dictionary<long, object> DisabledBlocks = new Dictionary<long, object>();
    }
}
namespace VanillaPlusFramework.Utilities
{
    public struct EmpStats
    {
        public float EMP_Radius = 10;
        public int EMP_TimeDisabled = 60;
        public bool EMP_AllowFriendlyFire;
        public EmpStats() { EMP_AllowFriendlyFire = false; }
    }
    public static class FrameworkUtilities
    {
        public static int JumpHits;
        public static int EmpApplications;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void EMP(Vector3D position, float radius, int duration, IMyEntity owner, bool friendly)
        {
            EmpApplications++;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void JDI_Hit(MyEntity hitEntity, float JDI_PowerDrainInW, bool JDI_DistributePower, IMyEntity owner, bool allowFriendlyFire)
        {
            JumpHits++;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DisableBlock(int EMP_TimeDisabled, IMyEntity owner, IMyFunctionalBlock FunctionalBlock)
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;
            VanillaPlusFramework.I.DisabledBlocks[((MyEntity)FunctionalBlock).EntityId] = new object();
            FunctionalBlock.Enabled = false;
        }
        public static void ApplyEMP(IMyFunctionalBlock block, int duration, IMyEntity owner = null) { DisableBlock(duration, owner, block); }
    }
}
namespace VanillaPlusFramework.Missiles
{
    public class VPFMissile
    {
        private bool IsClosed;
        public bool IsDisarmed;
        public object EMP_Stats;
        public IMyEntity MissileLauncher;
        public Vector3D CurrentPosition { get; private set; }
        public VPFMissile(Vector3D position, object stats, IMyEntity owner = null) { CurrentPosition = position; EMP_Stats = stats; MissileLauncher = owner; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Close() { IsClosed = true; }
        public bool ClosedForTest => IsClosed;
    }
}
namespace VanillaPlusFramework.BeamLogic
{
    public class BeamDefinition
    {
        public global::VanillaPlusFramework.Utilities.EmpStats? EMP_Stats;
        public Vector3D End;
        public Vector3D? HitPosition;
        public bool EndBeam = true;
        public bool Throw;
    }
    public class Beams
    {
        public static Beams I;
        public List<BeamEnd> OnBeamEndList = new List<BeamEnd>();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void GenerateBeam(Vector3D StartPos, Vector3D Forwards, Vector3D? Velocity, ref BeamDefinition Definition, IMyEntity OwnerEntity, int layers = 0, int GL_Layer = 0, int SPL_RecursionLayer = 0)
        {
            if (Definition.Throw) throw new InvalidOperationException("Beam failed");
            var hitPosition = Definition.HitPosition ?? Definition.End;
            var positions = new Ref<MyTuple<Vector3D, Vector3D, Vector3D?>>(new MyTuple<Vector3D, Vector3D, Vector3D?>(StartPos, Definition.End, Velocity));
            var values = new Ref<MyTuple<float, float, float, float, float>>();
            foreach (var callback in OnBeamEndList) callback("Test", positions, OwnerEntity, values, layers, null);
            if (Definition.EndBeam)
            {
                if (Definition.EMP_Stats != null && MyAPIGateway.Multiplayer.IsServer)
                    global::VanillaPlusFramework.Utilities.FrameworkUtilities.EMP(hitPosition, Definition.EMP_Stats.Value.EMP_Radius,
                        Definition.EMP_Stats.Value.EMP_TimeDisabled, OwnerEntity, Definition.EMP_Stats.Value.EMP_AllowFriendlyFire);
            }
        }
    }
}

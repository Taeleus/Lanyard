using System;
using System.Collections.Generic;
using VRageMath;

namespace VRageMath
{
    public struct Vector3D
    {
        public double X, Y, Z;
        public Vector3D(double value) { X = Y = Z = value; }
        public static Vector3D operator +(Vector3D a, Vector3D b) => new Vector3D { X = a.X + b.X, Y = a.Y + b.Y, Z = a.Z + b.Z };
        public static Vector3D operator -(Vector3D a, Vector3D b) => new Vector3D { X = a.X - b.X, Y = a.Y - b.Y, Z = a.Z - b.Z };
        public static Vector3D operator *(Vector3D a, double b) => new Vector3D { X = a.X * b, Y = a.Y * b, Z = a.Z * b };
    }
    public struct MatrixD { public int Id; public Vector3D Translation, Forward; }
    public struct BoundingBoxD
    {
        public Vector3D Min, Max;
        public BoundingBoxD(Vector3D min, Vector3D max) { Min = min; Max = max; }
    }
}

namespace Sandbox.Game.Entities
{
    public sealed class Position
    {
        public BoundingBoxD LocalAABB = new BoundingBoxD(new Vector3D(-10), new Vector3D(10));
        public Vector3D GetPosition() => new Vector3D();
    }
    public sealed class MyCubeGrid
    {
        private static int nextId;
        public bool Closed, MarkedForClose;
        public bool AllowBounds = true;
        public double Distance;
        public Position PositionComp = new Position();
        public MatrixD WorldMatrix = new MatrixD { Id = ++nextId };
    }
    public sealed class SlimBlock { public MyCubeGrid CubeGrid = new MyCubeGrid(); }
}

namespace Sandbox.Game.EntityComponents
{
    public sealed class MyCasterComponent { public Sandbox.Game.Entities.SlimBlock HitBlock = new Sandbox.Game.Entities.SlimBlock(); }
}

namespace VRage.Game
{
    public enum MySimpleObjectRasterizer { Wireframe }
    public static class MySimpleObjectDraw
    {
        public static readonly List<int> DrawnGrids = new List<int>();
        public static readonly List<float> Widths = new List<float>();
        public static void DrawTransparentBox(ref MatrixD matrix, ref BoundingBoxD bounds, ref Color color,
            MySimpleObjectRasterizer rasterizer, int divisions, float width, object lineMaterial)
        { DrawnGrids.Add(matrix.Id); Widths.Add(width); }
    }
}

namespace VRage.Game.ModAPI
{
    public interface IMyHudNotification
    {
        string Text { get; set; }
        string Font { get; set; }
        int AliveTime { get; set; }
        void Show();
        void Hide();
        void ResetAliveTime();
    }
}

namespace Sandbox.ModAPI
{
    public sealed partial class Utilities
    {
        public readonly List<RetainedNotification> Retained = new List<RetainedNotification>();
        public Action<RetainedNotification> OnCreate, OnShow, OnHide;
        public bool ThrowCreate, ThrowShow, ThrowHide;
        public VRage.Game.ModAPI.IMyHudNotification CreateNotification(string text, int milliseconds, string font)
        {
            if (ThrowCreate) throw new InvalidOperationException("Notification creation unavailable.");
            var notification = new RetainedNotification(this) { Text = text, AliveTime = milliseconds, Font = font };
            Retained.Add(notification);
            OnCreate?.Invoke(notification);
            return notification;
        }
    }

    public sealed class RetainedNotification : VRage.Game.ModAPI.IMyHudNotification
    {
        private readonly Utilities owner;
        public RetainedNotification(Utilities owner) { this.owner = owner; }
        public string Text { get; set; }
        public string Font { get; set; }
        public int AliveTime { get; set; }
        public bool Visible;
        public int Shows, Hides;
        public void Show()
        {
            if (owner.ThrowShow) throw new InvalidOperationException("Notification show unavailable.");
            Shows++;
            Visible = true;
            owner.Notifications.Add(Text);
            owner.OnShow?.Invoke(this);
        }
        public void Hide()
        {
            Visible = false;
            Hides++;
            owner.OnHide?.Invoke(this);
            if (owner.ThrowHide) throw new InvalidOperationException("Notification hide unavailable.");
        }
        public void ResetAliveTime() { }
    }
}

namespace VRage.Utils
{
    public struct MyStringId { public static MyStringId GetOrCompute(string value) => new MyStringId(); }
}

namespace Lanyard
{
    internal sealed class BoundsAccess : IDisposable
    {
        private readonly Sandbox.Game.Entities.MyCubeGrid grid;
        public static int Active;
        public BoundsAccess(Sandbox.Game.Entities.MyCubeGrid grid) { this.grid = grid; Active++; }
        public bool IsAllowed() => grid.AllowBounds && !grid.Closed && !grid.MarkedForClose;
        public void Dispose() { Active--; }
    }
}

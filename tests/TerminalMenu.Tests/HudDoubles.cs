using System.Collections.Generic;
using VRageMath;
using VRage.Utils;

namespace Sandbox
{
    public static class MySandboxGame
    {
        public static ConfigData Config = new ConfigData();
        public sealed class ConfigData { public float UIBkOpacity = 1f, UIOpacity = 1f; }
    }
}

namespace Sandbox.Game.Gui
{
    public static class MyHud
    {
        public static bool MinimalHud, IsHudMinimal, CutsceneHud;
        public static bool IsVisible = true;
    }
}

namespace Sandbox.Game.World
{
    public sealed class MySession
    {
        public static MySession Static = new MySession();
        public bool IsUnloading;
        public Character LocalCharacter = new Character();
    }
    public sealed class Character
    {
        public bool IsDead;
        public bool Closed, MarkedForClose;
        public Tool EquippedTool = new Sandbox.Game.Weapons.MyWelder();
        public Sandbox.Game.Entities.Position PositionComp = new Sandbox.Game.Entities.Position();
        public HandItem HandItemDefinition = new HandItem();
    }
    public class Tool
    {
        public bool Closed, MarkedForClose;
        public ComponentCollection Components = new ComponentCollection();
    }
    public sealed class ComponentCollection
    {
        public Sandbox.Game.EntityComponents.MyCasterComponent Caster = new Sandbox.Game.EntityComponents.MyCasterComponent();
        public T Get<T>() where T : class => Caster as T;
    }
    public sealed class HandItem { public DefinitionId Id = new DefinitionId(); }
    public sealed class DefinitionId { public string SubtypeName = "Welder"; }
}

namespace Sandbox.Game.Weapons
{
    public class MyWelder : Sandbox.Game.World.Tool { }
    public class MyAngleGrinder : Sandbox.Game.World.Tool { }
    public class MyHandDrill : Sandbox.Game.World.Tool { }
    public class MyBlockPlacerBase : Sandbox.Game.World.Tool { }
    public class MyAutomaticRifleGun : Sandbox.Game.World.Tool { }
}

namespace Sandbox.Graphics
{
    public static class MyGuiManager
    {
        public sealed class DrawnText
        {
            public string Text;
            public Vector2 Position;
            public float Scale, MaximumWidth;
            public Color Color;
            public bool FullClient;
        }
        public static readonly List<DrawnText> Text = new List<DrawnText>();
        public static Vector2 MouseCursorPosition;
        public static Rectangle GuiRectangle = new Rectangle(0, 0, 1600, 900);
        public static Rectangle FullscreenRectangle = new Rectangle(0, 0, 1600, 900);
        public static Rectangle GetSafeFullscreenRectangle() => FullscreenRectangle;
        public static Vector2 GetScreenSizeFromNormalizedSize(Vector2 size, bool useFullClientArea = false)
        {
            var rectangle = useFullClientArea ? FullscreenRectangle : GuiRectangle;
            return new Vector2((rectangle.Width + 1) * size.X, rectangle.Height * size.Y);
        }
        public static Vector2 GetScreenCoordinateFromNormalizedCoordinate(Vector2 position, bool useFullClientArea = false)
        {
            var rectangle = useFullClientArea ? FullscreenRectangle : GuiRectangle;
            return new Vector2(rectangle.Left + rectangle.Width * position.X, rectangle.Top + rectangle.Height * position.Y);
        }
        public static readonly List<(Vector2 Position, Vector2 Size, Color Color, bool WaitTillLoaded)> Backgrounds = new List<(Vector2, Vector2, Color, bool)>();
        public static readonly List<bool> SpriteWaitFlags = new List<bool>();
        public static void DrawSpriteBatch(string texture, Vector2 position, Vector2 size, Color color, MyGuiDrawAlignEnum alignment, bool useFullClientArea = false, bool waitTillLoaded = true)
        {
            Backgrounds.Add((position, size, color, waitTillLoaded));
            SpriteWaitFlags.Add(waitTillLoaded);
        }
        public static void DrawString(string font, string text, Vector2 position, float scale, Color color,
            MyGuiDrawAlignEnum alignment = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, bool useFullClientArea = false, float maxTextWidth = float.PositiveInfinity)
            => Text.Add(new DrawnText { Text = text, Position = position, Scale = scale, Color = color, MaximumWidth = maxTextWidth, FullClient = useFullClientArea });
    }
}

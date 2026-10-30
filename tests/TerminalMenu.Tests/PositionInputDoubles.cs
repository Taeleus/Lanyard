namespace VRage.Input
{
    public enum MyKeys { Escape = 27 }
    public static class MyInput { public static Input Static = new Input(); }
    public sealed class Input
    {
        public bool Escape, Left, NewLeft;
        public bool IsNewKeyPressed(MyKeys key) => key == MyKeys.Escape && Escape;
        public bool IsNewLeftMousePressed() => NewLeft;
        public bool IsLeftMousePressed() => Left;
    }
}

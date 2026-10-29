using VRage.Plugins;
using Lanyard;
using Sandbox.Game.World;

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "Lanyard";

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        OwnGrindingPatches.Initialize();
        Reflections.Initialize();
        TerminalMenu.Initialize();
        PlacementSupply.Initialize();
        MySession.OnUnloading += LinkController.SuspendForWorldChange;
    }

    public void Dispose()
    {
        MySession.OnUnloading -= LinkController.SuspendForWorldChange;
        LinkController.SuspendForWorldChange();
        PlacementSupply.Dispose();
        TerminalMenu.Dispose();
        LanyardScreen.Close();
        BoundsPreview.Clear();
        OwnGrindingPatches.Dispose();
    }

    public void Update()
    {
        LinkController.Update();
    }

}

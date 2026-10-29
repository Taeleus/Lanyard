using VRage.Plugins;
using Lanyard;

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
    }

    public void Dispose()
    {
        PlacementSupply.Dispose();
        TerminalMenu.Dispose();
        LanyardScreen.Close();
        LinkController.Disconnect();
        BoundsPreview.Clear();
        CombatLockout.Clear();
        OwnGrindingPatches.Dispose();
    }

    public void Update()
    {
        LinkController.Update();
    }

}

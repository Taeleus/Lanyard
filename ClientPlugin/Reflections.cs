using System.Collections.Generic;
using System.Reflection;
using Sandbox.Game;
using Sandbox.Game.Gui;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Utils;

namespace Lanyard;

public static class Reflections
{
    public static void Initialize()
    {
        Withdraw = typeof(MyGuiScreenGamePlay).GetMethod(
            "ProcessWithdraw", BindingFlags.NonPublic | BindingFlags.Static);
        PlacementSources = typeof(MyGuiScreenGamePlay).Assembly.GetType("Sandbox.Game.Gui.MyTerminalInventoryController")?.GetMethod(
            "GetAvailableInventoriesStatic", BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(MyEntity), typeof(MyDefinitionId), typeof(List<MyInventory>), typeof(MyEntity), typeof(bool) }, null);
        if (PlacementSources == null)
            MyLog.Default?.WriteLineAndConsole("Lanyard placement supply unavailable: inventory source method not found.");
    }

    public static MethodInfo Withdraw;
    public static MethodInfo PlacementSources;
}

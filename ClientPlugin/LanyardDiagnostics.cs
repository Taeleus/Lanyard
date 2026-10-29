using System;
using Sandbox.ModAPI;
using VRage.Utils;

namespace Lanyard;

internal static class LanyardDiagnostics
{
    public static void Show(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        MyLog.Default.WriteLineAndConsole("Lanyard: " + message);
        try { MyAPIGateway.Utilities?.ShowNotification(message, 4000, "White"); }
        catch (Exception exception) { MyLog.Default.WriteLineAndConsole("Lanyard notification failed: " + exception.Message); }
    }
}
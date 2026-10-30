using System;
using Sandbox.ModAPI;
using VRage.Game.Components;

namespace Lanyard;

[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class Commands : MySessionComponentBase
{
    public override void BeforeStart()
    {
        LanyardPreferences.EnsureLoaded();
        LanyardShortcuts.ClearTransient();
        LinkController.BeginSession();
        MyAPIUtilities.Static.MessageEntered += Command;
    }

    protected override void UnloadData()
    {
        MyAPIUtilities.Static.MessageEntered -= Command;
        LanyardHud.Clear();
        LanyardShortcuts.ClearTransient();
        LinkController.SuspendForWorldChange();
        TerminalMenu.Clear();
        LanyardScreen.Close();
        BoundsPreview.Clear();
        base.UnloadData();
    }

    public override void Draw() { BoundsPreview.Draw(); LanyardHud.Draw(); }

    private static void Command(string message, ref bool sendToOthers)
    {
        var parts = (message ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        if (parts[0].Equals("/tether", StringComparison.OrdinalIgnoreCase))
        {
            sendToOthers = false;
            MyAPIGateway.Utilities?.ShowNotification("Use /ly to link, /ly menu for controls, or /ly off to disconnect.", 5000, "White");
            return;
        }
        if (!parts[0].Equals("/lanyard", StringComparison.OrdinalIgnoreCase) &&
            !parts[0].Equals("/ly", StringComparison.OrdinalIgnoreCase)) return;
        sendToOthers = false;
        if (parts.Length == 1) GetTargetedBlock.GetPort();
        else if (parts.Length == 2 && parts[1].Equals("menu", StringComparison.OrdinalIgnoreCase))
            LanyardScreen.Open();
        else if (parts.Length == 2 && parts[1].Equals("off", StringComparison.OrdinalIgnoreCase))
            LinkController.Disconnect("Lanyard disconnected.");
        else if (parts.Length == 2 && parts[1].Equals("pause", StringComparison.OrdinalIgnoreCase))
            LinkController.SetUserPaused(true);
        else if (parts.Length == 2 && parts[1].Equals("resume", StringComparison.OrdinalIgnoreCase))
            LinkController.SetUserPaused(false);
        else if (parts.Length == 2 && parts[1].Equals("bounds", StringComparison.OrdinalIgnoreCase))
            BoundsPreview.Toggle();
        else if (parts.Length == 3 && parts[1].Equals("bounds", StringComparison.OrdinalIgnoreCase) &&
                 parts[2].Equals("linked", StringComparison.OrdinalIgnoreCase))
            BoundsPreview.ToggleLinked();
        else if (parts.Length == 3 && parts[1].Equals("bounds", StringComparison.OrdinalIgnoreCase) &&
                 parts[2].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            BoundsPreview.Clear();
            MyAPIGateway.Utilities?.ShowNotification("Lanyard bounds hidden.", 3000, "White");
        }
        else MyAPIGateway.Utilities?.ShowNotification("Usage: /ly (or /lanyard), /ly menu, /ly off, /ly pause, /ly resume, /ly bounds [linked|off]", 5000, "White");
    }
}

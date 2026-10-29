using System;
using Sandbox.ModAPI;
using VRage.Game.Components;

namespace Lanyard;

[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class Commands : MySessionComponentBase
{
    public override void BeforeStart()
    {
        LinkController.Disconnect();
        BoundsPreview.Clear();
        CombatLockout.Start();
        MyAPIUtilities.Static.MessageEntered += Command;
    }

    protected override void UnloadData()
    {
        MyAPIUtilities.Static.MessageEntered -= Command;
        TerminalMenu.Clear();
        LanyardScreen.Close();
        LinkController.Disconnect();
        BoundsPreview.Clear();
        CombatLockout.Clear();
        base.UnloadData();
    }

    public override void Draw() { BoundsPreview.Draw(); }

    private static void Command(string message, ref bool sendToOthers)
    {
        var parts = (message ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 ||
            (!parts[0].Equals("/lanyard", StringComparison.OrdinalIgnoreCase) &&
             !parts[0].Equals("/ly", StringComparison.OrdinalIgnoreCase))) return;
        sendToOthers = false;
        if (parts.Length == 1) GetTargetedBlock.GetPort();
        else if (parts.Length == 2 && parts[1].Equals("menu", StringComparison.OrdinalIgnoreCase))
            LanyardScreen.Open();
        else if (parts.Length == 2 && parts[1].Equals("off", StringComparison.OrdinalIgnoreCase))
            LinkController.Disconnect("Lanyard disconnected.");
        else if (parts.Length == 2 && parts[1].Equals("bounds", StringComparison.OrdinalIgnoreCase))
            BoundsPreview.Toggle();
        else if (parts.Length == 3 && parts[1].Equals("bounds", StringComparison.OrdinalIgnoreCase) &&
                 parts[2].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            BoundsPreview.Clear();
            MyAPIGateway.Utilities?.ShowNotification("Lanyard bounds hidden.", 3000, "White");
        }
        else MyAPIGateway.Utilities?.ShowNotification("Usage: /ly (or /lanyard), /ly menu, /ly off, /ly bounds, /ly bounds off", 5000, "White");
    }
}

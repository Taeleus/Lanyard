using System;
using System.Linq;
using Lanyard;
using Sandbox.ModAPI;

internal static class Program
{
    private static int assertions;

    private sealed class Session : Commands
    {
        public void Unload() => UnloadData();
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }

    private static void Reset()
    {
        LanyardScreen.Close();
        BoundsPreview.Clear();
        Sandbox.Game.World.MySession.Static = new Sandbox.Game.World.MySession();
        GetTargetedBlock.selectedBlock = new SelectedBlock();
        LinkRecovery.HasCurrentContext = true;
        LinkController.Access = true;
        Calls.Actions.Clear();
        MyAPIGateway.Utilities = new Utilities();
    }

    private static void Main()
    {
        StartupIsolationChecks.Run(Check);
        HudToolVisibilityChecks.Run(Check);
        EditorLifecycleChecks.Run(Check);
        var session = new Session();
        session.BeforeStart();
        foreach (var prefix in new[] { "/lanyard", "/ly", "/LaNyArD", "/LY" })
        {
            foreach (var command in new[]
            {
                (Suffix: "", Action: "link"),
                (Suffix: " menu", Action: "menu"),
                (Suffix: " off", Action: "disconnect"),
                (Suffix: " pause", Action: "pause"),
                (Suffix: " resume", Action: "resume"),
                (Suffix: " bounds", Action: "bounds"),
                (Suffix: " bounds linked", Action: "linked bounds"),
                (Suffix: " bounds off", Action: "bounds off")
            })
            {
                Reset();
                var message = prefix + command.Suffix;
                Check(!MyAPIUtilities.Static.Enter(message), message + " stays local");
                if (command.Action == "bounds" || command.Action == "linked bounds")
                    Check(Calls.Actions.Count == 0 && MyAPIGateway.Utilities.Notifications.Count == 1 &&
                        MyAPIGateway.Utilities.Notifications[0].StartsWith("Green:"), message + " shows bounds through the production handler");
                else if (command.Action == "bounds off")
                    Check(Calls.Actions.Count == 0 && MyAPIGateway.Utilities.Notifications.SequenceEqual(new[] { "Lanyard bounds hidden." }), message + " hides bounds through the production handler");
                else Check(Calls.Actions.SequenceEqual(new[] { command.Action }), message + " dispatches exactly once");
            }
        }

        Reset();
        Check(!MyAPIUtilities.Static.Enter(" \t/LY\tBoUnDs \tOFF  "), "whitespace and mixed-case arguments stay local");
        Check(Calls.Actions.Count == 0 && MyAPIGateway.Utilities.Notifications.SequenceEqual(new[] { "Lanyard bounds hidden." }), "whitespace dispatches bounds off");

        LegacyRedirectChecks();

        foreach (var message in new[] { null, "", " \t ", "hello", "/lyx", "/lanyardx", "/ly/menu", "x /ly" })
        {
            Reset();
            Check(MyAPIUtilities.Static.Enter(message), "unrelated chat passes through: " + message);
            Check(Calls.Actions.Count == 0 && MyAPIGateway.Utilities.Notifications.Count == 0,
                "unrelated chat has no effects: " + message);
        }

        foreach (var message in new[] { "/ly unknown", "/lanyard unknown", "/ly menu extra", "/ly bounds off extra" })
        {
            Reset();
            Check(!MyAPIUtilities.Static.Enter(message), "invalid Lanyard subcommand stays local: " + message);
            Check(Calls.Actions.Count == 0, "invalid subcommand takes no action: " + message);
            Check(MyAPIGateway.Utilities.Notifications.Count == 1 &&
                MyAPIGateway.Utilities.Notifications[0].Contains("/ly") &&
                MyAPIGateway.Utilities.Notifications[0].Contains("/lanyard"), "usage advertises both prefixes");
        }

        Reset();
        MyAPIGateway.Utilities = null;
        Check(!MyAPIUtilities.Static.Enter("/ly unknown"), "missing notification utility is safe");
        Check(!MyAPIUtilities.Static.Enter("/ly bounds off"), "bounds off tolerates missing notification utility");
        session.Unload();
        Reset();
        Check(MyAPIUtilities.Static.Enter("/ly menu") && Calls.Actions.Count == 0,
            "session unload detaches command handler");
        session = new Session();
        session.BeforeStart();
        Reset();
        Check(!MyAPIUtilities.Static.Enter("/ly menu") && Calls.Actions.SequenceEqual(new[] { "menu" }),
            "rejoin attaches one command handler");
        session.Unload();
        TerminalChecks.Run(Check);
        PresentationChecks.Run(Check);
        BoundsChecks.Run(Check);
        Console.WriteLine("PASS: " + assertions + " command/menu assertions (production commands, menus, HUD, bounds and range warning with real Harmony and offline game API doubles).");
    }

    private static void LegacyRedirectChecks()
    {
        foreach (var message in new[]
        {
            "/tether", "/TETHER", " \t/TeThEr\tmenu  ", "/tether off", "/tether pause",
            "/tether resume", "/tether bounds", "/tether bounds off", "/tether unknown extra", "/tether /ly off"
        })
        {
            Reset();
            var paused = LanyardPreferences.Current.ManualPaused;
            Check(!MyAPIUtilities.Static.Enter(message), "legacy command is consumed locally");
            Check(Calls.Actions.Count == 0 && LanyardPreferences.Current.ManualPaused == paused,
                "legacy command never executes an action or changes pause state");
            Check(MyAPIGateway.Utilities.Notifications.Count == 1 &&
                MyAPIGateway.Utilities.Notifications[0].Contains("/ly to link") &&
                MyAPIGateway.Utilities.Notifications[0].Contains("/ly menu for controls") &&
                MyAPIGateway.Utilities.Notifications[0].Contains("/ly off to disconnect"),
                "legacy command explains the supported link, menu and disconnect commands");
        }

        foreach (var message in new[]
        {
            "/tetherx", "/tethers off", "/tether/menu", "/tethered", "hello /tether", "x /tether menu", "tether"
        })
        {
            Reset();
            Check(MyAPIUtilities.Static.Enter(message), "legacy lookalikes and ordinary mentions remain public chat");
            Check(Calls.Actions.Count == 0 && MyAPIGateway.Utilities.Notifications.Count == 0,
                "legacy lookalikes and ordinary mentions cause no local effects");
        }

        Reset();
        MyAPIGateway.Utilities = null;
        Check(!MyAPIUtilities.Static.Enter(" \t/TETHER off "), "legacy redirect tolerates missing notification utilities");
        Check(Calls.Actions.Count == 0, "missing notification utilities never trigger a legacy action");
        Check(!MyAPIUtilities.Static.Enter("/tether menu", false), "legacy redirect never re-enables an already suppressed broadcast");
    }
}

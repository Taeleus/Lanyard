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
        Calls.Actions.Clear();
        MyAPIGateway.Utilities = new Utilities();
    }

    private static void Main()
    {
        var session = new Session();
        session.BeforeStart();
        foreach (var prefix in new[] { "/lanyard", "/ly", "/LaNyArD", "/LY" })
        {
            foreach (var command in new[]
            {
                (Suffix: "", Action: "link"),
                (Suffix: " menu", Action: "menu"),
                (Suffix: " off", Action: "disconnect"),
                (Suffix: " bounds", Action: "bounds"),
                (Suffix: " bounds off", Action: "bounds off")
            })
            {
                Reset();
                var message = prefix + command.Suffix;
                Check(!MyAPIUtilities.Static.Enter(message), message + " stays local");
                Check(Calls.Actions.SequenceEqual(new[] { command.Action }), message + " dispatches exactly once");
            }
        }

        Reset();
        Check(!MyAPIUtilities.Static.Enter(" \t/LY\tBoUnDs \tOFF  "), "whitespace and mixed-case arguments stay local");
        Check(Calls.Actions.SequenceEqual(new[] { "bounds off" }), "whitespace dispatches bounds off");

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
        Console.WriteLine("PASS: " + assertions + " command/menu assertions (production Commands, TerminalMenu and LanyardPanel with real Harmony and offline game API doubles).");
    }
}

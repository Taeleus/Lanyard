using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Lanyard;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.Graphics.GUI;
using VRage.Utils;

internal static class TerminalChecks
{
    private const string PatchOwner = "Taeleus.Lanyard.TerminalMenu";
    private static readonly MethodInfo CreateTabs = AccessTools.Method(typeof(MyGuiScreenTerminal), "CreateTabs");
    private static readonly MethodInfo Attach = AccessTools.Method(typeof(TerminalMenu), "CreateTabsPostfix");
    private static MyGuiControlTabPage LanyardPage(MyGuiScreenTerminal terminal) =>
        terminal.Tabs.Pages.Single(page => page.Name == "PageLanyard");
    private static MyGuiControlButton[] Buttons(MyGuiControlTabPage page) => page.Controls.OfType<MyGuiControlButton>().ToArray();
    private static bool Dense(MyGuiControlTabControl tabs) =>
        Enumerable.Range(0, tabs.Pages.Count).All(key => tabs.Pages.TryGetValue(key, out _));
    private sealed class Session : Commands { public void Unload() => UnloadData(); }

    public static void Run(Action<bool, string> check)
    {
        TerminalMenu.Initialize();
        TerminalMenu.Initialize();
        check(Harmony.GetPatchInfo(CreateTabs).Postfixes.Count(patch => patch.owner == PatchOwner) == 1,
            "terminal initialization installs one CreateTabs postfix");
        var terminal = new MyGuiScreenTerminal();
        terminal.Build();
        var tabs = terminal.Tabs;
        var page = LanyardPage(terminal);
        var buttons = Buttons(page);
        check(tabs.Pages.Count == 8 && page.PageKey == 7 && Dense(tabs), "Lanyard appends one dense native tab");
        check(Enumerable.Range(0, 7).All(key => tabs.GetTabSubControl(key).Name == "Native" + key),
            "existing native tabs retain identity");
        check(page.Text.ToString() == "Lanyard" && buttons.Where(button => button.Visible).Select(button => button.Text.ToString()).SequenceEqual(
            new[] { "Connection", "Tools", "Display", "Shortcuts", "Change connection", "Pause", "Disconnect", "Toggle working area", "Toggle aimed grid" }), "terminal exposes compact connection controls and native subviews");
        check(tabs.SelectedPage == 0, "insertion preserves original selection");
        Attach.Invoke(null, new object[] { terminal });
        check(tabs.Pages.Count == 8 && tabs.HandlerCount == 1 && terminal.ClosedHandlerCount == 1,
            "repeated insertion creates no duplicate tab or callback");

        var reads = LinkController.StatusReads;
        LinkController.StateText = "Linked to Cargo";
        terminal.Update(true);
        check(LinkController.StatusReads == reads, "hidden Lanyard tab does not refresh status");
        tabs.SelectedPage = page.PageKey;
        check(ReferenceEquals(terminal.FocusedControl, buttons.Single(button => button.Name == "LanyardLink")), "selecting Lanyard focuses link control");
        check(page.Controls.OfType<MyGuiControlMultilineText>().First().Text.ToString() == "Linked to Cargo",
            "tab selection displays current status");
        LinkController.StateText = "Lanyard unavailable.";
        terminal.Update(true);
        check(page.Controls.OfType<MyGuiControlMultilineText>().First().Text.ToString() == "Lanyard unavailable.",
            "selected tab follows changing status");
        reads = LinkController.StatusReads;
        new MyGuiScreenTerminal().Update(true);
        check(LinkController.StatusReads == reads, "another terminal cannot refresh the active tab");
        Calls.Actions.Clear();
        foreach (var button in buttons.Where(button => button.Visible && !button.Name.StartsWith("LanyardView"))) button.Click();
        check(Calls.Actions.SequenceEqual(new[] { "link", "pause", "disconnect" }) &&
            MyAPIGateway.Utilities.Notifications.Any(text => text.StartsWith("Green:")) &&
            MyAPIGateway.Utilities.Notifications.Any(text => text.Contains("bounds hidden")),
            "shared panel delegates each action once to existing handlers");
        buttons.Single(button => button.Name == "LanyardViewShortcuts").Click();
        buttons.Single(button => button.Name == "LanyardBindLink").Click();
        check(LanyardShortcuts.IsCapturing, "terminal shortcut capture starts on the visible subview");
        tabs.SelectedPage = 0;
        check(!LanyardShortcuts.IsCapturing, "leaving the Lanyard terminal tab cancels shortcut capture");
        tabs.SelectedPage = page.PageKey;
        check(ReferenceEquals(terminal.FocusedControl, buttons.Single(button => button.Name == "LanyardViewShortcuts")),
            "returning to the terminal tab focuses a control in the retained subview");

        terminal.Close();
        check(tabs.HandlerCount == 0 && terminal.ClosedHandlerCount == 0, "closing terminal detaches callbacks");
        Calls.Actions.Clear();
        foreach (var button in buttons) button.Click();
        check(buttons.All(button => !button.Enabled) && Calls.Actions.Count == 0,
            "closing terminal neutralizes retained action callbacks");
        reads = LinkController.StatusReads;
        terminal.Update(true);
        tabs.SelectedPage = page.PageKey;
        check(LinkController.StatusReads == reads, "closed terminal no longer refreshes Lanyard");

        terminal.Build();
        tabs = terminal.Tabs;
        page = LanyardPage(terminal);
        buttons = Buttons(page);
        terminal.Build();
        check(tabs.HandlerCount == 0 && buttons.All(button => !button.Enabled),
            "recreating terminal releases the previous controls");
        tabs = terminal.Tabs;
        page = LanyardPage(terminal);
        check(tabs.Pages.Count == 8 && tabs.HandlerCount == 1 && terminal.ClosedHandlerCount == 1,
            "recreated terminal has one live Lanyard page");
        tabs.SelectedPage = page.PageKey;
        TerminalMenu.Clear();
        check(tabs.Pages.Count == 7 && Dense(tabs) && tabs.SelectedPage == 0,
            "cleanup removes final Lanyard page and restores native selection");
        check(tabs.HandlerCount == 0 && terminal.ClosedHandlerCount == 0 && page.Controls.Count == 0,
            "cleanup detaches handlers and clears removed controls");

        terminal.Build();
        tabs = terminal.Tabs;
        page = LanyardPage(terminal);
        buttons = Buttons(page);
        var later = tabs.GetTabSubControl(8);
        later.Name = "LaterPlugin";
        tabs.SelectedPage = page.PageKey;
        TerminalMenu.Clear();
        check(tabs.Pages.Count == 9 && Dense(tabs) && ReferenceEquals(tabs.GetTabSubControl(8), later),
            "cleanup preserves later plugins and dense page keys");
        check(!page.Enabled && !page.IsTabVisible && !page.Visible && page.Controls.Count == 0,
            "middle Lanyard page becomes an inert hidden placeholder");
        check(tabs.SelectedPage == 0 && tabs.HandlerCount == 0 && terminal.ClosedHandlerCount == 0,
            "placeholder cleanup restores selection and detaches callbacks");
        Calls.Actions.Clear();
        foreach (var button in buttons) button.Click();
        check(Calls.Actions.Count == 0, "placeholder controls cannot perform stale actions");
        TerminalMenu.Clear();
        check(tabs.Pages.Count == 9, "repeated cleanup leaves other plugins unchanged");

        terminal = new MyGuiScreenTerminal { Keys = new[] { 0, 2 } };
        terminal.Build();
        check(terminal.Tabs.Pages.Count == 2 && terminal.Tabs.HandlerCount == 0 && terminal.ClosedHandlerCount == 0,
            "sparse native page keys skip integration without attaching handlers");
        check(MyLog.Default.Lines.Any(line => line.Contains("sparse")), "sparse-key rejection is logged");
        terminal = new MyGuiScreenTerminal { Keys = Enumerable.Range(0, 8).ToArray() };
        terminal.Build();
        check(LanyardPage(terminal).PageKey == 8 && terminal.Tabs.GetTabSubControl(7).Name == "Native7",
            "existing plugin page seven is preserved and Lanyard chooses the next key");
        tabs = terminal.Tabs;
        tabs.SelectedPage = 7;
        TerminalMenu.Clear();
        check(tabs.SelectedPage == 7 && tabs.Pages.Count == 8, "cleanup preserves selection of another plugin tab");

        var session = new Session();
        session.BeforeStart();
        terminal.Build();
        tabs = terminal.Tabs;
        page = LanyardPage(terminal);
        session.Unload();
        check(tabs.Pages.Count == 8 && tabs.HandlerCount == 0 && terminal.ClosedHandlerCount == 0 && page.Controls.Count == 0,
            "production session unload releases terminal controls and callbacks");

        terminal = new MyGuiScreenTerminal { Keys = new[] { 0 } };
        terminal.Build();
        TerminalMenu.Clear();
        terminal.Tabs.GetTabSubControl(0).Name = "PageLanyard";
        Attach.Invoke(null, new object[] { terminal });
        check(terminal.Tabs.Pages.Count == 1 && terminal.Tabs.HandlerCount == 0,
            "existing named Lanyard page is not overwritten");
        TerminalMenu.Dispose();
        TerminalMenu.Dispose();
        check(!(Harmony.GetPatchInfo(CreateTabs)?.Owners.Contains(PatchOwner) ?? false),
            "plugin disposal removes only its terminal patches");
        terminal.Build();
        check(terminal.Tabs.Pages.Count == 1, "disposed plugin no longer inserts a terminal page");
    }
}

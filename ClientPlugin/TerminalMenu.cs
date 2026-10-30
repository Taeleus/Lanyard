using System;
using System.Text;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using VRage.Utils;

namespace Lanyard;

/// <summary>Append only Lanyard's connection panel to the native K terminal.</summary>
internal static class TerminalMenu
{
    private const string HarmonyId = "Taeleus.Lanyard.TerminalMenu";
    private const string PageName = "PageLanyard";
    private static Harmony harmony;
    private static MyGuiScreenTerminal terminal;
    private static MyGuiControlTabControl tabs;
    private static MyGuiControlTabPage page;
    private static LanyardPanel panel;
    private static int previousPage;

    public static void Initialize()
    {
        if (harmony != null) return;
        try
        {
            var create = AccessTools.Method(typeof(MyGuiScreenTerminal), "CreateTabs", Type.EmptyTypes);
            var update = AccessTools.Method(typeof(MyGuiScreenTerminal), "Update", new[] { typeof(bool) });
            var input = AccessTools.DeclaredMethod(typeof(MyGuiScreenTerminal), "HandleInput", new[] { typeof(bool) });
            if (create == null || update == null || input == null) throw new MissingMethodException("Terminal menu methods are unavailable.");
            harmony = new Harmony(HarmonyId);
            harmony.Patch(create, postfix: new HarmonyMethod(typeof(TerminalMenu), nameof(CreateTabsPostfix)));
            harmony.Patch(update, postfix: new HarmonyMethod(typeof(TerminalMenu), nameof(UpdatePostfix)));
            harmony.Patch(input, prefix: new HarmonyMethod(typeof(TerminalMenu), nameof(HandleInputPrefix)));
        }
        catch (Exception exception)
        {
            Dispose();
            LogFailure("initialization", exception);
        }
    }

    public static void Dispose()
    {
        Clear();
        try { harmony?.UnpatchAll(HarmonyId); }
        catch (Exception exception) { LogFailure("unpatch", exception); }
        finally { harmony = null; }
    }

    public static void Clear() => Release(removePage: true);

    // Explicit patch registration keeps these optional UI hooks out of combat PatchAll.
    private static void CreateTabsPostfix(MyGuiScreenTerminal __instance)
    {
        try
        {
            var newTabs = __instance.Controls.GetControlByName("TerminalTabs") as MyGuiControlTabControl;
            if (newTabs == null) return;
            if (ReferenceEquals(terminal, __instance) && ReferenceEquals(tabs, newTabs) && page != null &&
                newTabs.Pages.TryGetValue(page.PageKey, out var existing) && ReferenceEquals(existing, page)) return;
            Clear();
            foreach (var existingPage in newTabs.Pages)
                if (existingPage.Name == PageName) return;

            // Native controller/Ctrl+Tab navigation indexes every key from zero to Count-1.
            var key = newTabs.Pages.Count;
            for (var index = 0; index < key; index++)
                if (!newTabs.Pages.TryGetValue(index, out _))
                    throw new InvalidOperationException("Terminal tab keys are sparse; Lanyard tab was skipped.");

            terminal = __instance;
            tabs = newTabs;
            previousPage = newTabs.SelectedPage;
            page = newTabs.GetTabSubControl(key);
            page.Name = PageName;
            page.Text = new StringBuilder("Lanyard");
            page.TextScale = 0.7005405f;
            panel = new LanyardPanel(page.Controls, terminal);
            tabs.TabButtonScale = tabs.TabButtonScale;
            tabs.OnPageChanged += PageChanged;
            terminal.Closed += TerminalClosed;
        }
        catch (Exception exception)
        {
            Clear();
            LogFailure("creation", exception);
        }
    }

    private static void UpdatePostfix(MyGuiScreenTerminal __instance, bool hasFocus)
    {
        if (!ReferenceEquals(terminal, __instance) || page == null) return;
        var active = tabs.SelectedPage == page.PageKey && hasFocus;
        panel?.SetActive(active);
        if (active) panel?.RefreshStatus();
        var restore = active ? panel?.TakeFocusRequest() : null;
        if (restore != null) terminal.FocusedControl = restore;
    }

    private static bool HandleInputPrefix(MyGuiScreenTerminal __instance)
    {
        if (!ReferenceEquals(terminal, __instance) || page == null || tabs.SelectedPage != page.PageKey) return true;
        try
        {
            if (!LanyardShortcuts.HandleCaptureEscape(__instance)) return true;
            panel?.RefreshStatus();
            return false;
        }
        catch (Exception exception) { LogFailure("shortcut input", exception); return true; }
    }

    private static void PageChanged()
    {
        try
        {
            if (page == null) return;
            panel?.SetActive(tabs.SelectedPage == page.PageKey);
            if (tabs.SelectedPage != page.PageKey) return;
            panel?.RefreshStatus();
            terminal.FocusedControl = panel?.FocusControl;
        }
        catch (Exception exception) { LogFailure("selection", exception); }
    }

    private static void TerminalClosed(MyGuiScreenBase source, bool isUnloading)
    {
        if (ReferenceEquals(terminal, source)) Release(removePage: false);
    }

    private static void Release(bool removePage)
    {
        var oldTerminal = terminal;
        var oldTabs = tabs;
        var oldPage = page;
        var oldPanel = panel;
        terminal = null;
        tabs = null;
        page = null;
        panel = null;
        try
        {
            if (oldTerminal != null) oldTerminal.Closed -= TerminalClosed;
            if (oldTabs != null) oldTabs.OnPageChanged -= PageChanged;
            oldPanel?.Dispose();
            if (!removePage || oldPage == null || oldTabs == null ||
                !oldTabs.Pages.TryGetValue(oldPage.PageKey, out var existing) || !ReferenceEquals(existing, oldPage)) return;

            var selected = oldTabs.SelectedPage;
            if (selected == oldPage.PageKey)
                selected = oldTabs.Pages.TryGetValue(previousPage, out var prior) && prior.Enabled ? previousPage : 0;
            oldPage.Controls.Clear();
            if (oldPage.PageKey == oldTabs.Pages.Count - 1)
            {
                oldTabs.Controls.Remove(oldPage);
                oldTabs.RecreatePages();
            }
            else
            {
                // A later plugin may have appended a page. Keep its numeric keys dense.
                oldPage.Enabled = false;
                oldPage.IsTabVisible = false;
                oldPage.Visible = false;
                oldTabs.TabButtonScale = oldTabs.TabButtonScale;
            }
            if (oldTabs.Pages.TryGetValue(selected, out var selectedPage) && selectedPage.Enabled)
                oldTabs.SelectedPage = selected;
            else oldTabs.SelectedPage = 0;
        }
        catch (Exception exception) { LogFailure("cleanup", exception); }
    }

    private static void LogFailure(string operation, Exception exception)
    {
        try { MyLog.Default?.WriteLineAndConsole("Lanyard terminal " + operation + " failed: " + exception); }
        catch { }
    }
}

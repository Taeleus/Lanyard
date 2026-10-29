using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using VRage.Game;

namespace Lanyard;

// Caches ownership groups, not enemy/faction results. Relations stay live each scan.
internal sealed class GridOwnershipCache : IDisposable
{
    private readonly MyCubeGrid grid;
    private readonly List<MyTerminalBlock> subscribed = new List<MyTerminalBlock>();
    private readonly List<MyCubeBlock> representatives = new List<MyCubeBlock>();
    private readonly List<MyCubeBlock> uncached = new List<MyCubeBlock>();
    private bool dirty = true;

    public GridOwnershipCache(MyCubeGrid grid)
    {
        this.grid = grid;
        grid.OnFatBlockAdded += StructureChanged;
        grid.OnFatBlockRemoved += StructureChanged;
        grid.OnBlockOwnershipChanged += GridOwnershipChanged;
    }

    private void StructureChanged(MyCubeBlock block) { dirty = true; }
    private void GridOwnershipChanged(MyCubeGrid changed) { dirty = true; }
    private void OwnershipChanged(MyTerminalBlock block) { dirty = true; }

    public IEnumerable<MyCubeBlock> Candidates()
    {
        // MarkForClose can precede a structural removal event. A dead group
        // representative must not hide other live blocks with the same owner.
        if (!dirty)
            foreach (var block in representatives)
                if (block.Closed || block.MarkedForClose)
                {
                    dirty = true;
                    break;
                }
        if (dirty) Rebuild();
        foreach (var block in representatives) yield return block;
        // Non-terminal blocks do not expose the same immediate ownership event.
        // Keep checking them individually rather than risk a stale grouping.
        foreach (var block in uncached) yield return block;
    }

    private void Rebuild()
    {
        UnsubscribeBlocks();
        representatives.Clear();
        uncached.Clear();
        var groups = new Dictionary<long, HashSet<MyOwnershipShareModeEnum>>();
        foreach (var block in grid.GetFatBlocks())
        {
            if (block.Closed || block.MarkedForClose) continue;
            var terminal = block as MyTerminalBlock;
            if (terminal == null)
            {
                uncached.Add(block);
                continue;
            }
            // Subscribe even if unowned: acquiring ownership must invalidate us.
            terminal.OwnershipChanged += OwnershipChanged;
            subscribed.Add(terminal);
            var module = block.IDModule;
            if (module == null || block.OwnerId == 0) continue;
            if (!groups.TryGetValue(block.OwnerId, out var modes))
            {
                modes = new HashSet<MyOwnershipShareModeEnum>();
                groups.Add(block.OwnerId, modes);
            }
            if (modes.Add(module.ShareMode)) representatives.Add(block);
        }
        dirty = false;
    }

    private void UnsubscribeBlocks()
    {
        foreach (var block in subscribed) block.OwnershipChanged -= OwnershipChanged;
        subscribed.Clear();
    }

    public void Dispose()
    {
        grid.OnFatBlockAdded -= StructureChanged;
        grid.OnFatBlockRemoved -= StructureChanged;
        grid.OnBlockOwnershipChanged -= GridOwnershipChanged;
        UnsubscribeBlocks();
        representatives.Clear();
        uncached.Clear();
    }
}
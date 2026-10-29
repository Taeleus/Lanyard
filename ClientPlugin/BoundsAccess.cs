using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;

namespace Lanyard;

// Cache structure only. Ownership, sharing and faction membership must stay live.
internal sealed class BoundsAccess : IDisposable
{
    private readonly MyCubeGrid grid;
    private readonly List<MyCubeBlock> blocks = new List<MyCubeBlock>();
    private readonly Dictionary<long, bool> factionOwners = new Dictionary<long, bool>();
    private bool dirty = true;

    public BoundsAccess(MyCubeGrid grid)
    {
        this.grid = grid;
        grid.OnFatBlockAdded += StructureChanged;
        grid.OnFatBlockRemoved += StructureChanged;
    }

    private void StructureChanged(MyCubeBlock block) { dirty = true; }

    public bool IsAllowed()
    {
        var identity = MySession.Static?.LocalHumanPlayer?.Identity?.IdentityId ?? 0;
        if (identity == 0 || grid.Closed || grid.MarkedForClose) return false;
        if (dirty)
        {
            blocks.Clear();
            foreach (var block in grid.GetFatBlocks()) blocks.Add(block);
            dirty = false;
        }
        factionOwners.Clear();
        var factions = MyAPIGateway.Session?.Factions;
        var ours = factions?.TryGetPlayerFaction(identity);
        int total = 0, permitted = 0;
        foreach (var block in blocks)
        {
            if (block.Closed || block.MarkedForClose || block.IDModule == null) continue;
            // Include unowned ownable blocks, but not armor, in the denominator.
            total++;
            long owner = block.OwnerId;
            if (owner == 0) continue;
            if (owner == identity) { permitted++; continue; }
            if (ours == null || block.IDModule.ShareMode != MyOwnershipShareModeEnum.Faction) continue;
            if (!factionOwners.TryGetValue(owner, out var sameFaction))
            {
                var theirs = factions.TryGetPlayerFaction(owner);
                sameFaction = theirs != null && ours.FactionId == theirs.FactionId;
                factionOwners.Add(owner, sameFaction);
            }
            if (sameFaction) permitted++;
        }
        return permitted > total / 2;
    }

    public void Dispose()
    {
        grid.OnFatBlockAdded -= StructureChanged;
        grid.OnFatBlockRemoved -= StructureChanged;
        blocks.Clear();
        factionOwners.Clear();
    }
}

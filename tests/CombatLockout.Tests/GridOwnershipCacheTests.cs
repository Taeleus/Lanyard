using System;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Lanyard;

internal static class GridOwnershipCacheTests
{
    public static void Run(Action<bool, string> check)
    {
        var grid = new MyCubeGrid();
        var first = new MyTerminalBlock { OwnerId = 3, IDModule = new OwnershipModule() };
        var second = new MyTerminalBlock { OwnerId = 3, IDModule = new OwnershipModule() };
        grid.Add(first);
        grid.Add(second);
        int before = grid.Subscribers;
        using (var cache = new GridOwnershipCache(grid))
        {
            check(cache.Candidates().Single() == first, "ownership cache: same owner/share represented once");
            first.MarkedForClose = true;
            check(cache.Candidates().Single() == second, "ownership cache: marked representative replaced before removal event");
            check(first.OwnershipSubscribers == 0 && second.OwnershipSubscribers == 1,
                "ownership cache: replacement releases old subscription without duplicates");
            first.MarkedForClose = false;
            grid.Remove(first);
            grid.Add(first);
            check(cache.Candidates().Single() == second, "ownership cache: structural change rebuild keeps live representative");
            second.Closed = true;
            check(cache.Candidates().Single() == first, "ownership cache: closed representative replaced before removal event");
            first.OwnerId = 4;
            first.ChangeOwnership();
            check(cache.Candidates().Single() == first && first.OwnershipSubscribers == 1,
                "ownership cache: ownership refresh retains one handler");
        }
        check(grid.Subscribers == before && first.OwnershipSubscribers == 0 && second.OwnershipSubscribers == 0,
            "ownership cache: grid and block handlers released on disposal");
    }
}

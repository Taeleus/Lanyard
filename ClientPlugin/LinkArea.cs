using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Lanyard;

// Only inspects entities replicated to this client; not server-authoritative.
public static class LinkArea
{
    private static readonly Dictionary<MyCubeGrid, GridOwnershipCache> ownershipCaches =
        new Dictionary<MyCubeGrid, GridOwnershipCache>();

    public static void ClearCache()
    {
        foreach (var cache in ownershipCaches.Values) cache.Dispose();
        ownershipCaches.Clear();
    }

    public const double BreakDistance = 100.0;
    public const double EnemyScanDistance = 5000.0;

    public static double DistanceOutside(MyCubeGrid grid, Vector3D position)
    {
        var local = Vector3D.Transform(position, MatrixD.Invert(grid.WorldMatrix));
        var bounds = grid.PositionComp.LocalAABB;
        var closest = Vector3D.Clamp(local, (Vector3D)bounds.Min, (Vector3D)bounds.Max);
        return Vector3D.Distance(local, closest);
    }

    // Enclosing world AABBs deliberately underestimate separation between rotated
    // grids: candidate filtering only; actual blocking requires oriented overlap.
    public static double BoundsDistance(BoundingBoxD a, BoundingBoxD b)
    {
        var x = Math.Max(0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
        var y = Math.Max(0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
        var z = Math.Max(0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
        return Math.Sqrt(x * x + y * y + z * z);
    }

    public static bool BoundsOverlap(MyCubeGrid first, MyCubeGrid second)
    {
        var a = first.PositionComp.LocalAABB;
        var b = second.PositionComp.LocalAABB;
        var firstBounds = new MyOrientedBoundingBoxD(
            new BoundingBoxD(a.Min, a.Max), first.WorldMatrix);
        var secondBounds = new MyOrientedBoundingBoxD(
            new BoundingBoxD(b.Min, b.Max), second.WorldMatrix);
        return firstBounds.Intersects(ref secondBounds);
    }

    public static bool CheckEnemies(MyCubeGrid anchor, out string reason)
    {
        reason = null;
        var seen = new HashSet<MyCubeGrid>();
        try
        {
            var identity = MySession.Static?.LocalHumanPlayer?.Identity?.IdentityId ?? 0;
            if (identity == 0 || anchor == null || anchor.Closed || anchor.MarkedForClose)
            {
                reason = "Lanyard unavailable: player or grid unavailable.";
                return false;
            }
            // This cache lasts only for this scan. Ownership/sharing and faction
            // changes are therefore re-evaluated on the next scan as before.
            var checkedRelations = new Dictionary<long, HashSet<MyOwnershipShareModeEnum>>();
            foreach (var entity in MyEntities.GetEntities())
            {
                var grid = entity as MyCubeGrid;
                if (grid == null || grid.Closed || grid.MarkedForClose) continue;
                if (BoundsDistance(anchor.PositionComp.WorldAABB, grid.PositionComp.WorldAABB) > EnemyScanDistance)
                    continue;
                if (!BoundsOverlap(anchor, grid)) continue;
                seen.Add(grid);
                if (!ownershipCaches.TryGetValue(grid, out var cache))
                {
                    cache = new GridOwnershipCache(grid);
                    ownershipCaches.Add(grid, cache);
                }
                // Include the anchor itself: enemy-owned blocks on mixed grids count.
                foreach (var block in cache.Candidates())
                {
                    if (block.Closed || block.MarkedForClose || block.OwnerId == 0) continue;
                    var module = block.IDModule;
                    if (module == null) continue;
                    if (!checkedRelations.TryGetValue(block.OwnerId, out var shareModes))
                    {
                        shareModes = new HashSet<MyOwnershipShareModeEnum>();
                        checkedRelations.Add(block.OwnerId, shareModes);
                    }
                    if (!shareModes.Add(module.ShareMode)) continue;
                    if (!EnemyOwnership.IsEnemy(block.OwnerId, identity))
                        continue;
                    reason = "Lanyard blocked: enemy grid overlaps the linked grid bounds.";
                    return false;
                }
            }
            return true;
        }
        catch (Exception exception)
        {
            MyLog.Default.WriteLineAndConsole("Lanyard enemy check failed: " + exception);
            reason = "Lanyard disconnected: enemy check failed. See game log.";
            return false;
        }
        finally
        {
            if (reason != null)
                ClearCache();
            else
            {
                var stale = new List<MyCubeGrid>();
                foreach (var grid in ownershipCaches.Keys)
                    if (!seen.Contains(grid)) stale.Add(grid);
                foreach (var grid in stale)
                {
                    ownershipCaches[grid].Dispose();
                    ownershipCaches.Remove(grid);
                }
            }
        }
    }
}

using System;
using Lanyard;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using VRage.Game.ModAPI;

internal static partial class Program
{
    private static MyCubeGrid LinkedWeldGrid()
    {
        var grid = World();
        StartObserved();
        Check(CombatLockout.CanConnect(grid), "welding: quiet grid is available");
        return grid;
    }

    private static MySlimBlock WeldBlock(MyCubeGrid grid, float maxIntegrity, Action<MySlimBlock> beforeCorrection = null)
    {
        var block = new MySlimBlock { CubeGrid = grid, ComponentStack = new MyComponentStack(maxIntegrity) };
        // As in MySlimBlock.OnIntegrityChanged, only decreases raise grid damage.
        block.ComponentStack.IntegrityChanged += (stack, previous, current) =>
        {
            if (previous <= current) return;
            beforeCorrection?.Invoke(block);
            grid.Damage(block, current - previous);
        };
        grid.CubeBlocks.Add(block);
        return block;
    }

    private static void WeldingDamageTests()
    {
        // These two normalized deltas were also observed on the installed game
        // DLL for vanilla small and large light armor, respectively.
        const float completionDelta = -1.1920929E-07f;
        const float missingPlateDelta = -7.4505806E-09f;
        foreach (var example in new[] { (maximum: 100f, delta: completionDelta), (maximum: 2500f, delta: missingPlateDelta) })
        {
            var grid = LinkedWeldGrid();
            var block = WeldBlock(grid, example.maximum);
            var events = 0;
            grid.OnGridBlockDamaged += (target, amount, hit, attacker) =>
            {
                events++;
                Check(target == block && amount == example.delta, "welding: original game damage callback preserved");
            };
            block.ComponentStack.IncreaseMountLevel(25f, new MyConstructionStockpile());
            Check(events == 1 && block.ComponentStack.Integrity == 100f, "welding: plate mounted and correction dispatched");
            Check(!CombatLockout.IsBlocked, "welding: component boundary does not start a cooldown");
            grid.Damage(block, example.delta);
            Check(CombatLockout.IsBlocked, "welding: identical damage to the block after welding still blocks");
        }

        foreach (var hitAmount in new[] { 5f, -0.01f, missingPlateDelta })
        foreach (var otherTarget in new[] { false, true })
        {
            var grid = LinkedWeldGrid();
            var neighbor = WeldBlock(grid, 100f);
            var hitObserved = false;
            var block = WeldBlock(grid, 100f, welding =>
            {
                grid.Damage(otherTarget ? neighbor : welding, hitAmount);
                hitObserved = CombatLockout.IsBlocked && CombatLockout.RemainingSeconds == 120;
            });
            block.ComponentStack.IncreaseMountLevel(25f);
            Check(hitObserved && CombatLockout.IsBlocked,
                $"welding: damage {hitAmount} during the correction starts a full cooldown (other target: {otherTarget})");
        }

        foreach (var source in new[] {
            (otherBlock: true,  hit: (MyHitInfo?)null, attacker: 0L),
            (otherBlock: false, hit: new MyHitInfo(), attacker: 0L),
            (otherBlock: false, hit: (MyHitInfo?)null, attacker: 42L) })
        {
            var grid = LinkedWeldGrid();
            var neighbor = WeldBlock(grid, 100f);
            var block = WeldBlock(grid, 100f, welding =>
            {
                grid.Damage(source.otherBlock ? neighbor : welding, completionDelta, source.hit, source.attacker);
                Check(CombatLockout.IsBlocked, "welding: an equally small hit with a different target or attribution counts immediately");
            });
            block.ComponentStack.IncreaseMountLevel(25f);
        }

        var duplicate = LinkedWeldGrid();
        var sameBlock = WeldBlock(duplicate, 100f, welding => duplicate.Damage(welding, completionDelta));
        sameBlock.ComponentStack.IncreaseMountLevel(25f);
        Check(CombatLockout.IsBlocked && CombatLockout.RemainingSeconds == 120,
            "welding: two identical anonymous events on the same block can consume only one correction");

        var active = LinkedWeldGrid();
        active.Damage(5f);
        var originalDeadline = Deadline();
        WeldBlock(active, 100f).ComponentStack.IncreaseMountLevel(25f);
        WeldBlock(active, 2500f).ComponentStack.IncreaseMountLevel(25f, new MyConstructionStockpile());
        Check(CombatLockout.IsBlocked && Deadline() == originalDeadline, "welding: repeated corrections preserve an existing deadline");

        var failed = LinkedWeldGrid();
        var failure = new InvalidOperationException("Simulated construction callback failure");
        var interrupted = WeldBlock(failed, 100f, block => throw failure);
        Exception observed = null;
        try { interrupted.ComponentStack.IncreaseMountLevel(25f); }
        catch (Exception ex) { observed = ex; }
        Check(ReferenceEquals(observed, failure), "welding: vanilla callback exceptions propagate");
        failed.Damage(interrupted, completionDelta);
        Check(CombatLockout.IsBlocked, "welding: an interrupted correction leaves no damage exemption");
    }
}

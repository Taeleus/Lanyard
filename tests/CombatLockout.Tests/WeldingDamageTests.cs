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
        => WeldBlock(grid, new MyComponentStack(maxIntegrity), beforeCorrection);

    private static MySlimBlock WeldBlock(MyCubeGrid grid, MyComponentStack stack, Action<MySlimBlock> beforeCorrection = null)
    {
        var block = new MySlimBlock { CubeGrid = grid, ComponentStack = stack };
        // As in MySlimBlock.OnIntegrityChanged, only decreases raise grid damage.
        stack.IntegrityChanged += (_, previous, current) =>
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

        WeldDamageControls((grid, callback) => WeldBlock(grid, 100f, callback), completionDelta);
        var active = LinkedWeldGrid();
        active.Damage(5f);
        var originalDeadline = Deadline();
        WeldBlock(active, 100f).ComponentStack.IncreaseMountLevel(25f);
        WeldBlock(active, 2500f).ComponentStack.IncreaseMountLevel(25f, new MyConstructionStockpile());
        Check(CombatLockout.IsBlocked && Deadline() == originalDeadline, "welding: repeated corrections preserve an existing deadline");

        MediumCargoContainerWeldTests();
    }

    // SmallBlockMediumContainer in vanilla CubeBlocks_Logistics.sbc starts with
    // 20 Interior Plates (15 integrity each) and ends with 10 more. Components.sbc
    // gives a total of 30*15 + 10*30 + 4*1 + 4*40 + 1*5 = 919 integrity.
    private const float MediumCargoMax = 919f;
    private const float MediumCargoShortageDelta = -1.6763806E-08f;

    private static MySlimBlock MediumCargoAtFirstPlate(MyCubeGrid grid, Action<MySlimBlock> beforeCorrection = null)
        => WeldBlock(grid, new MyComponentStack(MediumCargoMax, initialIntegrity: 15f,
            componentEnd: 15f, nextComponent: "InteriorPlate"), beforeCorrection);

    private static void MediumCargoContainerWeldTests()
    {
        var grid = LinkedWeldGrid();
        var cargo = MediumCargoAtFirstPlate(grid);
        var empty = new MyConstructionStockpile();
        var events = 0;
        float delta = 0;
        float peakIntegrity = 0;
        cargo.ComponentStack.IntegrityChanged += (stack, previous, current) =>
        {
            if (current > previous) peakIntegrity = stack.Integrity;
        };
        grid.OnGridBlockDamaged += (block, amount, hit, attacker) =>
        {
            if (block != cargo) return;
            Check(!hit.HasValue && attacker == 0, "cargo: welding sends an anonymous integrity event");
            events++;
            delta = amount;
        };
        for (int i = 0; i < 10; i++)
        {
            cargo.ComponentStack.IncreaseMountLevel(25f, empty);
            Check(events == i + 1 && delta == MediumCargoShortageDelta &&
                empty.RequestedComponent == "InteriorPlate" && peakIntegrity == 15.0000152587890625f &&
                cargo.ComponentStack.Integrity == 15f,
                "cargo: each retry reaches the same missing plate and reports the issue #2 delta");
            Check(!CombatLockout.IsBlocked, "cargo: missing Interior Plate does not start the cooldown");
        }
        // Complete the final mounted plate on the same vanilla definition.
        var lastPlate = WeldBlock(grid, new MyComponentStack(MediumCargoMax, initialIntegrity: 918f, componentEnd: MediumCargoMax));
        lastPlate.ComponentStack.IncreaseMountLevel(25f, empty);
        Check(lastPlate.ComponentStack.Integrity == MediumCargoMax && !CombatLockout.IsBlocked,
            "cargo: finishing the container preserves availability");
        grid.Damage(cargo, MediumCargoShortageDelta);
        Check(CombatLockout.IsBlocked && CombatLockout.RemainingSeconds == 120,
            "cargo: identical anonymous damage outside welding starts the full cooldown");
        var deadline = Deadline();
        cargo.ComponentStack.IncreaseMountLevel(25f, empty);
        Check(Deadline() == deadline, "cargo: welding preserves an existing cooldown deadline");

        WeldDamageControls(MediumCargoAtFirstPlate, MediumCargoShortageDelta);
    }

    private static void WeldDamageControls(Func<MyCubeGrid, Action<MySlimBlock>, MySlimBlock> createBlock, float correctionDelta)
    {
        foreach (var hitAmount in new[] { 5f, -0.01f, -7.4505806E-09f })
        foreach (var otherTarget in new[] { false, true })
        {
            var grid = LinkedWeldGrid();
            var neighbor = WeldBlock(grid, 100f);
            var hitObserved = false;
            var block = createBlock(grid, welding =>
            {
                grid.Damage(otherTarget ? neighbor : welding, hitAmount);
                hitObserved = CombatLockout.IsBlocked && CombatLockout.RemainingSeconds == 120;
            });
            block.ComponentStack.IncreaseMountLevel(25f, new MyConstructionStockpile());
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
            var block = createBlock(grid, welding =>
            {
                grid.Damage(source.otherBlock ? neighbor : welding, correctionDelta, source.hit, source.attacker);
                Check(CombatLockout.IsBlocked, "welding: an equally small hit with a different target or attribution counts immediately");
            });
            block.ComponentStack.IncreaseMountLevel(25f, new MyConstructionStockpile());
        }

        var duplicate = LinkedWeldGrid();
        var sameBlock = createBlock(duplicate, welding => duplicate.Damage(welding, correctionDelta));
        sameBlock.ComponentStack.IncreaseMountLevel(25f, new MyConstructionStockpile());
        Check(CombatLockout.IsBlocked && CombatLockout.RemainingSeconds == 120,
            "welding: two identical anonymous events on the same block can consume only one correction");

        var failed = LinkedWeldGrid();
        var failure = new InvalidOperationException("Simulated construction callback failure");
        var interrupted = createBlock(failed, block => throw failure);
        Exception observed = null;
        try { interrupted.ComponentStack.IncreaseMountLevel(25f, new MyConstructionStockpile()); }
        catch (Exception ex) { observed = ex; }
        Check(ReferenceEquals(observed, failure), "welding: vanilla callback exceptions propagate");
        failed.Damage(interrupted, correctionDelta);
        Check(CombatLockout.IsBlocked, "welding: an interrupted correction leaves no damage exemption");
    }
}

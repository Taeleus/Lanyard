using System;
using System.Runtime.CompilerServices;

namespace Sandbox.Game.Entities
{
    public class MyConstructionStockpile
    {
        public bool HasNextPlate;
    }

    // The first steel plate has 100 integrity. Keep the four setter call sites
    // from the native mount routine so the production Harmony transpiler runs.
    public class MyComponentStack
    {
        private float integrity = 75f;
        public MyComponentStack(float maxIntegrity) { MaxIntegrity = maxIntegrity; }

        public float MaxIntegrity { get; }
        public float IntegrityRatio => Integrity / MaxIntegrity;
        public event Action<MyComponentStack, float, float> IntegrityChanged;

        public float Integrity
        {
            get => integrity;
            private set
            {
                var previous = IntegrityRatio;
                integrity = value;
                IntegrityChanged?.Invoke(this, previous, IntegrityRatio);
            }
        }

        public void IncreaseMountLevel(float mountAmount, MyConstructionStockpile stockpile = null)
            => IncreaseMountLevelInternal(mountAmount, stockpile);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void IncreaseMountLevelInternal(float mountAmount, MyConstructionStockpile stockpile)
        {
            const float componentBoundary = 1.5259022E-05f;
            var remaining = 100f - Integrity;
            if (mountAmount < remaining)
            {
                Integrity += mountAmount;
                return;
            }
            Integrity += remaining + componentBoundary;
            if (MaxIntegrity == 100f)
                Integrity = MaxIntegrity;
            else if (stockpile != null && !stockpile.HasNextPlate)
                Integrity -= componentBoundary;
        }
    }
}

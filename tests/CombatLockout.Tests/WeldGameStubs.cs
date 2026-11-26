using System;
using System.Runtime.CompilerServices;

namespace Sandbox.Game.Entities
{
    public class MyConstructionStockpile
    {
        public string RequestedComponent { get; private set; }

        public bool RemoveItem(string subtype)
        {
            RequestedComponent = subtype;
            return false; // The construction stockpile in these fixtures is empty.
        }
    }

    // Model one mounted component. Keep the four native setter call sites so
    // the production Harmony transpiler runs against the real float operations.
    public class MyComponentStack
    {
        private readonly float componentEnd;
        private readonly string nextComponent;
        private float integrity;

        public MyComponentStack(float maxIntegrity, float initialIntegrity = 75f,
            float componentEnd = 100f, string nextComponent = "SteelPlate")
        {
            MaxIntegrity = maxIntegrity;
            integrity = initialIntegrity;
            this.componentEnd = componentEnd;
            this.nextComponent = nextComponent;
        }

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
            var remaining = componentEnd - Integrity;
            if (mountAmount < remaining)
            {
                Integrity += mountAmount;
                return;
            }
            Integrity += remaining + componentBoundary;
            if (MaxIntegrity == componentEnd)
                Integrity = MaxIntegrity;
            else if (stockpile != null && !stockpile.RemoveItem(nextComponent))
                Integrity -= componentBoundary;
        }
    }
}

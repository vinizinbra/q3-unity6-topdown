namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks down PixieBombCharge.Remaining, removing it once it expires unused - a charge left
    // unspent after the window closes "cures" instead of empowering a much-later throw, and removing
    // the whole component is also what resets every line's own multiplier fields back to clean.
    // Same shape as ExplodeOnDeathTimerSystem. Renamed from PixieHotFuseTimerSystem when the charge
    // itself was generalized to serve both Hot Fuse and Blast Jump.
    [Preserve]
    public unsafe class PixieBombChargeSystem : SystemMainThreadFilter<PixieBombChargeSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            filter.Charge->Remaining -= f.DeltaTime;

            if (filter.Charge->Remaining <= FP._0)
            {
                f.Remove<PixieBombCharge>(filter.Entity);
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public PixieBombCharge* Charge;
        }
    }
}

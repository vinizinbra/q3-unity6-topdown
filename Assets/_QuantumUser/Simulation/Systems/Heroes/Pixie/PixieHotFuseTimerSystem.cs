namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks down PixieHotFuseCharge.Remaining, removing it once it expires unused - a charge left
    // unspent after the window closes "cures" instead of empowering a much-later throw. Same shape
    // as ExplodeOnDeathTimerSystem.
    [Preserve]
    public unsafe class PixieHotFuseTimerSystem : SystemMainThreadFilter<PixieHotFuseTimerSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            filter.Charge->Remaining -= f.DeltaTime;

            if (filter.Charge->Remaining <= FP._0)
            {
                f.Remove<PixieHotFuseCharge>(filter.Entity);
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public PixieHotFuseCharge* Charge;
        }
    }
}

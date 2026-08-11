namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks down ExplodeOnDeath.Remaining for every enemy currently marked (see
    // DamageUtility.TryMarkExplodeOnDeath), removing the tag once it expires unfulfilled - an enemy
    // left alone long enough after being marked "cures" instead of carrying the tag forever until it
    // happens to die some other way. Same shape as JuggernautDischargeCooldownSystem, its own system
    // for the same reason: needs to keep counting down regardless of whether the marking hero is
    // still nearby or even still using the upgrade.
    [Preserve]
    public unsafe class ExplodeOnDeathTimerSystem : SystemMainThreadFilter<ExplodeOnDeathTimerSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            filter.Explode->Remaining -= f.DeltaTime;

            if (filter.Explode->Remaining <= FP._0)
            {
                f.Remove<ExplodeOnDeath>(filter.Entity);
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public ExplodeOnDeath* Explode;
        }
    }
}

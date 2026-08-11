namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Continuous flat-rate heal for anything with Health.RegenRate above 0 - most heroes start at
    // 0 (see CharacterData.BaseHealthRegenRate) until a HealthRegenUpgradeData grant raises it.
    // Unlike ShieldSystem there's no on-hit delay to gate on - this is meant to be a small constant
    // trickle rather than a big chunk worth suppressing while under fire.
    [Preserve]
    public unsafe class HealthRegenSystem : SystemMainThreadFilter<HealthRegenSystem.Filter>
    {
        // Batch window - regen accumulates for this long, then applies as ONE heal. Keeps the heal
        // event/particle to once a second instead of once a frame (ApplyFlatHeal raises EntityHealed
        // on every call - see HealUtility/EffectsManager.OnEntityHealed).
        private static readonly FP RegenInterval = FP._1;

        public override void Update(Frame f, ref Filter filter)
        {
            Health* health = filter.Health;

            if (health->RegenRate <= FP._0)
                return;

            if (health->CurrentHealth >= health->MaxHealth)
                return;

            health->RegenTimer += f.DeltaTime;

            if (health->RegenTimer < RegenInterval)
                return;

            // Heal the full accumulated window (RegenRate * elapsed), not a fixed RegenRate * 1, so
            // the effective HP/sec stays identical to the old per-frame version - just delivered in
            // one heal (one particle) per second. Reset to 0 to start the next window clean.
            FP amount = health->RegenRate * health->RegenTimer;
            health->RegenTimer = FP._0;

            HealUtility.ApplyFlatHeal(f, filter.Entity, filter.Entity, health, amount);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Health* Health;
        }
    }
}

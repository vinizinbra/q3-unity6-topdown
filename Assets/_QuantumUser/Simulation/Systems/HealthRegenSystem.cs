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
        public override void Update(Frame f, ref Filter filter)
        {
            Health* health = filter.Health;

            if (health->RegenRate <= FP._0)
                return;

            if (health->CurrentHealth >= health->MaxHealth)
                return;

            HealUtility.ApplyFlatHeal(f, filter.Entity, filter.Entity, health, health->RegenRate * f.DeltaTime);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Health* Health;
        }
    }
}

namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drives Brute's Protector Aura - continuously finds nearby enemies (Intimidate, plus Iron
    // Presence's slow/reduced-knockback-resistance if that ascension is active) and nearby allies
    // (Guardian's Damage Reduction, if active). Refresh-only, same idiom SentryAuraSystem already
    // uses for its own aura - reapplied every tick a target stays in range, so it decays on its own
    // the instant it leaves, no removal logic needed.
    [Preserve]
    public unsafe class ProtectorAuraSystem : SystemMainThreadFilter<ProtectorAuraSystem.Filter>
    {
        private static readonly FP AuraRefreshDuration = FP._1;

        public override void Update(Frame f, ref Filter filter)
        {
            ProtectorAura* aura = filter.ProtectorAura;
            FPVector3 center = filter.Transform3D->Position;

            ApplyToEnemies(f, aura, center);
            ApplyToAllies(f, aura, center);
        }

        private static void ApplyToEnemies(Frame f, ProtectorAura* aura, FPVector3 center)
        {
            var enemies = f.Filter<Enemy, Transform3D>();

            while (enemies.Next(out EntityRef enemyEntity, out Enemy _, out Transform3D enemyTransform))
            {
                if ((enemyTransform.Position - center).SqrMagnitude > aura->Radius * aura->Radius)
                    continue;

                StatusEffectUtility.ApplyIntimidate(f, enemyEntity, AuraRefreshDuration, aura->IntimidateDamageMultiplier);

                // Iron Presence - both off (slow at 0, resist multiplier at 1) until that ascension
                // sets them, so this is a no-op either way until then.
                if (aura->IntimidateSlowMultiplier > FP._0)
                {
                    StatusEffectUtility.ApplyIce(f, enemyEntity, AuraRefreshDuration, aura->IntimidateSlowMultiplier);
                }

                if (aura->IntimidateKnockbackTakenMultiplier > FP._1)
                {
                    StatusEffectUtility.ApplyKnockbackTaken(f, enemyEntity, AuraRefreshDuration, aura->IntimidateKnockbackTakenMultiplier);
                }
            }
        }

        // Guardian only - 0 AllyDamageReductionAmount (the base passive's default) means the
        // ascension hasn't been taken, so this is skipped entirely. Includes Brute himself if he's
        // standing inside his own aura (FindPlayersInRadius doesn't exclude the source) - the
        // Protector protecting himself too is a reasonable reading of the aura, not a bug.
        private static void ApplyToAllies(Frame f, ProtectorAura* aura, FPVector3 center)
        {
            if (aura->AllyDamageReductionAmount <= FP._0)
                return;

            var allies = EnemyMovementUtility.FindPlayersInRadius(f, center, aura->Radius);

            for (int i = 0; i < allies.Count; i++)
            {
                StatusEffectUtility.ApplyGuardianDamageReduction(f, allies[i].Entity, AuraRefreshDuration, aura->AllyDamageReductionAmount);
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public ProtectorAura* ProtectorAura;
        }
    }
}

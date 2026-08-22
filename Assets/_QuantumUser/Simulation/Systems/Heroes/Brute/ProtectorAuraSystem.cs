namespace Quantum
{
    using System;
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

        // Guardian only - both effects are off (DR at 0, knockback multiplier at 1) until that
        // ascension sets them, so this is skipped entirely otherwise. Includes Brute himself if he's
        // standing inside his own aura (FindPlayersInRadius doesn't exclude the source) - the
        // Protector protecting himself too is a reasonable reading of the aura, not a bug.
        //
        // The DR goes through the SHARED aura-DR slot (StatusEffectUtility.ApplyAuraDamageReduction),
        // which is what enforces the brief's "Guardian aura from multiple Brutes must NOT stack
        // additively": two Brutes' auras write the same slot, strongest wins, no per-hero special
        // case anywhere.
        private static void ApplyToAllies(Frame f, ProtectorAura* aura, FPVector3 center)
        {
            bool hasDamageReduction = aura->AllyDamageReductionAmount > FP._0;
            bool hasKnockbackResist = aura->AllyKnockbackTakenMultiplier > FP._0 && aura->AllyKnockbackTakenMultiplier < FP._1;

            if (hasDamageReduction == false && hasKnockbackResist == false)
                return;

            Span<EntityRef> allies = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int alliesCount = EnemyMovementUtility.FindPlayersInRadius(f, center, aura->Radius, allies);
            for (int i = 0; i < alliesCount; i++)
            {
                if (hasDamageReduction == true)
                {
                    StatusEffectUtility.ApplyAuraDamageReduction(f, allies[i], AuraRefreshDuration, aura->AllyDamageReductionAmount);
                }

                if (hasKnockbackResist == true)
                {
                    StatusEffectUtility.ApplyKnockbackTaken(f, allies[i], AuraRefreshDuration, aura->AllyKnockbackTakenMultiplier);
                }
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

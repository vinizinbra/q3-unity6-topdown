namespace Quantum
{
    using Photon.Deterministic;

    // Direct Hit's damage bonus + rank 3 knockback (see Heroes/Pixie/DemolitionMastery.qtn) reacts
    // to "how close is this target to the explosion's own center" - resolves the distance fraction
    // once and applies both, rather than recomputing it twice. Called from the only two places in
    // the codebase where a radius, a center, and a per-target position are all known together for a
    // genuine explosion: HitEffectUtility.ApplyInRadius (bomb-type blasts) and HitEffectUtility.
    // ApplyDamageInRadius (weapon-perk-type blasts, reached via ApplyExplosion). Gated entirely by
    // TryGetPointer on the owner's own DirectHitUpgrade - an owner without the trait costs one failed
    // pointer lookup, zero behavior change, same idiom every other optional reaction here follows.
    public static unsafe class DemolitionMasteryUtility
    {
        // damage is passed by ref so Direct Hit can scale it in place before the caller's own
        // ApplyDamage/HitEffectContext consumes it - matches how DamageEffectData already applies
        // its own DamageMultiplier before calling ApplyDamage.
        public static void ApplyProximityEffects(Frame f, EntityRef owner, EntityRef target,
            FPVector3 center, FP radius, FPVector3 targetPosition, ref FP damage)
        {
            if (radius <= FP._0)
                return;

            FP distanceFraction = FPVector3.Distance(center, targetPosition) / radius;

            ApplyDirectHit(f, owner, target, distanceFraction, targetPosition - center, ref damage);
        }

        // Binary inner zone, matching Direct Hit's own "Inner 35%... multiplier" design - a target
        // right at the edge of that zone still counts as inside it (<=), so a bomb's own directly-
        // struck target (distanceFraction 0) always qualifies. Rank 3's knockback (HasKnockback) uses
        // the exact same inner-zone gate and the same arcade falloff the old standalone Concussive
        // Force ascension used: full KnockbackForce out to InnerRadiusFraction, then a linear taper
        // to 0 at the blast edge - Elite tier additionally scales the result down; Boss needs nothing
        // here at all (TierStats/BossRuntimeState already resist/track displacement regardless of
        // what triggered the damage).
        private static void ApplyDirectHit(Frame f, EntityRef owner, EntityRef target, FP distanceFraction,
            FPVector3 pushDirection, ref FP damage)
        {
            if (f.Unsafe.TryGetPointer<DirectHitUpgrade>(owner, out var directHit) == false)
                return;

            if (distanceFraction > directHit->InnerRadiusFraction)
                return;

            damage *= FP._1 + directHit->DamageMultiplierBonus;

            if (directHit->HasKnockback == false || target == owner)
                return;

            FP taperRange = FP._1 - directHit->InnerRadiusFraction;
            FP falloff = taperRange > FP._0
                ? FPMath.Clamp((FP._1 - distanceFraction) / taperRange, FP._0, FP._1)
                : FP._1;

            FP force = directHit->KnockbackForce * falloff * ResolveEliteMultiplier(f, target, directHit->KnockbackEliteMultiplier);

            if (force <= FP._0)
                return;

            DamageUtility.ApplyKnockback(f, target, pushDirection, force, directHit->KnockbackUpwardForce, owner);
        }

        private static FP ResolveEliteMultiplier(Frame f, EntityRef target, FP eliteMultiplier)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return FP._1;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            return data.Tier == EnemyTier.Elite ? eliteMultiplier : FP._1;
        }
    }
}

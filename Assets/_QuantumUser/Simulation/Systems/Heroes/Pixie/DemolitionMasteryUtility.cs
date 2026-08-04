namespace Quantum
{
    using Photon.Deterministic;

    // Direct Hit's damage bonus and Concussive Force's knockback (see Heroes/Pixie/
    // DemolitionMastery.qtn) both react to "how close is this target to the explosion's own
    // center" - a single shared entry point resolves the distance fraction once and applies both,
    // rather than each caller/each trait recomputing it independently. Called from the only two
    // places in the codebase where a radius, a center, and a per-target position are all known
    // together for a genuine explosion: HitEffectUtility.ApplyInRadius (bomb-type blasts) and
    // HitEffectUtility.ApplyDamageInRadius (weapon-perk-type blasts, reached via ApplyExplosion).
    // Gated entirely by TryGetPointer on the owner's own upgrade components - an owner without
    // either trait costs one failed pointer lookup each, zero behavior change, same idiom every
    // other optional reaction in this codebase already follows.
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

            ApplyDirectHit(f, owner, distanceFraction, ref damage);
            TryApplyConcussiveForce(f, owner, target, targetPosition - center, distanceFraction);
        }

        // Binary inner zone, matching Direct Hit's own "Inner 35%... multiplier" design - a target
        // right at the edge of that zone still counts as inside it (<=), so a bomb's own directly-
        // struck target (distanceFraction 0) always qualifies.
        private static void ApplyDirectHit(Frame f, EntityRef owner, FP distanceFraction, ref FP damage)
        {
            if (f.Unsafe.TryGetPointer<DirectHitUpgrade>(owner, out var directHit) == false)
                return;

            if (distanceFraction > directHit->InnerRadiusFraction)
                return;

            damage *= FP._1 + directHit->DamageMultiplierBonus;
        }

        // Arcade falloff, per explicit design direction: full Force out to InnerRadiusFraction (a
        // generous sweet spot, not a pinpoint), then a linear taper to 0 at the blast edge. Elite
        // tier additionally scales the result down; Boss needs nothing here at all - see this
        // component's own qtn comment for why (TierStats/BossRuntimeState already do that work,
        // regardless of what triggered the damage).
        private static void TryApplyConcussiveForce(Frame f, EntityRef owner, EntityRef target,
            FPVector3 pushDirection, FP distanceFraction)
        {
            if (f.Unsafe.TryGetPointer<ConcussiveForceUpgrade>(owner, out var concussive) == false)
                return;

            if (target == owner)
                return;

            FP taperRange = FP._1 - concussive->InnerRadiusFraction;
            FP falloff = taperRange > FP._0
                ? FPMath.Clamp((FP._1 - distanceFraction) / taperRange, FP._0, FP._1)
                : FP._1;

            FP force = concussive->Force * falloff * ResolveEliteMultiplier(f, target, concussive->EliteMultiplier);

            if (force <= FP._0)
                return;

            DamageUtility.ApplyKnockback(f, target, pushDirection, force, concussive->UpwardForce, owner);
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

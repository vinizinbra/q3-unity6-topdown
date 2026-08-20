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
        // Unstable Mixture - resolved ONCE per blast (not per target), right before
        // HitEffectUtility.ApplyInRadius/ApplyDamageInRadius run their overlap loop, so the whole
        // explosion is empowered rather than each caught enemy re-rolling it. Consumes every banked
        // stack and, at max stacks with rank 3 equipped, schedules the delayed secondary blast.
        //
        // Called only for a genuine, non-chained explosion (isExplosion && !isChainedExplosion at the
        // call site) - see UnstableMixture.qtn for why that pairing is what makes the whole line
        // recursion-safe with no bespoke guard. A no-op (leaves damage/radius untouched) for any owner
        // without the Ascension or with no stacks banked, same idiom ApplyProximityEffects uses.
        public static void ResolveExplosionEmpowerment(Frame f, EntityRef owner, FPVector3 center, ref FP damage, ref FP radius)
        {
            if (f.Unsafe.TryGetPointer<UnstableMixtureUpgrade>(owner, out var mixture) == false || mixture->Stacks == 0)
                return;

            byte stacks = mixture->Stacks;
            mixture->Stacks = 0;

            FP empoweredDamage = damage * (FP._1 + mixture->DamageBonusPerStack * stacks);
            FP empoweredRadius = radius * (FP._1 + mixture->RadiusBonusPerStack * stacks);

            damage = empoweredDamage;
            radius = empoweredRadius;

            if (mixture->SecondaryDamagePercent <= FP._0 || stacks < mixture->MaxStacks)
                return;

            // Overwrites any still-pending secondary rather than queueing a second one - see
            // DelayedBlast's own comment. IsChainedExplosion is what makes the secondary a payout
            // rather than a new link: it can neither consume empowerment (this method is only called
            // for a non-chained blast) nor generate more (DamageUtility only banks stacks off a
            // non-chained explosion kill).
            f.AddOrGet<DelayedBlast>(owner, out var delayed);
            delayed->Remaining = mixture->SecondaryDelay;
            delayed->Position = center;
            delayed->Damage = empoweredDamage * mixture->SecondaryDamagePercent;
            delayed->Radius = empoweredRadius * mixture->SecondaryRadiusMultiplier;
            delayed->StunDuration = FP._0;
            delayed->IsExplosion = true;
            delayed->IsChainedExplosion = true;

            Log.Debug($"[Skill] {owner}'s Unstable Mixture spent {stacks} stack(s) and armed a secondary blast at {center}");
        }

        // Stack GAIN - called from DamageUtility.ApplyDamage's own death branch, but only for a kill
        // dealt by a genuine, non-chained explosion. See UnstableMixture.qtn. Holds at MaxStacks
        // rather than overflowing.
        public static void OnExplosionKill(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<UnstableMixtureUpgrade>(owner, out var mixture) == false)
                return;

            if (mixture->Stacks >= mixture->MaxStacks)
                return;

            mixture->Stacks++;
        }

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

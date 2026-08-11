namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Buffs allies standing within a Sentry's own Range - Fire Rate (SentryFireRateAuraUpgrade, via
    // StatusEffectUtility.ApplyHaste) and Shield Area Rate (SentryShieldAreaRateUpgrade, via
    // StatusEffectUtility.ApplyShieldRegen) are each optional and independent, baked onto the sentry
    // itself at spawn time (see SpawnSentrySkillAction) rather than read live off the caster who
    // deployed it, so the aura keeps working even if that caster is no longer nearby/alive. Neither
    // is a required filter field (a sentry might have zero, one, or both) - skips entirely if it has
    // neither, so a plain damage-only sentry doesn't pay for the FindPlayersInRadius query at all.
    //
    // Continuously refreshed rather than a persistent flag, so each buff naturally fades some time
    // after leaving the radius instead of needing its own explicit removal path. Fire Rate reuses
    // RuntimeConfig.EffectConfig.HasteDuration - the same lingering window Zara's HasteOnHealUpgrade
    // grants via HasteEffectData - so "how long Haste lingers" is tuned in one place regardless of
    // source, rather than this aura fading near-instantly on a short local constant while the
    // Speaker's much sparser heal-pulse cadence leaves a multi-second tail. Shield Area Rate has no
    // other source granting it, so it keeps its own short AuraRefreshDuration instead.
    [Preserve]
    public unsafe class SentryAuraSystem : SystemMainThreadFilter<SentryAuraSystem.Filter>
    {
        private static readonly FP AuraRefreshDuration = FP._1;

        // Shield regen reaches a smaller area than fire rate/targeting - half of the sentry's own
        // Range, not the full radius - so it needs its own OverlapShape query instead of sharing the
        // fire-rate one below.
        private static readonly FP ShieldAreaRangeRatio = FP._0_50;

        public override void Update(Frame f, ref Filter filter)
        {
            bool hasFireRate = f.Unsafe.TryGetPointer<SentryFireRateAuraUpgrade>(filter.Entity, out var fireRate) == true;
            bool hasShieldRate = f.Unsafe.TryGetPointer<SentryShieldAreaRateUpgrade>(filter.Entity, out var shieldRate) == true;

            if (hasFireRate == false && hasShieldRate == false)
                return;

            if (hasFireRate == true)
            {
                FP hasteDuration = ResolveHasteDuration(f);
                var hits = EnemyMovementUtility.FindPlayersInRadius(f, filter.Transform3D->Position, filter.Sentry->Range);

                for (int i = 0; i < hits.Count; i++)
                {
                    StatusEffectUtility.ApplyHaste(f, hits[i].Entity, filter.Entity, hasteDuration, fireRate->AttackSpeedMultiplier);
                }
            }

            if (hasShieldRate == true)
            {
                FP shieldRadius = filter.Sentry->Range * ShieldAreaRangeRatio;
                var hits = EnemyMovementUtility.FindPlayersInRadius(f, filter.Transform3D->Position, shieldRadius);

                // Throttled to once a second (same shape as ShieldSystem's own stuck-shield error) -
                // the aura's radius is HALF of Sentry.Range, smaller than the range indicator/circle
                // players actually see, so "standing inside the visible ring but nothing happens" is
                // expected if they're outside this smaller radius - this line is what tells you
                // whether that's what's going on versus the upgrade never having reached this sentry.
                if (f.Number % f.UpdateRate == 0)
                    Log.Debug($"[Sentry] {filter.Entity} Shield Area Rate - {hits.Count} ally(ies) within {shieldRadius} (half of {filter.Sentry->Range})");

                for (int i = 0; i < hits.Count; i++)
                {
                    StatusEffectUtility.ApplyShieldRegen(f, hits[i].Entity, AuraRefreshDuration, shieldRate->ShieldRegenMultiplier);
                }
            }
        }

        // Falls back to the aura's own short constant if RuntimeConfig.EffectConfig can't resolve,
        // same defensive shape as HasteEffectData.Apply's own missing-config check - a config asset
        // that isn't assigned yet shouldn't leave the aura granting a permanent (never-decaying)
        // buff, it should just behave like it did before this shared duration existed.
        private static FP ResolveHasteDuration(Frame f)
        {
            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
            {
                return AuraRefreshDuration;
            }

            return config.HasteDuration;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public Sentry* Sentry;
        }
    }
}

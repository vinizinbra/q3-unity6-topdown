namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Lux's Fortification Ascension, ranks 2-3 - the support half of "fight around the machine".
    // Reapplies Shield Battery (a flat Shield-per-second trickle) and Fire Support (an
    // AllyBuffEffectData: Fire Rate + Damage Reduction) to allies standing inside the sentry's aura.
    //
    // Both are baked onto the sentry itself at deploy time (see SpawnSentrySkillAction) rather than
    // read live off the Lux who deployed it, so the aura keeps working even once she's no longer
    // nearby or alive.
    //
    // Continuously refreshed rather than a persistent flag, so each buff naturally fades shortly after
    // leaving the radius instead of needing its own explicit removal path - the same idiom
    // ProtectorAuraSystem uses. That, plus Fire Support's Damage Reduction landing in the single
    // shared aura-DR slot (take-the-stronger, see StatusEffectUtility.ApplyAuraDamageReduction), is
    // what makes "buffs from multiple Sentries must not stack" true by construction: two Sentries
    // covering the same ally write the same slot, and the stronger simply wins.
    //
    // Skips entirely for a sentry with neither upgrade, so a baseline machine doesn't pay for the
    // FindPlayersInRadius query at all.
    [Preserve]
    public unsafe class SentryAuraSystem : SystemMainThreadFilter<SentryAuraSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (f.Unsafe.TryGetPointer<SentryFortificationUpgrade>(filter.Entity, out var fortification) == false)
                return;

            bool hasShieldBattery = fortification->AllyShieldPerSecond > FP._0;
            bool hasFireSupport = fortification->FireSupportEffect.IsValid;

            if (hasShieldBattery == false && hasFireSupport == false)
                return;

            FP ratio = fortification->AuraRangeRatio > FP._0 ? fortification->AuraRangeRatio : FP._1;
            FP auraRadius = filter.Sentry->Range * ratio;

            if (auraRadius <= FP._0)
                return;

            Span<EntityRef> allies = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int alliesCount = EnemyMovementUtility.FindPlayersInRadius(f, filter.Transform3D->Position, auraRadius, allies);
            if (alliesCount == 0)
                return;

            HitEffectData fireSupport = hasFireSupport ? f.FindAsset(fortification->FireSupportEffect) : null;

            for (int i = 0; i < alliesCount; i++)
            {
                EntityRef ally = allies[i];

                // Shield Battery - a real per-second amount, converted to this tick's share. Flat by
                // design: the old version multiplied the ally's OWN shield recharge rate, which scaled
                // with the recipient and was effectively unbounded.
                if (hasShieldBattery == true && f.Unsafe.TryGetPointer<Shield>(ally, out var shield) == true)
                {
                    ShieldUtility.ApplyFlatShield(f, ally, filter.Sentry->Owner, shield, fortification->AllyShieldPerSecond * f.DeltaTime);
                }

                if (fireSupport == null)
                    continue;

                var context = new HitEffectContext
                {
                    // Owner is the deploying Lux, not the sentry - so the Haste inside the buff bundle
                    // keys its per-source slot to HER, meaning two Sentries she owns share one slot
                    // (they're the same source and must not compound) while a second Lux's Sentry gets
                    // its own.
                    Owner = filter.Sentry->Owner,
                    Target = ally,
                    Position = filter.Transform3D->Position,
                    PushDirection = FPVector3.Zero,
                    Damage = FP._0,
                    Source = DamageSource.Skill,
                    Element = ElementType.Neutral,
                    SourceEntity = filter.Entity,
                };

                fireSupport.Apply(f, ref context);
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public Sentry* Sentry;
        }
    }
}

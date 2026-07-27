namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Swaps the entity's own AreaDamage.Damage/TargetMask/Effects between AlternatingArea's Heal and
    // Damage configs right before AreaDamageSystem applies them - must run before AreaDamageSystem
    // (see SystemSetup.User). Predicts the same "is a pulse about to fire" condition
    // AreaDamageSystem itself checks a moment later, rather than tracking a second timer -
    // TickInterval/TickTimer stay AreaDamage's alone.
    [Preserve]
    public unsafe class AlternatingAreaSystem : SystemMainThreadFilter<AlternatingAreaSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.AreaDamage->TickTimer > f.DeltaTime)
                return;

            bool healNext = filter.Alternating->CurrentlyHealing == false;
            filter.Alternating->CurrentlyHealing = healNext;

            if (healNext == true)
            {
                filter.AreaDamage->Damage = FP._0;
                filter.AreaDamage->TargetMask = filter.Alternating->HealTargetMask;
                CopyEffects(filter.Alternating->HealEffects, filter.AreaDamage);
            }
            else
            {
                filter.AreaDamage->Damage = filter.Alternating->DamageAmount;
                filter.AreaDamage->TargetMask = filter.Alternating->DamageMask;
                CopyEffects(filter.Alternating->DamageEffects, filter.AreaDamage);

                filter.Alternating->DamagePulseCount++;
                TryApplyStunUpgrade(f, filter.Entity, filter.Alternating->DamagePulseCount, filter.AreaDamage);
            }

            f.Events.AlternatingAreaPulsed(filter.Entity, healNext);
        }

        // AlternatingArea.DamageEffects (copied in above) already includes anything baked in at
        // spawn time - see SpawnAlternatingAreaEffectData.ApplyPoisonUpgrade. That used to be
        // checked live here instead, off the speaker's owner, but the owning upgrade's Begin/End
        // only brackets the throw itself, which ends before this speaker's later pulses would ever
        // see it - baking it in once at spawn is what actually works for a speaker's whole lifetime.
        private static void CopyEffects(FixedArray<AssetRef<HitEffectData>> source, AreaDamage* areaDamage)
        {
            for (int i = 0; i < source.Length; i++)
            {
                areaDamage->Effects[i] = source[i];
            }
        }

        // StunEveryWavesUpgrade (see Heroes/Zara/StunEveryWavesSkillAction) - unlike
        // PoisonDamageWavesUpgrade/KnockbackOnDamageUpgrade, this can't be baked in once at spawn,
        // since it should only apply on every Interval-th pulse, not every one. Checking it live
        // here, every damage pulse, is safe specifically because the upgrade is Begin-only and
        // never revoked - there's no End racing against a live read that never stops happening.
        private static void TryApplyStunUpgrade(Frame f, EntityRef speaker, int pulseCount, AreaDamage* areaDamage)
        {
            if (f.Unsafe.TryGetPointer<AreaOwner>(speaker, out var areaOwner) == false)
                return;

            if (f.Unsafe.TryGetPointer<StunEveryWavesUpgrade>(areaOwner->Owner, out var upgrade) == false
                || upgrade->Interval == 0 || upgrade->StunEffect.IsValid == false)
                return;

            if (pulseCount % upgrade->Interval != 0)
                return;

            for (int i = 0; i < areaDamage->Effects.Length; i++)
            {
                if (areaDamage->Effects[i].IsValid == true)
                    continue;

                areaDamage->Effects[i] = upgrade->StunEffect;

                Log.Debug($"[Skill] {speaker}'s wave {pulseCount} stunned (every {upgrade->Interval} waves)");
                return;
            }

            Log.Error($"[Skill] {areaOwner->Owner}'s StunEveryWavesUpgrade couldn't fit - {speaker}'s DamageEffects already fills all 4 slots this pulse");
        }

        public struct Filter
        {
            public EntityRef Entity;
            public AlternatingArea* Alternating;
            public AreaDamage* AreaDamage;
        }
    }
}

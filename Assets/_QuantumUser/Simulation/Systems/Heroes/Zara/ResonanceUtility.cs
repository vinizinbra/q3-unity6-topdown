namespace Quantum
{
    using Photon.Deterministic;

    // Gain/pulse side of Zara's Resonance passive - see Resonance.qtn (the component). Mirrors
    // AdrenalineUtility's static-utility shape.
    public static unsafe class ResonanceUtility
    {
        public static void OnDamageDealt(Frame f, EntityRef owner, FP damage)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(owner, out var resonance) == false)
                return;

            AddResonance(f, owner, resonance, damage * resonance->GenerationPerDamage);
        }

        // Flat grant - used by the Quick Tempo dash ascension (dashing generates Resonance), unlike
        // OnDamageDealt's damage-scaled gain.
        public static void Grant(Frame f, EntityRef owner, FP amount)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(owner, out var resonance) == false)
                return;

            AddResonance(f, owner, resonance, amount);
        }

        private static void AddResonance(Frame f, EntityRef owner, Resonance* resonance, FP amount)
        {
            if (amount <= FP._0)
                return;

            resonance->Current += amount;

            if (resonance->Current < resonance->Max)
                return;

            // Carries the remainder forward rather than dropping it to 0, so a big hit that
            // overshoots the threshold doesn't waste progress toward the next pulse.
            resonance->Current -= resonance->Max;

            if (f.Unsafe.TryGetPointer<Transform3D>(owner, out var transform) == true)
            {
                FirePulse(f, owner, resonance, transform->Position);
            }
        }

        // Direct HealUtility/DamageUtility calls rather than the HitEffectData/Effects-list
        // indirection (HitEffectUtility.ApplyInRadius) - Zara's pulse doesn't need per-instance
        // customizable on-hit effects, just a simultaneous heal-allies + damage-enemies burst, so
        // there's nothing that indirection would buy here.
        public static void FirePulse(Frame f, EntityRef owner, Resonance* resonance, FPVector3 position)
        {
            // Skill Area (CharacterStats.AreaRadiusMultiplier) - one effective radius for the whole
            // pulse (heal allies, damage enemies, shockwave), so the healing/damage area grows with
            // the upgrade. 1x for anyone without it.
            FP radius = resonance->Radius * StatUtility.GetAreaMultiplier(f, owner);

            var allies = EnemyMovementUtility.FindPlayersInRadius(f, position, radius);

            for (int i = 0; i < allies.Count; i++)
            {
                HealUtility.ApplyHeal(f, allies[i].Entity, owner, resonance->HealPercent);
            }

            resonance->PulseCount++;

            // Remix ascension - resolved once per pulse (not per enemy), so every enemy this pulse
            // catches gets the same randomly-chosen effect, and the pulse's own shockwave (below)
            // can be tinted to match it.
            AssetRef<HitEffectData> remixEffect = ResolveRemixEffect(f, resonance);

            var enemies = f.Filter<Enemy, Transform3D>();

            while (enemies.Next(out EntityRef enemyEntity, out Enemy _, out Transform3D enemyTransform))
            {
                if ((enemyTransform.Position - position).SqrMagnitude > radius * radius)
                    continue;

                DamageUtility.ApplyDamage(f, enemyEntity, resonance->DamageAmount, owner, DamageSource.Skill);

                if (remixEffect.IsValid == true)
                {
                    var context = new HitEffectContext
                    {
                        Owner = owner,
                        Target = enemyEntity,
                        Position = enemyTransform.Position,
                        PushDirection = enemyTransform.Position - position,
                        Damage = resonance->DamageAmount,
                        Source = DamageSource.Skill,
                        Element = ElementType.Neutral,
                    };

                    f.FindAsset(remixEffect).Apply(f, ref context);
                    f.Events.HitEffectApplied(owner, enemyEntity, enemyTransform.Position, true);
                }
            }

            f.Events.ResonancePulseReleased(owner, position, radius);

            // Every pulse is a genuine shockwave, not a bespoke per-enemy knockback loop - reuses the
            // exact same generic push Kai's Dash Shockwave/Empty Chamber already use (including its
            // own ShockwaveReleased view hook). Force comes from the shared KnockbackTier table
            // (EffectConfig.GetKnockback) - the same one every KnockbackEffectData hit effect in the
            // game already reads - rather than a bespoke magnitude, so Zara's shockwave pushes
            // exactly as hard as everything else on a given tier. UpwardForce is dropped, matching
            // ApplyShockwave's own "no vertical lift" contract. remixEffect rides along so the View
            // can tint this specific shockwave (see ResonanceFxView) - invalid/default on a non-Remix
            // pulse, same as every other ApplyShockwave caller.
            EffectConfig effectConfig = StatusEffectUtility.GetEffectConfig(f);

            if (effectConfig != null)
            {
                effectConfig.GetKnockback((KnockbackTier)resonance->KnockbackTier, out FP force, out _);
                HitEffectUtility.ApplyShockwave(f, position, radius, owner, force, effect: remixEffect);
            }
        }

        // Remix ascension - every 3rd pulse, one random HitEffectData from the authored pool (see
        // RemixPassiveUpgradeData) is applied to every enemy the pulse damages, instead of a bespoke
        // fixed effect - reuses whatever generic HitEffectData assets already exist (Burn/Void/
        // Slow/Stun) rather than inventing Remix-specific behavior. Contiguous-from-0 fill (see
        // RemixPassiveUpgradeData.Apply), so the first invalid slot marks the end of the authored
        // list - an empty pool (the base passive's default) means the ascension hasn't been taken.
        private static AssetRef<HitEffectData> ResolveRemixEffect(Frame f, Resonance* resonance)
        {
            if (resonance->PulseCount % 3 != 0)
                return default;

            var effects = resonance->RemixEffects;
            int count = 0;

            while (count < effects.Length && effects[count].IsValid == true)
                count++;

            if (count == 0)
                return default;

            int roll = f.RNG->Next(0, count);
            AssetRef<HitEffectData> chosen = effects[roll];

            Log.Debug($"[Resonance] pulse #{resonance->PulseCount} triggered Remix - picked {chosen} (of {count} authored)");

            return chosen;
        }
    }
}

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

        // Flat grant - used by the Afterbeat dash ascension's own rank 3 per-enemy-hit bonus, unlike
        // OnDamageDealt's damage-scaled gain.
        public static void Grant(Frame f, EntityRef owner, FP amount)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(owner, out var resonance) == false)
                return;

            AddResonance(f, owner, resonance, amount);
        }

        // Percent-of-Max grant - used by Afterbeat rank 1 ("Quick Tempo": dashing grants a percent
        // of the Resonance threshold rather than a flat amount, so it scales automatically if Max is
        // ever itself raised by a future upgrade). Resolves Max off the same Resonance pointer Grant/
        // OnDamageDealt already require, so it shares the exact same "no-op without Resonance" guard.
        public static void GrantPercent(Frame f, EntityRef owner, FP percent)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(owner, out var resonance) == false)
                return;

            AddResonance(f, owner, resonance, resonance->Max * percent);
        }

        private static void AddResonance(Frame f, EntityRef owner, Resonance* resonance, FP amount)
        {
            if (amount <= FP._0)
                return;

            resonance->Current += amount;

            if (resonance->Current < resonance->Max)
                return;

            // Faster Tempo rank 3 "Never Stop" - retains RetainFraction of Max instead of always
            // fully wrapping to 0, while still carrying forward any overshoot past Max so a big hit
            // doesn't waste progress toward the next pulse either. RetainFraction is 0 at every rank
            // below 3 (and unpicked), reproducing the old unconditional-subtract behavior exactly.
            FP overflow = resonance->Current - resonance->Max;
            FP retainFloor = resonance->Max * resonance->RetainFraction;
            resonance->Current = FPMath.Max(retainFloor, overflow);

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
                EntityRef ally = allies[i].Entity;

                if (f.Unsafe.TryGetPointer<Health>(ally, out var health) == false)
                    continue;

                // Restorative Beat - requested is computed pre-owner-heal-multiplier (the nominal
                // ask), applied is what HealUtility.ApplyFlatHeal actually let through (post-
                // multiplier, post-cap) - the gap between the two is this heal's own "excess," a
                // decisive simplification for rank 3's overheal-to-Shield conversion rather than
                // threading the heal multiplier through that calc too.
                FP requested = health->MaxHealth * resonance->HealPercent;
                FP applied = HealUtility.ApplyFlatHeal(f, ally, owner, health, requested);

                if (resonance->HasteOnHealDuration > FP._0)
                {
                    StatusEffectUtility.ApplyHaste(f, ally, owner, resonance->HasteOnHealDuration, resonance->HasteOnHealMultiplier);
                }

                if (resonance->ShieldConversionPercent > FP._0)
                {
                    FP excess = requested - applied;

                    if (excess > FP._0)
                    {
                        ShieldUtility.ApplyOvershield(f, ally, owner, excess * resonance->ShieldConversionPercent, resonance->OvershieldCapMultiplier);
                    }
                }
            }

            resonance->PulseCount++;

            // Remix ascension - resolved once per pulse (not per enemy), so every enemy this pulse
            // catches gets the same randomly-chosen effect(s), and the pulse's own shockwave (below)
            // can be tinted to match. Rank 3 "Full Remix" resolves a second, guaranteed-distinct
            // effect - see ResolveRemixEntries.
            bool isRemixPulse = resonance->RemixRank > 0 && resonance->PulseCount % 3 == 0;
            RemixPoolEntry remixEntry1 = default;
            RemixPoolEntry remixEntry2 = default;
            bool hasSecondRemixEntry = false;

            if (isRemixPulse == true)
            {
                ResolveRemixEntries(f, resonance, out remixEntry1, out remixEntry2, out hasSecondRemixEntry);
            }

            var enemies = f.Filter<Enemy, Transform3D>();

            while (enemies.Next(out EntityRef enemyEntity, out Enemy _, out Transform3D enemyTransform))
            {
                if ((enemyTransform.Position - position).SqrMagnitude > radius * radius)
                    continue;

                // generatesResonance: false - Resonance Pulse damage must not generate more
                // Resonance, or a big enough pulse radius could re-trigger itself mid-loop. See
                // DamageUtility.ApplyDamage's own comment.
                DamageUtility.ApplyDamage(f, enemyEntity, resonance->DamageAmount, owner, DamageSource.Skill, generatesResonance: false);

                if (remixEntry1.Effect.IsValid == true)
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

                    ZaraRemixUtility.ApplyRemixEffect(f, ref context, remixEntry1, resonance->RemixRank);

                    if (hasSecondRemixEntry == true)
                    {
                        ZaraRemixUtility.ApplyRemixEffect(f, ref context, remixEntry2, resonance->RemixRank);
                    }

                    f.Events.HitEffectApplied(owner, enemyEntity, enemyTransform.Position, true);
                }
            }

            f.Events.ResonancePulseReleased(owner, position, radius);

            if (isRemixPulse == true)
            {
                f.Events.RemixPulseTriggered(owner, position, radius, remixEntry1.Effect, hasSecondRemixEntry ? remixEntry2.Effect : default);
            }

            // Every pulse is a genuine shockwave, not a bespoke per-enemy knockback loop - reuses the
            // exact same generic push Kai's Dash Shockwave/Empty Chamber already use (including its
            // own ShockwaveReleased view hook). Force comes from the shared KnockbackTier table
            // (EffectConfig.GetKnockback) - the same one every KnockbackEffectData hit effect in the
            // game already reads - rather than a bespoke magnitude, so Zara's shockwave pushes
            // exactly as hard as everything else on a given tier. UpwardForce is dropped, matching
            // ApplyShockwave's own "no vertical lift" contract. remixEntry1.Effect rides along so the
            // View can tint this specific shockwave (see ResonanceFxView) - invalid/default on a
            // non-Remix pulse, same as every other ApplyShockwave caller.
            EffectConfig effectConfig = StatusEffectUtility.GetEffectConfig(f);

            if (effectConfig != null)
            {
                effectConfig.GetKnockback((KnockbackTier)resonance->KnockbackTier, out FP force, out _);
                HitEffectUtility.ApplyShockwave(f, position, radius, owner, force, effect: remixEntry1.Effect);

                // Heavy Bass rank 3 "Subwoofer" - schedules a second, smaller delayed shockwave from
                // this same position/radius, reusing this pulse's own KnockbackTier-derived force
                // rather than a separately-tuned one. 0 SubwooferDamagePercent (rank<3/unpicked)
                // never schedules anything - see ZaraSubwooferPulseSystem.
                if (resonance->SubwooferDamagePercent > FP._0)
                {
                    f.AddOrGet<ZaraSubwooferPulse>(owner, out var sub);
                    sub->Remaining = resonance->SubwooferDelay;
                    sub->Position = position;
                    sub->Damage = resonance->DamageAmount * resonance->SubwooferDamagePercent;
                    sub->Radius = radius * resonance->SubwooferRadiusMultiplier;
                    sub->KnockbackForce = force;
                }
            }
        }

        // Remix ascension - every 3rd pulse (Resonance.PulseCount % 3 == 0), 1 (rank 1-2) or 2
        // distinct (rank 3 "Full Remix") randomly-chosen entries from the authored RemixPool. Pool
        // is filled contiguous-from-0 (see RemixPassiveUpgradeData.Apply), so the first invalid slot
        // marks the end of the authored list. The second draw skips over the first index rather than
        // rerolling on collision - the minimal correct "pick 2 distinct of N" primitive, deterministic
        // and O(1), since Remix only ever needs exactly 2.
        private static void ResolveRemixEntries(Frame f, Resonance* resonance, out RemixPoolEntry entry1,
            out RemixPoolEntry entry2, out bool hasSecond)
        {
            entry1 = default;
            entry2 = default;
            hasSecond = false;

            var pool = resonance->RemixPool;
            int count = 0;

            while (count < pool.Length && pool[count].Effect.IsValid == true)
                count++;

            if (count == 0)
                return;

            int i = f.RNG->Next(0, count);
            entry1 = pool[i];

            Log.Debug($"[Resonance] pulse #{resonance->PulseCount} triggered Remix - picked {entry1.Effect} (of {count} authored)");

            if (resonance->RemixRank < 3 || count < 2)
                return;

            int j = f.RNG->Next(0, count - 1);

            if (j >= i)
                j++;

            entry2 = pool[j];
            hasSecond = true;

            Log.Debug($"[Resonance] Full Remix also picked {entry2.Effect}");
        }
    }
}

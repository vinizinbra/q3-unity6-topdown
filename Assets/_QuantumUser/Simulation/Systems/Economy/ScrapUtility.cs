namespace Quantum
{
    using Photon.Deterministic;

    // Spawn/grant side of Lux's Scrap Collector passive - see ScrapOrb.qtn (the pickup) and
    // ScrapOrbSystem (collection). Mirrors ExperienceUtility's static-utility shape, but gated on the
    // killing owner actually carrying LuxScrapCollector - unlike ExpOrb, which drops for every kill
    // regardless of who's playing, Scrap only ever means anything to a Lux.
    //
    // Everything here is scoped to ONE Lux's own component, so two Luxes in the same match have fully
    // independent Scrap progressions, Fabrication Charges and Field Modification stacks.
    public static unsafe class ScrapUtility
    {
        public static void TrySpawnDrop(Frame f, EntityRef target, EntityRef owner)
        {
            if (owner == EntityRef.None)
                return;

            EntityRef realOwner = ResolveRealOwner(f, owner);

            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(realOwner, out var collector) == false)
                return;

            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            int dropCount = ResolveDropCount(f, collector, data.Tier);

            if (dropCount <= 0)
                return;

            if (f.RuntimeConfig.Prefabs.ScrapOrbPrototype.IsValid == false)
            {
                Log.Debug($"[Scrap] {target} died eligible for Scrap but RuntimeConfig has no ScrapOrbPrototype assigned - drop skipped");
                return;
            }

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return;

            for (int i = 0; i < dropCount; i++)
            {
                SpawnOrb(f, targetTransform->Position);
            }
        }

        // How many orbs this kill produces. Scavenger rank 3 "Jackpot" is the only thing that ever
        // returns more than 1, or that bypasses the chance roll entirely - Boss has its own count so
        // it can be tuned independently of the tier rule.
        private static int ResolveDropCount(Frame f, LuxScrapCollector* collector, EnemyTier tier)
        {
            if (tier == EnemyTier.Boss && collector->BossGuaranteedScrap > 0)
                return collector->BossGuaranteedScrap;

            if (collector->GuaranteedDropCount > 0 && (byte)tier >= collector->GuaranteedDropTierIndex
                && tier != EnemyTier.Boss)
                return collector->GuaranteedDropCount;

            // Filler has its own (lower) chance and is excluded entirely until Scavenger rank 1 opens
            // it up; everything Normal and above shares the main DropChance.
            if (tier == EnemyTier.Filler)
            {
                if (collector->IncludeFillerTier == false)
                    return 0;

                return DamageUtility.RollChance(f, collector->FillerDropChance) ? 1 : 0;
            }

            return DamageUtility.RollChance(f, collector->DropChance) ? 1 : 0;
        }

        private static void SpawnOrb(Frame f, FPVector3 position)
        {
            EntityRef orb = f.Create(f.RuntimeConfig.Prefabs.ScrapOrbPrototype);

            FP lifetime = 30;
            FP minOffset = FP._0;
            FP maxOffset = FP._0;

            if (f.RuntimeConfig.ScrapConfig.IsValid == true)
            {
                ScrapConfig config = f.FindAsset(f.RuntimeConfig.ScrapConfig);
                lifetime = config.OrbLifetime;

                // Scattered away from the exact death position - ExpOrb always spawns exactly there,
                // so leaving Scrap there too would stack the two pickups directly on top of each
                // other. Also what keeps a multi-orb Jackpot drop from landing as one indistinguishable
                // pile.
                minOffset = config.MinSpawnOffset;
                maxOffset = config.MaxSpawnOffset;
            }

            OrbSpawnUtility.SpawnWithPop(f, orb, position, minOffset, maxOffset);

            f.AddOrGet<DestroyAfterTime>(orb, out var destroy);
            destroy->RemainingTime = lifetime;
        }

        // A kill landed by Lux's own deployed Sentry attributes `owner` to the barrel entity that
        // actually fired (each SentryBarrel carries its own independent Weapon), not to Lux herself -
        // LuxScrapCollector only ever lives on her own player entity, so without this trace every
        // Sentry kill would silently never drop Scrap. No-op (returns owner unchanged) for a kill Lux
        // (or any other hero) landed directly, which is the common case.
        private static EntityRef ResolveRealOwner(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<SentryBarrel>(owner, out var barrel) == false)
                return owner;

            if (f.Unsafe.TryGetPointer<Sentry>(barrel->Sentry, out var sentry) == false)
                return owner;

            return sentry->Owner;
        }

        // Called by ScrapOrbSystem when the owning Lux walks over an orb. Unlike
        // ExperienceUtility.Grant, there's no shared run-wide total to credit - every effect here is
        // scoped to this one collecting entity, and (for Field Modifications) to one machine she owns.
        public static void Grant(Frame f, EntityRef collectorEntity, LuxScrapCollector* collector)
        {
            // Rapid Recycling ranks 1-2 - 0 (the base passive's default) means the ascension hasn't
            // been taken, so this is skipped rather than calling ReduceCooldown with a no-op amount.
            if (collector->CooldownReductionPerPickup > FP._0 && f.Unsafe.TryGetPointer<CharacterSkills>(collectorEntity, out var skills) == true)
            {
                SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, collector->CooldownReductionPerPickup);
            }

            ApplyFieldModification(f, collectorEntity, collector);
            TryGrantFreeCharge(f, collectorEntity, collector);

            Log.Debug($"[Scrap] {collectorEntity} collected Scrap - {collector->ScrapStacks}/{collector->StacksRequired} toward a Fabrication Charge");
        }

        // The base passive's real payoff - every pickup adds a stack. Once StacksRequired is met, stops
        // counting further (holds at the threshold rather than overflowing) and marks the Hero Skill's
        // next cast free via SkillSystem.GrantFreeCast - the earn moment. ScrapStacks itself is NOT
        // reset here: it only resets when that free cast is actually spent (OnFreeCastConsumed below) -
        // the spend moment. Reaching the threshold again while one is still banked is a no-op, not a
        // second Charge, which is what enforces "maximum 1 stored Fabrication Charge".
        private static void TryGrantFreeCharge(Frame f, EntityRef collectorEntity, LuxScrapCollector* collector)
        {
            byte required = collector->StacksRequired > 0 ? collector->StacksRequired : (byte)10;

            if (collector->ScrapStacks >= required)
                return;

            collector->ScrapStacks++;

            if (collector->ScrapStacks < required)
                return;

            if (f.Unsafe.TryGetPointer<CharacterSkills>(collectorEntity, out var skills) == false)
                return;

            SkillSystem.GrantFreeCast(f, skills, SkillSlotId.HeroSkill);

            // Rapid Recycling rank 3 "Instant Assembly" - a further one-off cooldown refund at the
            // moment the Charge is earned, on top of the per-pickup amount. Deliberately separate from
            // the Charge itself: a Charge is a free DEPLOY regardless of cooldown, this reduces the
            // cooldown, and both being live at once is the rank's actual payoff.
            if (collector->CooldownReductionOnCharge > FP._0)
            {
                SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, collector->CooldownReductionOnCharge);
            }

            Log.Debug($"[Scrap] {collectorEntity} reached {required} Scrap - Hero Skill's next use is free");
        }

        // OnFreeCastUsed fires from SkillSystem.TryBegin at the exact tick the free cast granted above
        // is spent - only then does the Scrap counter actually reset, matching "collect, then use it,
        // then it resets" rather than resetting the instant the threshold was reached.
        public static void OnFreeCastConsumed(Frame f, EntityRef entity, SkillSlotId slotId)
        {
            if (slotId != SkillSlotId.HeroSkill)
                return;

            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(entity, out var collector) == false)
                return;

            collector->ScrapStacks = 0;
            Log.Debug($"[Scrap] {entity} spent its free Hero Skill cast - Scrap reset to 0");
        }

        // Field Modifications - powers up ONE active Sentry, not every one she owns: the most recently
        // deployed (SentryUtility.FindNewestOwnedSentry), which is the brief's own preferred starting
        // rule. Stacks live on that machine and die with it (see SentryModifications), which is what
        // makes the loop "deploy -> collect -> upgrade this machine before it expires".
        private static void ApplyFieldModification(Frame f, EntityRef collectorEntity, LuxScrapCollector* collector)
        {
            if (collector->FieldModMaxStacks == 0)
                return;

            EntityRef sentryEntity = SentryUtility.FindNewestOwnedSentry(f, collectorEntity);

            if (sentryEntity == EntityRef.None)
                return;

            if (f.Unsafe.TryGetPointer<SentryModifications>(sentryEntity, out var modifications) == false
                || modifications->Stacks >= modifications->MaxStacks)
                return;

            modifications->Stacks++;

            // Fire rate is sentry-wide and recomposed onto every barrel each tick from
            // Sentry.FireRateMultiplier (see SentryBarrelSystem), so raising it here takes effect
            // immediately on the barrels already attached and can't compound across ticks.
            if (f.Unsafe.TryGetPointer<Sentry>(sentryEntity, out var sentry) == true && modifications->FireRatePerStack > FP._0)
            {
                sentry->FireRateMultiplier += modifications->FireRatePerStack;
            }

            ApplyDamageStackToBarrels(f, sentryEntity, modifications->DamagePerStack);
            TryApplyMkII(f, sentryEntity, modifications);

            Log.Debug($"[Scrap] {collectorEntity}'s sentry {sentryEntity} gained a Field Modification ({modifications->Stacks}/{modifications->MaxStacks})");
        }

        // Damage compounds into each barrel's own Weapon.DamageMultiplier - the exact same idiom
        // DamageMultiplierWeaponPerkData/WeaponSystem.AddLevel already use for a player weapon, so it
        // composes with everything else that scales a weapon's damage.
        private static void ApplyDamageStackToBarrels(Frame f, EntityRef sentryEntity, FP damagePerStack)
        {
            if (damagePerStack <= FP._0)
                return;

            var barrels = f.Filter<SentryBarrel, Weapon>();

            while (barrels.Next(out EntityRef barrelEntity, out SentryBarrel barrel, out Weapon _))
            {
                if (barrel.Sentry != sentryEntity)
                    continue;

                if (f.Unsafe.TryGetPointer<Weapon>(barrelEntity, out var weapon) == true)
                {
                    weapon->DamageMultiplier *= FP._1 + damagePerStack;
                }
            }
        }

        // Rank 3 "MK II" - a plain weapon swap on the slot-0 (Cannon) barrel through the ordinary
        // WeaponSystem.Equip path, latched so it happens exactly once. Deliberately NOT a separate
        // turret implementation: a Twin Cannon is just a different WeaponDataAsset.
        //
        // Equip re-seeds that barrel's stats from the new asset, which also clears whatever damage
        // stacks were compounded into it - so they're re-applied here at the new weapon's baseline.
        private static void TryApplyMkII(Frame f, EntityRef sentryEntity, SentryModifications* modifications)
        {
            if (modifications->MkIIApplied == true || modifications->MkIIWeapon.IsValid == false)
                return;

            if (modifications->Stacks < modifications->MaxStacks)
                return;

            var barrels = f.Filter<SentryBarrel, Weapon>();

            while (barrels.Next(out EntityRef barrelEntity, out SentryBarrel barrel, out Weapon _))
            {
                if (barrel.Sentry != sentryEntity || barrel.SlotIndex != 0)
                    continue;

                if (f.Unsafe.TryGetPointer<Weapon>(barrelEntity, out var weapon) == false)
                    continue;

                WeaponSystem.Equip(f, barrelEntity, weapon, modifications->MkIIWeapon);

                // Equip reset DamageMultiplier to 1 - re-apply every stack earned so far so the swap
                // is an upgrade, never a silent reset.
                for (int i = 0; i < modifications->Stacks; i++)
                {
                    weapon->DamageMultiplier *= FP._1 + modifications->DamagePerStack;
                }

                if (f.Unsafe.TryGetPointer<SentryBarrel>(barrelEntity, out var liveBarrel) == true)
                {
                    liveBarrel->BaseFireCooldownMultiplier = weapon->FireCooldownMultiplier;
                }

                modifications->MkIIApplied = true;
                f.Events.SentryUpgradedToMkII(sentryEntity, barrelEntity);

                Log.Debug($"[Scrap] sentry {sentryEntity} upgraded to MK II");
                return;
            }
        }
    }
}

namespace Quantum
{
    using Photon.Deterministic;

    // Spawn/grant side of Lux's Scrap Collector passive - see ScrapOrb.qtn (the pickup) and
    // ScrapOrbSystem (collection). Mirrors ExperienceUtility's static-utility shape, but gated on
    // the killing owner actually carrying LuxScrapCollector (granted by ScrapCollectorPassiveData) -
    // unlike ExpOrb, which drops for every kill regardless of who's playing, Scrap only ever means
    // anything to a Lux who's taken this passive.
    public static unsafe class ScrapUtility
    {
        public static void TrySpawnDrop(Frame f, EntityRef target, EntityRef owner)
        {
            if (owner == EntityRef.None)
                return;

            EntityRef realOwner = ResolveRealOwner(f, owner);

            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(realOwner, out var collector) == false)
            {
                Log.Debug($"[Scrap] {target} died but {realOwner} (resolved from {owner}) has no LuxScrapCollector - drop skipped");
                return;
            }

            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            bool eligibleTier = data.Tier >= EnemyTier.Normal
                || (data.Tier == EnemyTier.Filler && collector->IncludeFillerTier == true);

            if (eligibleTier == false)
                return;

            if (DamageUtility.RollChance(f, collector->DropChance) == false)
                return;

            if (f.RuntimeConfig.ScrapOrbPrototype.IsValid == false)
            {
                Log.Debug($"[Scrap] {target} died eligible for Scrap but RuntimeConfig has no ScrapOrbPrototype assigned - drop skipped");
                return;
            }

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return;

            EntityRef orb = f.Create(f.RuntimeConfig.ScrapOrbPrototype);

            FP lifetime = 30;
            FPVector3 spawnPosition = targetTransform->Position;

            if (f.RuntimeConfig.ScrapConfig.IsValid == true)
            {
                ScrapConfig config = f.FindAsset(f.RuntimeConfig.ScrapConfig);
                lifetime = config.OrbLifetime;

                // Scattered away from the exact death position - ExpOrb always spawns exactly there,
                // so leaving Scrap there too would stack the two pickups directly on top of each other.
                if (config.MaxSpawnOffset > FP._0)
                {
                    spawnPosition = EnemyMovementUtility.RandomPositionInRing(f, spawnPosition, config.MinSpawnOffset, config.MaxSpawnOffset);
                }
            }

            if (f.Unsafe.TryGetPointer<Transform3D>(orb, out var orbTransform) == true)
            {
                orbTransform->Position = spawnPosition;
            }

            f.AddOrGet<DestroyAfterTime>(orb, out var destroy);
            destroy->RemainingTime = lifetime;
        }

        // A kill landed by Lux's own deployed Sentry attributes `owner` to the barrel entity that
        // actually fired (each SentryBarrel carries its own independent Weapon - see
        // SentryBarrelSystem/WeaponSystem), not to Lux herself - LuxScrapCollector only ever lives on
        // her own player entity, so without this trace every Sentry kill would silently never drop
        // Scrap. Same "chassis/barrel -> owning player" resolution DamageFeedbackManager's own
        // View-side "is this hit mine" check already does (SentryBarrel.Sentry -> Sentry.Owner), just
        // needed here on the simulation side too. No-op (returns owner unchanged) for a kill Lux (or
        // any other hero) landed directly, which is the common case.
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
        // scoped to this one collecting entity.
        public static void Grant(Frame f, EntityRef collectorEntity, LuxScrapCollector* collector)
        {
            // Rapid Recycling only - 0 (the base passive's default) means the ascension hasn't been
            // taken, so this is skipped entirely rather than calling ReduceCooldown with a no-op amount.
            if (collector->CooldownReductionPerPickup > FP._0 && f.Unsafe.TryGetPointer<CharacterSkills>(collectorEntity, out var skills) == true)
            {
                SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, collector->CooldownReductionPerPickup);
            }

            if (collector->MachineHealthBonusPerPickup > FP._0)
            {
                ApplyToOwnedSentry(f, collectorEntity, collector);
            }

            TryGrantFreeCharge(f, collectorEntity, collector);

            Log.Debug($"[Scrap] {collectorEntity} collected Scrap - {collector->ScrapStacks}/{collector->StacksRequired} toward a free Hero Skill charge");
        }

        // The base passive's real payoff - every pickup adds a stack. Once StacksRequired is met,
        // stops counting further (holds at the threshold rather than overflowing) and marks the
        // Hero Skill's next cast free via SkillSystem.GrantFreeCast - the earn moment. ScrapStacks
        // itself is NOT reset here: it only resets when that free cast is actually spent, see
        // OnFreeCastUsed below - the spend moment. Reaching 10 again while still waiting to spend
        // the last one is a no-op, not a second banked cast.
        private static void TryGrantFreeCharge(Frame f, EntityRef collectorEntity, LuxScrapCollector* collector)
        {
            byte required = collector->StacksRequired > 0 ? collector->StacksRequired : (byte)10;

            if (collector->ScrapStacks >= required)
                return;

            collector->ScrapStacks++;

            if (collector->ScrapStacks < required)
                return;

            if (f.Unsafe.TryGetPointer<CharacterSkills>(collectorEntity, out var skills) == true)
            {
                SkillSystem.GrantFreeCast(f, skills, SkillSlotId.HeroSkill);
                Log.Debug($"[Scrap] {collectorEntity} reached {required} Scrap - Hero Skill's next use is free");
            }
        }

        // OnFreeCastUsed fires from SkillSystem.TryBegin at the exact tick the free cast granted
        // above is spent - only then does the Scrap counter actually reset, matching "collect,
        // then use it, then it resets" rather than resetting the instant the threshold was reached.
        public static void OnFreeCastConsumed(Frame f, EntityRef entity, SkillSlotId slotId)
        {
            if (slotId != SkillSlotId.HeroSkill)
                return;

            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(entity, out var collector) == false)
                return;

            collector->ScrapStacks = 0;
            Log.Debug($"[Scrap] {entity} spent its free Hero Skill cast - Scrap reset to 0");
        }

        // Finds the Sentry this Lux currently owns (if any) - Sentry.Owner already exists for
        // exactly this "trace a chassis back to the player who deployed it" purpose (see
        // SpawnSentrySkillAction/SentryDecaySystem). At most one match in practice (a fresh deploy
        // replaces the old chassis's Duration-driven decay), but this doesn't assume that - every
        // owned Sentry found gets the same treatment.
        private static void ApplyToOwnedSentry(Frame f, EntityRef owner, LuxScrapCollector* collector)
        {
            var sentries = f.Filter<Sentry>();

            while (sentries.Next(out EntityRef sentryEntity, out Sentry sentry))
            {
                if (sentry.Owner != owner)
                    continue;

                if (f.Unsafe.TryGetPointer<Health>(sentryEntity, out var health) == false)
                    continue;

                health->MaxHealth += collector->MachineHealthBonusPerPickup;
                health->CurrentHealth = FPMath.Min(health->CurrentHealth + collector->MachineHealthBonusPerPickup, health->MaxHealth);
            }
        }
    }
}

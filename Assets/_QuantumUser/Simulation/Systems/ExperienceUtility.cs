namespace Quantum
{
    using Photon.Deterministic;

    // Spawn/grant side of the experience-drop mechanic - see ExpOrb.qtn (the pickup itself) and
    // ExpOrbSystem (collection). Mirrors DamageUtility's static-utility shape.
    public static unsafe class ExperienceUtility
    {
        // Called from DamageUtility.ApplyDamage right where it fires EntityDied, for every dying
        // entity regardless of tier - owner is whoever landed the killing hit, same value the
        // EntityDied event just carried. EntityRef.None means there was no traceable instigator
        // (fall/void death, an un-authored level hazard - see EnemySystem.CheckFallDeath and
        // AreaDamageSystem.ResolveOwner), which is exactly the case this refuses to drop for. A
        // player-owned hazard (e.g. a skill's fire trail) still carries a real owner and still
        // drops, since the kill IS player-caused.
        public static void TrySpawnDrop(Frame f, EntityRef target, EntityRef owner)
        {
            if (owner == EntityRef.None)
                return;

            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            TierStats tierStats = EnemyTierStatsConfig.Resolve(f, data.Tier);

            if (tierStats.ExpValue <= FP._0)
                return;

            if (f.RuntimeConfig.ExpOrbPrototype.IsValid == false)
            {
                Log.Debug($"[Experience] {target} died with ExpValue {tierStats.ExpValue} but RuntimeConfig has no ExpOrbPrototype assigned - drop skipped");
                return;
            }

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return;

            EntityRef orb = f.Create(f.RuntimeConfig.ExpOrbPrototype);

            if (f.Unsafe.TryGetPointer<Transform3D>(orb, out var orbTransform) == true)
            {
                orbTransform->Position = targetTransform->Position;
            }

            if (f.Unsafe.TryGetPointer<ExpOrb>(orb, out var expOrb) == true)
            {
                expOrb->Value = tierStats.ExpValue;
            }

            FP lifetime = 30;

            if (f.RuntimeConfig.ExperienceConfig.IsValid == true)
            {
                ExperienceConfig config = f.FindAsset(f.RuntimeConfig.ExperienceConfig);
                lifetime = config.OrbLifetime;
            }

            f.AddOrGet<DestroyAfterTime>(orb, out var destroy);
            destroy->RemainingTime = lifetime;
        }

        // Called by ExpOrbSystem when ANY player walks over an orb - this is co-op, so exp is one
        // shared run total (Frame.Global, see Experience.qtn), not tracked per-player. Adds to the
        // running total, then re-derives Level by walking RequiredExperience upward. No perk/skill
        // grant is triggered on a level-up (that mechanism, GrantWeaponPerkCommand/
        // GrantSkillUpgradeCommand, stays debug-only for now - see docs/experience-drops.md).
        //
        // Global.Level counts level-ups earned so far and stays at its natural unseeded 0, same as
        // every other Frame.Global field - the DISPLAYED/curve-facing level is always Level + 1
        // (see ExpBarUiWidget), since RequiredExperience is authored 1-indexed (its first keyframe
        // is "level 1 costs 0 exp"). So the threshold to advance past the current display level is
        // Evaluate(Level + 2) - the NEXT display level - not Evaluate(Level + 1).
        public static void Grant(Frame f, FP amount)
        {
            f.Global->TotalExperience += amount;

            if (f.RuntimeConfig.ExperienceConfig.IsValid == false)
                return;

            ExperienceConfig config = f.FindAsset(f.RuntimeConfig.ExperienceConfig);
            int levelBefore = f.Global->Level;

            while (f.Global->Level + 1 < config.MaxLevel
                   && f.Global->TotalExperience >= config.RequiredExperience.Evaluate(f.Global->Level + 2))
            {
                f.Global->Level++;
            }

            Log.Debug($"[Experience] run gained {amount} exp -> {f.Global->TotalExperience} total, level {f.Global->Level + 1}");

            // One screen per Grant call regardless of how many levels its while loop just covered
            // (a single big orb crossing more than one threshold at once) - see
            // docs/level-up-upgrades.md for why this collapses rather than queuing one screen per
            // level gained.
            if (f.Global->Level > levelBefore)
            {
                LevelUpUtility.BeginLevelUpScreen(f);
            }
        }
    }
}

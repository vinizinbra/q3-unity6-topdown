namespace Quantum
{
    using Photon.Deterministic;

    // Reads an attacker's effective stats. Anything without CharacterStats (every enemy today) gets
    // the authored value back untouched.
    public static unsafe class StatUtility
    {
        public static FP GetSkillDuration(Frame f, EntityRef owner, FP baseDuration)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return baseDuration;

            return baseDuration * stats->SkillDurationMultiplier;
        }

        // Divides rather than multiplies - AttackSpeedMultiplier/ReloadSpeedMultiplier express a
        // rate (higher = faster), while FireCooldown/ReloadDuration are the time it takes, so a
        // faster rate must shrink the time. Folds in StatusEffectUtility's temporary Haste buff
        // (e.g. Zara's Haste-on-heal upgrade) alongside the permanent CharacterStats multiplier -
        // the two are independent components, so either can be present without the other.
        public static FP GetFireCooldown(Frame f, EntityRef owner, FP baseCooldown)
        {
            FP multiplier = StatusEffectUtility.GetAttackSpeedMultiplier(f, owner);

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == true)
            {
                multiplier *= stats->AttackSpeedMultiplier;
            }

            return baseCooldown / multiplier;
        }

        public static FP GetReloadDuration(Frame f, EntityRef owner, FP baseDuration)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return baseDuration;

            return baseDuration / stats->ReloadSpeedMultiplier;
        }

        // Same divide-by-rate convention as GetFireCooldown/GetReloadDuration above. Dash and Hero
        // Skill scale off independent CharacterStats fields (DashCooldownMultiplier/
        // SkillCooldownMultiplier) since they're picked independently at level-up - see
        // CharacterStats.qtn.
        public static FP GetSkillCooldown(Frame f, EntityRef owner, SkillSlotId slotId, FP baseCooldown)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return baseCooldown;

            FP multiplier = slotId == SkillSlotId.DashSkill ? stats->DashCooldownMultiplier : stats->SkillCooldownMultiplier;

            return baseCooldown / multiplier;
        }

        // Multiplies rather than divides, unlike the rate-based stats above - AreaRadiusMultiplier
        // expresses a size directly (1 = authored size), same convention as SkillSlot.AreaMultiplier
        // it stacks alongside (see HitPathSkillAction/SpawnEntitySkillAction).
        public static FP GetAreaMultiplier(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return FP._1;

            return stats->AreaRadiusMultiplier;
        }

        // The full outgoing-damage multiplier a DamageSource.Skill hit from this owner would receive -
        // the global DamageMultiplier and the Skill-scoped one together, exactly as
        // DamageUtility.ResolveOutgoingDamage composes them for a live hit.
        //
        // Exists for the one case that CANNOT go through that per-hit path: damage that has to be
        // BAKED up front onto something that will later deal it on its own, with itself as the owner.
        // Lux's Sentry barrels are the case - each barrel is its own entity carrying its own Weapon,
        // and WeaponSystem fires it with the BARREL as owner. A barrel has no CharacterStats, so
        // ResolveOutgoingDamage returns at its own stats gate and the shot receives none of Lux's
        // build at all. Baking her skill-damage multiplier in at deploy time is what reconnects them.
        //
        // Crit and range falloff are deliberately NOT folded in here: both are per-hit rolls/distances
        // that have no meaning as a baked constant.
        public static FP GetSkillDamageMultiplier(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return FP._1;

            return stats->DamageMultiplier * stats->SkillDamageMultiplier;
        }

        // Same shape as GetAreaMultiplier - a plain size multiplier (1 = authored speed), folded
        // into a projectile's spawn velocity once in ProjectileSpawner.Spawn rather than threaded
        // through every ProjectileMovementData subclass's own Speed field.
        public static FP GetProjectileSpeedMultiplier(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return FP._1;

            return stats->ProjectileSpeedMultiplier;
        }

        // The wielder's per-character WeaponPosition, mirrored onto the muzzle: X flips sign with
        // the character's facing (matching the view's binary left/right sprite flip - see
        // WeaponViewController) rather than rotating continuously with aim angle the way the
        // weapon's own SpawnOffset does, since this represents a fixed hold point on the
        // character's body, not something anchored to the gun barrel. Takes Aim.FacingSign
        // (AimSystem) rather than re-deriving it from the angle here, so the muzzle can never
        // disagree with the sprite flip it's supposed to match. Anything without CharacterStats
        // (every enemy today) gets no hold offset at all.
        public static FPVector3 GetWeaponHoldOffset(Frame f, EntityRef owner, FP facingSign)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return FPVector3.Zero;

            FPVector3 weaponPosition = f.FindAsset(stats->CharacterData).WeaponPosition;
            weaponPosition.X = FPMath.Abs(weaponPosition.X) * facingSign;

            return weaponPosition;
        }
    }
}

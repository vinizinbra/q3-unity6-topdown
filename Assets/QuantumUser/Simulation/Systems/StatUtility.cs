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

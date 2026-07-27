namespace Quantum
{
    using Photon.Deterministic;

    // Advance/revert logic for RageOverdrive, split out of RageOverdriveSkillAction because
    // TryAdvanceStack is called from DamageUtility on every landed weapon hit, not from the skill
    // lifecycle - only the grant/revoke (Begin/End) belongs on the action itself.
    public static unsafe class RageOverdriveUtility
    {
        public static void TryAdvanceStack(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<RageOverdrive>(owner, out var rage) == false || rage->Overdriven == true)
                return;

            if (rage->Stacks < rage->MaxStacks)
            {
                rage->Stacks++;
            }

            if (rage->Stacks < rage->MaxStacks)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return;

            ApplyCorrection(stats, rage, apply: true);
            rage->Overdriven = true;

            Log.Debug($"[Skill] {owner} reached Rage Overdrive ({rage->Stacks}/{rage->MaxStacks})");
        }

        // No-ops if Overdrive never triggered this activation - nothing to undo.
        public static void Revert(Frame f, EntityRef owner, RageOverdrive* rage)
        {
            if (rage->Overdriven == false)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return;

            ApplyCorrection(stats, rage, apply: false);

            Log.Debug($"[Skill] {owner}'s Rage Overdrive correction reverted");
        }

        // The doubled bonus replaces the granting skill's own contribution rather than stacking
        // blindly on top of it - going from base factor (1+bonus) to (1+bonus*OverdriveMultiplier)
        // is a single multiplicative correction, applied here and undone the same way in Revert.
        private static void ApplyCorrection(CharacterStats* stats, RageOverdrive* rage, bool apply)
        {
            FP fireRate = Correction(rage->FireRateBonus, rage->OverdriveMultiplier);
            FP moveSpeed = Correction(rage->MoveSpeedBonus, rage->OverdriveMultiplier);
            FP reloadSpeed = Correction(rage->ReloadSpeedBonus, rage->OverdriveMultiplier);

            if (apply == true)
            {
                stats->AttackSpeedMultiplier *= fireRate;
                stats->MoveSpeedMultiplier *= moveSpeed;
                stats->ReloadSpeedMultiplier *= reloadSpeed;
            }
            else
            {
                stats->AttackSpeedMultiplier /= fireRate;
                stats->MoveSpeedMultiplier /= moveSpeed;
                stats->ReloadSpeedMultiplier /= reloadSpeed;
            }
        }

        private static FP Correction(FP bonus, FP overdriveMultiplier)
        {
            FP baseFactor = FP._1 + bonus;
            FP overdriveFactor = FP._1 + bonus * overdriveMultiplier;

            return overdriveFactor / baseFactor;
        }
    }
}

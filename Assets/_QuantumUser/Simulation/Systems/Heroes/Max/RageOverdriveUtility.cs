namespace Quantum
{
    using Photon.Deterministic;

    // Advance/reset logic for RageOverdrive, plus the max-Rage threshold transition itself - split
    // out of BerserkSkillData/MaxOverdriveReactionSystem because TryAdvanceStack/ResetStacks are
    // called from DamageUtility/combat reactions on every landed hit or hit taken, not from the skill
    // lifecycle itself. Rage's own baseline has zero stat effect - reaching max Rage is purely a
    // condition (IsAtMaxRage) that Ascensions react to on their own. Full Throttle's stat toggle,
    // Ignition's Burn-on-hit toggle and Last Stand's Damage Reduction all hook the entering/leaving-
    // max-Rage transition detected here
    // directly (same "read optional upgrade components inline, no dispatcher" idiom
    // JuggernautSkillData.Discharge already established for Brute) rather than each polling every
    // tick.
    public static unsafe class RageOverdriveUtility
    {
        public static bool IsAtMaxRage(Frame f, EntityRef owner)
        {
            return f.Unsafe.TryGetPointer<RageOverdrive>(owner, out var rage) == true && rage->Stacks >= rage->MaxStacks;
        }

        // Called from DamageUtility.ApplyDamage on every landed weapon hit - a no-op once already at
        // max Rage (further hits land no-ops, matching the pre-refactor behavior).
        public static void TryAdvanceStack(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<RageOverdrive>(owner, out var rage) == false)
                return;

            if (rage->Stacks >= rage->MaxStacks)
                return;

            rage->Stacks++;

            if (rage->Stacks < rage->MaxStacks)
                return;

            EnterMaxRage(f, owner);
        }

        // Also called directly from FullThrottleSkillAction/IgnitionSkillAction/LastStandSkillAction's
        // own Begin-phase Execute - every Overdrive STARTS at max Rage, so there is no TryAdvanceStack
        // crossing to hook for the opening window. Every half is idempotent (Full Throttle latches on
        // Applied, Ignition on Applied/InfernoTriggeredThisActivation, Last Stand on
        // AppliedDamageReduction), so calling this on a Rage total that is already max is safe
        // regardless of which path got there.
        public static void EnterMaxRage(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<FullThrottleUpgrade>(owner, out var fullThrottle) == true)
            {
                MaxAscensionUtility.ApplyFullThrottle(f, owner, fullThrottle);
            }

            if (f.Unsafe.TryGetPointer<IgnitionUpgrade>(owner, out var ignition) == true)
            {
                MaxAscensionUtility.OnEnteredMaxRage(f, owner, ignition);
            }

            if (f.Unsafe.TryGetPointer<LastStandUpgrade>(owner, out var lastStand) == true)
            {
                MaxAscensionUtility.ApplyLastStand(f, owner, lastStand);
            }

            Log.Debug($"[Skill] {owner} is at max Rage");
        }

        // Called from MaxOverdriveReactionSystem when the owner takes damage while Overdrive is
        // active. Removes RageOverdrive.LossFraction (BerserkSkillData's baseline) of the CURRENT
        // Rage, or Last Stand's softer RageLossFraction when that's lower - a drain, not a cliff.
        // CeilToInt so a partial loss always costs at least one stack - a fraction that rounds to 0
        // would make getting hit entirely free, which is the opposite of Max's intended weakness.
        public static void ResetStacks(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<RageOverdrive>(owner, out var rage) == false || rage->Stacks == 0)
                return;

            FP lossFraction = FPMath.Clamp(rage->LossFraction, FP._0, FP._1);

            if (f.Unsafe.TryGetPointer<LastStandUpgrade>(owner, out var lastStand) == true && lastStand->RageLossFraction > FP._0)
            {
                lossFraction = FPMath.Min(lossFraction, FPMath.Clamp(lastStand->RageLossFraction, FP._0, FP._1));
            }

            int lost = FPMath.CeilToInt(rage->Stacks * lossFraction);

            if (lost <= 0)
                return;

            bool wasAtMax = rage->Stacks >= rage->MaxStacks;
            rage->Stacks = lost >= rage->Stacks ? (byte)0 : (byte)(rage->Stacks - lost);

            if (wasAtMax == true && rage->Stacks < rage->MaxStacks)
            {
                RevertMaxRageEffects(f, owner);
            }

            Log.Debug($"[Skill] {owner} took damage - Rage dropped by {lost} to {rage->Stacks}/{rage->MaxStacks}");
        }

        // Called from BerserkSkillData.End - no-op if Rage never reached max this activation,
        // otherwise reverts whatever max-Rage effects are still applied before RageOverdrive itself
        // is removed.
        public static void Revert(Frame f, EntityRef owner, RageOverdrive* rage)
        {
            if (rage->Stacks < rage->MaxStacks)
                return;

            RevertMaxRageEffects(f, owner);
        }

        private static void RevertMaxRageEffects(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<FullThrottleUpgrade>(owner, out var fullThrottle) == true)
            {
                MaxAscensionUtility.RevertFullThrottle(f, owner, fullThrottle);
            }

            if (f.Unsafe.TryGetPointer<IgnitionUpgrade>(owner, out var ignition) == true)
            {
                MaxAscensionUtility.RevertIgnition(f, owner, ignition);
            }

            if (f.Unsafe.TryGetPointer<LastStandUpgrade>(owner, out var lastStand) == true)
            {
                MaxAscensionUtility.RevertLastStand(f, owner, lastStand);
            }
        }
    }
}

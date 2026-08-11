namespace Quantum
{
    using Photon.Deterministic;

    // Advance/reset logic for RageOverdrive, plus the max-Rage threshold transition itself - split
    // out of BerserkSkillData/MaxOverdriveReactionSystem because TryAdvanceStack/ResetStacks are
    // called from DamageUtility/combat reactions on every landed hit or hit taken, not from the skill
    // lifecycle itself. Rage's own baseline has zero stat effect - reaching max Rage is purely a
    // condition (IsAtMaxRage) that Ascensions react to on their own. Full Throttle's stat toggle and
    // Ignition's Burn-on-hit toggle both hook the entering/leaving-max-Rage transition detected here
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

            // Entering max Rage - Full Throttle/Ignition react here, not via a per-tick poll.
            if (f.Unsafe.TryGetPointer<FullThrottleUpgrade>(owner, out var fullThrottle) == true)
            {
                MaxAscensionUtility.ApplyFullThrottle(f, owner, fullThrottle);
            }

            if (f.Unsafe.TryGetPointer<IgnitionUpgrade>(owner, out var ignition) == true)
            {
                MaxAscensionUtility.OnEnteredMaxRage(f, owner, ignition);
            }

            Log.Debug($"[Skill] {owner} reached max Rage ({rage->Stacks}/{rage->MaxStacks})");
        }

        // Called from MaxOverdriveReactionSystem when the owner takes damage while Overdrive is
        // active - a no-op if they carry RageRetentionUpgrade (Last Stand rank 1) or already have 0
        // stacks.
        public static void ResetStacks(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<RageOverdrive>(owner, out var rage) == false || rage->Stacks == 0)
                return;

            if (f.Has<RageRetentionUpgrade>(owner) == true)
                return;

            bool wasAtMax = rage->Stacks >= rage->MaxStacks;
            rage->Stacks = 0;

            if (wasAtMax == true)
            {
                RevertMaxRageEffects(f, owner);
            }

            Log.Debug($"[Skill] {owner} took damage - Rage reset to 0");
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
        }
    }
}

namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Max's Hero Skill - a pure self-buff channel with no movement/projectile of its own: Begin
    // multiplies CharacterStats by the authored bonuses, Tick just counts the duration down, End
    // divides them back out. Dividing back out (rather than re-seeding stats) composes correctly
    // with any permanent stat change picked up mid-Overdrive, since it only ever undoes this skill's
    // own multiplicative contribution - whatever else changed the stat in between is left alone.
    //
    // FireRateBonus is deliberately NOT the naive "+50%" you'd expect from a flat read of Max's
    // Overdrive fantasy - Max's own baseline (MaxCharacterData.AttackSpeedMultiplier = 1.20, his
    // permanent +20% Fire Rate identity) is already folded into CharacterStats.AttackSpeedMultiplier
    // before this skill ever runs. Overdrive's own FireRateBonus (0.25) is the RELATIVE delta on top
    // of that baseline needed to land on the correct +50% TOTAL: 1.20 * 1.25 = 1.50. No special
    // "replace instead of stack" logic needed - this is just correct algebra on the existing
    // multiplicative Begin/End composition.
    //
    // Also grants a fresh RageOverdrive every activation (baseline behavior) - landing weapon hits
    // builds stacks that give NO bonus on their own; Ascensions (Full Throttle, Ignition) react to
    // reaching max Rage on their own terms, not a baked-in correction here (see
    // RageOverdriveUtility). Getting hit resets those stacks back to 0 unless Last Stand rank 1 is
    // equipped (RageRetentionUpgrade, see MaxOverdriveReactionSystem).
    public unsafe partial class BerserkSkillData : SkillData
    {
        public FP Duration = 10;

        public override FP GetActiveDuration()
        {
            return Duration;
        }

        public FP FireRateBonus = FP.FromString("0.25");
        public FP MoveSpeedBonus = FP._0_25;
        public FP ReloadSpeedBonus = FP.FromString("0.3");

        [Header("Rage")]
        public byte MaxRageStacks = 10;

        public override bool Begin(Frame f, ref SkillSystem.Filter filter, Input* input, SkillSlot* slot)
        {
            slot->StateTimer = Duration;

            if (TryGetStats(f, filter.Entity, out var stats) == true)
            {
                stats->AttackSpeedMultiplier *= FP._1 + FireRateBonus;
                stats->MoveSpeedMultiplier *= FP._1 + MoveSpeedBonus;
                stats->ReloadSpeedMultiplier *= FP._1 + ReloadSpeedBonus;
            }

            f.AddOrGet<RageOverdrive>(filter.Entity, out var rage);
            rage->Stacks = 0;
            rage->MaxStacks = MaxRageStacks;

            Log.Debug($"[Skill] {filter.Entity} began Overdrive for {Duration}s");
            return false; // runs for its full Duration, never resolves on the same tick
        }

        public override bool Tick(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)
        {
            slot->StateTimer -= f.DeltaTime;
            return slot->StateTimer <= FP._0;
        }

        public override void End(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)
        {
            // Unwinds whatever max-Rage Ascension effects are currently applied BEFORE dividing out
            // Overdrive's own baseline bonus below - RageOverdriveUtility.Revert's reads are relative
            // to that baseline still being present on CharacterStats.
            if (f.Unsafe.TryGetPointer<RageOverdrive>(filter.Entity, out var rage) == true)
            {
                RageOverdriveUtility.Revert(f, filter.Entity, rage);
                f.Remove<RageOverdrive>(filter.Entity);
            }

            if (TryGetStats(f, filter.Entity, out var stats) == true)
            {
                stats->AttackSpeedMultiplier /= FP._1 + FireRateBonus;
                stats->MoveSpeedMultiplier /= FP._1 + MoveSpeedBonus;
                stats->ReloadSpeedMultiplier /= FP._1 + ReloadSpeedBonus;
            }

            Log.Debug($"[Skill] {filter.Entity}'s Overdrive ended");
        }

        private static bool TryGetStats(Frame f, EntityRef entity, out CharacterStats* stats)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out stats) == true)
                return true;

            Log.Error($"[Skill] {entity} has no CharacterStats - Overdrive cannot apply its buff");
            return false;
        }
    }
}

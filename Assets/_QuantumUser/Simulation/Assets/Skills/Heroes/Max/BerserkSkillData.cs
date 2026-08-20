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
    // RageOverdriveUtility). Getting hit removes Rage - all of it by default, or only Last Stand rank
    // 2's authored RageLossFraction once that's equipped (see MaxOverdriveReactionSystem). Last Stand
    // rank 1 additionally parks whatever Rage survives an activation and hands it back at the next
    // Begin, so Overdrive can start already spun up.
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

        [Header("Extension")]
        [Tooltip("Baseline ceiling on how many extra seconds ANY combination of Ascensions may add to a single Overdrive activation. Uncontrolled Fury raises it to its own ranked value; nothing can exceed whichever is higher. See OverdriveExtension.")]
        public FP BaseMaxExtension = 4;

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
            rage->MaxStacks = MaxRageStacks;

            // Last Stand rank 1 - Rage parked at the end of the previous activation is handed back
            // here rather than always starting from 0 (see LastStandUpgrade.StoredRageStacks). The
            // Ascension actions that react to being at max Rage (Full Throttle, Ignition) run LATER
            // in this same Begin phase and each re-check IsAtMaxRage themselves, which is what makes
            // an Overdrive that starts already maxed apply its effects immediately instead of waiting
            // for a threshold crossing that already happened.
            byte carried = 0;

            if (f.Unsafe.TryGetPointer<LastStandUpgrade>(filter.Entity, out var lastStand) == true && lastStand->PersistsRage == true)
            {
                carried = lastStand->StoredRageStacks;
            }

            rage->Stacks = carried > MaxRageStacks ? MaxRageStacks : carried;

            // The per-activation extension ledger every Overdrive-lengthening effect books against
            // (see OverdriveExtension/OverdriveUtility.TryExtend). Added here, zeroed here, so
            // nothing carries over between casts regardless of which Ascensions are equipped;
            // Uncontrolled Fury raises MaxExtension later in this same Begin phase if it's picked.
            f.AddOrGet<OverdriveExtension>(filter.Entity, out var extension);
            extension->MaxExtension = BaseMaxExtension;
            extension->AccumulatedExtension = FP._0;
            extension->PerKillExtension = FP._0;
            extension->VendettaKillExtension = FP._0;
            extension->KillCount = 0;
            extension->KillsPerExtension = 0;

            Log.Debug($"[Skill] {filter.Entity} began Overdrive for {Duration}s (carried Rage {rage->Stacks}/{MaxRageStacks})");
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

                // Last Stand rank 1 - park whatever Rage survived, for the next activation to pick
                // back up. Read BEFORE the component is removed; parked as a plain number on the
                // persistent upgrade component rather than by keeping RageOverdrive alive, since its
                // presence is what everything else reads as "Overdrive is running".
                if (f.Unsafe.TryGetPointer<LastStandUpgrade>(filter.Entity, out var lastStand) == true && lastStand->PersistsRage == true)
                {
                    lastStand->StoredRageStacks = rage->Stacks;
                }

                f.Remove<RageOverdrive>(filter.Entity);
            }

            // Removed rather than zeroed, so a dormant Overdrive carries no stale headroom and
            // OverdriveUtility.TryExtend's own SkillState.Active check is the only thing gating a
            // between-activation extension attempt.
            f.Remove<OverdriveExtension>(filter.Entity);

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

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
    // Also grants a fresh RageOverdrive every activation, STARTING AT MAX - Overdrive pays off the
    // moment it's cast, and the skill expression is staying there. Rage gives NO bonus on its own;
    // Ascensions (Full Throttle, Ignition, Last Stand) react to being at max Rage on their own terms
    // (see RageOverdriveUtility). Getting hit removes RageLossFraction of the current Rage (Last
    // Stand can soften it - see MaxOverdriveReactionSystem), and landed weapon hits build it back.
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

        [Tooltip("Fraction of CURRENT Rage removed when Max takes damage during Overdrive. Last Stand can only lower it.")]
        public FP RageLossFraction = FP._0_50;

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

            // Starts full. The Ascension actions run BEFORE this Begin (see EnterMaxRage call below),
            // so this method is what applies Full Throttle/Ignition/Last Stand at cast time.
            f.AddOrGet<RageOverdrive>(filter.Entity, out var rage);
            rage->MaxStacks = MaxRageStacks;
            rage->Stacks = MaxRageStacks;
            rage->LossFraction = RageLossFraction;

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

            // SkillSystem.TryBegin runs the Ascension actions BEFORE this Begin, so their own
            // "already at max Rage?" checks (Full Throttle/Ignition/Last Stand) saw no RageOverdrive
            // yet and never applied; with Rage starting full there is no later threshold crossing
            // either. Fire the entry transition here, now that Rage exists. Idempotent per upgrade.
            RageOverdriveUtility.EnterMaxRage(f, filter.Entity);

            Log.Debug($"[Skill] {filter.Entity} began Overdrive for {Duration}s at max Rage {MaxRageStacks}");
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

            // Last Stand rank 2 - its first-hit block belongs to this activation only. Cleared only
            // when Max granted it himself, so a teammate's guard (Bodyguard, Covering Fire) survives.
            if (StatusEffectUtility.GetFreeHitGuardSource(f, filter.Entity) == filter.Entity)
            {
                StatusEffectUtility.TryConsumeFreeHitGuard(f, filter.Entity, out _);
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

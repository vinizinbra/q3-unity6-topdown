namespace Quantum
{
    using Photon.Deterministic;

    // Max's Hero Skill - a pure self-buff channel with no movement/projectile of its own: Begin
    // multiplies CharacterStats by the authored bonuses, Tick just counts the duration down, End
    // divides them back out. Dividing back out (rather than re-seeding stats) composes correctly
    // with any permanent stat change picked up mid-Berserk, since it only ever undoes this skill's
    // own multiplicative contribution - whatever else changed the stat in between is left alone.
    public unsafe partial class BerserkSkillData : SkillData
    {
        public FP Duration = 10;

        public FP FireRateBonus = FP._0_50;
        public FP MoveSpeedBonus = FP._0_25;
        public FP ReloadSpeedBonus = FP.FromString("0.3");

        public override bool Begin(Frame f, ref SkillSystem.Filter filter, Input* input, SkillSlot* slot)
        {
            slot->StateTimer = Duration;

            if (TryGetStats(f, filter.Entity, out var stats) == true)
            {
                stats->AttackSpeedMultiplier *= FP._1 + FireRateBonus;
                stats->MoveSpeedMultiplier *= FP._1 + MoveSpeedBonus;
                stats->ReloadSpeedMultiplier *= FP._1 + ReloadSpeedBonus;
            }

            Log.Debug($"[Skill] {filter.Entity} began Berserk for {Duration}s");
            return false; // runs for its full Duration, never resolves on the same tick
        }

        public override bool Tick(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)
        {
            slot->StateTimer -= f.DeltaTime;
            return slot->StateTimer <= FP._0;
        }

        public override void End(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)
        {
            if (TryGetStats(f, filter.Entity, out var stats) == true)
            {
                stats->AttackSpeedMultiplier /= FP._1 + FireRateBonus;
                stats->MoveSpeedMultiplier /= FP._1 + MoveSpeedBonus;
                stats->ReloadSpeedMultiplier /= FP._1 + ReloadSpeedBonus;
            }

            Log.Debug($"[Skill] {filter.Entity}'s Berserk ended");
        }

        private static bool TryGetStats(Frame f, EntityRef entity, out CharacterStats* stats)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out stats) == true)
                return true;

            Log.Error($"[Skill] {entity} has no CharacterStats - Berserk cannot apply its buff");
            return false;
        }
    }
}

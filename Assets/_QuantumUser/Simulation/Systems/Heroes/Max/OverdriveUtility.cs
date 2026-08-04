namespace Quantum
{
    using Photon.Deterministic;

    // Shared "extend the current Overdrive (Berserk) activation" mechanism - reused by Vendetta
    // Rush (VendettaRushExtension, one flat extension per Vendetta-consuming kill) and Uncontrolled
    // Fury (UncontrolledFuryExtension, a smaller per-kill extension capped per activation). Reads
    // CharacterSkills.HeroSkill directly via SkillSystem.ResolveSlot rather than assuming any
    // particular filter/system is calling in - both callers react to signals (OnEntityKilled), not a
    // filtered Update.
    public static unsafe class OverdriveUtility
    {
        // No-ops (returns false) if Overdrive isn't active right now - neither upgrade extends a
        // dormant Hero Skill, only a channel actually in progress. seconds <= 0 is also a no-op, so
        // callers don't need their own guard before calling in.
        public static bool TryExtend(Frame f, EntityRef entity, FP seconds)
        {
            if (seconds <= FP._0)
                return false;

            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == false)
                return false;

            SkillSlot* heroSkill = SkillSystem.ResolveSlot(skills, SkillSlotId.HeroSkill);

            if (heroSkill == null || heroSkill->State != SkillState.Active)
                return false;

            heroSkill->StateTimer += seconds;
            return true;
        }
    }
}

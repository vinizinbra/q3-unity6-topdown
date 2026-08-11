namespace Quantum
{
    using Photon.Deterministic;

    // Shared "extend the current Overdrive (Berserk) activation" mechanism - Uncontrolled Fury's
    // only consumer today, via two independent bonuses off the same OnEntityKilled dispatch: the
    // capped per-N-kills pool (UncontrolledFuryExtension.PerKillExtension/MaxExtension) and rank 3's
    // separate, uncapped Vendetta-kill bonus (VendettaKillExtension) - see
    // MaxOverdriveReactionSystem.OnEntityKilled. Reads CharacterSkills.HeroSkill directly via
    // SkillSystem.ResolveSlot rather than assuming any particular filter/system is calling in.
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

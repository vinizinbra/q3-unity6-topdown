namespace Quantum
{
    using Photon.Deterministic;

    // The single entry point for extending the current Overdrive (Berserk) activation, and the one
    // place its per-activation ceiling is enforced - see OverdriveExtension. Three sources call in
    // today (Uncontrolled Fury's per-N-kills grant, its rank 3 Vendetta-kill grant, and Vendetta
    // Strike rank 3's dash bonus); routing all of them through one capped ledger is what guarantees
    // no build can combine them into a permanent Overdrive.
    //
    // Reads CharacterSkills.HeroSkill directly via SkillSystem.ResolveSlot rather than assuming any
    // particular filter/system is calling in.
    public static unsafe class OverdriveUtility
    {
        // No-ops (returns false) if Overdrive isn't active right now - no upgrade extends a dormant
        // Hero Skill, only a channel actually in progress. seconds <= 0 is also a no-op, so callers
        // don't need their own guard before calling in.
        //
        // Clamps against whatever headroom OverdriveExtension has left and books the spend. An
        // activation with no ledger at all (nothing seeded it) is treated as uncapped - which can't
        // happen in practice, since BerserkSkillData.Begin always adds one.
        public static bool TryExtend(Frame f, EntityRef entity, FP seconds)
        {
            if (seconds <= FP._0)
                return false;

            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == false)
                return false;

            SkillSlot* heroSkill = SkillSystem.ResolveSlot(skills, SkillSlotId.HeroSkill);

            if (heroSkill == null || heroSkill->State != SkillState.Active)
                return false;

            OverdriveExtension* ledger = null;

            if (f.Unsafe.TryGetPointer<OverdriveExtension>(entity, out ledger) == true)
            {
                FP headroom = ledger->MaxExtension - ledger->AccumulatedExtension;

                if (headroom <= FP._0)
                    return false;

                seconds = FPMath.Min(seconds, headroom);
            }

            heroSkill->StateTimer += seconds;

            if (ledger != null)
            {
                ledger->AccumulatedExtension += seconds;
            }

            return true;
        }
    }
}

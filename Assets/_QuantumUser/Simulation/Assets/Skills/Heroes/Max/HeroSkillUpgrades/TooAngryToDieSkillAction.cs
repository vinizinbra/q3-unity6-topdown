namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade for Berserk/Overdrive - while equipped, lethal Health damage taken during
    // Overdrive clamps Max to 1 Health and force-ends the activation early instead of dying (see
    // CheatDeathUtility, hooked into DamageUtility.ApplyDamage's own Health clamp). Begin grants the
    // tag, End revokes it - the actual save-or-not check happens live in DamageUtility, not here,
    // same shape OverdriveInstantReloadSkillAction already uses for a live external gating check.
    public unsafe partial class TooAngryToDieSkillAction : SkillActionData
    {
        // Brief Invulnerable window opened the instant a save actually triggers (see
        // CheatDeathUtility.TryPreventLethal) - without it, whatever else is hitting Max this same
        // tick (or the next few) would just kill him again right through the 1 Health he was left
        // at. Baked into CheatDeathGuard at Begin so TryPreventLethal has it on hand without needing
        // to reach back into this asset.
        public FP ImmunityDuration = FP._0_50;

        public TooAngryToDieSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.End;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (firedPhase == SkillActionPhase.Begin)
            {
                f.AddOrGet<CheatDeathGuard>(filter.Entity, out var guard);
                guard->ImmunityDuration = ImmunityDuration;
            }
            else
            {
                f.Remove<CheatDeathGuard>(filter.Entity);
            }
        }
    }
}

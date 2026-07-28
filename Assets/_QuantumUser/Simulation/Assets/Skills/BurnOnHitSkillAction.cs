namespace Quantum
{
    // Hero Skill Upgrade - grants guaranteed Burn on every weapon hit for as long as the slot it's
    // attached to stays Active (see StatusEffectUtility.TryApplyElementalStatus /
    // CharacterStats.BurnOnHitStacks). Begin|End paired: increments the owner's stack count when
    // the skill activates, decrements it when the skill ends, so it composes with any other source
    // of the same stat without one's End wiping out another's still-active grant. Carries no
    // Berserk-specific logic itself - attach it to any skill's Upgrades (or Actions) list to grant
    // the same effect elsewhere.
    public unsafe partial class BurnOnHitSkillAction : SkillActionData
    {
        public BurnOnHitSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.End;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(filter.Entity, out var stats) == false)
                return;

            if (firedPhase == SkillActionPhase.Begin)
            {
                stats->BurnOnHitStacks++;
            }
            else
            {
                stats->BurnOnHitStacks--;
            }

            Log.Debug($"[Skill] {filter.Entity}'s BurnOnHitStacks -> {stats->BurnOnHitStacks} ({firedPhase})");
        }
    }
}

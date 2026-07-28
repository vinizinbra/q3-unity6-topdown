namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, whatever the skill spawns lives DurationBonus longer than
    // its authored Duration (on top of the caster's own SkillDurationMultiplier) - see
    // SpawnedEntitySpawner.ResolveDuration. Generic rather than hero-specific, same reasoning as
    // SpawnRadiusUpSkillAction: one class covers every hero - create a separate .asset instance per
    // hero and name it accordingly rather than a separate subclass.
    //
    // Begin-only, deliberately not paired with End: this configures what the skill produces, not a
    // temporary buff that should only apply while the skill is actively resolving. Revoking on End
    // would race against the moment the throw/cast actually spawns the entity - Begin/End brackets
    // the throw itself, which ends the tick after the projectile lands (or immediately, for a
    // direct spawn), often before the spawn logic gets a chance to read this tag. Re-granting fresh
    // (idempotent) every activation and never removing it sidesteps that race entirely.
    public unsafe partial class IncreaseDurationSkillAction : SkillActionData
    {
        public FP DurationBonus = FP._0_50;

        public IncreaseDurationSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<IncreaseDurationUpgrade>(filter.Entity, out var upgrade);
            upgrade->DurationBonus = DurationBonus;
        }
    }
}

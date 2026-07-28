namespace Quantum
{
    // Hero Skill Upgrade - while equipped, the bomb detonates on any contact (ground, wall, enemy)
    // instead of the fuse/pass-through behavior authored on its AreaHitData - see
    // AreaHitData.ShouldDetonate and the InstantDetonate tag it reads.
    //
    // Begin-only, deliberately not paired with End: this configures what the skill produces, not a
    // temporary buff that should only apply while the skill is actively resolving. Revoking on End
    // would race against ApplyHit actually reading it - Begin/End brackets the throw itself, which
    // ends the tick after the bomb detonates, often before the detonation logic gets a chance to
    // read this tag. Re-granting fresh (idempotent) every activation and never removing it
    // sidesteps that race entirely.
    public unsafe partial class BombInstantDetonateSkillAction : SkillActionData
    {
        public BombInstantDetonateSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<InstantDetonate>(filter.Entity, out _);
        }
    }
}

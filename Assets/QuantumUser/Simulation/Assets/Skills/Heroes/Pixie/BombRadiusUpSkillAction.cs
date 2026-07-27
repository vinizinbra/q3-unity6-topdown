namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, the bomb's blast radius (and the fuse's overlap check) is
    // RadiusBonus bigger than what's authored on its AreaHitData - see AreaHitData.ResolveRadiusBonus.
    //
    // Begin-only, deliberately not paired with End: this configures what the skill produces, not a
    // temporary buff that should only apply while the skill is actively resolving. Revoking on End
    // would race against Detonate() actually reading it - Begin/End brackets the throw itself, which
    // ends the tick after the bomb detonates, often before the detonation logic gets a chance to
    // read this tag. Re-granting fresh (idempotent) every activation and never removing it
    // sidesteps that race entirely.
    public unsafe partial class BombRadiusUpSkillAction : SkillActionData
    {
        public FP RadiusBonus = 1;

        public BombRadiusUpSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<BlastRadiusUpgrade>(filter.Entity, out var upgrade);
            upgrade->RadiusBonus = RadiusBonus;
        }
    }
}

namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, a ProjectileSkillData's own Damage is multiplied by
    // Multiplier before the projectile is spawned - see ProjectileSkillData.ResolveDamageMultiplier.
    // Generic rather than tied to any one hero - works on any skill built on ProjectileSkillData
    // (Pixie's bomb today, whatever else uses that base class later).
    //
    // Begin-only, deliberately not paired with End: this configures what the skill produces, not a
    // temporary buff that should only apply while the skill is actively resolving. Revoking on End
    // would race against Fire() actually reading it - Begin/End brackets the throw itself, and nothing
    // guarantees Fire() runs before End does on every activation. Re-granting fresh (idempotent)
    // every activation and never removing it sidesteps that race entirely.
    public unsafe partial class IncreaseProjectileDamageSkillAction : SkillActionData
    {
        public FP Multiplier = FP._2;

        public IncreaseProjectileDamageSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<ProjectileDamageUpgrade>(filter.Entity, out var upgrade);
            upgrade->Multiplier = Multiplier;
        }
    }
}

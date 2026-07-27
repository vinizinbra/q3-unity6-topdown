namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, the vortex periodically deals Damage in a small Radius at
    // a random point inside its own pull area, every TickInterval seconds, for as long as it's alive
    // - see VortexRandomExplosionUpgrade and VortexSystem.TryRandomExplosion.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class VortexRandomExplosionSkillAction : SkillActionData
    {
        public FP Damage = 5;
        public FP Radius = 1;
        public FP TickInterval = FP._0_50;

        public VortexRandomExplosionSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<VortexRandomExplosionUpgrade>(filter.Entity, out var upgrade);
            upgrade->Damage = Damage;
            upgrade->Radius = Radius;
            upgrade->TickInterval = TickInterval;
            upgrade->Source = this;
        }
    }
}

namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, the vortex deals Damage in a radius (its own
    // PhysicsCollider3D) at its own position the instant it's destroyed (expired, or otherwise
    // removed) - see VortexExplodeOnDestroy and VortexSystem.TryExplodeOnDestroy.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class VortexExplodeOnDestroySkillAction : SkillActionData
    {
        public FP Damage = 20;

        public VortexExplodeOnDestroySkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<VortexExplodeOnDestroy>(filter.Entity, out var upgrade);
            upgrade->Damage = Damage;
            upgrade->Source = this;
        }
    }
}

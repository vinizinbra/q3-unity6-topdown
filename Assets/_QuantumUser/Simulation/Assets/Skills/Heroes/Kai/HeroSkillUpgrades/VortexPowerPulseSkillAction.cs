namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, the vortex's pull Force and pull TickInterval are
    // overridden by Power/TickInterval here instead of "Force = the hit's own Damage" and whatever's
    // authored on the prototype - see VortexPowerPulseUpgrade and SpawnVortexEffectData.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class VortexPowerPulseSkillAction : SkillActionData
    {
        public FP Power = 20;
        public FP TickInterval = FP._0_50;

        public VortexPowerPulseSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = Power - e.g. "overrides the vortex's pull force to {0} and makes it pulse every tick,
        // instead of scaling off the cast's own damage."
        protected override object[] DescriptionArgs => new object[] { Power };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<VortexPowerPulseUpgrade>(filter.Entity, out var upgrade);
            upgrade->Power = Power;
            upgrade->TickInterval = TickInterval;
        }
    }
}

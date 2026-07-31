namespace Quantum
{
    using Photon.Deterministic;

    // Dash Ascension (Quick Tempo) - dashing generates Resonance. Flat grant via
    // ResonanceUtility.Grant, the same threshold/pulse-firing logic OnDamageDealt uses, just not
    // scaled by damage.
    public unsafe partial class QuickTempoSkillAction : SkillActionData
    {
        public FP ResonanceOnDash = 20;

        public QuickTempoSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        protected override object[] DescriptionArgs => new object[] { ResonanceOnDash };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            ResonanceUtility.Grant(f, filter.Entity, ResonanceOnDash);
        }
    }
}

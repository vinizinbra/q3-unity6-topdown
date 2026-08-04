namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade for Berserk/Overdrive - while equipped, killing Max's current Vendetta
    // target (consuming the mark, see MaxVendettaSystem.OnEntityKilled) extends the current
    // Overdrive activation by ExtensionSeconds. Begin (re-)seeds a fresh VendettaRushExtension every
    // activation, End revokes it - the actual extension happens live in MaxVendettaSystem, not here,
    // same shape RageOverdriveSkillAction/OverdriveDamageSkillAction already use for Begin-seeded,
    // externally-consumed state.
    public unsafe partial class VendettaRushSkillAction : SkillActionData
    {
        public FP ExtensionSeconds = 2;

        // {0} = ExtensionSeconds - e.g. "Killing your Vendetta target extends Overdrive by {0}s."
        protected override object[] DescriptionArgs => new object[] { ExtensionSeconds };

        public VendettaRushSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.End;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (firedPhase == SkillActionPhase.Begin)
            {
                f.AddOrGet<VendettaRushExtension>(filter.Entity, out var rush);
                rush->ExtensionSeconds = ExtensionSeconds;
            }
            else
            {
                f.Remove<VendettaRushExtension>(filter.Entity);
            }
        }
    }
}

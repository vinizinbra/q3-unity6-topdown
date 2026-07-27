namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, Juggernaut deals Damage in a Radius around Brutus's own
    // position the instant the channel ends (Duration running out) - see JuggernautEndExplosionUpgrade
    // and JuggernautSkillData.End. Separate from the discharge pulse (knockback only, no damage,
    // triggered by touching an enemy while Charged) - this fires once, on expiry, regardless of
    // Charge state.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class JuggernautEndExplosionSkillAction : SkillActionData
    {
        public FP Damage = 20;
        public FP Radius = 4;
        public FP PushDuration = FP._0_25;

        public JuggernautEndExplosionSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<JuggernautEndExplosionUpgrade>(filter.Entity, out var upgrade);
            upgrade->Damage = Damage;
            upgrade->Radius = Radius;
            upgrade->PushDuration = PushDuration;
            upgrade->Source = this;
        }
    }
}

namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, the End Explosion's damage scales with how many enemies
    // Brutus actually knocked back with a discharge over the whole activation: DamagePerUnit more
    // damage per unit hit, on top of JuggernautEndExplosionUpgrade's own authored Damage - see
    // JuggernautCharge.UnitsHit and JuggernautSkillData.ResolveStackDamageBonus. Only matters if
    // JuggernautEndExplosionUpgrade is also equipped.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class JuggernautStackDamageSkillAction : SkillActionData
    {
        public FP DamagePerUnit = FP._0_10;

        public JuggernautStackDamageSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<JuggernautStackDamageUpgrade>(filter.Entity, out var upgrade);
            upgrade->DamagePerUnit = DamagePerUnit;
        }
    }
}

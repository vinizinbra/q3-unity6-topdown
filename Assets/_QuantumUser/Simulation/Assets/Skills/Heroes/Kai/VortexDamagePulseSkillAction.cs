namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, the vortex also applies DamageEffect (typically a
    // DamageEffectData asset, dealing Damage) to every enemy caught, every TickInterval seconds - a
    // real change from the base vortex's "pure crowd control, no damage of its own". See
    // VortexDamageUpgrade and SpawnVortexEffectData, which attaches a real AreaDamage component
    // rather than a bespoke damage-tick system.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class VortexDamagePulseSkillAction : SkillActionData
    {
        public FP Damage = 10;
        public FP TickInterval = FP._0_50;
        [ExpandableAsset] public AssetRef<HitEffectData> DamageEffect;

        public VortexDamagePulseSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = Damage, {1} = TickInterval (seconds) - e.g. "deals {0} damage to every enemy caught in
        // the vortex every {1}s via its damage effect."
        protected override object[] DescriptionArgs => new object[] { Damage, TickInterval };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<VortexDamageUpgrade>(filter.Entity, out var upgrade);
            upgrade->Damage = Damage;
            upgrade->TickInterval = TickInterval;
            upgrade->DamageEffect = DamageEffect;
        }
    }
}

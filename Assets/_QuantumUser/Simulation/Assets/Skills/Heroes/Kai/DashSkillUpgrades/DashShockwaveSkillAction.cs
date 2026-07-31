namespace Quantum
{
    using Photon.Deterministic;

    // Dash Ascension (Shockwave) - a knockback pulse at the dash's end position. Direct reuse of
    // HitEffectUtility.ApplyShockwave (existing, currently only called by the Empty Chamber weapon
    // perk) - no new mechanism needed at all. Its particle is likewise the existing shared one -
    // ApplyShockwave fires EventShockwaveReleased regardless of caller, and EffectsManager plays
    // shockwaveEffectPrefab for it (see that method's own comment) - nothing new to wire up here.
    //
    // Knockback strength is authored as a Tier (Small/Medium/Strong), same as every
    // KnockbackEffectData - Force itself comes from the shared RuntimeConfig.EffectConfig
    // (EffectConfig.GetKnockback) so this pushes exactly as hard as every other Tier-authored
    // knockback in the game, not a bespoke number. UpwardForce is deliberately discarded -
    // ApplyShockwave's contract is a flat radial push, no vertical lift (see its own comment).
    public unsafe partial class DashShockwaveSkillAction : SkillActionData
    {
        public FP Radius = 5;
        public KnockbackTier Tier = KnockbackTier.Medium;

        public DashShockwaveSkillAction()
        {
            Phase = SkillActionPhase.End;
        }

        protected override object[] DescriptionArgs => new object[] { Radius, Tier };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);
            if (config == null)
                return;

            config.GetKnockback(Tier, out FP force, out _);

            HitEffectUtility.ApplyShockwave(f, filter.Transform3D->Position, Radius, filter.Entity, force);
        }
    }
}

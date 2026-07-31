namespace Quantum
{
    using Photon.Deterministic;

    // Dash Ascension (Backblast) - an explosion where the dash fires from, on Begin, or where it
    // lands, on End - whichever this asset's own Phase is set to. Direct reuse of
    // HitEffectUtility.ApplyExplosion, flagged isDashExplosion so Volatile Escape (if taken) lets it
    // mark regardless of tier - see DamageUtility.TryMarkExplodeOnDeath's own comment.
    public unsafe partial class BackblastSkillAction : SkillActionData
    {
        public FP Radius = 4;
        public FP Damage = 20;

        public BackblastSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        protected override object[] DescriptionArgs => new object[] { Radius, Damage };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Bigger Boom (Pixie passive ascension) - scales this dash explosion the same way it
            // scales her weapon procs/bomb - see DamageUtility.ResolvePixieExplosionRadiusMultiplier.
            FP radius = Radius * DamageUtility.ResolvePixieExplosionRadiusMultiplier(f, filter.Entity);

            // Begin fires before the dash moves the entity, so slot->StartPosition and the entity's
            // live position are still the same point - only End actually needs to distinguish them,
            // once the dash has relocated the entity to slot->TargetPosition.
            FPVector3 position = firedPhase == SkillActionPhase.End ? filter.Transform3D->Position : slot->StartPosition;

            HitEffectUtility.ApplyExplosion(f, position, radius, filter.Entity, Damage, DamageSource.Skill, isDashExplosion: true);
        }
    }
}

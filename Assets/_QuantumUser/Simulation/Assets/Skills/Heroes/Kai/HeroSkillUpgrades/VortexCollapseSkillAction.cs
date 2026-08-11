namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Vortex Ascension (Vortex Collapse, line 3/4) - see docs/kai-ascensions.md. Repurposes the
    // old (single-pick) VortexExplodeOnDestroySkillAction - when the vortex expires or is destroyed, it
    // detonates for DamagePercent of Vortex Skill Damage in a radius (see VortexExplodeOnDestroy/
    // VortexSystem.TryExplodeOnDestroy, which already predicts destruction one tick early). Rank 2
    // grows the blast radius; rank 3 "Event Collapse" additionally performs one strong inward pull
    // immediately before detonating.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class VortexCollapseSkillAction : SkillActionData
    {
        public FP[] DamagePercent = { FP._1_50, FP._2, FP.FromString("2.50") };
        public FP[] RadiusMultiplier = { FP._1, FP.FromString("1.25"), FP._1_50 };

        // Rank 3 "Event Collapse" only (0 at ranks 1-2, which leaves the pre-explosion pull disabled -
        // VortexSystem.TryExplodeOnDestroy only pulls when this is > 0).
        public FP[] PreExplosionPullForce = { FP._0, FP._0, 12 };

        public VortexCollapseSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<VortexExplodeOnDestroy>(filter.Entity, out var upgrade);
            upgrade->Damage = DamagePercent[index] * KaiAscensionUtility.ResolveVortexSkillDamage(f, filter.Entity);
            upgrade->RadiusMultiplier = RadiusMultiplier[index];
            upgrade->PreExplosionPullForce = PreExplosionPullForce[index];
            upgrade->Source = this;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}

namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Juggernaut Ascension - absorbs Heavy Impact + old Concussive Impact + Lasting Impact +
    // Overwhelming Force + Crushing Blow. Lives on JuggernautSkillData.Actions (Activated = false) -
    // see MomentumSkillAction's own comment for why this is a Hero Skill Ascension, not
    // PassiveUpgradeData. Baseline Discharge keeps its normal knockback/launch; this Ascension makes a
    // launch dangerous - landing damage + stun (unconditional, not a chance roll), extra knockback
    // force, and (rank 3) a further impact shockwave on landing plus a flat bonus against any enemy
    // Brute has Stunned, applying to weapon/skill/dash damage alike via the generic outgoing-damage
    // pipeline (see StunDamageBonusUpgrade in KnockbackMastery.qtn - the renamed, reused mechanism the
    // old standalone Crushing Blow ascension used). Bakes Source as a self-reference every Begin so the
    // view can resolve ImpactEffectPrefab off the exact asset that granted this - same pattern
    // GroundPoundPassiveUpgradeData.Source/VortexExplodeOnDestroy.Source already use.
    public unsafe partial class ConcussiveImpactSkillAction : SkillActionData
    {
        public FP[] LandingDamagePercent = { FP.FromString("0.30"), FP._0_50, FP.FromString("0.75") };
        public FP[] LandingStunDuration = { FP.FromString("0.75"), FP._1, FP.FromString("1.25") };
        public FP[] KnockbackForceBonus = { FP._0, FP.FromString("0.25"), FP.FromString("0.25") };
        public FP[] ShockwaveRadius = { FP._0, FP._0, FP.FromString("2.5") };
        public FP[] ShockwaveDamagePercent = { FP._0, FP._0, FP.FromString("0.40") };
        public FP[] ShockwaveStunDuration = { FP._0, FP._0, FP._1 };

        // Ported from the deleted standalone Crushing Blow ascension - only takes effect at rank 3.
        public FP StunDamageBonus = FP.FromString("0.40");

        public ConcussiveImpactSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<ConcussiveImpactUpgrade>(filter.Entity, out var upgrade);
            upgrade->LandingDamagePercent = LandingDamagePercent[index];
            upgrade->LandingStunDuration = LandingStunDuration[index];
            upgrade->KnockbackForceBonus = KnockbackForceBonus[index];
            upgrade->ShockwaveRadius = ShockwaveRadius[index];
            upgrade->ShockwaveDamagePercent = ShockwaveDamagePercent[index];
            upgrade->ShockwaveStunDuration = ShockwaveStunDuration[index];
            upgrade->Source = this;

            if (rank >= 3)
            {
                f.AddOrGet<StunDamageBonusUpgrade>(filter.Entity, out var stunBonus);
                stunBonus->DamageMultiplierBonus = StunDamageBonus;
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}

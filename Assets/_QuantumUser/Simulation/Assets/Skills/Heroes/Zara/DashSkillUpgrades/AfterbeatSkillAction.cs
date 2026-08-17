namespace Quantum
{
    using Photon.Deterministic;

    // Dash Ascension (Afterbeat, ranked, line 1/2 on Dash) - see docs/zara-ascensions.md. Absorbs
    // Quick Tempo (rank 1: dashing grants Resonance, a percent of Resonance.Max rather than a flat
    // amount) and adds a delayed damaging/knockback pulse at the dash's own starting position
    // (rank 2), then a second identical pulse at the dash's ending position too (rank 3 "Double
    // Beat"), whose enemies-hit also generate extra, capped Resonance (that bonus stays a flat
    // per-enemy amount, not a percent - see ResonancePerEnemyHit below). Countdowns/positions live
    // on ZaraAfterbeat, ticked by ZaraAfterbeatSystem - no EntityPrototype authoring needed, same as
    // before this line was ranked.
    public unsafe partial class AfterbeatSkillAction : SkillActionData
    {
        // Percent of Resonance.Max, not a flat amount - see ResonanceUtility.GrantPercent.
        public FP ResonancePercentOnDash = FP._0_20;
        public FP Delay = FP._1;

        // Index 0 (rank 1) is never read - rank 1 only grants the percent-of-Max Resonance above.
        public FP[] DamagePercentOfSkill = { FP._0, FP.FromString("0.75"), FP.FromString("0.75") };
        public FP[] Radius = { FP._0, FP._4, FP._4 };
        public FP[] KnockbackForce = { FP._0, FP._6, FP._6 };

        // Rank 3 only.
        public FP ResonancePerEnemyHit = 5;
        public FP MaxResonancePerDash = 30;

        public AfterbeatSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.End;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<ZaraAfterbeat>(filter.Entity, out var afterbeat);

            if (firedPhase == SkillActionPhase.Begin)
            {
                // Quick Tempo (rank 1+) - percent of Resonance.Max, not a flat amount.
                ResonanceUtility.GrantPercent(f, filter.Entity, ResonancePercentOnDash);

                afterbeat->ResonanceGrantedThisDash = FP._0;
                afterbeat->ResonancePerEnemyHit = rank >= 3 ? ResonancePerEnemyHit : FP._0;
                afterbeat->MaxResonancePerDash = rank >= 3 ? MaxResonancePerDash : FP._0;

                if (rank < 2)
                    return;

                afterbeat->StartRemaining = Delay;
                afterbeat->StartPosition = slot->StartPosition;
                afterbeat->StartDamage = ZaraAscensionUtility.ResolveHeroSkillDamage(f, filter.Entity) * DamagePercentOfSkill[index];
                afterbeat->StartRadius = Radius[index];
                afterbeat->StartKnockbackForce = KnockbackForce[index];
            }
            else
            {
                if (rank < 3)
                    return;

                afterbeat->EndRemaining = Delay;
                afterbeat->EndPosition = filter.Transform3D->Position;
                afterbeat->EndDamage = ZaraAscensionUtility.ResolveHeroSkillDamage(f, filter.Entity) * DamagePercentOfSkill[index];
                afterbeat->EndRadius = Radius[index];
                afterbeat->EndKnockbackForce = KnockbackForce[index];
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}

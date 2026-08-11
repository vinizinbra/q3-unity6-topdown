namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Dash Ascension (Bodyguard) - allies near the dash destination recover a fraction of
    // their max Shield, growing radius/fraction per rank; rank 3 additionally grants a temporary DR
    // buff (see StatusEffectUtility.ApplyTemporaryDamageReduction, the same shared primitive
    // Guardian's own rank 3 reactive proc uses). Same "restore % of Max, clamped" shape Lux's
    // RepairNearbyMachinesSkillAction/PortableCoverSkillAction already use for machines - here applied
    // to nearby players instead. Brute himself is included in the ally scan (he trivially ends the
    // dash within his own Radius of himself) but only gets SelfEffectMultiplier of the full ally
    // amount - a reduced, not full, self-benefit, configurable rather than hardcoded.
    public unsafe partial class BodyguardSkillAction : SkillActionData
    {
        public FP[] Radius = { FP._6, FP._8, FP._8 };
        public FP[] ShieldRestoreFraction = { FP.FromString("0.10"), FP.FromString("0.15"), FP._0_20 };
        public FP SelfEffectMultiplier = FP._0_50;

        public BodyguardSkillAction()
        {
            Phase = SkillActionPhase.End;
        }

        public override FP EffectRadius => Radius[Radius.Length - 1];

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            FPVector3 position = filter.Transform3D->Position;
            var allies = EnemyMovementUtility.FindPlayersInRadius(f, position, Radius[index]);
            FP fraction = ShieldRestoreFraction[index];

            for (int i = 0; i < allies.Count; i++)
            {
                EntityRef ally = allies[i].Entity;
                FP allyFraction = ally == filter.Entity ? fraction * SelfEffectMultiplier : fraction;

                ShieldUtility.ApplyShield(f, ally, filter.Entity, allyFraction);

                if (rank >= 3)
                {
                    StatusEffectUtility.ApplyTemporaryDamageReduction(f, ally, FP._2, FP._0_20);
                }
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}

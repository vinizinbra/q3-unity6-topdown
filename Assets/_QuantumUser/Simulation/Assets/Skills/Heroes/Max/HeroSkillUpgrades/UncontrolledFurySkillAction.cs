namespace Quantum
{
    using Photon.Deterministic;

    // Overdrive rank 3 - extends the current Overdrive activation every KillsPerExtension kills (not
    // every kill), up to MaxExtension total for that single activation - see
    // UncontrolledFuryExtension.qtn/MaxOverdriveReactionSystem.OnEntityKilled for the actual
    // reaction. Rank 3 additionally grants a separate, uncapped bonus for killing a Vendetta-marked
    // enemy. Fires on every Berserk Begin, same "refresh fresh off the live rank each cast" idiom
    // Brute's MomentumSkillAction already established - AccumulatedExtension/KillCount always reset
    // to 0 for a new activation, the cap never carries over.
    public unsafe partial class UncontrolledFurySkillAction : SkillActionData
    {
        public FP[] PerKillExtension = { 1, 1, 1 };
        public byte[] KillsPerExtension = { 3, 2, 2 };
        public FP[] MaxExtension = { 3, 5, 7 };
        public FP[] VendettaKillExtension = { FP._0, FP._0, 2 };

        public UncontrolledFurySkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<UncontrolledFuryExtension>(filter.Entity, out var fury);
            fury->PerKillExtension = PerKillExtension[index];
            fury->KillsPerExtension = KillsPerExtension[index];
            fury->MaxExtension = MaxExtension[index];
            fury->VendettaKillExtension = VendettaKillExtension[index];
            fury->AccumulatedExtension = FP._0;
            fury->KillCount = 0;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}

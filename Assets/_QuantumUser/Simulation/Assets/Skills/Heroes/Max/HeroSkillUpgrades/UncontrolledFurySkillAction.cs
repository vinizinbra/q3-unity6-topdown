namespace Quantum
{
    using Photon.Deterministic;

    // Overdrive rank 3 - extends the current Overdrive activation every KillsPerExtension kills (not
    // every kill), up to MaxExtension total for that single activation - see
    // OverdriveExtension.qtn/MaxOverdriveReactionSystem.OnEntityKilled for the actual
    // reaction. Rank 3 makes a Vendetta-marked kill worth more (VendettaKillExtension INSTEAD of
    // PerKillExtension for that kill), drawing from the same capped pool - there is deliberately no
    // uncapped path, so no kill loop can hold Overdrive open forever. Fires on every Berserk Begin,
    // same "refresh fresh off the live rank each cast" idiom Brute's MomentumSkillAction already
    // established - AccumulatedExtension/KillCount always reset to 0 for a new activation, the cap
    // never carries over.
    public unsafe partial class UncontrolledFurySkillAction : SkillActionData
    {
        public FP[] PerKillExtension = { 1, 1, 1 };
        public byte[] KillsPerExtension = { 3, 2, 2 };

        // Hard per-activation ceiling on every extension this line grants, Vendetta kills included.
        public FP[] MaxExtension = { 3, 5, 10 };
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

            // BerserkSkillData.Begin already added the ledger for this activation (zeroed, with its
            // own BaseMaxExtension) - this raises the ceiling to this line's ranked value and turns
            // on the kill gating. AccumulatedExtension/KillCount are deliberately NOT re-zeroed here:
            // Begin owns that, and it runs first.
            f.AddOrGet<OverdriveExtension>(filter.Entity, out var ledger);
            ledger->PerKillExtension = PerKillExtension[index];
            ledger->KillsPerExtension = KillsPerExtension[index];
            ledger->MaxExtension = FPMath.Max(ledger->MaxExtension, MaxExtension[index]);
            ledger->VendettaKillExtension = VendettaKillExtension[index];
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}

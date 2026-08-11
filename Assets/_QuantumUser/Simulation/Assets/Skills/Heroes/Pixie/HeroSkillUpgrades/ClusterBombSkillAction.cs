namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Hero Skill Ascension - while equipped, the bomb's detonation also launches Count smaller
    // bombs in an even spread, each dealing DamagePercent of the triggering explosion's own damage
    // (see AreaHitData.TrySpawnClusterBomblets). Projectile's own AreaHitData needs
    // TriggersSpawnUpgrades = false, or each bomblet would cluster-bomb again on its own detonation,
    // cascading forever.
    //
    // Begin-only, deliberately not paired with End: this configures what the skill produces, not a
    // temporary buff that should only apply while the skill is actively resolving. Revoking on End
    // would race against Detonate() actually reading it - Begin/End brackets the throw itself, which
    // ends the tick after the bomb detonates, often before the detonation logic gets a chance to
    // read this tag. Re-granting fresh (idempotent) every activation and never removing it sidesteps
    // that race entirely - and reads the live rank fresh each time via selfRef, so a rank-up mid-run
    // takes effect on the very next throw.
    public unsafe partial class ClusterBombSkillAction : SkillActionData
    {
        public byte[] Count = { 2, 3, 4 };
        public FP[] DamagePercent = { FP.FromString("0.40"), FP.FromString("0.45"), FP.FromString("0.50") };

        [ExpandableAsset] public AssetRef<ProjectileDataAsset> Projectile;

        public ClusterBombSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<ClusterBombUpgrade>(filter.Entity, out var cluster);
            cluster->Count = Count[index];
            cluster->DamagePercent = DamagePercent[index];
            cluster->Projectile = Projectile;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}

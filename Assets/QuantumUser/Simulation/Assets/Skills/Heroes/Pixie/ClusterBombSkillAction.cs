namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, the bomb's detonation also launches Count smaller bombs
    // in an even spread (see AreaHitData.TrySpawnClusterBomblets). Projectile's own AreaHitData
    // needs TriggersSpawnUpgrades = false, or each bomblet would cluster-bomb again on its own
    // detonation, cascading forever.
    //
    // Begin-only, deliberately not paired with End: this configures what the skill produces, not a
    // temporary buff that should only apply while the skill is actively resolving. Revoking on End
    // would race against Detonate() actually reading it - Begin/End brackets the throw itself, which
    // ends the tick after the bomb detonates, often before the detonation logic gets a chance to
    // read this tag. Re-granting fresh (idempotent) every activation and never removing it
    // sidesteps that race entirely.
    public unsafe partial class ClusterBombSkillAction : SkillActionData
    {
        public int Count = 3;
        public FP Damage = 5;

        [ExpandableAsset] public AssetRef<ProjectileDataAsset> Projectile;

        public ClusterBombSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<ClusterBombUpgrade>(filter.Entity, out var cluster);
            cluster->Count = (byte)Count;
            cluster->Damage = Damage;
            cluster->Projectile = Projectile;
        }
    }
}

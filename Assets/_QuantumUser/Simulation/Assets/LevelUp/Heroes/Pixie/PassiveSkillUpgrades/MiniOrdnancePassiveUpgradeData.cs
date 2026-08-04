namespace Quantum
{
    using Photon.Deterministic;

    // Demolition Mastery Hero Trait - the design's own "Cluster Charges" trait (see this asset's own
    // DisplayName), kept as a deliberately distinct class/component name (MiniOrdnanceUpgrade, not
    // anything containing "Cluster") so it can never be confused with the pre-existing
    // ClusterBombUpgrade/ClusterBombSkillAction (Pixie's Bunny Bomb Hero Skill Upgrade - a different
    // mechanism entirely, deterministic Projectile-based bomblets, must not be touched by this).
    // Any qualifying explosion (see PixieDemolitionMasterySystem.OnAreaExplosionDetonated) rolls
    // Chance to drop a stationary Mini Bomb at the blast center - see Heroes/Pixie/
    // DemolitionMastery.qtn for why a Mini Bomb's own detonation can never trigger this again.
    public unsafe partial class MiniOrdnancePassiveUpgradeData : PassiveUpgradeData
    {
        public FP Chance = FP.FromString("0.25");
        public FP Damage = 10;
        public FP Fuse = FP.FromString("0.4");

        [ExpandableAsset] public AssetRef<EntityPrototype> MiniBombPrototype;
        [ExpandableAsset] public AssetRef<AreaHitData> Explosion;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<MiniOrdnanceUpgrade>(entity, out var upgrade);
            upgrade->Chance = FPMath.Max(upgrade->Chance, Chance);
            upgrade->Damage += Damage;
            upgrade->Fuse = Fuse;
            upgrade->MiniBombPrototype = MiniBombPrototype;
            upgrade->Explosion = Explosion;
        }
    }
}

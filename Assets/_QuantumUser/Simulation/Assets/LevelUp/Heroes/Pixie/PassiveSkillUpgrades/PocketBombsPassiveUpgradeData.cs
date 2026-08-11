namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Ascension - the design's own "Cluster Charges" trait (see this asset's own DisplayName),
    // kept as a deliberately distinct class/component name (PocketBombsUpgrade, not anything
    // containing "Cluster") so it can never be confused with the pre-existing ClusterBombUpgrade/
    // ClusterBombSkillAction (Pixie's Bunny Bomb Hero Skill Upgrade - a different mechanism entirely,
    // deterministic Projectile-based bomblets, must not be touched by this). Any qualifying explosion
    // (see PixieDemolitionMasterySystem.OnAreaExplosionDetonated) rolls Chance to drop a stationary
    // Mini Bomb at the blast center dealing DamagePercent of Bunny Bomb's own base damage - see
    // Heroes/Pixie/DemolitionMastery.qtn for why a Mini Bomb's own detonation can never trigger this
    // again. Each rank SETS the total Chance/DamagePercent (not additive across ranks).
    public unsafe partial class PocketBombsPassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] Chance = { FP.FromString("0.15"), FP.FromString("0.25"), FP.FromString("0.35") };
        public FP[] DamagePercent = { FP.FromString("0.35"), FP.FromString("0.45"), FP.FromString("0.55") };
        public FP Fuse = FP.FromString("0.4");

        [ExpandableAsset] public AssetRef<EntityPrototype> MiniBombPrototype;
        [ExpandableAsset] public AssetRef<AreaHitData> Explosion;

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            f.AddOrGet<PocketBombsUpgrade>(entity, out var upgrade);

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            upgrade->Chance = Chance[index];
            upgrade->DamagePercent = DamagePercent[index];
            upgrade->Fuse = Fuse;
            upgrade->MiniBombPrototype = MiniBombPrototype;
            upgrade->Explosion = Explosion;
        }
    }
}

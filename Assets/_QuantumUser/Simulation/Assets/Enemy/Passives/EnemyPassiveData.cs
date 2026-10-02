namespace Quantum
{
    // A reusable, data-carrying enemy behaviour that isn't an attack - an oil trail, later auras,
    // on-death spawns, etc. Listed on EnemyDataAsset.Passives and applied once at spawn by
    // EnemySystem.SeedFromEnemyData, so any enemy (a Normal, or an Elite reusing the same asset) gets
    // it by adding one entry - no new enemy code. Typically Apply just adds/configures a component
    // that its own system then drives. Distinct from EnemyStatsData.Traits, which are bare flags.
    public abstract unsafe class EnemyPassiveData : AssetObject
    {
        public abstract void Apply(Frame f, EntityRef enemy);
    }
}

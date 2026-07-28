namespace Quantum
{
    using Photon.Deterministic;

    // Drops a prefab where the hit landed - a grenade that keeps burning after the blast. What the
    // spawned thing then does is the prototype's own business (an AreaDamage on it hurts whoever
    // stands in it), so this is the projectile-side twin of SpawnEntitySkillAction: the same spawn
    // off a different trigger.
    public unsafe class SpawnEntityEffectData : HitEffectData
    {
        public AssetRef<EntityPrototype> Prototype;

        public FP Duration = 3;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            SpawnedEntitySpawner.Spawn(f, context.Owner, Prototype, Duration, context.Position, context.Source, context.Element);
        }
    }
}

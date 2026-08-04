namespace Quantum
{
    using Photon.Deterministic;

    // Fires a follow-up shot from wherever the parent hit landed (shards, chain bolts, cluster
    // bomblets). Pointing this at a ProjectileDataAsset that leads back to itself recurses until
    // the frame dies - the recursion is unguarded on purpose, since a depth counter would have to
    // live on every projectile for a case authoring can simply avoid.
    public unsafe class SpawnProjectileEffectData : HitEffectData
    {
        [ExpandableAsset] public AssetRef<ProjectileDataAsset> ProjectileData;

        public FP Damage = 5;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            ProjectileDataAsset projectileData = f.FindAsset(ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            ProjectileLaunch launch = movement.GetLaunch(f, context.Position, context.PushDirection);

            if (launch.IsValid == false)
                return;

            ProjectileSpawner.Spawn(f, context.Owner, ProjectileData, launch, Damage, context.Source, target: context.Target);
        }
    }
}

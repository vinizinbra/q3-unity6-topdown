namespace Quantum
{
    using Photon.Deterministic;

    // Launches a real projectile (ballistic arc, per-tick raycast hit detection, ProjectileHitData
    // settle/detonate) as a skill action, rather than SpawnEntitySkillAction's instant drop-in-place -
    // right for anything that needs to fly and land (a bomb tossed mid-dash) instead of appearing
    // fully formed where it's cast. Fires the same way ProjectileSkillData's own throw does (see
    // ProjectileAimUtility) so a skill's primary shot and an action-triggered one aim identically -
    // just wired to a phase in a skill's Actions list instead of being the skill itself.
    public unsafe partial class SpawnProjectileSkillAction : SkillActionData
    {
        [ExpandableAsset] public AssetRef<ProjectileDataAsset> Projectile;

        public FP Damage = 10;

        public ProjectileSpawnAnchor SpawnAnchor = ProjectileSpawnAnchor.OnSelf;
        public FPVector3 SpawnOffset;

        // Only matters for SpawnAnchor.OnTarget or a free-aimed cast with no lock - see
        // ProjectileSkillData.Range's own doc comment, same synthetic-destination fallback.
        public FP Range = 10;

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            FPVector3 casterPosition = filter.Transform3D->Position;
            FP aimAngle = filter.Aim->Angle;
            FPVector3 flatDirection = FPQuaternion.Euler(0, aimAngle, 0) * FPVector3.Forward;
            bool aimAtCenter = ProjectileAimUtility.ResolveAimsAtCenter(f, Projectile);
            FPVector3 fallbackTarget = casterPosition + flatDirection * Range;
            FPVector3 spawnPosition = ProjectileSpawner.ResolveSpawnOrigin(casterPosition, fallbackTarget, aimAngle, SpawnAnchor, SpawnOffset);
            FPVector3 aimDirection = ProjectileAimUtility.ResolveAimDirection(f, filter.Aim->Target, spawnPosition, flatDirection, aimAtCenter);

            ProjectileDataAsset projectileData = f.FindAsset(Projectile);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            ProjectileLaunch launch = ProjectileAimUtility.TryGetAimPoint(f, filter.Aim->Target, aimAtCenter, out FPVector3 aimPoint)
                ? movement.GetLaunchToTarget(f, ProjectileSpawner.ResolveSpawnOrigin(casterPosition, aimPoint, aimAngle, SpawnAnchor, SpawnOffset), aimPoint, filter.Aim->Target)
                : movement.GetLaunch(f, spawnPosition, aimDirection);

            if (launch.IsValid == false)
            {
                Log.Error($"[Skill] {filter.Entity} resolved no valid projectile launch - nothing spawned");
                return;
            }

            EntityRef spawned = ProjectileSpawner.Spawn(f, filter.Entity, Projectile, launch, Damage, DamageSource.Skill, target: filter.Aim->Target);

            Log.Debug($"[Skill] {filter.Entity} spawned {spawned} at {launch.SpawnPosition} on {firedPhase}");
        }
    }
}

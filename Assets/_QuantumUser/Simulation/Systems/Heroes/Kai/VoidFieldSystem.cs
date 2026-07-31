namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drives every ProjectileSlowField entity - Kai's continuous Void Field passive (added directly
    // onto his own entity, so it follows him via his own Transform3D) and any standalone SlowArea
    // dash-ascension drop. One System rather than two: both halves read the same field/radius, and
    // neither has enough going on alone to justify splitting (same reasoning CombatDirectorSystem's
    // own comment gives for merging its two domains).
    //
    // Must run after SkillSystem (so a SlowArea entity spawned this same tick already exists here)
    // and before both EnemySystem (so a slowed enemy's Active-phase Tick reads this tick's fresh
    // TimeDilation, not last tick's stale value) and ProjectileSystem (same reasoning for
    // SpeedMultiplier) - see SystemSetup.User.cs.
    [Preserve]
    public unsafe class VoidFieldSystem : SystemMainThread
    {
        // Refresh-only, same idiom SentryAuraSystem's own AuraRefreshDuration uses - reapplied every
        // tick a target stays in range, so it decays on its own the instant it leaves without any
        // removal logic needed.
        private static readonly FP EnemyRefreshDuration = FP._1;

        public override void Update(Frame f)
        {
            ResetProjectileMultipliers(f);
            ApplyFieldsToProjectiles(f);
            ApplyFieldsToEnemies(f);
        }

        private static void ResetProjectileMultipliers(Frame f)
        {
            var projectiles = f.Filter<Projectile>();

            while (projectiles.Next(out EntityRef entity, out Projectile _))
            {
                if (f.Unsafe.TryGetPointer<Projectile>(entity, out var projectile) == true)
                {
                    projectile->SpeedMultiplier = FP._1;
                }
            }
        }

        // Only enemy-owned projectiles are affected - Kai's own field never slows a teammate's
        // weapon fire passing through it.
        private static void ApplyFieldsToProjectiles(Frame f)
        {
            var fields = f.Filter<ProjectileSlowField, Transform3D>();

            while (fields.Next(out EntityRef _, out ProjectileSlowField field, out Transform3D fieldTransform))
            {
                var projectiles = f.Filter<Projectile, Transform3D>();

                while (projectiles.Next(out EntityRef projectileEntity, out Projectile projectile, out Transform3D projectileTransform))
                {
                    if (f.Has<Enemy>(projectile.Owner) == false)
                        continue;

                    if ((projectileTransform.Position - fieldTransform.Position).SqrMagnitude > field.Radius * field.Radius)
                        continue;

                    if (f.Unsafe.TryGetPointer<Projectile>(projectileEntity, out var live) == false)
                        continue;

                    // Min, not overwrite - a projectile briefly overlapping two fields at once takes
                    // whichever slows it hardest, rather than whichever field's loop iteration happened
                    // to run last.
                    live->SpeedMultiplier = FPMath.Min(live->SpeedMultiplier, field.SpeedMultiplier);
                }
            }
        }

        // Void Pressure only - a field with EnemyTimeDilationMultiplier still at its 0 default (every
        // field, until that ascension is taken) skips this entirely. Tier-gated to Filler/Normal/
        // Specialist - never Elite/Boss.
        private static void ApplyFieldsToEnemies(Frame f)
        {
            var fields = f.Filter<ProjectileSlowField, Transform3D>();

            while (fields.Next(out EntityRef _, out ProjectileSlowField field, out Transform3D fieldTransform))
            {
                if (field.EnemyTimeDilationMultiplier <= FP._0)
                    continue;

                var enemies = f.Filter<Enemy, Transform3D>();

                while (enemies.Next(out EntityRef enemyEntity, out Enemy enemy, out Transform3D enemyTransform))
                {
                    EnemyDataAsset data = f.FindAsset(enemy.EnemyData);

                    if (data.Tier > EnemyTier.Specialist)
                        continue;

                    if ((enemyTransform.Position - fieldTransform.Position).SqrMagnitude > field.Radius * field.Radius)
                        continue;

                    StatusEffectUtility.ApplyTimeDilation(f, enemyEntity, EnemyRefreshDuration, field.EnemyTimeDilationMultiplier);
                }
            }
        }
    }
}

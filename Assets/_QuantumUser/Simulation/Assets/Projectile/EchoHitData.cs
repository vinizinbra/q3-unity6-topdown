namespace Quantum
{
    using Photon.Deterministic;

    // ProjectileHitData for a Damage Echo delivered as a real traveling projectile (see DamageEcho.qtn
    // /DamageEchoSystem.SpawnEchoProjectile) - deliberately minimal: no Effects list, no elemental
    // status application, no on-hit proc chain of any kind. It exists to do exactly one thing: deal
    // Projectile.Damage (already resolved - the original shot's own pre-hit damage x DamageMultiplier,
    // captured once back when the echo was scheduled at fire time) to whatever it lands on, via
    // DamageUtility.ApplyDamage with bypassOutgoingResolution: true - the same "no re-resolution,
    // structurally can't schedule another echo, can't retrigger Echo Chamber/Infinite Echo/Double Tap"
    // guarantee every other weapon impact already has (see DamageEchoUpgrade.EchoHit's own comment).
    public unsafe class EchoHitData : ProjectileHitData
    {
        public override bool ApplyHit(Frame f, EntityRef entity, Projectile* projectile, EntityRef hitEntity, FPVector3 point)
        {
            if (ShouldDetonate(f, projectile, hitEntity) == false)
            {
                // Scenery/an ignored contact doesn't count as landing - keep flying (a homing shot
                // with nothing left to home on just continues straight, see
                // HomingProjectileMovementData's own comment) rather than settling in place.
                return false;
            }

            DamageUtility.ApplyDamage(f, hitEntity, projectile->Damage, projectile->Owner, projectile->Source, bypassOutgoingResolution: true);

            AssetRef<DamageEchoVisualData> visual = f.Unsafe.TryGetPointer<EchoProjectile>(entity, out var echo)
                ? echo->Visual
                : default;

            f.Events.DamageEchoTriggered(projectile->Owner, hitEntity, point, projectile->Damage, visual);

            return true;
        }

        // Ran out of flight time/distance without connecting (it simply missed, flying along its
        // fixed fired-at-schedule-time direction - see PendingDamageEcho.Direction) - just vanishes:
        // no damage, no event.
        public override void ApplyExpire(Frame f, Projectile* projectile, FPVector3 position)
        {
        }
    }
}

namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class DirectHitData : ProjectileHitData
    {
        // Entities the shot passes through before it's spent; 1 stops on the first one.
        public int PierceCount = 1;

        public override void Initialize(Projectile* projectile)
        {
            projectile->RemainingPierces = PierceCount;
        }

        public override bool ApplyHit(Frame f, Projectile* projectile, EntityRef hitEntity, FPVector3 point)
        {
            ApplyEffects(f, projectile, hitEntity, point, projectile->Velocity.Normalized);

            // Level geometry stops the shot however much pierce is left.
            if (hitEntity == EntityRef.None)
                return true;

            projectile->RemainingPierces--;

            return projectile->RemainingPierces <= 0;
        }

        // Ran out of Lifetime/MaxDistance without connecting - still applies Effects (target
        // EntityRef.None, same as ApplyHit already does for a level-geometry hit) so a
        // SpawnEntityEffectData-family effect (e.g. Kai's vortex bolt, meant to activate at a fixed
        // range regardless of whether it hit anything) still fires. Effects that need a real Target
        // (DamageEffectData etc.) naturally no-op against None, same as they already do on a
        // level-geometry hit.
        public override void ApplyExpire(Frame f, Projectile* projectile, FPVector3 position)
        {
            ApplyEffects(f, projectile, EntityRef.None, position, projectile->Velocity.Normalized);
        }
    }
}

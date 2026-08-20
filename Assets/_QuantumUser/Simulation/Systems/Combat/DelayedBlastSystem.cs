namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Fires a pending DelayedBlast once its countdown lapses - see that component. Generic and
    // hero-agnostic: Pixie's Unstable Mixture rank 3 secondary explosion and Brute's Aftershock rank 3
    // "Earthquake" shockwave both route through this rather than each shipping a near-identical
    // countdown system.
    [Preserve]
    public unsafe class DelayedBlastSystem : SystemMainThreadFilter<DelayedBlastSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            filter.Blast->Remaining -= f.DeltaTime;

            if (filter.Blast->Remaining > FP._0)
                return;

            // Copied out BEFORE the component is removed - filter.Blast is a pointer into that
            // component's own memory and must not be read afterwards.
            FPVector3 position = filter.Blast->Position;
            FP radius = filter.Blast->Radius;
            FP damage = filter.Blast->Damage;
            FP stunDuration = filter.Blast->StunDuration;
            bool isExplosion = filter.Blast->IsExplosion;
            bool isChained = filter.Blast->IsChainedExplosion;

            // Removed BEFORE firing, so nothing the blast itself triggers can re-enter this entity's
            // own single pending slot mid-resolution.
            f.Remove<DelayedBlast>(filter.Entity);

            if (radius <= FP._0)
                return;

            if (damage > FP._0)
            {
                HitEffectUtility.ApplyDamageInRadius(f, position, radius, filter.Entity, damage,
                    DamageSource.Skill, DamageTargetMask.Enemies, isChained, isExplosion);
            }

            if (stunDuration > FP._0)
            {
                // Damage already applied above (or deliberately skipped) - this pass is stun-only, so
                // it passes 0 damage rather than double-hitting.
                BruteAscensionUtility.ApplyRadialStunDamage(f, position, radius, filter.Entity, FP._0, stunDuration);
            }

            f.Events.WeaponExplosionReleased(filter.Entity, position, radius);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public DelayedBlast* Blast;
        }
    }
}

namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    [Preserve]
    public unsafe class DestroyAfterTimeSystem : SystemMainThreadFilter<DestroyAfterTimeSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            filter.DestroyAfterTime->RemainingTime -= f.DeltaTime;

            if (filter.DestroyAfterTime->RemainingTime > FP._0)
                return;

            // ExplodeOnDestroy (see ExplodeOnDestroy.qtn) - optional and fully generic, so every
            // other DestroyAfterTime user (fire trails, blasts, decoys, orb pickups, ...) is
            // completely unaffected. One of ExplodeOnDestroy's two trigger points - see
            // ExplodeOnDestroyUtility, shared with DamageUtility.ApplyDamage's own death branch so a
            // damageable Mini Bomb (Health 1 + Decoy, a real trap) detonates identically whether its
            // fuse ran out or an enemy killed it first.
            ExplodeOnDestroyUtility.TryDetonate(f, filter.Entity);

            Log.Debug($"[Lifetime] {filter.Entity} expired");
            f.Destroy(filter.Entity);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public DestroyAfterTime* DestroyAfterTime;
        }
    }
}

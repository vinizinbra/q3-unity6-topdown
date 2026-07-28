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

            if (filter.DestroyAfterTime->RemainingTime <= FP._0)
            {
                Log.Debug($"[Lifetime] {filter.Entity} expired");
                f.Destroy(filter.Entity);
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public DestroyAfterTime* DestroyAfterTime;
        }
    }
}

namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drives Overload's chain over real simulated time, one hop per OverloadChainDelay. The chain
    // lives on its own entity (see OverloadChain.qtn/StatusEffects.qtn) so it survives its origin
    // enemy dying - which trash-tier enemies do instantly. Destroyed once no hops remain.
    [Preserve]
    public unsafe class OverloadChainSystem : SystemMainThreadFilter<OverloadChainSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            OverloadChain* chain = filter.Chain;

            if (chain->HopsRemaining > 0)
            {
                chain->HopTimer -= f.DeltaTime;

                if (chain->HopTimer <= FP._0)
                    StatusEffectUtility.TryAdvanceOverloadChain(f, filter.Entity, chain);
            }

            if (chain->HopsRemaining == 0)
                f.Destroy(filter.Entity);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public OverloadChain* Chain;
        }
    }
}

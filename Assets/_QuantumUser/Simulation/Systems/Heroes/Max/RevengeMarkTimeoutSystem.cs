namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks every active Vendetta mark down, independent of whoever holds it - runs once per marked
    // enemy (RevengeMark now lives on the enemy, see Vendetta.qtn, so an arbitrary number of these
    // can be active at once with no per-holder cap). No ordering dependency on anything else, same
    // reasoning JuggernautDischargeCooldownSystem's own placement comment gives. A mark that expires
    // here (as opposed to being consumed by a kill, see MaxVendettaSystem.OnEntityKilled) simply
    // lapses - no heal, matching "killing the marked target consumes the mark" vs. the timer running
    // out.
    [Preserve]
    public unsafe class RevengeMarkTimeoutSystem : SystemMainThreadFilter<RevengeMarkTimeoutSystem.Filter>
    {
        public struct Filter
        {
            public EntityRef Entity;
            public RevengeMark* RevengeMark;
        }

        public override void Update(Frame f, ref Filter filter)
        {
            filter.RevengeMark->RemainingDuration -= f.DeltaTime;

            if (filter.RevengeMark->RemainingDuration > FP._0)
                return;

            f.Remove<RevengeMark>(filter.Entity);
        }
    }
}

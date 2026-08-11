namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks First Strike rank 3's ("Perfect Opening") refresh window down - a mark with
    // RemainingGrace == 0 (rank 1-2, or no upgrade at all) is left untouched forever here, preserving
    // "never removed" exactly; only a mark whose RemainingGrace was actually seeded > 0 (rank 3) ever
    // gets ticked/freed. See DamageUtility.ResolveOutgoingDamage, which seeds RemainingGrace fresh off
    // FirstStrikeUpgrade.RefreshWindow on every qualifying hit. No ordering dependency on anything
    // else, same reasoning RevengeMarkTimeoutSystem's own placement comment gives.
    [Preserve]
    public unsafe class FirstStrikeMarkTimeoutSystem : SystemMainThreadFilter<FirstStrikeMarkTimeoutSystem.Filter>
    {
        public struct Filter
        {
            public EntityRef Entity;
            public FirstStrikeMark* FirstStrikeMark;
        }

        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.FirstStrikeMark->RemainingGrace <= FP._0)
                return;

            filter.FirstStrikeMark->RemainingGrace -= f.DeltaTime;

            if (filter.FirstStrikeMark->RemainingGrace > FP._0)
                return;

            f.Remove<FirstStrikeMark>(filter.Entity);
        }
    }
}

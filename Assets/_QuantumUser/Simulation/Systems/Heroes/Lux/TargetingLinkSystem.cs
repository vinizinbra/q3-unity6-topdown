namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks down Lux's Targeting Link mark (Assault Rifle Mastery R3) and removes it on expiry -
    // refresh-duration, never stacking, since HeroMasteryUtility.ApplyTargetingLinkMark always
    // overwrites Remaining outright rather than adding to it. Filtered on TargetingLinkMark, so this
    // costs nothing for any target Lux hasn't marked.
    [Preserve]
    public unsafe class TargetingLinkSystem : SystemMainThreadFilter<TargetingLinkSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            filter.Mark->Remaining -= f.DeltaTime;

            if (filter.Mark->Remaining <= FP._0)
            {
                f.Remove<TargetingLinkMark>(filter.Entity);
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public TargetingLinkMark* Mark;
        }
    }
}

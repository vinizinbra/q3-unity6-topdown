namespace Quantum
{
    using UnityEngine.Scripting;

    // Keeps every POI's own PoiActivation.State fresh - unfiltered SystemMainThread (like
    // CombatDirectorSystem) rather than a SystemMainThreadFilter, since it touches several
    // unrelated component types (HealingShrine, CursedRift, Store, Blacksmith) in one pass. A
    // future POI kind adds one more small loop here, not a new system. Cheap by construction - at
    // most a handful of POI instances, each checking a handful of connected players (see
    // PoiActivationUtility.AnyConnectedPlayerCanUse).
    [Preserve]
    public unsafe class PoiActivationSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            var shrines = f.Filter<HealingShrine>();

            while (shrines.Next(out EntityRef entity, out HealingShrine shrine))
            {
                PoiActivationUtility.Refresh(f, entity, shrine.Availability, shrine.UsagePolicy);
            }

            var rifts = f.Filter<CursedRift>();

            while (rifts.Next(out EntityRef entity, out CursedRift rift))
            {
                PoiActivationUtility.Refresh(f, entity, rift.Availability, rift.UsagePolicy);
            }

            // Store has no PoiUsagePolicy of its own (see Store.qtn) - per-offer purchase state is
            // tracked separately (StorePurchases), not via the generic whole-POI PoiUsage mechanism
            // PoiActivationUtility.AnyConnectedPlayerCanUse reads - so Reusable ("always usable
            // while Available") is the correct policy to resolve PoiActivation.State against.
            var stores = f.Filter<Store>();

            while (stores.Next(out EntityRef entity, out Store store))
            {
                PoiActivationUtility.Refresh(f, entity, store.Availability, PoiUsagePolicy.Reusable);
            }

            var forges = f.Filter<Blacksmith>();

            while (forges.Next(out EntityRef entity, out Blacksmith forge))
            {
                PoiActivationUtility.Refresh(f, entity, forge.Availability, forge.UsagePolicy);
            }

            // Per-PLAYER, not per-POI (unlike every loop above) - decays any Cooldown-policy
            // PoiUsage entries so a cooldown keeps counting down through a Breathing Break, not
            // just through Combat (see PoiUsageUtility.TickCooldowns). Lives here anyway since this
            // is already the single generic per-tick POI-infra pass.
            var players = f.Filter<PoiUsage>();

            while (players.Next(out EntityRef entity, out PoiUsage _))
            {
                PoiUsageUtility.TickCooldowns(f, entity);
            }
        }
    }
}

namespace Quantum
{
    // Resolves the shared PoiViewState (Inactive/Active/Expired) any POI's View can read off its
    // own PoiActivation component - see Poi.qtn's own comment. Called once per POI entity per
    // tick by PoiActivationSystem; POI-specific resolvers (HealingShrineUtility, CursedRiftUtility)
    // never touch PoiActivation themselves, keeping "is this thing usable" and "what should it
    // look like" cleanly separate.
    public static unsafe class PoiActivationUtility
    {
        public static void Refresh(Frame f, EntityRef poi, PoiAvailability availability, PoiUsagePolicy usagePolicy)
        {
            f.AddOrGet<PoiActivation>(poi, out var activation);

            if (PoiAvailabilityUtility.IsAvailable(f, availability) == false)
            {
                activation->State = PoiViewState.Inactive;
                return;
            }

            activation->State = AnyConnectedPlayerCanUse(f, poi, usagePolicy) ? PoiViewState.Active : PoiViewState.Expired;
        }

        // "Expired" means every CONNECTED player (not just this client's own local ones) has
        // already used this POI - a genuinely shared, deterministic fact every client resolves
        // identically, not a per-viewer approximation.
        private static bool AnyConnectedPlayerCanUse(Frame f, EntityRef poi, PoiUsagePolicy usagePolicy)
        {
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink _))
            {
                if (PoiUsageUtility.CanUse(f, entity, poi, usagePolicy) == true)
                    return true;
            }

            return false;
        }
    }
}

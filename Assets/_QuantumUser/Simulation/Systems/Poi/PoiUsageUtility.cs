namespace Quantum
{
    using Photon.Deterministic;

    // Per-player POI usage tracking - see Poi.qtn's own comment for the overall design. A POI
    // system calls CanUse before letting a player trigger its effect, then MarkUsed right after -
    // this utility owns the entire "have they already used THIS specific POI instance, under
    // THIS policy" question so no POI system needs its own bookkeeping.
    public static unsafe class PoiUsageUtility
    {
        public static bool CanUse(Frame f, EntityRef player, EntityRef poi, PoiUsagePolicy policy)
        {
            if (policy == PoiUsagePolicy.Reusable)
                return true;

            if (policy == PoiUsagePolicy.OncePerWorld)
            {
                Log.Error("[Poi] OncePerWorld usage policy is not implemented yet (no POI needs it this pass) - treating as unusable rather than silently misbehaving. See Poi.qtn's own comment.");
                return false;
            }

            if (f.Unsafe.TryGetPointer<PoiUsage>(player, out var usage) == false)
                return true; // no usage record at all yet - this player has never used anything

            if (TryFindEntry(usage, poi, out PoiUsageEntry entry) == false)
                return true;

            if (policy == PoiUsagePolicy.Cooldown)
                return entry.CooldownRemaining <= FP._0;

            if (policy == PoiUsagePolicy.OncePerPlayerPerRun)
                return false; // any recorded entry at all means already used, permanently (see the -1 sentinel MarkUsed writes)

            // OncePerPlayerPerBreak - eligible again once Global.BreathingIndex has moved past
            // whichever Break they used it during.
            return entry.UsedAtBreathingIndex != f.Global->BreathingIndex;
        }

        // cooldownDuration is only read under the Cooldown policy - the POI's own component owns
        // that value (e.g. HealingShrine.CooldownDuration), so every call site just forwards its
        // own field here regardless of which policy is actually authored, harmlessly ignored
        // otherwise.
        public static void MarkUsed(Frame f, EntityRef player, EntityRef poi, PoiUsagePolicy policy, FP cooldownDuration = default)
        {
            if (policy == PoiUsagePolicy.Reusable || policy == PoiUsagePolicy.OncePerWorld)
                return; // Reusable never needs a record; OncePerWorld isn't implemented (see CanUse)

            f.AddOrGet<PoiUsage>(player, out var usage);
            var entries = usage->Entries;

            // OncePerPlayerPerRun sentinel (-1) never matches a real Global.BreathingIndex, so
            // CanUse's OncePerPlayerPerBreak branch above is never reached for a stored -1 anyway -
            // the OncePerPlayerPerRun branch above always short-circuits on "any entry exists" first.
            int usedAtIndex = policy == PoiUsagePolicy.OncePerPlayerPerRun ? -1 : f.Global->BreathingIndex;
            FP cooldownRemaining = policy == PoiUsagePolicy.Cooldown ? cooldownDuration : FP._0;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Poi != poi)
                    continue;

                PoiUsageEntry entry = entries[i];
                entry.UsedAtBreathingIndex = usedAtIndex;
                entry.CooldownRemaining = cooldownRemaining;
                entries[i] = entry;
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Poi != EntityRef.None)
                    continue;

                entries[i] = new PoiUsageEntry { Poi = poi, UsedAtBreathingIndex = usedAtIndex, CooldownRemaining = cooldownRemaining };
                return;
            }

            Log.Error($"[Poi] {player} has no free PoiUsage slot for {poi} - usage won't be tracked, treat as a headroom bug (see Poi.qtn's own array-size comment)");
        }

        // Ticks every Cooldown-policy entry on this player down by one frame's worth of real time -
        // called once per player per tick by PoiActivationSystem, the single generic per-tick POI-
        // infra pass. Deliberately unconditional (no early-out on "does this player use any Cooldown
        // POI") - cheap by construction, same reasoning PoiActivationSystem's own POI-side loops
        // already document.
        public static void TickCooldowns(Frame f, EntityRef player)
        {
            if (f.Unsafe.TryGetPointer<PoiUsage>(player, out var usage) == false)
                return;

            var entries = usage->Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].CooldownRemaining <= FP._0)
                    continue;

                PoiUsageEntry entry = entries[i];
                entry.CooldownRemaining = FPMath.Max(FP._0, entry.CooldownRemaining - f.DeltaTime);
                entries[i] = entry;
            }
        }

        private static bool TryFindEntry(PoiUsage* usage, EntityRef poi, out PoiUsageEntry entry)
        {
            var entries = usage->Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Poi == poi)
                {
                    entry = entries[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }
    }
}

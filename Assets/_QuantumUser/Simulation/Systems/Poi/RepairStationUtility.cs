namespace Quantum
{
    // The Base's Repair Station (Rift Anchor L2, meta progression - see RepairStation.qtn). A one-shot press
    // like HealingShrineUtility: restores Accessory Guard blocks for free. Its own charge rule instead of a
    // PoiUsagePolicy: one use per Break, re-armed the moment each Break STARTS (BreaksStarted) - not ready at
    // the start of a run. "Nothing to repair" reuses AccessoryServiceUtility.ResolveService, the same
    // eligibility the Merchant's paid repair reads.
    public static unsafe class RepairStationUtility
    {
        // Breaks that have begun this run: BreathingIndex counts ENDED Breaks (CombatDirectorSystem bumps it
        // as one closes), plus the one currently running.
        public static int BreaksStarted(Frame f)
        {
            return f.Global->BreathingIndex + (f.Global->CombatPhaseState == GameState.Breathing ? 1 : 0);
        }

        // This player's charge is armed: a Break has started since they last used a station.
        public static bool CanUse(Frame f, EntityRef player)
        {
            int usedAt = f.Unsafe.TryGetPointer<RepairStationUsage>(player, out var usage) ? usage->UsedAtBreaksStarted : 0;
            return BreaksStarted(f) > usedAt;
        }

        public static ContextInteractionState ResolveInteractionState(Frame f, EntityRef player, EntityRef station)
        {
            if (f.Unsafe.TryGetPointer<RepairStation>(station, out var repairStation) == false)
                return ContextInteractionState.None;

            if (PoiAvailabilityUtility.IsAvailable(f, repairStation->Availability) == false)
                return ContextInteractionState.PhaseUnavailable;

            if (CanUse(f, player) == false)
                return BreaksStarted(f) == 0 ? ContextInteractionState.PhaseUnavailable : ContextInteractionState.AlreadyUsed;

            if (AccessoryServiceUtility.ResolveService(f, player) == AccessoryServiceKind.None)
                return ContextInteractionState.NotNeeded;

            return ContextInteractionState.Available;
        }

        // Called from SkillSystem on a Base Skill press while this station is the ContextInteraction target -
        // NotNeeded is let through there, so it's handled here (toast), same as HealingShrineUtility.TryInteract.
        public static void TryInteract(Frame f, EntityRef player, EntityRef station)
        {
            ContextInteractionState state = ResolveInteractionState(f, player, station);

            if (state == ContextInteractionState.NotNeeded)
            {
                f.Events.ContextInteractionRejected(player, station);
                return;
            }

            if (state != ContextInteractionState.Available)
                return;

            if (f.Unsafe.TryGetPointer<RepairStation>(station, out var repairStation) == false)
                return;

            int points = repairStation->RepairPoints > 0 ? repairStation->RepairPoints : 1;
            AccessoryGuardUtility.RestorePoints(f, player, points);

            f.AddOrGet<RepairStationUsage>(player, out var usage);
            usage->UsedAtBreaksStarted = BreaksStarted(f);

            if (f.Unsafe.TryGetPointer<AccessoryGuard>(player, out var guard))
                f.Events.AccessoryRestored(player, false, guard->CurrentDurability);

            Log.Debug($"[RepairStation] {player} repaired {points} Accessory Guard block(s) at {station}");
        }

        // PoiActivation for the shared View state (PoiActivationUtility.Refresh's shape, with this station's own
        // charge rule instead of a PoiUsagePolicy): Active while any connected player's charge is armed.
        public static void RefreshActivation(Frame f, EntityRef station, PoiAvailability availability)
        {
            f.AddOrGet<PoiActivation>(station, out var activation);

            if (PoiAvailabilityUtility.IsAvailable(f, availability) == false)
            {
                activation->State = PoiViewState.Inactive;
                return;
            }

            var players = f.Filter<PlayerLink>();
            while (players.Next(out EntityRef player, out PlayerLink _))
            {
                if (CanUse(f, player))
                {
                    activation->State = PoiViewState.Active;
                    return;
                }
            }

            activation->State = PoiViewState.Expired;
        }
    }
}

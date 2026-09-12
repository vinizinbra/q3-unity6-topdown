namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Cursed Rift's own interaction session - see CursedRift.qtn/docs/breathing-poi.md. Deliberately
    // a Cursed-Rift-specific interaction (not a generic transaction engine) - what's generic is
    // POI availability/usage (Poi.qtn) and the Base-Skill redirect (ContextInteraction.qtn), not
    // this one-sacrifice-for-one-mutation flow itself.
    public static unsafe class CursedRiftUtility
    {
        private struct Candidate
        {
            public AssetRef<SacrificeDefinition> Sacrifice;
            public int Weight;
        }

        // Re-checked by TryBeginInteraction right before actually starting - "am I still fully
        // eligible the instant the button press is processed this same tick," defense-in-depth
        // independent of whatever ContextInteractionSystem last resolved. Busy is checked here too
        // (not just at the ContextInteractionSystem call site) so this stays a safe, standalone
        // entry point on its own.
        public static bool CanInteract(Frame f, EntityRef player, EntityRef rift)
        {
            if (f.Has<CursedRiftInteraction>(player) == true)
                return false;

            return ResolveInteractionState(f, player, rift) == ContextInteractionState.Available;
        }

        // Read by ContextInteractionSystem's own per-kind dispatch (radius/closest-candidate
        // resolution already happened there via the sibling Interactable component; busy-ness is
        // also checked at that call site, uniformly across every InteractableKind, not repeated
        // here) - the richer WHY behind CanInteract's bool, so the world-space prompt
        // (InteractionPromptWidget) can explain itself instead of silently hiding.
        public static ContextInteractionState ResolveInteractionState(Frame f, EntityRef player, EntityRef rift)
        {
            if (f.Unsafe.TryGetPointer<CursedRift>(rift, out var cursedRift) == false)
                return ContextInteractionState.None;

            if (PoiAvailabilityUtility.IsAvailable(f, cursedRift->Availability) == false)
                return ContextInteractionState.PhaseUnavailable;

            if (PoiUsageUtility.CanUse(f, player, rift, cursedRift->UsagePolicy) == false)
                return ContextInteractionState.AlreadyUsed;

            return ContextInteractionState.Available;
        }

        // Called from SkillSystem when a locked-in ContextInteraction.ActiveTarget's Base Skill
        // button is pressed. Re-validates in full (never trusts the View/target resolution alone -
        // see docs/breathing-poi.md's own "Interaction validity confirmed in deterministic Quantum
        // simulation" requirement). Reuses this (player, rift)'s already-rolled CursedRiftOffers
        // entry if one exists and is still valid - the offer is meant to persist for the rest of
        // the run (Cancel + reopen shows the SAME sacrifice+mutation pair), not reroll on every
        // attempt. Only rolls a fresh sacrifice AND mutation reward (both together, nothing applied
        // yet - see Confirm) when there's no cached offer or it's gone stale (e.g. the cached
        // sacrifice's own eligibility changed since it was rolled). A no-op (logged, not silently
        // swallowed) if a fresh roll comes up empty either side - the player simply gets no
        // interaction that tick, same as pressing a skill button with 0 charges.
        public static void TryBeginInteraction(Frame f, EntityRef player, EntityRef rift)
        {
            if (CanInteract(f, player, rift) == false)
                return;

            if (f.RuntimeConfig.CursedRiftConfig.IsValid == false || f.RuntimeConfig.LevelUpConfig.IsValid == false)
            {
                Log.Error("[CursedRift] interaction requested but RuntimeConfig has no CursedRiftConfig/LevelUpConfig assigned - ignored");
                return;
            }

            AssetRef<SacrificeDefinition> sacrificeRef;
            LevelUpOption mutation;

            if (TryGetCachedOffer(f, player, rift, out sacrificeRef, out mutation) &&
                IsOfferStillValid(f, player, sacrificeRef, mutation) == true)
            {
                Log.Debug($"[CursedRift] {player} reopened their persisted offer at {rift}");
            }
            else
            {
                CursedRiftConfig config = f.FindAsset(f.RuntimeConfig.CursedRiftConfig);

                if (config.SacrificePool.IsValid == false)
                {
                    Log.Error("[CursedRift] CursedRiftConfig has no SacrificePool assigned - ignored");
                    return;
                }

                SacrificePoolData pool = f.FindAsset(config.SacrificePool);
                AssetRef<SacrificeDefinition>[] rolledSacrifice = RollSacrificeOptions(f, player, pool, 1);

                if (rolledSacrifice.Length == 0)
                {
                    Log.Debug($"[CursedRift] {player} has no eligible sacrifice right now - interaction skipped");
                    return;
                }

                LevelUpConfig levelUpConfig = f.FindAsset(f.RuntimeConfig.LevelUpConfig);
                LevelUpOption[] rolledMutation = LevelUpUtility.RollMutationOptions(f, player, levelUpConfig, 1);

                if (rolledMutation.Length == 0)
                {
                    Log.Debug($"[CursedRift] {player} has no eligible mutation reward right now - interaction skipped");
                    return;
                }

                sacrificeRef = rolledSacrifice[0];
                mutation = rolledMutation[0];
                SetCachedOffer(f, player, rift, sacrificeRef, mutation);

                Log.Debug($"[CursedRift] {player} rolled a new offer at {rift} - sacrifice {sacrificeRef} for a mutation reward");
            }

            f.AddOrGet<CursedRiftInteraction>(player, out var interaction);
            interaction->Rift = rift;
            interaction->Sacrifice = sacrificeRef;
            interaction->Mutation = mutation;
        }

        // Is a cached CursedRiftOfferEntry still safe to show as-is? Re-checks the exact same two
        // gates a fresh roll already filters by (SacrificeDefinition.IsEligible,
        // RiftMutationUtility.IsBlocked) - e.g. a cached Coin Offering goes stale the instant the
        // player spends their last Coin at the Store in between, and should reroll rather than
        // show an offer that can no longer actually be paid.
        private static bool IsOfferStillValid(Frame f, EntityRef player, AssetRef<SacrificeDefinition> sacrificeRef, LevelUpOption mutation)
        {
            if (sacrificeRef.IsValid == false || mutation.Upgrade.IsValid == false)
                return false;

            SacrificeDefinition sacrifice = f.FindAsset(sacrificeRef);

            if (sacrifice == null || sacrifice.IsEligible(f, player) == false)
                return false;

            if (mutation.Kind != LevelUpPoolKind.RiftMutation)
                return false;

            var mutationRef = new AssetRef<RiftMutationData>(mutation.Upgrade.Id);
            return RiftMutationUtility.IsBlocked(f, player, mutationRef) == false;
        }

        // CursedRiftOffers is per-player, keyed by the rolling Rift's own EntityRef - same
        // fixed-array-of-keyed-entries convention PoiUsage/RiftMutationPicks already use (see
        // CursedRift.qtn's own comment on the 8-slot size).
        private static bool TryGetCachedOffer(Frame f, EntityRef player, EntityRef rift, out AssetRef<SacrificeDefinition> sacrifice, out LevelUpOption mutation)
        {
            sacrifice = default;
            mutation = default;

            if (f.Unsafe.TryGetPointer<CursedRiftOffers>(player, out var offers) == false)
                return false;

            var entries = offers->Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Rift != rift)
                    continue;

                sacrifice = entries[i].Sacrifice;
                mutation = entries[i].Mutation;
                return true;
            }

            return false;
        }

        private static void SetCachedOffer(Frame f, EntityRef player, EntityRef rift, AssetRef<SacrificeDefinition> sacrifice, LevelUpOption mutation)
        {
            f.AddOrGet<CursedRiftOffers>(player, out var offers);
            var entries = offers->Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Rift != rift && entries[i].Rift != EntityRef.None)
                    continue;

                entries[i] = new CursedRiftOfferEntry { Rift = rift, Sacrifice = sacrifice, Mutation = mutation };
                return;
            }

            Log.Error($"[CursedRift] {player} has no free CursedRiftOffers slot for {rift} - this offer won't persist across cancel/reopen");
        }

        // Called by Confirm once the offer is actually taken - a fresh roll is due next time (e.g.
        // a Reusable/Cooldown Rift). Cancel deliberately never calls this.
        private static void ClearCachedOffer(Frame f, EntityRef player, EntityRef rift)
        {
            if (f.Unsafe.TryGetPointer<CursedRiftOffers>(player, out var offers) == false)
                return;

            var entries = offers->Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Rift != rift)
                    continue;

                entries[i] = default;
                return;
            }
        }

        // Weighted draw without replacement among currently-eligible sacrifices only - same shape
        // LevelUpUtility.RollOptionsFor/DrawWeighted uses, kept as its own small implementation
        // rather than forced through a shared generic helper since AssetRef<SacrificeDefinition>
        // isn't a LevelUpOption (no Kind/Slot/WeaponData baggage to carry). choiceCount is always 1
        // today (see TryBeginInteraction) but left general - the draw-without-replacement shape
        // costs nothing extra.
        private static AssetRef<SacrificeDefinition>[] RollSacrificeOptions(Frame f, EntityRef player, SacrificePoolData pool, int choiceCount)
        {
            List<Candidate> candidates = new List<Candidate>();
            int totalWeight = 0;

            for (int i = 0; i < pool.Sacrifices.Count; i++)
            {
                AssetRef<SacrificeDefinition> sacrificeRef = pool.Sacrifices[i];

                if (sacrificeRef.IsValid == false)
                    continue;

                SacrificeDefinition data = f.FindAsset(sacrificeRef);

                if (data == null || data.IsEligible(f, player) == false)
                    continue;

                candidates.Add(new Candidate { Sacrifice = sacrificeRef, Weight = data.Weight });
                totalWeight += data.Weight;
            }

            int drawCount = choiceCount < candidates.Count ? choiceCount : candidates.Count;
            AssetRef<SacrificeDefinition>[] rolled = new AssetRef<SacrificeDefinition>[drawCount];

            for (int slot = 0; slot < drawCount && totalWeight > 0; slot++)
            {
                int roll = f.RNG->Next(0, totalWeight);
                int cursor = 0;
                int pick = candidates.Count - 1;

                for (int i = 0; i < candidates.Count; i++)
                {
                    cursor += candidates[i].Weight;

                    if (roll < cursor)
                    {
                        pick = i;
                        break;
                    }
                }

                rolled[slot] = candidates[pick].Sacrifice;
                totalWeight -= candidates[pick].Weight;
                candidates.RemoveAt(pick);
            }

            return rolled;
        }

        // Called from CursedRiftSystem when a ConfirmCursedRiftCommand lands - the single
        // "SACRIFICE" button click, applying the rolled sacrifice's cost and granting the rolled
        // mutation reward in the same tick, same "one click = one irreversible pick" idiom every
        // other Choose Window screen already uses. Re-validates the sacrifice's eligibility one
        // last time (defensive against it having become invalid between roll and click - nothing
        // currently runs mid-interaction to actually cause that, but the check is cheap and correct
        // either way).
        public static void Confirm(Frame f, EntityRef player, CursedRiftInteraction* interaction)
        {
            AssetRef<SacrificeDefinition> sacrificeRef = interaction->Sacrifice;
            SacrificeDefinition sacrifice = sacrificeRef.IsValid ? f.FindAsset(sacrificeRef) : null;

            if (sacrifice == null || sacrifice.IsEligible(f, player) == false)
            {
                Log.Debug($"[CursedRift] {player}'s rolled sacrifice {sacrificeRef} is no longer eligible - confirm ignored");
                return;
            }

            sacrifice.ApplyCost(f, player);

            LevelUpOption mutation = interaction->Mutation;

            if (mutation.Kind == LevelUpPoolKind.RiftMutation && mutation.Upgrade.IsValid == true)
            {
                var mutationRef = new AssetRef<RiftMutationData>(mutation.Upgrade.Id);
                RiftMutationUtility.Grant(f, player, mutationRef);
                LevelUpUtility.RecordHistory(f, player, LevelUpPoolKind.RiftMutation, mutation.Upgrade);
            }

            EntityRef rift = interaction->Rift;

            if (f.Unsafe.TryGetPointer<CursedRift>(rift, out var cursedRift) == true)
            {
                PoiUsageUtility.MarkUsed(f, player, rift, cursedRift->UsagePolicy);
            }

            ClearCachedOffer(f, player, rift);
            f.Remove<CursedRiftInteraction>(player);

            Log.Debug($"[CursedRift] {player} confirmed the sacrifice - interaction complete");
        }

        // Called from CursedRiftSystem when a CancelCursedRiftCommand lands - always meaningful now
        // (nothing is applied until Confirm above), unlike the old two-stage flow where Cancel
        // became a no-op once past the Sacrifice stage. Deliberately does NOT clear the cached
        // CursedRiftOffers entry - the whole point of the cache is that reopening after a Cancel
        // shows the exact same offer, not a fresh roll.
        public static void Cancel(Frame f, EntityRef player, CursedRiftInteraction* interaction)
        {
            f.Remove<CursedRiftInteraction>(player);
            Log.Debug($"[CursedRift] {player} cancelled their Cursed Rift interaction before paying anything");
        }
    }
}

namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Blacksmith's own interaction - see Blacksmith.qtn/docs/store-blacksmith.md. Mirrors
    // CursedRiftUtility's overall shape (ResolveInteractionState/TryBeginInteraction/command-driven
    // mutators), but Blacksmith is a single committed action, not a multi-stage session - roll 3
    // perk choices (cached for the rest of this Breathing Break, see BlacksmithOffer/
    // EnsureOfferRolled), pick one (spends Coins, adds the perk to the buyer's own equipped
    // weapon), or cancel and come back later this same Break to the exact same 3 options.
    public static unsafe class BlacksmithUtility
    {
        // Read by ContextInteractionSystem's own per-kind dispatch (radius/closest-candidate
        // resolution and Busy already happened there via the sibling Interactable component).
        public static ContextInteractionState ResolveInteractionState(Frame f, EntityRef player, EntityRef forge)
        {
            if (f.Unsafe.TryGetPointer<Blacksmith>(forge, out var blacksmith) == false)
                return ContextInteractionState.None;

            if (PoiAvailabilityUtility.IsAvailable(f, blacksmith->Availability) == false)
                return ContextInteractionState.PhaseUnavailable;

            if (PoiUsageUtility.CanUse(f, player, forge, ResolveUsagePolicy(f, blacksmith)) == false)
                return ContextInteractionState.AlreadyUsed;

            // Checked last, same ordering HealingShrineUtility uses for its own full-Health check -
            // a player who's both already used it AND has nothing eligible should see "already
            // used" (the more permanent reason).
            if (HasEligiblePerks(f, player) == false)
                return ContextInteractionState.NotNeeded;

            return ContextInteractionState.Available;
        }

        // NotNeeded covers both "weapon has 0 free perk slots" and "every remaining perk in the
        // pool is already equipped" - generic reasons a Blacksmith visit would be pointless right
        // now, same NotNeeded value/toast path HealingShrineUtility's own full-Health check
        // established (reused, not new).
        private static bool HasEligiblePerks(Frame f, EntityRef player)
        {
            if (f.GameConfig.BlacksmithConfig.IsValid == false)
                return false;

            BlacksmithConfig config = f.FindAsset(f.GameConfig.BlacksmithConfig);

            if (config.PerkPool.IsValid == false)
                return false;

            if (f.Unsafe.TryGetPointer<Weapon>(player, out var weapon) == false || HasFreeSlot(weapon) == false)
                return false;

            WeaponPerkPoolData pool = f.FindAsset(config.PerkPool);

            for (int i = 0; i < pool.Perks.Count; i++)
            {
                AssetRef<WeaponPerkData> perkRef = pool.Perks[i];

                if (WeaponPerkEligibility.IsEligible(f, perkRef, weapon) == true)
                    return true;
            }

            return false;
        }

        private static bool HasFreeSlot(Weapon* weapon)
        {
            var perks = weapon->Perks;

            for (int i = 0; i < perks.Length; i++)
            {
                if (perks[i].IsValid == false)
                    return true;
            }

            return false;
        }

        // Called from SkillSystem when a locked-in ContextInteraction.ActiveTarget's Base Skill
        // button is pressed. Re-validates in full (never trusts the View/target resolution alone),
        // rolls up to BlacksmithConfig.PerkChoiceCount eligible perks, and opens the pick screen.
        // Mirrors HealingShrineUtility.TryInteract's own shape (switches on the full
        // ContextInteractionState, not a collapsed bool) - SkillSystem's own redirect gate lets
        // BOTH Available and NotNeeded through, so this has to handle NotNeeded explicitly (fire a
        // toast) rather than silently no-op, or a genuinely deliberate press (e.g. weapon fully
        // upgraded) gives the player zero feedback.
        public static void TryBeginInteraction(Frame f, EntityRef player, EntityRef forge)
        {
            if (f.Has<BlacksmithInteraction>(player) == true)
                return;

            ContextInteractionState state = ResolveInteractionState(f, player, forge);

            if (state == ContextInteractionState.NotNeeded)
            {
                f.Events.ContextInteractionRejected(player, forge);
                return;
            }

            if (state != ContextInteractionState.Available)
                return;

            if (f.GameConfig.BlacksmithConfig.IsValid == false)
            {
                Log.Error("[Blacksmith] interaction requested but RuntimeConfig has no BlacksmithConfig assigned - ignored");
                return;
            }

            BlacksmithConfig config = f.FindAsset(f.GameConfig.BlacksmithConfig);
            BlacksmithOffer* offer = EnsureOfferRolled(f, player, config);

            if (offer->PerkChoiceCount == 0)
            {
                Log.Debug($"[Blacksmith] {player} has no eligible perks right now - interaction skipped");
                return;
            }

            f.AddOrGet<BlacksmithInteraction>(player, out var interaction);
            interaction->Forge = forge;

            var choices = interaction->PerkChoices;

            for (int i = 0; i < choices.Length; i++)
            {
                choices[i] = i < offer->PerkChoiceCount ? offer->PerkChoices[i] : default;
            }

            interaction->PerkChoiceCount = offer->PerkChoiceCount;

            Log.Debug($"[Blacksmith] {player} began an interaction with {forge} - {offer->PerkChoiceCount} perk option(s)");
        }

        // Rolls this player's own Blacksmith offer exactly once per Breathing Break - idempotent
        // re-check against RolledAtBreathingIndex, same lazy-restock idiom
        // StoreUtility.EnsureInventoryRolled uses for the shared StoreInventory. Called every time
        // TryBeginInteraction runs (including a re-open after Cancel), so only the FIRST visit this
        // Break actually rolls - every subsequent open/cancel/reopen within the same Break just
        // reads the cached result back out, fixing what used to be a fresh reroll on every visit.
        private static BlacksmithOffer* EnsureOfferRolled(Frame f, EntityRef player, BlacksmithConfig config)
        {
            // Seeded via TryGetPointer first rather than trusting AddOrGet's own zero-init, same
            // reasoning StoreUtility.EnsureInventoryRolled documents - a fresh 0 default would
            // silently equal BreathingIndex 0 and skip the very first roll of a run.
            bool hasOffer = f.Unsafe.TryGetPointer<BlacksmithOffer>(player, out var offer);

            if (hasOffer == false)
            {
                f.AddOrGet<BlacksmithOffer>(player, out offer);
                offer->RolledAtBreathingIndex = -1;
            }

            if (offer->RolledAtBreathingIndex == f.Global->BreathingIndex)
                return offer;

            // Gunsmith L1 rolls once per run; a fresh offer every Break is Gunsmith L2 (meta progression).
            if (offer->RolledAtBreathingIndex >= 0 && HasRestock(f) == false)
                return offer;

            AssetRef<WeaponPerkData>[] rolled = RollPerkOptions(f, player, config);
            var choices = offer->PerkChoices;

            for (int i = 0; i < choices.Length; i++)
            {
                choices[i] = i < rolled.Length ? rolled[i] : default;
            }

            offer->PerkChoiceCount = (byte)rolled.Length;
            offer->RolledAtBreathingIndex = f.Global->BreathingIndex;

            Log.Debug($"[Blacksmith] {player} rolled a new offer for BreathingIndex {f.Global->BreathingIndex} - {rolled.Length} perk option(s)");

            return offer;
        }

        // Weighted draw without replacement among currently-eligible perks only - excludes anything
        // already on this player's own weapon (LevelUpUtility.AlreadyEquipped, promoted internal
        // for this reuse - confirmed with the user: Blacksmith never offers an already-owned perk,
        // no rank-upgrade mechanic), weighted by the CURRENT Breathing Break's own rarity tuning
        // (BlacksmithConfig.ResolveBreakTuning) rather than the pool's own flat Common/Rare/Epic/
        // LegendaryWeight fields - the whole point of Blacksmith is getting rarer as a run
        // progresses.
        private static AssetRef<WeaponPerkData>[] RollPerkOptions(Frame f, EntityRef player, BlacksmithConfig config)
        {
            List<WeightedDrawUtility.Candidate<AssetRef<WeaponPerkData>>> candidates = new List<WeightedDrawUtility.Candidate<AssetRef<WeaponPerkData>>>();

            if (config.PerkPool.IsValid == false || f.Unsafe.TryGetPointer<Weapon>(player, out var weapon) == false)
                return System.Array.Empty<AssetRef<WeaponPerkData>>();

            WeaponPerkPoolData pool = f.FindAsset(config.PerkPool);
            // Gunsmith L3 "Master Craftsman" (meta progression) reads the rarity table ahead, as if later in the run.
            int tuningBreak = f.Global->BreathingIndex;
            TalentsConfig talents = TalentUtility.GetConfig(f);
            if (talents != null && WorldUnlockUtility.IsUnlocked(f, WorldUnlock.Gunsmith, MetaUnlockLevels.GunsmithMasterCraftsman))
                tuningBreak += talents.GunsmithRarityBreakOffset;
            BlacksmithBreakTuning tuning = config.ResolveBreakTuning(tuningBreak);

            // Same exclusions as a level-up draw: already owned, can do nothing on this weapon, or
            // made redundant by a perk it already has would each be a dead purchase. See
            // WeaponPerkEligibility.
            for (int i = 0; i < pool.Perks.Count; i++)
            {
                AssetRef<WeaponPerkData> perkRef = pool.Perks[i];

                if (WeaponPerkEligibility.IsEligible(f, perkRef, weapon) == false)
                    continue;

                WeaponPerkData data = f.FindAsset(perkRef);

                int weight = tuning.GetWeight(data.Rarity);

                if (weight <= 0)
                    continue;

                candidates.Add(new WeightedDrawUtility.Candidate<AssetRef<WeaponPerkData>> { Value = perkRef, Weight = weight });
            }

            return WeightedDrawUtility.Draw(f, candidates, config.PerkChoiceCount);
        }

        // Called from BlacksmithSystem when a SelectBlacksmithPerkCommand lands - re-validates,
        // spends Coins, adds the perk to the buyer's own weapon (WeaponSystem.AddPerk, reused
        // unchanged), marks this Forge used for this player under its own configured usage policy,
        // then completes the interaction.
        public static void SelectPerk(Frame f, EntityRef player, BlacksmithInteraction* interaction, int optionIndex)
        {
            if (optionIndex < 0 || optionIndex >= interaction->PerkChoiceCount)
            {
                Log.Error($"[Blacksmith] {player} sent SelectPerk OptionIndex {optionIndex}, outside 0-{interaction->PerkChoiceCount - 1} - ignored");
                return;
            }

            AssetRef<WeaponPerkData> perkRef = interaction->PerkChoices[optionIndex];

            if (perkRef.IsValid == false)
                return;

            if (f.GameConfig.BlacksmithConfig.IsValid == false)
                return;

            // The offer is cached per Break (see RollOffer), so the weapon it was rolled for may have
            // been swapped since - a Store purchase in the same Break. Re-check against the weapon
            // actually held now, before any Coins move, rather than selling a perk it already has
            // (a doubled additive bake) or one it can't use.
            if (f.Unsafe.TryGetPointer<Weapon>(player, out var heldWeapon) == false
                || WeaponPerkEligibility.IsEligible(f, perkRef, heldWeapon) == false)
            {
                Log.Debug($"[Blacksmith] {player} picked {perkRef}, no longer eligible for the weapon they now hold - ignored");
                return;
            }

            BlacksmithConfig config = f.FindAsset(f.GameConfig.BlacksmithConfig);
            WeaponPerkData perkData = f.FindAsset(perkRef);
            FP price = config.ResolvePerkPrice(perkData.Rarity);

            if (CoinUtility.TrySpend(f, player, price) == false)
            {
                Log.Debug($"[Blacksmith] {player} can't afford perk {perkRef} ({price} Coins)");
                return;
            }

            if (f.Unsafe.TryGetPointer<Weapon>(player, out var weapon) == false || WeaponSystem.AddPerk(f, player, weapon, perkRef) == false)
            {
                // Weapon perk slots filled between roll and pick (or no Weapon at all) - refund
                // rather than silently eat the Coins.
                CoinUtility.Grant(f, player, price);
                Log.Debug($"[Blacksmith] {player} couldn't be granted {perkRef} - refunded");
                return;
            }

            EntityRef forge = interaction->Forge;

            if (f.Unsafe.TryGetPointer<Blacksmith>(forge, out var blacksmith) == true)
            {
                PoiUsageUtility.MarkUsed(f, player, forge, ResolveUsagePolicy(f, blacksmith));
            }

            f.Remove<BlacksmithInteraction>(player);

            Log.Debug($"[Blacksmith] {player} bought perk {perkRef} for {price} Coins - interaction complete");
        }

        // Gunsmith L1 is one purchase per RUN; per-Break purchases (the authored policy) come with Gunsmith L2.
        public static PoiUsagePolicy ResolveUsagePolicy(Frame f, Blacksmith* blacksmith)
        {
            return HasRestock(f) ? blacksmith->UsagePolicy : PoiUsagePolicy.OncePerPlayerPerRun;
        }

        private static bool HasRestock(Frame f)
        {
            return WorldUnlockUtility.IsUnlocked(f, WorldUnlock.Gunsmith, MetaUnlockLevels.GunsmithRestock);
        }

        // Gunsmith L4 (meta progression): true while this player still has this Break's reroll.
        public static bool CanReroll(Frame f, EntityRef player)
        {
            if (WorldUnlockUtility.IsUnlocked(f, WorldUnlock.Gunsmith, MetaUnlockLevels.GunsmithReroll) == false)
                return false;

            return f.Unsafe.TryGetPointer<BlacksmithOffer>(player, out var offer) == false
                   || offer->RerolledAtBreathingIndexPlusOne != f.Global->BreathingIndex + 1;
        }

        // Free reroll (Gunsmith L4) plus the player's shared CharacterStats.RerollQuantity charges.
        public static int GetRerollCharges(Frame f, EntityRef player)
        {
            return (CanReroll(f, player) ? 1 : 0) + LevelUpUtility.GetRerollCharges(f, player);
        }

        // Called from BlacksmithSystem when a RerollBlacksmithCommand lands - redraws the cached offer AND the
        // open window's copy (so a Cancel + reopen keeps the rerolled options). Uses this Break's free
        // Gunsmith L4 reroll first, then one shared CharacterStats.RerollQuantity charge.
        public static void Reroll(Frame f, EntityRef player, BlacksmithInteraction* interaction)
        {
            bool free = CanReroll(f, player);

            if ((free == false && LevelUpUtility.GetRerollCharges(f, player) <= 0) || f.GameConfig.BlacksmithConfig.IsValid == false)
                return;

            if (f.Unsafe.TryGetPointer<BlacksmithOffer>(player, out var offer) == false)
                return;

            AssetRef<WeaponPerkData>[] rolled = RollPerkOptions(f, player, f.FindAsset(f.GameConfig.BlacksmithConfig));
            var offerChoices = offer->PerkChoices;
            var windowChoices = interaction->PerkChoices;

            for (int i = 0; i < offerChoices.Length; i++)
            {
                offerChoices[i] = i < rolled.Length ? rolled[i] : default;
                windowChoices[i] = offerChoices[i];
            }

            offer->PerkChoiceCount = (byte)rolled.Length;
            interaction->PerkChoiceCount = (byte)rolled.Length;
            if (free)
                offer->RerolledAtBreathingIndexPlusOne = f.Global->BreathingIndex + 1;
            else
                LevelUpUtility.SpendRerollCharge(f, player);

            Log.Debug($"[Blacksmith] {player} rerolled the offer - {rolled.Length} perk option(s)");
        }

        // Called from BlacksmithSystem when a CancelBlacksmithCommand lands - free (PoiUsage is
        // NOT marked), same "walk away without committing" idiom Cursed Rift's pre-payment Cancel
        // offers, except Blacksmith has no payment step before the pick itself, so this is always
        // safe to allow. Only closes the window (BlacksmithInteraction) - the underlying roll
        // (BlacksmithOffer) is untouched, so reopening later this same Break shows the identical
        // 3 options rather than rerolling.
        public static void Cancel(Frame f, EntityRef player, BlacksmithInteraction* interaction)
        {
            f.Remove<BlacksmithInteraction>(player);
            Log.Debug($"[Blacksmith] {player} cancelled their interaction");
        }
    }
}

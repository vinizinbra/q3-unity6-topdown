namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Blacksmith's own interaction - see Blacksmith.qtn/docs/store-blacksmith.md. Mirrors
    // CursedRiftUtility's overall shape (ResolveInteractionState/TryBeginInteraction/command-driven
    // mutators), but Blacksmith is a single committed action, not a multi-stage session - roll 3
    // perk choices, pick one (spends Coins, adds the perk to the buyer's own equipped weapon), or
    // cancel for a free re-roll later this same Break.
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

            if (PoiUsageUtility.CanUse(f, player, forge, blacksmith->UsagePolicy) == false)
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
            if (f.RuntimeConfig.BlacksmithConfig.IsValid == false)
                return false;

            BlacksmithConfig config = f.FindAsset(f.RuntimeConfig.BlacksmithConfig);

            if (config.PerkPool.IsValid == false)
                return false;

            if (f.Unsafe.TryGetPointer<Weapon>(player, out var weapon) == false || HasFreeSlot(weapon) == false)
                return false;

            WeaponPerkPoolData pool = f.FindAsset(config.PerkPool);

            for (int i = 0; i < pool.Perks.Count; i++)
            {
                AssetRef<WeaponPerkData> perkRef = pool.Perks[i];

                if (perkRef.IsValid == true && LevelUpUtility.AlreadyEquipped(weapon, perkRef) == false)
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

            if (f.RuntimeConfig.BlacksmithConfig.IsValid == false)
            {
                Log.Error("[Blacksmith] interaction requested but RuntimeConfig has no BlacksmithConfig assigned - ignored");
                return;
            }

            BlacksmithConfig config = f.FindAsset(f.RuntimeConfig.BlacksmithConfig);
            AssetRef<WeaponPerkData>[] rolled = RollPerkOptions(f, player, config);

            if (rolled.Length == 0)
            {
                Log.Debug($"[Blacksmith] {player} has no eligible perks right now - interaction skipped");
                return;
            }

            f.AddOrGet<BlacksmithInteraction>(player, out var interaction);
            interaction->Forge = forge;

            var choices = interaction->PerkChoices;

            for (int i = 0; i < choices.Length; i++)
            {
                choices[i] = i < rolled.Length ? rolled[i] : default;
            }

            interaction->PerkChoiceCount = (byte)rolled.Length;

            Log.Debug($"[Blacksmith] {player} began an interaction with {forge} - {rolled.Length} perk option(s)");
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
            BlacksmithBreakTuning tuning = config.ResolveBreakTuning(f.Global->BreathingIndex);

            // Same exclusion as an already-owned perk: a perk that can do nothing on this weapon's
            // fire type would be a dead purchase. See WeaponPerkData.SupportsFireType.
            WeaponFireType fireType = WeaponGenerator.ResolveFireType(f, weapon->WeaponData);

            for (int i = 0; i < pool.Perks.Count; i++)
            {
                AssetRef<WeaponPerkData> perkRef = pool.Perks[i];

                if (perkRef.IsValid == false || LevelUpUtility.AlreadyEquipped(weapon, perkRef) == true)
                    continue;

                WeaponPerkData data = f.FindAsset(perkRef);

                if (data.SupportsFireType(fireType) == false)
                    continue;

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

            if (f.RuntimeConfig.BlacksmithConfig.IsValid == false)
                return;

            BlacksmithConfig config = f.FindAsset(f.RuntimeConfig.BlacksmithConfig);
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
                PoiUsageUtility.MarkUsed(f, player, forge, blacksmith->UsagePolicy);
            }

            f.Remove<BlacksmithInteraction>(player);

            Log.Debug($"[Blacksmith] {player} bought perk {perkRef} for {price} Coins - interaction complete");
        }

        // Called from BlacksmithSystem when a CancelBlacksmithCommand lands - free (PoiUsage is
        // NOT marked), same "walk away without committing" idiom Cursed Rift's pre-payment Cancel
        // offers, except Blacksmith has no payment step before the pick itself, so this is always
        // safe to allow.
        public static void Cancel(Frame f, EntityRef player, BlacksmithInteraction* interaction)
        {
            f.Remove<BlacksmithInteraction>(player);
            Log.Debug($"[Blacksmith] {player} cancelled their interaction");
        }
    }
}

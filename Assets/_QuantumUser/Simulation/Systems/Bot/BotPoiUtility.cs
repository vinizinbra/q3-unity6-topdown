namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // POI use for bots. Breathing Break: Store, Blacksmith and Cursed Rift at most once per Break
    // (BotBrain.*AttemptedAtBreathingIndex). Healing Shrine: whenever its own availability/usage
    // policy says it's usable - including mid-Survival for a shrine authored AvailableInCombat, so a
    // Cooldown shrine is re-used every time it comes back off cooldown.
    //
    // A bot has no screen to open a ChooseWindow on, so - like LevelUpSystem.AutoPickForBots - it
    // skips the ContextInteraction/Command dance and calls each POI's own utility directly: open,
    // decide, close, all in the same tick, so the bot's client never sees a window flash open.
    // Every utility still re-validates availability, usage and Coins itself.
    public static unsafe class BotPoiUtility
    {
        // Only worth a Shrine visit below this Health fraction - lower mid-Survival, where walking
        // off to it costs fight time and a Cooldown shrine shouldn't be burnt on a scratch.
        private static readonly FP ShrineHealthFraction = FP.FromString("0.8");
        private static readonly FP CombatShrineHealthFraction = FP._0_50;

        // Below this Health fraction the Store's heal food is worth buying.
        private static readonly FP StoreHealHealthFraction = FP.FromString("0.7");

        // Safety cap on accessory points bought in one visit (one durability point per purchase).
        private const int MaxAccessoryPurchasesPerVisit = 8;

        // How far inside the POI's own Interactable.Radius the bot walks before using it.
        private static readonly FP ArrivalRadiusFraction = FP.FromString("0.6");

        // Nearest POI this bot still wants to use this Break, reachable by land.
        public static bool TryPick(Frame f, EntityRef self, BotBrain* brain, RuntimeConfig.BotSettings settings, FP healthFraction,
            HashSet<EntityRef> reachable, FPVector3 position, out EntityRef poi)
        {
            poi = EntityRef.None;
            FP bestSqrDistance = default;

            var interactables = f.Filter<Interactable, Transform3D>();

            while (interactables.Next(out EntityRef entity, out Interactable interactable, out Transform3D transform))
            {
                if (entity == brain->IgnoredTarget)
                    continue;

                if (WantsToUse(f, self, brain, settings, healthFraction, entity, interactable.Kind) == false)
                    continue;

                if (BotNavigation.IsPositionReachable(f, reachable, transform.Position) == false)
                    continue;

                FP sqrDistance = (transform.Position - position).SqrMagnitude;

                if (poi != EntityRef.None && sqrDistance >= bestSqrDistance)
                    continue;

                poi = entity;
                bestSqrDistance = sqrDistance;
            }

            return poi != EntityRef.None;
        }

        // Still worth walking to (re-checked every tick while the goal is held).
        public static bool IsStillWanted(Frame f, EntityRef self, BotBrain* brain, RuntimeConfig.BotSettings settings, FP healthFraction, EntityRef poi)
        {
            return f.Unsafe.TryGetPointer<Interactable>(poi, out var interactable) == true
                && WantsToUse(f, self, brain, settings, healthFraction, poi, interactable->Kind) == true;
        }

        public static FP ResolveArrivalDistance(Frame f, EntityRef poi)
        {
            return f.Unsafe.TryGetPointer<Interactable>(poi, out var interactable) == true
                ? interactable->Radius * ArrivalRadiusFraction
                : FP._1;
        }

        private static bool WantsToUse(Frame f, EntityRef self, BotBrain* brain, RuntimeConfig.BotSettings settings, FP healthFraction, EntityRef poi, InteractableKind kind)
        {
            int breathingIndex = f.Global->BreathingIndex;

            // Mid-Survival only the Shrine (and Team Challenge Ready-up) is worth leaving the fight for.
            if (f.Global->CurrentState == GameState.Survival && kind != InteractableKind.HealingShrine && kind != InteractableKind.TeamChallenge)
                return false;

            switch (kind)
            {
                case InteractableKind.Store:
                    return settings.DisableStore == false
                        && brain->StoreAttemptedAtBreathingIndex != breathingIndex
                        && StoreUtility.ResolveInteractionState(f, self, poi) == ContextInteractionState.Available
                        && WantsAnythingFromStore(f, self, poi, healthFraction) == true;

                case InteractableKind.Blacksmith:
                    return settings.DisableBlacksmith == false
                        && brain->BlacksmithAttemptedAtBreathingIndex != breathingIndex
                        && BlacksmithUtility.ResolveInteractionState(f, self, poi) == ContextInteractionState.Available
                        && CanAffordCheapestPerk(f, self) == true;

                // No per-Break guard: PoiUsageUtility (OncePerPlayerPerBreak/Cooldown/...) already says
                // when it's usable again, and TryInteract can't refuse an Available shrine.
                case InteractableKind.HealingShrine:
                    return settings.DisableHealingShrine == false
                        && healthFraction < (f.Global->CurrentState == GameState.Survival ? CombatShrineHealthFraction : ShrineHealthFraction)
                        && HealingShrineUtility.ResolveInteractionState(f, self, poi) == ContextInteractionState.Available;

                case InteractableKind.CursedRift:
                    return settings.DisableCursedRift == false
                        && brain->CursedRiftAttemptedAtBreathingIndex != breathingIndex
                        && CursedRiftUtility.ResolveInteractionState(f, self, poi) == ContextInteractionState.Available;

                // Only the Ready-up press (Available/WaitingForTeam) - a human can claim the reward.
                // Already-Ready resolves to AlreadyUsed, so the bot stops wanting it once pressed.
                case InteractableKind.TeamChallenge:
                    return settings.DisableTeamChallenge == false
                        && f.Unsafe.TryGetPointer<TeamChallenge>(poi, out var challenge) == true
                        && (challenge->State == TeamChallengeState.Available || challenge->State == TeamChallengeState.WaitingForTeam)
                        && TeamChallengeUtility.ResolveInteractionState(f, self, poi) == ContextInteractionState.Available;

                default:
                    return false;
            }
        }

        // The Team Challenge POI this bot is Ready for, or None. A Ready bot holds near it (see
        // BotInputSystem.TrySelectInteract) - TeamChallengeSystem cancels Ready outside ReadyCancelRadius.
        public static EntityRef GetReadiedTeamChallenge(Frame f, EntityRef self)
        {
            return f.Unsafe.TryGetPointer<TeamChallengeReady>(self, out var ready) == true ? ready->Poi : EntityRef.None;
        }

        // Called once the bot stands inside the POI's radius. Marks the kind attempted for this
        // Break whatever happens, so a refused interaction can't pin the bot to the POI.
        public static void Use(Frame f, EntityRef self, BotBrain* brain, EntityRef poi, FP healthFraction)
        {
            if (f.Unsafe.TryGetPointer<Interactable>(poi, out var interactable) == false)
                return;

            int breathingIndex = f.Global->BreathingIndex;

            switch (interactable->Kind)
            {
                case InteractableKind.Store:
                    brain->StoreAttemptedAtBreathingIndex = breathingIndex;
                    UseStore(f, self, poi, healthFraction);
                    break;

                case InteractableKind.Blacksmith:
                    brain->BlacksmithAttemptedAtBreathingIndex = breathingIndex;
                    UseBlacksmith(f, self, poi);
                    break;

                case InteractableKind.HealingShrine:
                    HealingShrineUtility.TryInteract(f, self, poi);
                    break;

                case InteractableKind.CursedRift:
                    brain->CursedRiftAttemptedAtBreathingIndex = breathingIndex;
                    UseCursedRift(f, self, poi);
                    break;

                case InteractableKind.TeamChallenge:
                    TeamChallengeUtility.TryInteract(f, self, poi);
                    break;
            }
        }

        // Spends in survival order: heal food when hurt, then the accessory (replacement/repair,
        // one point per purchase until full or broke), then a level for the weapon the bot already
        // has. It deliberately doesn't buy a new weapon - a random swap would throw away the hero's
        // level-up weapon picks and perks mid-test.
        private static void UseStore(Frame f, EntityRef self, EntityRef store, FP healthFraction)
        {
            StoreUtility.TryBeginInteraction(f, self, store);

            if (f.Unsafe.TryGetPointer<StoreInteraction>(self, out var interaction) == false)
                return;

            if (healthFraction < StoreHealHealthFraction)
            {
                int healOffer = FindAffordableHealOffer(f, self, store);

                if (healOffer >= 0)
                {
                    StoreUtility.BuyFood(f, self, interaction, healOffer);
                }
            }

            for (int i = 0; i < MaxAccessoryPurchasesPerVisit && WantsAccessoryService(f, self) == true; i++)
            {
                AccessoryServiceUtility.TryPurchaseService(f, self);
            }

            if (CanAffordWeaponLevelUp(f, self) == true)
            {
                StoreUtility.BuyWeaponLevelUp(f, self);
            }

            StoreUtility.Close(f, self);
        }

        // Worth the walk if any of UseStore's purchases could happen. The food inventory is only
        // rolled when the first player opens the Store this Break, so before that a hurt bot with
        // any Coins goes and looks - at worst one wasted visit per Break.
        private static bool WantsAnythingFromStore(Frame f, EntityRef self, EntityRef store, FP healthFraction)
        {
            if (CanAffordWeaponLevelUp(f, self) == true || WantsAccessoryService(f, self) == true)
                return true;

            if (healthFraction >= StoreHealHealthFraction)
                return false;

            if (f.Unsafe.TryGetPointer<StoreInventory>(store, out var inventory) == false
                || inventory->RolledAtBreathingIndex != f.Global->BreathingIndex)
                return GetCoins(f, self) > FP._0;

            return FindAffordableHealOffer(f, self, store) >= 0;
        }

        private static bool WantsAccessoryService(Frame f, EntityRef self)
        {
            return AccessoryServiceUtility.ResolveService(f, self) != AccessoryServiceKind.None
                && AccessoryServiceUtility.CanAfford(f, self) == true;
        }

        // First unpurchased, affordable HealFoodOfferData in this Break's rolled food offers, or -1.
        private static int FindAffordableHealOffer(Frame f, EntityRef self, EntityRef store)
        {
            if (f.Unsafe.TryGetPointer<StoreInventory>(store, out var inventory) == false)
                return -1;

            FP coins = GetCoins(f, self);

            for (int i = 0; i < inventory->FoodOfferCount; i++)
            {
                StoreFoodOffer offer = inventory->FoodOffers[i];

                if (offer.Food.IsValid == false || offer.Price > coins)
                    continue;

                if (f.FindAsset(offer.Food) is HealFoodOfferData == false)
                    continue;

                if (StoreUtility.IsPurchased(f, self, store, i, isWeaponOffer: false) == true)
                    continue;

                return i;
            }

            return -1;
        }

        // Buys the cheapest perk on offer it can afford (rarest first among equal prices isn't worth
        // the complexity - the roll already weights rarity by Break), or walks away for free.
        private static void UseBlacksmith(Frame f, EntityRef self, EntityRef forge)
        {
            BlacksmithUtility.TryBeginInteraction(f, self, forge);

            if (f.Unsafe.TryGetPointer<BlacksmithInteraction>(self, out var interaction) == false)
                return;

            BlacksmithConfig config = f.FindAsset(f.GameConfig.BlacksmithConfig);
            FP coins = GetCoins(f, self);
            int bestIndex = -1;
            FP bestPrice = default;

            for (int i = 0; i < interaction->PerkChoiceCount; i++)
            {
                AssetRef<WeaponPerkData> perkRef = interaction->PerkChoices[i];

                if (perkRef.IsValid == false)
                    continue;

                FP price = config.ResolvePerkPrice(f.FindAsset(perkRef).Rarity);

                if (price > coins || (bestIndex >= 0 && price >= bestPrice))
                    continue;

                bestIndex = i;
                bestPrice = price;
            }

            if (bestIndex >= 0)
            {
                BlacksmithUtility.SelectPerk(f, self, interaction, bestIndex);
            }

            // SelectPerk removes the interaction on success - anything left means nothing was bought.
            if (f.Unsafe.TryGetPointer<BlacksmithInteraction>(self, out var leftover) == true)
            {
                BlacksmithUtility.Cancel(f, self, leftover);
            }
        }

        private static void UseCursedRift(Frame f, EntityRef self, EntityRef rift)
        {
            CursedRiftUtility.TryBeginInteraction(f, self, rift);

            if (f.Unsafe.TryGetPointer<CursedRiftInteraction>(self, out var interaction) == false)
                return;

            CursedRiftUtility.Confirm(f, self, interaction);

            if (f.Unsafe.TryGetPointer<CursedRiftInteraction>(self, out var leftover) == true)
            {
                CursedRiftUtility.Cancel(f, self, leftover);
            }
        }

        private static bool CanAffordWeaponLevelUp(Frame f, EntityRef self)
        {
            if (f.GameConfig.StoreConfig.IsValid == false || f.Has<Weapon>(self) == false)
                return false;

            if (StoreUtility.IsWeaponLevelUpPurchased(f, self) == true)
                return false;

            StoreConfig config = f.FindAsset(f.GameConfig.StoreConfig);
            return GetCoins(f, self) >= StoreUtility.ResolveWeaponLevelUpPrice(f, self, config);
        }

        private static bool CanAffordCheapestPerk(Frame f, EntityRef self)
        {
            if (f.GameConfig.BlacksmithConfig.IsValid == false)
                return false;

            BlacksmithConfig config = f.FindAsset(f.GameConfig.BlacksmithConfig);
            return GetCoins(f, self) >= config.CommonPerkPrice;
        }

        private static FP GetCoins(Frame f, EntityRef self)
        {
            return f.Unsafe.TryGetPointer<CharacterStats>(self, out var stats) == true ? stats->Coins : FP._0;
        }
    }
}

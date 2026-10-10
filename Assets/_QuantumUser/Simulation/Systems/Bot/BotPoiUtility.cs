namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Breathing-Break POI use for bots: Store, Blacksmith, Healing Shrine and Cursed Rift, each at
    // most once per Break (BotBrain.*AttemptedAtBreathingIndex).
    //
    // A bot has no screen to open a ChooseWindow on, so - like LevelUpSystem.AutoPickForBots - it
    // skips the ContextInteraction/Command dance and calls each POI's own utility directly: open,
    // decide, close, all in the same tick, so the bot's client never sees a window flash open.
    // Every utility still re-validates availability, usage and Coins itself.
    public static unsafe class BotPoiUtility
    {
        // Only worth a Shrine visit below this Health fraction.
        private static readonly FP ShrineHealthFraction = FP.FromString("0.8");

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

            switch (kind)
            {
                case InteractableKind.Store:
                    return settings.DisableStore == false
                        && brain->StoreAttemptedAtBreathingIndex != breathingIndex
                        && StoreUtility.ResolveInteractionState(f, self, poi) == ContextInteractionState.Available
                        && CanAffordWeaponLevelUp(f, self) == true;

                case InteractableKind.Blacksmith:
                    return settings.DisableBlacksmith == false
                        && brain->BlacksmithAttemptedAtBreathingIndex != breathingIndex
                        && BlacksmithUtility.ResolveInteractionState(f, self, poi) == ContextInteractionState.Available
                        && CanAffordCheapestPerk(f, self) == true;

                case InteractableKind.HealingShrine:
                    return settings.DisableHealingShrine == false
                        && brain->ShrineAttemptedAtBreathingIndex != breathingIndex
                        && healthFraction < ShrineHealthFraction
                        && HealingShrineUtility.ResolveInteractionState(f, self, poi) == ContextInteractionState.Available;

                case InteractableKind.CursedRift:
                    return settings.DisableCursedRift == false
                        && brain->CursedRiftAttemptedAtBreathingIndex != breathingIndex
                        && CursedRiftUtility.ResolveInteractionState(f, self, poi) == ContextInteractionState.Available;

                default:
                    return false;
            }
        }

        // Called once the bot stands inside the POI's radius. Marks the kind attempted for this
        // Break whatever happens, so a refused interaction can't pin the bot to the POI.
        public static void Use(Frame f, EntityRef self, BotBrain* brain, EntityRef poi)
        {
            if (f.Unsafe.TryGetPointer<Interactable>(poi, out var interactable) == false)
                return;

            int breathingIndex = f.Global->BreathingIndex;

            switch (interactable->Kind)
            {
                case InteractableKind.Store:
                    brain->StoreAttemptedAtBreathingIndex = breathingIndex;
                    UseStore(f, self, poi);
                    break;

                case InteractableKind.Blacksmith:
                    brain->BlacksmithAttemptedAtBreathingIndex = breathingIndex;
                    UseBlacksmith(f, self, poi);
                    break;

                case InteractableKind.HealingShrine:
                    brain->ShrineAttemptedAtBreathingIndex = breathingIndex;
                    HealingShrineUtility.TryInteract(f, self, poi);
                    break;

                case InteractableKind.CursedRift:
                    brain->CursedRiftAttemptedAtBreathingIndex = breathingIndex;
                    UseCursedRift(f, self, poi);
                    break;
            }
        }

        // Levels up the weapon the bot already has. It deliberately doesn't buy a new weapon - a
        // random swap would throw away the hero's level-up weapon picks and perks mid-test.
        private static void UseStore(Frame f, EntityRef self, EntityRef store)
        {
            StoreUtility.TryBeginInteraction(f, self, store);

            if (f.Has<StoreInteraction>(self) == false)
                return;

            if (CanAffordWeaponLevelUp(f, self) == true)
            {
                StoreUtility.BuyWeaponLevelUp(f, self);
            }

            StoreUtility.Close(f, self);
        }

        // Buys the cheapest perk on offer it can afford (rarest first among equal prices isn't worth
        // the complexity - the roll already weights rarity by Break), or walks away for free.
        private static void UseBlacksmith(Frame f, EntityRef self, EntityRef forge)
        {
            BlacksmithUtility.TryBeginInteraction(f, self, forge);

            if (f.Unsafe.TryGetPointer<BlacksmithInteraction>(self, out var interaction) == false)
                return;

            BlacksmithConfig config = f.FindAsset(f.RuntimeConfig.BlacksmithConfig);
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
            if (f.RuntimeConfig.StoreConfig.IsValid == false || f.Has<Weapon>(self) == false)
                return false;

            if (StoreUtility.IsWeaponLevelUpPurchased(f, self) == true)
                return false;

            StoreConfig config = f.FindAsset(f.RuntimeConfig.StoreConfig);
            return GetCoins(f, self) >= StoreUtility.ResolveWeaponLevelUpPrice(f, self, config);
        }

        private static bool CanAffordCheapestPerk(Frame f, EntityRef self)
        {
            if (f.RuntimeConfig.BlacksmithConfig.IsValid == false)
                return false;

            BlacksmithConfig config = f.FindAsset(f.RuntimeConfig.BlacksmithConfig);
            return GetCoins(f, self) >= config.CommonPerkPrice;
        }

        private static FP GetCoins(Frame f, EntityRef self)
        {
            return f.Unsafe.TryGetPointer<CharacterStats>(self, out var stats) == true ? stats->Coins : FP._0;
        }
    }
}

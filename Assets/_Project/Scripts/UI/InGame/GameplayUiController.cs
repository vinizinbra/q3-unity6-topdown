using System;
using Photon.Deterministic;
using PrimeTween;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

public class GameplayUiController : QuantumGlobalMonoBehaviour
{
    // Which flow currently owns a given slot's choiceWindows[] instance - generalizes the old
    // binary "Global.LevelUpScreenOpen ? LevelUp : CursedRift" check into real per-slot resolution
    // now that Store/Blacksmith are a 3rd/4th flow sharing the same window (see ResolveOwner). Safe
    // as a plain sequential presence check (not real arbitration) because
    // PoiInteractionLockUtility's own Busy check already guarantees a player can never hold more
    // than one of {CursedRiftInteraction, StoreInteraction, BlacksmithInteraction} at once - opening
    // a 2nd is blocked at the source. See docs/store-blacksmith.md.
    private enum ChoiceWindowOwner { None, LevelUp, CursedRift, Store, Blacksmith }

    [SerializeField] private WindowManager windowManager;
    [SerializeField] private TMP_Text lives;
    [SerializeField] private TMP_Text rtt;

    [SerializeField, FormerlySerializedAs("upgradeWindows"),
     Tooltip("One per local player slot - choiceWindows[0] for slot 0, choiceWindows[1] for slot 1, etc. Unused slots (no 2nd local player) can be left null. " +
        "Serves FIVE flows on the exact same instance: a real Level-Up/Weapon-Upgrade/Chest (driven by UpdateUpgradeScreen, routed through WindowManager + a " +
        "Time.timeScale ramp - a whole-party pause), and Cursed Rift/Store/Blacksmith's own screens (driven by UpdatePoiWindow, shown/hidden directly per " +
        "slot, no WindowManager/timescale involvement at all). See both methods' own comments for how they share one instance without permanently fighting " +
        "each other - a real Level-Up for a DIFFERENT player can visually pre-empt this player's own in-progress POI screen (an accepted tradeoff, not a " +
        "bug - their own interaction component is untouched, so it picks back up the moment the other player's Level-Up closes).")]
    private ChooseWindow[] choiceWindows;

    [Header("Upgrade screen time-scale ease")]
    [SerializeField, Tooltip("How long Time.timeScale takes to ease down to upgradeTimeScale before the upgrade screen actually shows. Purely a client-side polish effect - the simulation itself is already paused deterministically via GameplaySystemGroup regardless of Time.timeScale (Quantum's own tick doesn't read it), see docs/level-up-upgrades.md.")]
    private float upgradeTimeScaleRampInDuration = 0.4f;
    [SerializeField, Tooltip("How long Time.timeScale takes to ease back up to 1 once the screen closes - kept separate from the ramp-in duration above and defaulted much shorter, since a slow build-up into the screen reads as intentional but the same slowness going back into gameplay after picking just reads as lag.")]
    private float upgradeTimeScaleRampOutDuration = 0.15f;
    [SerializeField, Tooltip("Target Time.timeScale while the upgrade screen is open - 0 fully freezes Update-driven animation/particles/etc.")]
    private float upgradeTimeScale = 0f;

    public bool isDead = false;
    private int _placement;
    private Action<int> _onLeave;
    private bool _upgradeScreenWasOpen;

    // Set once this client closes the screen itself (solo pick - see CloseUpgradeScreenIfSolo), so
    // the natural Frame.Global.LevelUpScreenOpen edge-detection above doesn't call CloseUpgradeScreen
    // a second time once the simulation catches up and actually clears the flag a tick or two later.
    private bool _upgradeScreenClosedEarly;
    private Action<int>[] _cardClickedHandlers;
    private Action<int>[] _weaponCardClickedHandlers;
    private Action[] _rerollClickedHandlers;
    private Action[] _secondaryButtonClickedHandlers;
    private Tween _timeScaleTween;

    // Cached per slot every QUpdate tick (see UpdateWindowOwners) - every click handler needs to
    // know which flow currently owns this slot's window without re-deriving it from a fresh Quantum
    // read inside the click handler itself.
    private ChoiceWindowOwner[] _windowOwner;

    // Cached per slot every UpdatePoiWindow tick - OnCardClicked needs to know whether a card click
    // (while ChoiceWindowOwner.CursedRift) means "pick a sacrifice" or "pick a mutation" (both
    // stages reuse the same 3-card grid/onCardClicked event), without re-deriving it from a fresh
    // Quantum read inside the click handler itself.
    private CursedRiftInteractionState[] _poiWindowStage;

    private void Start()
    {
        windowManager.ShowWindow<LoadingWindow>();

        _cardClickedHandlers = new Action<int>[choiceWindows.Length];
        _weaponCardClickedHandlers = new Action<int>[choiceWindows.Length];
        _rerollClickedHandlers = new Action[choiceWindows.Length];
        _secondaryButtonClickedHandlers = new Action[choiceWindows.Length];
        _windowOwner = new ChoiceWindowOwner[choiceWindows.Length];
        _poiWindowStage = new CursedRiftInteractionState[choiceWindows.Length];

        for (int i = 0; i < choiceWindows.Length; i++)
        {
            if (choiceWindows[i] == null)
                continue;

            int slotIndex = i;
            _cardClickedHandlers[i] = optionIndex => OnCardClicked(slotIndex, optionIndex);
            choiceWindows[i].onCardClicked += _cardClickedHandlers[i];

            _weaponCardClickedHandlers[i] = optionIndex => OnWeaponCardClicked(slotIndex, optionIndex);
            choiceWindows[i].onWeaponCardClicked += _weaponCardClickedHandlers[i];

            _rerollClickedHandlers[i] = () => OnRerollClicked(slotIndex);
            choiceWindows[i].onRerollClicked += _rerollClickedHandlers[i];

            _secondaryButtonClickedHandlers[i] = () => OnSecondaryButtonClicked(slotIndex);
            choiceWindows[i].onSecondaryButtonClicked += _secondaryButtonClickedHandlers[i];
        }
    }

    private void OnDestroy()
    {
        // Force-restore regardless of where the ease was mid-flight - Time.timeScale is a global
        // engine setting that outlives this object (e.g. leaving the match while the upgrade screen
        // is still open/closing), so leaving it stuck below 1 would freeze whatever loads next.
        _timeScaleTween.Stop();
        Time.timeScale = 1f;

        if (_cardClickedHandlers == null)
            return;

        for (int i = 0; i < choiceWindows.Length; i++)
        {
            if (choiceWindows[i] == null)
                continue;

            if (_cardClickedHandlers[i] != null)
                choiceWindows[i].onCardClicked -= _cardClickedHandlers[i];

            if (_weaponCardClickedHandlers[i] != null)
                choiceWindows[i].onWeaponCardClicked -= _weaponCardClickedHandlers[i];

            if (_rerollClickedHandlers[i] != null)
                choiceWindows[i].onRerollClicked -= _rerollClickedHandlers[i];

            if (_secondaryButtonClickedHandlers[i] != null)
                choiceWindows[i].onSecondaryButtonClicked -= _secondaryButtonClickedHandlers[i];
        }
    }

    void LoadWaitingWindow(EntityRef entityRef)
    {
        windowManager.ShowWindow<WaitingWindow>();
    }

    public void Leave()
    {
        // Same reasoning as InMatchWindow.OnLeaveClicked - quitting a run on purpose still leaves
        // it rejoinable until PlayerTtl expires, so the reconnect information is left alone here.
        MatchMakingConfig.Instance.Client.Disconnect();
        _onLeave?.Invoke(_placement);

    }

    public override void QStart(QuantumGame game)
    {

    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        UpdateWindowOwners(game);
        UpdateUpgradeScreen(game);
        UpdatePoiWindow(game);
    }

    // Resolves _windowOwner[] for every slot ONCE per tick, before either UpdateUpgradeScreen or
    // UpdatePoiWindow runs - both a real Level-Up and a POI interaction need it (click handlers can
    // fire for either), and UpdateUpgradeScreen's own early-return (nothing to refresh while no
    // screen is open) would otherwise leave a stale owner behind for whichever flow it skips this
    // tick. See ResolveOwner's own comment on why a plain sequential presence check is safe here.
    private unsafe void UpdateWindowOwners(QuantumGame game)
    {
        if (choiceWindows.Length == 0 || MyLocalPlayer.Instance == null)
            return;

        Frame frame = game.Frames.Predicted;
        var slots = MyLocalPlayer.Instance.Slots;

        for (int i = 0; i < choiceWindows.Length; i++)
        {
            bool slotValid = i < slots.Count && slots[i].IsSet == true;
            _windowOwner[i] = slotValid ? ResolveOwner(frame, slots[i].EntityRef) : ChoiceWindowOwner.None;
        }
    }

    private static unsafe ChoiceWindowOwner ResolveOwner(Frame frame, EntityRef entity)
    {
        if (frame.Global->LevelUpScreenOpen == true) return ChoiceWindowOwner.LevelUp;
        if (frame.Has<CursedRiftInteraction>(entity) == true) return ChoiceWindowOwner.CursedRift;
        if (frame.Has<StoreInteraction>(entity) == true) return ChoiceWindowOwner.Store;
        if (frame.Has<BlacksmithInteraction>(entity) == true) return ChoiceWindowOwner.Blacksmith;
        return ChoiceWindowOwner.None;
    }

    public override void QLateUpdate(QuantumGame game)
    {

    }

    // Polls Frame.Global.LevelUpScreenOpen and diffs against the last-seen value - same idiom
    // ExpBarUiWidget already uses for reading Global state every QUpdate, safe here because the
    // View (unlike the sim) is never rolled back. See docs/level-up-upgrades.md.
    private unsafe void UpdateUpgradeScreen(QuantumGame game)
    {
        if (choiceWindows.Length == 0 || MyLocalPlayer.Instance == null)
            return;

        Frame frame = game.Frames.Predicted;
        bool isOpen = frame.Global->LevelUpScreenOpen;

        if (isOpen == true && _upgradeScreenWasOpen == false)
        {
            _upgradeScreenClosedEarly = false;

            // Don't show the screen right away - ease Time.timeScale down first, then reveal it
            // once the ease finishes, so the world visibly slows to a stop before the cards appear.
            _timeScaleTween.Stop();
            _timeScaleTween = Tween.Custom(Time.timeScale, upgradeTimeScale, upgradeTimeScaleRampInDuration,
                onValueChange: v => Time.timeScale = (float)v, useUnscaledTime: true)
                .OnComplete(() => windowManager.ShowWindow<ChooseWindow>());
        }
        else if (isOpen == false && _upgradeScreenWasOpen == true && _upgradeScreenClosedEarly == false)
        {
            CloseUpgradeScreen();
        }

        _upgradeScreenWasOpen = isOpen;

        if (isOpen == false)
            return;

        var slots = MyLocalPlayer.Instance.Slots;

        for (int i = 0; i < choiceWindows.Length; i++)
        {
            if (choiceWindows[i] == null)
                continue;

            // The window only actually SetActive(true)s once the Time.timeScale ramp above finishes
            // (windowManager.ShowWindow<ChooseWindow>() in the OnComplete callback) - until then its
            // GameObject is still inactive, so Awake() hasn't run and cards/weaponCards are still
            // null. Skip refreshing until it's really shown, or SetCardFamilyActive NREs on those
            // null arrays for the ~upgradeTimeScaleRampDuration window between isOpen flipping true
            // and the reveal actually happening.
            if (choiceWindows[i].gameObject.activeInHierarchy == false)
                continue;

            // Every local slot's own LevelUpChoice is independent (see docs/level-up-upgrades.md),
            // so each local player refreshes their own window from their own entity.
            if (i >= slots.Count || slots[i].IsSet == false)
                continue;

            if (frame.Unsafe.TryGetPointer<LevelUpChoice>(slots[i].EntityRef, out var choice) == false)
            {
                // This slot rolled nothing this screen (every pool empty) - stay blank.
                continue;
            }

            int? confirmedIndex = choice->Confirmed ? (int?)choice->SelectedIndex : null;
            string title = BuildTitle(choice);

            int rerollCharges = frame.Unsafe.TryGetPointer<CharacterStats>(slots[i].EntityRef, out var stats) == true
                ? stats->RerollQuantity
                : 0;
            choiceWindows[i].UpdateRerollButton(rerollCharges, choice->Confirmed);

            // A LevelUpChoice is always homogeneous - either every rolled option is ChooseWeapon, or
            // none are (see LevelUpUtility.RollOptionsFor/RollChooseWeaponOptionsFor never mixing
            // ChooseWeapon into the ordinary weighted-candidate roll) - so the screen picks one card
            // family per player slot, not per card.
            bool isWeaponChoice = choice->OptionCount > 0 && choice->Options[0].Kind == LevelUpPoolKind.ChooseWeapon;

            if (isWeaponChoice)
            {
                var weaponCardData = new WeaponCardWidget.CardData[choice->Options.Length];

                for (int j = 0; j < choice->Options.Length; j++)
                {
                    weaponCardData[j] = j < choice->OptionCount
                        ? BuildWeaponCardData(frame, choice->Options[j].WeaponData, choice->Options[j].RolledPerks, choice->Options[j].RolledPerkCount)
                        : default;
                }

                choiceWindows[i].RefreshWeaponChoice(title, frame.Global->LevelUpTimeRemaining.AsFloat, weaponCardData, confirmedIndex);
            }
            else
            {
                var cardData = new UpgradeCardWidget.CardData[choice->Options.Length];

                for (int j = 0; j < choice->Options.Length; j++)
                {
                    cardData[j] = j < choice->OptionCount ? BuildCardData(frame, slots[i].EntityRef, choice->Options[j]) : default;
                }

                choiceWindows[i].Refresh(title, frame.Global->LevelUpTimeRemaining.AsFloat, cardData, confirmedIndex);
            }
        }
    }

    // Generalized POI Choice Window driver (was UpdateCursedRiftWindow - now also drives Store/
    // Blacksmith, see docs/store-blacksmith.md) - independent of UpdateUpgradeScreen above (runs
    // right after it every QUpdate), since none of these three flows ramp Time.timeScale or go
    // through WindowManager.ShowWindow<T>() itself (only affects THIS interacting player's own
    // slot, not every local slot together). Steps aside entirely whenever a real Level-Up is
    // currently open (Global.LevelUpScreenOpen is a single SHARED flag, not per-player) -
    // UpdateUpgradeScreen's own WindowManager sweep already owns every choiceWindows[] instance for
    // that duration, so fighting it here would just cause visual flicker. See
    // docs/choice-window-refactor.md.
    private unsafe void UpdatePoiWindow(QuantumGame game)
    {
        if (choiceWindows.Length == 0 || MyLocalPlayer.Instance == null)
            return;

        Frame frame = game.Frames.Predicted;

        if (frame.Global->LevelUpScreenOpen == true)
            return;

        var slots = MyLocalPlayer.Instance.Slots;

        for (int i = 0; i < choiceWindows.Length; i++)
        {
            if (choiceWindows[i] == null)
                continue;

            bool slotValid = i < slots.Count && slots[i].IsSet == true;
            EntityRef entity = slotValid ? slots[i].EntityRef : EntityRef.None;
            ChoiceWindowOwner owner = _windowOwner[i];

            if (slotValid == false || owner == ChoiceWindowOwner.None)
            {
                // Only hide a window that's actually still showing - a real Level-Up closing just
                // hid every choiceWindows[] instance itself (see CloseUpgradeScreen), so there's
                // nothing left to do here for a slot with no open POI interaction of its own.
                if (choiceWindows[i].gameObject.activeSelf == true)
                    choiceWindows[i].Hide();

                continue;
            }

            // Checked against the window's own LIVE state (not a separately-tracked bool) so this
            // self-heals regardless of what else touched it - a real Level-Up for a DIFFERENT
            // player hides every choiceWindows[] instance via WindowManager's own sweep (see
            // ChooseWindow's class comment), and this correctly re-shows (replaying the intro -
            // an accepted minor visual hiccup) THIS slot's own screen the moment that stops being
            // true, since the interaction itself was never touched.
            if (choiceWindows[i].gameObject.activeSelf == false)
                choiceWindows[i].Show();

            switch (owner)
            {
                case ChoiceWindowOwner.CursedRift:
                    if (frame.Unsafe.TryGetPointer<CursedRiftInteraction>(entity, out var cursedRift) == true)
                    {
                        _poiWindowStage[i] = cursedRift->State;
                        RefreshCursedRiftWindow(frame, entity, cursedRift, choiceWindows[i]);
                    }
                    break;

                case ChoiceWindowOwner.Store:
                    if (frame.Unsafe.TryGetPointer<StoreInteraction>(entity, out var store) == true)
                        RefreshStoreWindow(frame, entity, store, choiceWindows[i]);
                    break;

                case ChoiceWindowOwner.Blacksmith:
                    if (frame.Unsafe.TryGetPointer<BlacksmithInteraction>(entity, out var blacksmith) == true)
                        RefreshBlacksmithWindow(frame, entity, blacksmith, choiceWindows[i]);
                    break;
            }
        }
    }

    // Store's own screen - food/utility offers listed first, weapon offers second (per the user's
    // own layout decision), both live on the SAME ChooseWindow.RefreshStore call. Subtitle shows
    // this player's own live Coin total, same "read live, never cached" idiom every other purchase
    // affordance in this method uses.
    private static unsafe void RefreshStoreWindow(Frame frame, EntityRef entity, StoreInteraction* interaction, ChooseWindow window)
    {
        EntityRef store = interaction->Store;

        if (frame.RuntimeConfig.StoreConfig.IsValid == false || frame.Unsafe.TryGetPointer<StoreInventory>(store, out var inventory) == false)
            return;

        StoreConfig config = frame.FindAsset(frame.RuntimeConfig.StoreConfig);
        int weaponOfferCount = StoreUtility.ResolveWeaponOfferCount(frame, entity, store, config);

        // +1: the guaranteed "Increase Weapon Level" offer (see BuildWeaponLevelUpCardData) is
        // appended right after the rolled FoodOffers slots - StoreWeaponLevelUpCardIndex documents
        // why that index can never collide with a real, rolled food offer.
        var foodData = new UpgradeCardWidget.CardData[inventory->FoodOffers.Length + 1];

        for (int j = 0; j < inventory->FoodOffers.Length; j++)
        {
            foodData[j] = j < inventory->FoodOfferCount
                ? BuildFoodOfferCardData(frame, entity, store, j, inventory->FoodOffers[j])
                : default;
        }

        foodData[StoreWeaponLevelUpCardIndex] = BuildWeaponLevelUpCardData(frame, entity, config);

        var weaponData = new WeaponCardWidget.CardData[inventory->WeaponOffers.Length];

        for (int j = 0; j < inventory->WeaponOffers.Length; j++)
        {
            weaponData[j] = j < weaponOfferCount && j < inventory->WeaponOfferCount
                ? BuildStoreWeaponCardData(frame, entity, store, j, inventory->WeaponOffers[j], config)
                : default;
        }

        string subtitle = frame.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true
            ? $"YOUR COINS: {stats->Coins.AsFloat:0}"
            : null;

        window.RefreshStore("STORE", foodData, weaponData, subtitle);
    }

    // Blacksmith's own screen - a single homogeneous UpgradeCardWidget family, structurally
    // identical to Cursed Rift's Mutation stage, so it reuses the existing plain Refresh() call
    // (now with Purchase populated per-card, since perks cost Coins - see BuildPerkOfferCardData).
    private static unsafe void RefreshBlacksmithWindow(Frame frame, EntityRef entity, BlacksmithInteraction* interaction, ChooseWindow window)
    {
        if (frame.RuntimeConfig.BlacksmithConfig.IsValid == false)
            return;

        BlacksmithConfig config = frame.FindAsset(frame.RuntimeConfig.BlacksmithConfig);
        var cardData = new UpgradeCardWidget.CardData[interaction->PerkChoices.Length];

        for (int j = 0; j < interaction->PerkChoices.Length; j++)
        {
            cardData[j] = j < interaction->PerkChoiceCount
                ? BuildPerkOfferCardData(frame, entity, interaction->PerkChoices[j], config)
                : default;
        }

        window.Refresh("BLACKSMITH", 0f, cardData, null, subtitle: "CHOOSE A PERK TO ADD", allowCancel: true, allowReroll: false);
    }

    // Store food/utility card - mirrors BuildSacrificeCardData's own shape (reads the offer asset's
    // own fields + a live price/afford computation, never cached) but with a real purchase
    // affordance instead of a cost preview - a food offer is a REWARD bought with Coins, not a
    // sacrifice.
    private static unsafe UpgradeCardWidget.CardData BuildFoodOfferCardData(Frame frame, EntityRef entity, EntityRef store, int offerIndex, StoreFoodOffer offer)
    {
        if (offer.Food.IsValid == false)
            return default;

        FoodOfferData data = frame.FindAsset(offer.Food);
        bool purchased = StoreUtility.IsPurchased(frame, entity, store, offerIndex, isWeaponOffer: false);
        FP coins = frame.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true ? stats->Coins : FP._0;

        return new UpgradeCardWidget.CardData
        {
            HasOption = true,
            Icon = data.Icon,
            DisplayName = data.DisplayName,
            Description = data.Description,
            KindText = "FOOD & UTILITY",
            TopLabelOverride = string.IsNullOrEmpty(data.TopLabel) ? "FOOD" : data.TopLabel,
            ButtonLabel = string.IsNullOrEmpty(data.ButtonLabel) ? "BUY" : data.ButtonLabel,
            Purchase = new PurchasableCardState
            {
                ShowPurchaseUi = true,
                Price = offer.Price.AsFloat,
                Currency = CurrencyType.Coin,
                CanAfford = coins >= offer.Price,
                IsSoldOut = purchased
            }
        };
    }

    // Card index the guaranteed "Increase Weapon Level" offer is appended at within Store's own
    // foodData[] (see RefreshStoreWindow) - matches Store.qtn's StoreInventory.FoodOffers[2] ceiling
    // exactly, one past its last real slot. A real rolled food offer can never reach this index
    // (FoodOfferCount is always clamped <= FoodOffers.Length, see StoreUtility.RollFoodOffers), so
    // there's no collision risk even when 0 food offers roll. OnCardClicked uses this same constant
    // to route a click here to BuyStoreWeaponLevelCommand instead of BuyStoreFoodCommand.
    private const int StoreWeaponLevelUpCardIndex = 2;

    // Store's guaranteed "Increase Weapon Level" card - unlike every other Store card, this isn't
    // read off a rolled StoreInventory offer at all (nothing rolled about it); price/current level
    // are both resolved live off the buyer's own equipped Weapon (see
    // StoreUtility.ResolveWeaponLevelUpPrice), same "read live, never baked" idiom every other
    // Store price already follows.
    private static unsafe UpgradeCardWidget.CardData BuildWeaponLevelUpCardData(Frame frame, EntityRef entity, StoreConfig config)
    {
        FP price = StoreUtility.ResolveWeaponLevelUpPrice(frame, entity, config);
        bool purchased = StoreUtility.IsWeaponLevelUpPurchased(frame, entity);
        FP coins = frame.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true ? stats->Coins : FP._0;
        byte level = frame.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == true ? weapon->Level : (byte)0;

        return new UpgradeCardWidget.CardData
        {
            HasOption = true,
            DisplayName = "Weapon Level Up",
            Description = $"+{(config.WeaponLevelUpDamageBonusPerLevel * 100).AsFloat:0}% weapon damage. Currently Level {level}.",
            KindText = "FOOD & UTILITY",
            TopLabelOverride = "UPGRADE",
            ButtonLabel = "BUY",
            Purchase = new PurchasableCardState
            {
                ShowPurchaseUi = true,
                Price = price.AsFloat,
                Currency = CurrencyType.Coin,
                CanAfford = coins >= price,
                IsSoldOut = purchased
            }
        };
    }

    // Store weapon offer card - reuses BuildWeaponCardData (refactored to take raw fields, see its
    // own comment) unchanged, then layers the purchase affordance on top.
    private static unsafe WeaponCardWidget.CardData BuildStoreWeaponCardData(Frame frame, EntityRef entity, EntityRef store, int offerIndex, StoreWeaponOffer offer, StoreConfig config)
    {
        WeaponCardWidget.CardData data = BuildWeaponCardData(frame, offer.WeaponData, offer.RolledPerks, offer.RolledPerkCount, offer.WeaponLevel);

        // Preview the SAME level-adjusted damage the offer will actually equip with (WeaponSystem.
        // ResolveLevelDamageMultiplier - the same compounding step WeaponSystem.AddLevel itself
        // applies, extracted so this can be shown before purchase without mutating a real Weapon) -
        // otherwise the card would show a plain-Level-0 base Damage even though the weapon's own
        // "+N" title (see BuildWeaponCardData) already advertises a higher level.
        if (offer.WeaponLevel > 0)
        {
            FP multiplier = WeaponSystem.ResolveLevelDamageMultiplier(offer.WeaponLevel, config.WeaponLevelUpDamageBonusPerLevel);
            data.Damage *= multiplier.AsFloat;
        }

        bool purchased = StoreUtility.IsPurchased(frame, entity, store, offerIndex, isWeaponOffer: true);
        FP coins = frame.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true ? stats->Coins : FP._0;

        data.Purchase = new PurchasableCardState
        {
            ShowPurchaseUi = true,
            Price = offer.Price.AsFloat,
            Currency = CurrencyType.Coin,
            CanAfford = coins >= offer.Price,
            IsSoldOut = purchased
        };

        return data;
    }

    // Blacksmith perk offer card - reads WeaponPerkData's own Icon/DisplayName/GetDescription()/
    // Rarity directly (same fields BuildCardData would resolve generically for a rolled
    // LevelUpOption), plus the purchase affordance. Price is resolved from THIS perk's own
    // Rarity (BlacksmithConfig.ResolvePerkPrice) - a Legendary perk costs more than a Common one,
    // not one flat price for every offer. IsSoldOut is always false here - Blacksmith allows
    // exactly one successful pick per player per Break (PoiUsagePolicy.OncePerPlayerPerBreak), and
    // the whole interaction/window closes the instant that happens, so a still-rendered card is
    // never "sold out," only gone.
    private static unsafe UpgradeCardWidget.CardData BuildPerkOfferCardData(Frame frame, EntityRef entity, AssetRef<WeaponPerkData> perkRef, BlacksmithConfig config)
    {
        if (perkRef.IsValid == false)
            return default;

        WeaponPerkData data = frame.FindAsset(perkRef);
        FP price = config.ResolvePerkPrice(data.Rarity);
        FP coins = frame.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true ? stats->Coins : FP._0;

        return new UpgradeCardWidget.CardData
        {
            HasOption = true,
            Icon = data.Icon,
            DisplayName = data.DisplayName,
            Description = data.GetDescription(),
            RarityIndex = (int)data.Rarity,
            KindText = "Weapon Perk",
            Purchase = new PurchasableCardState
            {
                ShowPurchaseUi = true,
                Price = price.AsFloat,
                Currency = CurrencyType.Coin,
                CanAfford = coins >= price,
                IsSoldOut = false
            }
        };
    }

    private static unsafe void RefreshCursedRiftWindow(Frame frame, EntityRef entity, CursedRiftInteraction* interaction, ChooseWindow window)
    {
        switch (interaction->State)
        {
            case CursedRiftInteractionState.SelectingSacrifice:
            {
                var cardData = new UpgradeCardWidget.CardData[interaction->SacrificeChoices.Length];

                for (int j = 0; j < interaction->SacrificeChoices.Length; j++)
                {
                    cardData[j] = j < interaction->SacrificeChoiceCount
                        ? BuildSacrificeCardData(frame, entity, interaction->SacrificeChoices[j])
                        : default;
                }

                window.Refresh("CURSED RIFT", 0f, cardData, null, subtitle: "CHOOSE A SACRIFICE", allowCancel: true, allowReroll: false);
                break;
            }

            case CursedRiftInteractionState.SelectingMutation:
            {
                var cardData = new UpgradeCardWidget.CardData[interaction->MutationChoices.Length];

                for (int j = 0; j < interaction->MutationChoices.Length; j++)
                {
                    cardData[j] = j < interaction->MutationChoiceCount
                        ? BuildCardData(frame, entity, interaction->MutationChoices[j])
                        : default;
                }

                // Reuses BuildCardData unchanged (internal, see its own comment) - a
                // CursedRiftInteraction.MutationChoices entry is the exact same LevelUpOption
                // shape a normal level-up's RiftMutation category rolls.
                window.Refresh("RIFT AWAKENED", 0f, cardData, null, subtitle: "CHOOSE 1 MUTATION", allowCancel: false, allowReroll: false);
                break;
            }
        }
    }

    // Sacrifice cards are NOT UpgradeData (see SacrificeDefinition's own comment - a sacrifice
    // isn't an upgrade) - built from the asset's own DisplayName/Icon/Description/TopLabel/
    // ButtonLabel plus a live BuildValuePreview call (never cached, so it can't go stale between
    // roll and pick). KindText is a flat constant, not a switch - every sacrifice is "RIFT
    // SACRIFICE" (unlike a level-up option's KindText, which varies by Kind).
    private static unsafe UpgradeCardWidget.CardData BuildSacrificeCardData(Frame frame, EntityRef entity, AssetRef<SacrificeDefinition> sacrificeRef)
    {
        if (sacrificeRef.IsValid == false)
            return default;

        SacrificeDefinition data = frame.FindAsset(sacrificeRef);

        return new UpgradeCardWidget.CardData
        {
            HasOption = true,
            Icon = data.Icon,
            DisplayName = data.DisplayName,
            Description = data.Description,
            KindText = "RIFT SACRIFICE",
            TopLabelOverride = string.IsNullOrEmpty(data.TopLabel) ? "SACRIFICE" : data.TopLabel,
            ValuePreview = data.BuildValuePreview(frame, entity),
            ButtonLabel = string.IsNullOrEmpty(data.ButtonLabel) ? "SACRIFICE" : data.ButtonLabel
        };
    }

    // Card click from `cards[]` (UpgradeCardWidget family) - dispatches by _windowOwner[slotIndex]
    // (cached fresh every tick by UpdateWindowOwners, see its own comment) rather than a separately
    // tracked "mode" flag.
    private void OnCardClicked(int slotIndex, int optionIndex)
    {
        switch (_windowOwner[slotIndex])
        {
            case ChoiceWindowOwner.LevelUp:
                OnUpgradeCardClicked(slotIndex, optionIndex);
                break;

            case ChoiceWindowOwner.CursedRift:
                if (_poiWindowStage[slotIndex] == CursedRiftInteractionState.SelectingMutation)
                    _game.SendCommand(slotIndex, new SelectMutationCommand { OptionIndex = (byte)optionIndex });
                else
                    _game.SendCommand(slotIndex, new SelectSacrificeCommand { OptionIndex = (byte)optionIndex });
                break;

            case ChoiceWindowOwner.Store:
                if (optionIndex == StoreWeaponLevelUpCardIndex)
                    _game.SendCommand(slotIndex, new BuyStoreWeaponLevelCommand());
                else
                    _game.SendCommand(slotIndex, new BuyStoreFoodCommand { OfferIndex = (byte)optionIndex });
                break;

            case ChoiceWindowOwner.Blacksmith:
                _game.SendCommand(slotIndex, new SelectBlacksmithPerkCommand { OptionIndex = (byte)optionIndex });
                break;
        }
    }

    // Card click from `weaponCards[]` (WeaponCardWidget family) - split from OnCardClicked (see
    // ChooseWindow.onWeaponCardClicked's own comment) since Store's own screen shows both families
    // at once, mapping to different commands. A Choose-Weapon level-up option is granted the exact
    // same way a plain Level-Up card is (SelectLevelUpUpgradeCommand{OptionIndex} - the sim doesn't
    // care which card family the UI used, both index the same LevelUpChoice.Options array), so this
    // reuses OnUpgradeCardClicked unchanged for that case.
    private void OnWeaponCardClicked(int slotIndex, int optionIndex)
    {
        switch (_windowOwner[slotIndex])
        {
            case ChoiceWindowOwner.LevelUp:
                OnUpgradeCardClicked(slotIndex, optionIndex);
                break;

            case ChoiceWindowOwner.Store:
                _game.SendCommand(slotIndex, new BuyStoreWeaponCommand { OfferIndex = (byte)optionIndex });
                break;
        }
    }

    // ChooseWindow.secondaryButton is ONE button reused for four mutually-exclusive purposes -
    // "KEEP CURRENT" on a Choose-Weapon screen, "CANCEL" on Cursed Rift's Sacrifice stage/a
    // Blacksmith pick screen, "CLOSE" on the Store screen - dispatched the same way OnCardClicked
    // is. A plain (non-weapon) Level-Up never shows this button at all (Refresh's allowCancel
    // defaults false there), so LevelUp's own case here only ever means Choose-Weapon in practice.
    private void OnSecondaryButtonClicked(int slotIndex)
    {
        switch (_windowOwner[slotIndex])
        {
            case ChoiceWindowOwner.LevelUp:
                OnKeepCurrentClicked(slotIndex);
                break;

            case ChoiceWindowOwner.CursedRift:
                _game.SendCommand(slotIndex, new CancelCursedRiftCommand());
                break;

            case ChoiceWindowOwner.Store:
                _game.SendCommand(slotIndex, new CloseStoreCommand());
                break;

            case ChoiceWindowOwner.Blacksmith:
                _game.SendCommand(slotIndex, new CancelBlacksmithCommand());
                break;
        }
    }

    // A plain level-up always shows a generic title regardless of any LevelUpConfig.LevelSequence
    // category (see LevelUpUtility.RollOptionsFor's own comment on FromChest) - only a Chest names
    // its specific category, since each Chest is authored to exactly one in the Editor.
    private static unsafe string BuildTitle(LevelUpChoice* choice)
    {
        return choice->FromChest ? GetCategoryDisplayName(choice->Category) : "Level Up!";
    }

    private static string GetCategoryDisplayName(LevelUpCategory category)
    {
        switch (category)
        {
            case LevelUpCategory.HeroSkill: return "Hero Skill";
            case LevelUpCategory.GlobalUpgrade: return "Global Upgrade";
            case LevelUpCategory.RiftMutation: return "Rift Mutation";
            case LevelUpCategory.WeaponPerk: return "Weapon Perk";
            case LevelUpCategory.ChooseWeapon: return "Weapon";
            case LevelUpCategory.RiftMarkMutation: return "Rift Mark Mutation";
            default: return "Chest";
        }
    }

    // WeaponPerkData/SkillActionData/GlobalUpgradeData/PassiveUpgradeData/RiftMutationData all
    // derive from the shared UpgradeData base (Icon/DisplayName/GetDescription), so this needs no
    // switch on option.Kind at all for those - resolving the AssetRef<UpgradeData> generically is
    // enough. Rarity is the one field that ISN'T shared (only WeaponPerkData/RiftMutationData still
    // have one - see UpgradeData's own comment), so RarityIndex below is resolved with its own type
    // check rather than a plain data.Rarity read; -1 (no rarity) tells UpgradeCardWidget to hide the
    // rarity badge entirely. Stack info is the other kind-specific thing (only a capped
    // GlobalUpgradeData has it - see GlobalUpgradeData.MaxPicks/LevelUpUtility.IsCappedOut, the same
    // cap this reads back for display), so that part alone switches on Kind.
    // internal (not private) so CursedRift's mutation-reward stage (a LevelUpOption[3] stored on
    // CursedRiftInteraction.MutationChoices, the exact same shape LevelUpChoice.Options already
    // is) can reuse this unchanged instead of re-deriving equivalent card-building logic - see
    // RefreshCursedRiftWindow/docs/choice-window-refactor.md.
    internal static unsafe UpgradeCardWidget.CardData BuildCardData(Frame frame, EntityRef entity, LevelUpOption option)
    {
        UpgradeData data = frame.FindAsset(option.Upgrade);
        int currentStacks = 0;
        int maxStacks = 0;
        bool isRanked = false;
        string description = data.GetDescription();

        // Hero Ascension lines (Passive Upgrade or Skill Upgrade) with MaxRank > 1 - generic over
        // both kinds via IRankedUpgrade, so a future ranked ascension for any hero/pool gets the same
        // stack readout + next-rank description with no further UI changes. Checked ahead of the
        // Global Upgrade branch below since the two stacking pools are otherwise unrelated.
        if (data is IRankedUpgrade ranked && ranked.MaxRank > 1)
        {
            maxStacks = ranked.MaxRank;
            currentStacks = UpgradeHistoryUtility.GetCount(frame, entity, option.Kind, option.Upgrade);
            description = ranked.GetDescription(currentStacks + 1);
            isRanked = true;
        }
        else if (option.Kind == LevelUpPoolKind.GlobalUpgrade)
        {
            var upgradeRef = new AssetRef<GlobalUpgradeData>(option.Upgrade.Id);
            GlobalUpgradeData globalUpgrade = frame.FindAsset(upgradeRef);

            if (globalUpgrade.MaxPicks > 0)
            {
                maxStacks = globalUpgrade.MaxPicks;
                currentStacks = GlobalUpgradeUtility.GetPickCount(frame, entity, upgradeRef);
            }
        }

        int rarityIndex = data switch
        {
            WeaponPerkData weaponPerk => (int)weaponPerk.Rarity,
            RiftMutationData mutation => (int)mutation.Rarity,
            _ => -1
        };

        return new UpgradeCardWidget.CardData
        {
            HasOption = true,
            Icon = data.Icon,
            DisplayName = data.DisplayName,
            Description = description,
            RarityIndex = rarityIndex,
            KindText = KindText(option),
            CurrentStacks = currentStacks,
            MaxStacks = maxStacks,
            IsRanked = isRanked
        };
    }

    // ChooseWeapon has no single UpgradeData/Rarity to resolve generically like BuildCardData above
    // (see LevelUpOption's own WeaponData/RolledPerks fields) - built from WeaponDataAsset's own
    // GetIcon()/DisplayName instead, with each rolled perk resolved as its own UpgradeData into a
    // WeaponCardWidget.PerkRowData. Each row shows both the perk's own name (Title) and its
    // live-formatted GetDescription() (what it actually does, e.g. "+15% Damage, -10% Fire Rate").
    // Rendered via the dedicated WeaponCardWidget, never reaches BuildCardData/KindText below.
    // Takes the 3 raw fields directly (not a whole LevelUpOption) so Store's own weapon-offer
    // builder (BuildStoreWeaponCardData, StoreWeaponOffer carries the exact same 3 fields) reuses
    // this unchanged alongside the existing Choose-Weapon caller.
    private static unsafe WeaponCardWidget.CardData BuildWeaponCardData(Frame frame, AssetRef<WeaponDataAsset> weaponDataRef, FixedArray<AssetRef<WeaponPerkData>> rolledPerks, int rolledPerkCount, int weaponLevel = 0)
    {
        WeaponDataAsset weaponData = frame.FindAsset(weaponDataRef);
        var perks = new WeaponCardWidget.PerkRowData[rolledPerkCount];

        for (int i = 0; i < rolledPerkCount; i++)
        {
            WeaponPerkData perk = frame.FindAsset(rolledPerks[i]);

            perks[i] = new WeaponCardWidget.PerkRowData
            {
                Icon = perk.Icon,
                Title = perk.DisplayName,
                Description = perk.GetDescription(),
                RarityIndex = (int)perk.Rarity
            };
        }

        // DisplayName isn't authored on most WeaponDataAsset instances yet (see docs/weapon-perks.md) -
        // fall back to the asset's own file name, beautified (e.g. "AssaultRifleWeaponData" -> "Assault Rifle").
        string baseName = string.IsNullOrEmpty(weaponData.DisplayName)
            ? StringUtility.Beautify(weaponData.name, "WeaponData")
            : weaponData.DisplayName;

        return new WeaponCardWidget.CardData
        {
            HasOption = true,
            WeaponIcon = weaponData.GetIcon(),
            // weaponLevel is 0 for a plain Choose-Weapon level-up option (no level concept there) -
            // WithLevelSuffix no-ops in that case, so this call site is unaffected. Only a Store
            // offer (see BuildStoreWeaponCardData) ever passes a level > 0, e.g. "Shotgun +1".
            WeaponName = StringUtility.WithLevelSuffix(baseName, weaponLevel),
            Damage = weaponData.Damage.AsFloat,
            FireRate = weaponData.FireRate.AsFloat,
            Range = weaponData.Range.AsFloat,
            MagazineSize = weaponData.MagazineSize,
            CriticalChance = weaponData.CriticalChance.AsFloat,
            ElementIndex = (int)weaponData.Element,
            Perks = perks
        };
    }

    // SkillUpgrade is the one kind that isn't self-descriptive - it needs SkillUpgradeSlot to say
    // whether it's the hero's Dash or their unique skill (see LevelUpOption/SkillSlotId).
    internal static string KindText(LevelUpOption option)
    {
        switch (option.Kind)
        {
            case LevelUpPoolKind.WeaponPerk: return "Weapon Perk";
            case LevelUpPoolKind.GlobalUpgrade: return "Global Upgrade";
            case LevelUpPoolKind.RiftMutation: return "Rift Mutation";
            case LevelUpPoolKind.RiftMarkMutation: return "Rift Mark Mutation";
            case LevelUpPoolKind.PassiveUpgrade: return "Passive Upgrade";
            case LevelUpPoolKind.SkillUpgrade:
                switch (option.SkillUpgradeSlot)
                {
                    case SkillSlotId.DashSkill: return "Dash Skill";
                    case SkillSlotId.HeroSkill: return "Hero Skill";
                    default: return "Skill Upgrade";
                }
            default: return string.Empty;
        }
    }

    // With nobody else connected (no co-op partner, local couch or remote - see
    // CloseUpgradeScreenIfSolo/Frame.PlayerConnectedCount), nothing else is ever going to confirm, so
    // waiting for Frame.Global.LevelUpScreenOpen to clear would just be stalling on this player's own
    // pick. Close right away instead. With others still connected, the window stays open showing this
    // slot's card as locked/confirmed (see the confirmedIndex handling in UpdateUpgradeScreen) until
    // LevelUpScreenOpen actually clears - which only happens once every connected player has confirmed
    // or the timer expires (see LevelUpSystem.AllConfirmed).
    private void OnUpgradeCardClicked(int slotIndex, int optionIndex)
    {
        _game.SendCommand(slotIndex, new SelectLevelUpUpgradeCommand { OptionIndex = (byte)optionIndex });
        CloseUpgradeScreenIfSolo();
    }

    private void OnRerollClicked(int slotIndex)
    {
        _game.SendCommand(slotIndex, new RerollLevelUpOptionsCommand());
    }

    private void OnKeepCurrentClicked(int slotIndex)
    {
        _game.SendCommand(slotIndex, new KeepCurrentWeaponCommand());
        CloseUpgradeScreenIfSolo();
    }

    private void CloseUpgradeScreenIfSolo()
    {
        if (_upgradeScreenClosedEarly == false && _game.Frames.Predicted.PlayerConnectedCount <= 1)
        {
            _upgradeScreenClosedEarly = true;
            CloseUpgradeScreen();
        }
    }

    private void CloseUpgradeScreen()
    {
        // VERIFY IN EDITOR: GameplayWindow holds the per-player HUD widgets and looks like the
        // intended "back to normal play" window, but nothing in code shows it explicitly today
        // (it may just be the scene's default-active window under this WindowManager) - adjust
        // this call if that's not the case.
        //
        // This ALSO hides every choiceWindows[] instance, this player's own included, regardless of
        // whether IT was the one that triggered this Level-Up (WindowManager.ShowWindow<T>() hides
        // every window not of type T) - if this player was mid-Cursed-Rift/Store/Blacksmith,
        // UpdatePoiWindow re-shows their own screen again next tick (see its own comment) since
        // their own interaction component was never touched by any of this.
        windowManager.ShowWindow<GameplayWindow>();

        // Stops the ramp-down above if it hadn't finished yet (e.g. a Chest closing right after
        // a level-up opened) - Stop() cancels its OnComplete too, so a stale ShowWindow<
        // ChooseWindow>() can never fire after this switches back.
        _timeScaleTween.Stop();
        _timeScaleTween = Tween.Custom(Time.timeScale, 1f, upgradeTimeScaleRampOutDuration,
            onValueChange: v => Time.timeScale = (float)v, useUnscaledTime: true);
    }

}

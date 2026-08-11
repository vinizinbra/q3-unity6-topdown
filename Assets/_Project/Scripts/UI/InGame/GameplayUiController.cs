using System;
using PrimeTween;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

public class GameplayUiController : QuantumGlobalMonoBehaviour
{
    [SerializeField] private WindowManager windowManager;
    [SerializeField] private TMP_Text lives;
    [SerializeField] private TMP_Text rtt;
    [SerializeField, Tooltip("One per local player slot - upgradeWindows[0] for slot 0, upgradeWindows[1] for slot 1, etc. Unused slots (no 2nd local player) can be left null.")]
    private UpgradeWindow[] upgradeWindows;

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
    private Action[] _rerollClickedHandlers;
    private Action[] _keepCurrentClickedHandlers;
    private Tween _timeScaleTween;

    private void Start()
    {
        windowManager.ShowWindow<LoadingWindow>();

        _cardClickedHandlers = new Action<int>[upgradeWindows.Length];
        _rerollClickedHandlers = new Action[upgradeWindows.Length];
        _keepCurrentClickedHandlers = new Action[upgradeWindows.Length];

        for (int i = 0; i < upgradeWindows.Length; i++)
        {
            if (upgradeWindows[i] == null)
                continue;

            int slotIndex = i;
            _cardClickedHandlers[i] = optionIndex => OnUpgradeCardClicked(slotIndex, optionIndex);
            upgradeWindows[i].onCardClicked += _cardClickedHandlers[i];

            _rerollClickedHandlers[i] = () => OnRerollClicked(slotIndex);
            upgradeWindows[i].onRerollClicked += _rerollClickedHandlers[i];

            _keepCurrentClickedHandlers[i] = () => OnKeepCurrentClicked(slotIndex);
            upgradeWindows[i].onKeepCurrentClicked += _keepCurrentClickedHandlers[i];
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

        for (int i = 0; i < upgradeWindows.Length; i++)
        {
            if (upgradeWindows[i] != null && _cardClickedHandlers[i] != null)
                upgradeWindows[i].onCardClicked -= _cardClickedHandlers[i];

            if (upgradeWindows[i] != null && _rerollClickedHandlers[i] != null)
                upgradeWindows[i].onRerollClicked -= _rerollClickedHandlers[i];

            if (upgradeWindows[i] != null && _keepCurrentClickedHandlers[i] != null)
                upgradeWindows[i].onKeepCurrentClicked -= _keepCurrentClickedHandlers[i];
        }
    }

    void LoadWaitingWindow(EntityRef entityRef)
    {
        windowManager.ShowWindow<WaitingWindow>();
    }

    public void Leave()
    {
        MatchMakingConfig.Instance.Client.Disconnect();
        _onLeave?.Invoke(_placement);

    }

    public override void QStart(QuantumGame game)
    {

    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        UpdateUpgradeScreen(game);
    }

    public override void QLateUpdate(QuantumGame game)
    {

    }

    // Polls Frame.Global.LevelUpScreenOpen and diffs against the last-seen value - same idiom
    // ExpBarUiWidget already uses for reading Global state every QUpdate, safe here because the
    // View (unlike the sim) is never rolled back. See docs/level-up-upgrades.md.
    private unsafe void UpdateUpgradeScreen(QuantumGame game)
    {
        if (upgradeWindows.Length == 0 || MyLocalPlayer.Instance == null)
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
                .OnComplete(() => windowManager.ShowWindow<UpgradeWindow>());
        }
        else if (isOpen == false && _upgradeScreenWasOpen == true && _upgradeScreenClosedEarly == false)
        {
            CloseUpgradeScreen();
        }

        _upgradeScreenWasOpen = isOpen;

        if (isOpen == false)
            return;

        var slots = MyLocalPlayer.Instance.Slots;

        for (int i = 0; i < upgradeWindows.Length; i++)
        {
            if (upgradeWindows[i] == null)
                continue;

            // The window only actually SetActive(true)s once the Time.timeScale ramp above finishes
            // (windowManager.ShowWindow<UpgradeWindow>() in the OnComplete callback) - until then its
            // GameObject is still inactive, so Awake() hasn't run and cards/weaponCards are still
            // null. Skip refreshing until it's really shown, or SetCardFamilyActive NREs on those
            // null arrays for the ~upgradeTimeScaleRampDuration window between isOpen flipping true
            // and the reveal actually happening.
            if (upgradeWindows[i].gameObject.activeInHierarchy == false)
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
            upgradeWindows[i].UpdateRerollButton(rerollCharges, choice->Confirmed);

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
                    weaponCardData[j] = j < choice->OptionCount ? BuildWeaponCardData(frame, choice->Options[j]) : default;
                }

                upgradeWindows[i].RefreshWeaponChoice(title, frame.Global->LevelUpTimeRemaining.AsFloat, weaponCardData, confirmedIndex);
            }
            else
            {
                var cardData = new UpgradeCardWidget.CardData[choice->Options.Length];

                for (int j = 0; j < choice->Options.Length; j++)
                {
                    cardData[j] = j < choice->OptionCount ? BuildCardData(frame, slots[i].EntityRef, choice->Options[j]) : default;
                }

                upgradeWindows[i].Refresh(title, frame.Global->LevelUpTimeRemaining.AsFloat, cardData, confirmedIndex);
            }
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
            default: return "Chest";
        }
    }

    // WeaponPerkData/SkillActionData/GlobalUpgradeData/PassiveUpgradeData all derive from the
    // shared UpgradeData base (Icon/DisplayName/Rarity/GetDescription), so this needs no switch on
    // option.Kind at all - resolving the AssetRef<UpgradeData> generically is enough. Stack info is
    // the one thing that IS kind-specific (only a capped GlobalUpgradeData has it - see
    // GlobalUpgradeData.MaxPicks/LevelUpUtility.IsCappedOut, the same cap this reads back for
    // display), so that part alone switches on Kind.
    private static unsafe UpgradeCardWidget.CardData BuildCardData(Frame frame, EntityRef entity, LevelUpOption option)
    {
        UpgradeData data = frame.FindAsset(option.Upgrade);
        int currentStacks = 0;
        int maxStacks = 0;
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

        return new UpgradeCardWidget.CardData
        {
            HasOption = true,
            Icon = data.Icon,
            DisplayName = data.DisplayName,
            Description = description,
            RarityIndex = (int)data.Rarity,
            KindText = KindText(option),
            CurrentStacks = currentStacks,
            MaxStacks = maxStacks
        };
    }

    // ChooseWeapon has no single UpgradeData/Rarity to resolve generically like BuildCardData above
    // (see LevelUpOption's own WeaponData/RolledPerks fields) - built from WeaponDataAsset's own
    // GetIcon()/DisplayName instead, with each rolled perk resolved as its own UpgradeData into a
    // WeaponCardWidget.PerkRowData. Each row shows both the perk's own name (Title) and its
    // live-formatted GetDescription() (what it actually does, e.g. "+15% Damage, -10% Fire Rate").
    // Rendered via the dedicated WeaponCardWidget, never reaches BuildCardData/KindText below.
    private static unsafe WeaponCardWidget.CardData BuildWeaponCardData(Frame frame, LevelUpOption option)
    {
        WeaponDataAsset weaponData = frame.FindAsset(option.WeaponData);
        var perks = new WeaponCardWidget.PerkRowData[option.RolledPerkCount];

        for (int i = 0; i < option.RolledPerkCount; i++)
        {
            UpgradeData perk = frame.FindAsset(option.RolledPerks[i]);

            perks[i] = new WeaponCardWidget.PerkRowData
            {
                Icon = perk.Icon,
                Title = perk.DisplayName,
                Description = perk.GetDescription(),
                RarityIndex = (int)perk.Rarity
            };
        }

        return new WeaponCardWidget.CardData
        {
            HasOption = true,
            WeaponIcon = weaponData.GetIcon(),
            // DisplayName isn't authored on most WeaponDataAsset instances yet (see docs/weapon-perks.md) -
            // fall back to the asset's own file name, beautified (e.g. "AssaultRifleWeaponData" -> "Assault Rifle").
            WeaponName = string.IsNullOrEmpty(weaponData.DisplayName)
                ? StringUtility.Beautify(weaponData.name, "WeaponData")
                : weaponData.DisplayName,
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
    private static string KindText(LevelUpOption option)
    {
        switch (option.Kind)
        {
            case LevelUpPoolKind.WeaponPerk: return "Weapon Perk";
            case LevelUpPoolKind.GlobalUpgrade: return "Global Upgrade";
            case LevelUpPoolKind.RiftMutation: return "Rift Mutation";
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
        windowManager.ShowWindow<GameplayWindow>();

        // Stops the ramp-down above if it hadn't finished yet (e.g. a Chest closing right after
        // a level-up opened) - Stop() cancels its OnComplete too, so a stale ShowWindow<
        // UpgradeWindow>() can never fire after this switches back.
        _timeScaleTween.Stop();
        _timeScaleTween = Tween.Custom(Time.timeScale, 1f, upgradeTimeScaleRampOutDuration,
            onValueChange: v => Time.timeScale = (float)v, useUnscaledTime: true);
    }

}
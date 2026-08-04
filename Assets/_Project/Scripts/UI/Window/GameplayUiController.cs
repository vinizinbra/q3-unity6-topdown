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
    private Action<int>[] _cardClickedHandlers;
    private Tween _timeScaleTween;

    // Tracks, per local slot, whether that slot no longer needs waiting on this screen - either it
    // already clicked a card, or it never rolled a LevelUpChoice this round (nothing to pick - see
    // the "stay blank" branch in UpdateUpgradeScreen). Remote co-op teammates can still be holding
    // Frame.Global.LevelUpScreenOpen true (LevelUpSystem.AllConfirmed waits for every connected
    // player, not just this client's own) - once every LOCAL slot is done, there's no reason for
    // this client to keep staring at its own already-decided screen, so it closes and resumes
    // immediately instead of waiting on the simulation flag.
    private bool[] _localSlotDone;
    private bool _upgradeScreenClosedEarly;

    private void Start()
    {
        windowManager.ShowWindow<LoadingWindow>();

        _cardClickedHandlers = new Action<int>[upgradeWindows.Length];
        _localSlotDone = new bool[upgradeWindows.Length];

        for (int i = 0; i < upgradeWindows.Length; i++)
        {
            if (upgradeWindows[i] == null)
                continue;

            int slotIndex = i;
            _cardClickedHandlers[i] = optionIndex => OnUpgradeCardClicked(slotIndex, optionIndex);
            upgradeWindows[i].onCardClicked += _cardClickedHandlers[i];
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
            Array.Clear(_localSlotDone, 0, _localSlotDone.Length);
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
                // This slot rolled nothing this screen (every pool empty) - stay blank. Nothing to
                // pick means nothing to wait on either, so it can't block CloseUpgradeScreen below.
                _localSlotDone[i] = true;
                continue;
            }

            int? confirmedIndex = choice->Confirmed ? (int?)choice->SelectedIndex : null;
            string title = BuildTitle(choice);

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

        if (option.Kind == LevelUpPoolKind.GlobalUpgrade)
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
            Description = data.GetDescription(),
            RarityIndex = (int)data.Rarity,
            KindText = KindText(option),
            CurrentStacks = currentStacks,
            MaxStacks = maxStacks
        };
    }

    // ChooseWeapon has no single UpgradeData/Rarity to resolve generically like BuildCardData above
    // (see LevelUpOption's own WeaponData/RolledPerks fields) - built from WeaponDataAsset's own
    // GetIcon()/DisplayName instead, with each rolled perk resolved as its own UpgradeData into a
    // WeaponCardWidget.PerkRowData. Each row shows the perk's live-formatted GetDescription() (what
    // it actually does, e.g. "+15% Damage, -10% Fire Rate"), not its DisplayName - the weapon's own
    // name is already the card header, so repeating each perk's name would tell the player nothing.
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

    private void OnUpgradeCardClicked(int slotIndex, int optionIndex)
    {
        _game.SendCommand(slotIndex, new SelectLevelUpUpgradeCommand { OptionIndex = (byte)optionIndex });

        if (slotIndex < 0 || slotIndex >= _localSlotDone.Length)
            return;

        _localSlotDone[slotIndex] = true;

        // Don't wait for the simulation to confirm the pick or for remote co-op teammates to finish
        // their own screens (Frame.Global.LevelUpScreenOpen only clears once every connected player
        // is done - see LevelUpSystem.AllConfirmed) - once every LOCAL slot has picked, this
        // client's own screen is done and can close right away.
        if (_upgradeScreenClosedEarly == false && AllLocalSlotsDone() == true)
        {
            _upgradeScreenClosedEarly = true;
            CloseUpgradeScreen();
        }
    }

    private bool AllLocalSlotsDone()
    {
        for (int i = 0; i < _localSlotDone.Length; i++)
        {
            if (upgradeWindows[i] != null && _localSlotDone[i] == false)
                return false;
        }

        return true;
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
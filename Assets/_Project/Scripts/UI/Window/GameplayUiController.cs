using System;
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
    public bool isDead = false;
    private int _placement;
    private Action<int> _onLeave;
    private bool _upgradeScreenWasOpen;
    private Action<int>[] _cardClickedHandlers;

    private void Start()
    {
        windowManager.ShowWindow<LoadingWindow>();

        _cardClickedHandlers = new Action<int>[upgradeWindows.Length];

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
        PhotonMain.Disconnect();
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
            windowManager.ShowWindow<UpgradeWindow>();
        }
        else if (isOpen == false && _upgradeScreenWasOpen == true)
        {
            // VERIFY IN EDITOR: GameplayWindow holds the per-player HUD widgets and looks like the
            // intended "back to normal play" window, but nothing in code shows it explicitly today
            // (it may just be the scene's default-active window under this WindowManager) - adjust
            // this call if that's not the case.
            windowManager.ShowWindow<GameplayWindow>();
        }

        _upgradeScreenWasOpen = isOpen;

        if (isOpen == false)
            return;

        var slots = MyLocalPlayer.Instance.Slots;

        for (int i = 0; i < upgradeWindows.Length; i++)
        {
            if (upgradeWindows[i] == null)
                continue;

            // Every local slot's own LevelUpChoice is independent (see docs/level-up-upgrades.md),
            // so each local player refreshes their own window from their own entity.
            if (i >= slots.Count || slots[i].IsSet == false)
                continue;

            if (frame.Unsafe.TryGetPointer<LevelUpChoice>(slots[i].EntityRef, out var choice) == false)
                continue; // this slot rolled nothing this screen (every pool empty) - stay blank

            var cardData = new UpgradeCardWidget.CardData[choice->Options.Length];

            for (int j = 0; j < choice->Options.Length; j++)
            {
                cardData[j] = j < choice->OptionCount ? BuildCardData(frame, choice->Options[j]) : default;
            }

            int? confirmedIndex = choice->Confirmed ? (int?)choice->SelectedIndex : null;
            upgradeWindows[i].Refresh(frame.Global->LevelUpTimeRemaining.AsFloat, cardData, confirmedIndex);
        }
    }

    // WeaponPerkData/SkillActionData/GlobalUpgradeData/PassiveUpgradeData all derive from the
    // shared UpgradeData base (Icon/DisplayName/Rarity/GetDescription), so this needs no switch on
    // option.Kind at all - resolving the AssetRef<UpgradeData> generically is enough.
    private static UpgradeCardWidget.CardData BuildCardData(Frame frame, LevelUpOption option)
    {
        UpgradeData data = frame.FindAsset(option.Upgrade);

        return new UpgradeCardWidget.CardData
        {
            HasOption = true,
            Icon = data.Icon,
            DisplayName = data.DisplayName,
            Description = data.GetDescription(),
            RarityIndex = (int)data.Rarity,
            KindText = KindText(option)
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
    }

}
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
    [SerializeField] private UpgradeWindow upgradeWindow;
    public bool isDead = false;
    private int _placement;
    private Action<int> _onLeave;
    private bool _upgradeScreenWasOpen;

    private void Start()
    {
        windowManager.ShowWindow<LoadingWindow>();

        if (upgradeWindow != null)
            upgradeWindow.onCardClicked += OnUpgradeCardClicked;
    }

    private void OnDestroy()
    {
        if (upgradeWindow != null)
            upgradeWindow.onCardClicked -= OnUpgradeCardClicked;
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
        if (upgradeWindow == null)
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

        if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
            return;

        if (frame.Unsafe.TryGetPointer<LevelUpChoice>(MyLocalPlayer.Instance.EntityRef, out var choice) == false)
            return; // this client rolled nothing this screen (every pool empty) - stay blank

        var cardData = new UpgradeCardWidget.CardData[choice->Options.Length];

        for (int i = 0; i < choice->Options.Length; i++)
        {
            cardData[i] = i < choice->OptionCount ? BuildCardData(frame, choice->Options[i]) : default;
        }

        int? confirmedIndex = choice->Confirmed ? (int?)choice->SelectedIndex : null;
        upgradeWindow.Refresh(frame.Global->LevelUpTimeRemaining.AsFloat, cardData, confirmedIndex);
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
            RarityColor = RarityColor(data.Rarity)
        };
    }

    private static Color RarityColor(UpgradeRarity rarity)
    {
        switch (rarity)
        {
            case UpgradeRarity.Uncommon: return new Color(0.31f, 0.78f, 0.47f);
            case UpgradeRarity.Rare: return new Color(0.25f, 0.55f, 0.95f);
            case UpgradeRarity.Epic: return new Color(0.65f, 0.35f, 0.95f);
            case UpgradeRarity.Legendary: return new Color(0.95f, 0.65f, 0.15f);
            default: return Color.white; // Common
        }
    }

    private void OnUpgradeCardClicked(int index)
    {
        _game.SendCommand(new SelectLevelUpUpgradeCommand { OptionIndex = (byte)index });
    }

}
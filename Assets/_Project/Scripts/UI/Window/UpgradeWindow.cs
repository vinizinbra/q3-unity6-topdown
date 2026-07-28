using System;
using TMPro;
using UnityEngine;

// Level-up upgrade-choice screen - shown while Frame.Global.LevelUpScreenOpen is true (see
// GameplayUiController.QUpdate, which drives this window and owns all Quantum-facing reads). Just
// orchestrates a fixed array of UpgradeCardWidget children, one per LevelUpChoice.Options slot -
// this class itself has no Quantum dependency either.
public class UpgradeWindow : UiWindow
{
    [SerializeField] private UpgradeCardWidget[] cards;
    [SerializeField] private TMP_Text countdownText;

    // Raised with a card's index (0-based, matching LevelUpChoice.Options) when clicked -
    // GameplayUiController forwards this into a SelectLevelUpUpgradeCommand.
    public Action<int> onCardClicked;

    private void Awake()
    {
        for (int i = 0; i < cards.Length; i++)
        {
            int index = i; // capture by value, not by the loop variable
            cards[i].onClicked += _ => onCardClicked?.Invoke(index);
        }
    }

    // confirmedIndex is null while this client hasn't picked yet; once set, every card is locked
    // out (no more clicks can change the pick - see LevelUpUtility.ConfirmSelection on the sim side,
    // which already rejects a second click, but disabling the buttons here avoids a dead click in
    // the first place).
    public void Refresh(float timeRemaining, UpgradeCardWidget.CardData[] cardData, int? confirmedIndex)
    {
        if (countdownText != null)
            countdownText.text = Mathf.CeilToInt(Mathf.Max(timeRemaining, 0f)).ToString();

        bool interactable = confirmedIndex.HasValue == false;

        for (int i = 0; i < cards.Length; i++)
        {
            UpgradeCardWidget.CardData data = i < cardData.Length ? cardData[i] : default;
            cards[i].Setup(data, interactable);
        }
    }
}

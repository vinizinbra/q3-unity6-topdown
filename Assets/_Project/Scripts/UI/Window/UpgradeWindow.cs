using System;
using TMPro;
using UnityEngine;

// Level-up upgrade-choice screen - shown while Frame.Global.LevelUpScreenOpen is true (see
// GameplayUiController.QUpdate, which drives this window and owns all Quantum-facing reads). Just
// orchestrates an array of UpgradeCardWidget children, one per LevelUpChoice.Options slot - this
// class itself has no Quantum dependency either.
public class UpgradeWindow : UiWindow
{
    // cardPrefab doubles as the first card (left in place in the hierarchy, not actually
    // instantiated) - Awake clones it (cardCount - 1) more times under the same parent so the
    // scene only needs one hand-authored card. cardCount must match LevelUpChoice.Options' fixed
    // size (3) - see LevelUp.qtn.
    [SerializeField] private UpgradeCardWidget cardPrefab;
    [SerializeField] private int cardCount = 3;
    [SerializeField] private TMP_Text countdownText;

    // Raised with a card's index (0-based, matching LevelUpChoice.Options) when clicked -
    // GameplayUiController forwards this into a SelectLevelUpUpgradeCommand.
    public Action<int> onCardClicked;

    private UpgradeCardWidget[] cards;

    private void Awake()
    {
        cards = new UpgradeCardWidget[cardCount];
        cards[0] = cardPrefab;

        for (int i = 1; i < cardCount; i++)
        {
            cards[i] = Instantiate(cardPrefab, cardPrefab.transform.parent);
        }

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

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A single upgrade choice card, one per LevelUpChoice.Options entry - see UpgradeWindow, which owns
// an array of these. Pure view: takes plain data in via Setup(), has no Quantum dependency at all
// (GameplayUiController is the one place that reads a LevelUpOption and turns it into a CardData).
public class UpgradeCardWidget : MonoBehaviour
{
    [Serializable]
    public struct CardData
    {
        public bool HasOption;
        public Sprite Icon;
        public string DisplayName;
        public string Description;
        public Color RarityColor;
    }

    [SerializeField] private GameObject root;
    [SerializeField] private Image icon;
    [SerializeField, Tooltip("Tinted per the option's UpgradeRarity - e.g. a card border/background.")]
    private Image rarityFrame;
    [SerializeField] private TMP_Text displayName;
    [SerializeField] private TMP_Text description;
    [SerializeField] private Button button;

    public event Action<UpgradeCardWidget> onClicked;

    private void Awake()
    {
        if (button != null)
            button.onClick.AddListener(() => onClicked?.Invoke(this));
    }

    public void Setup(CardData data, bool interactable)
    {
        if (root != null)
            root.SetActive(data.HasOption);

        if (data.HasOption == false)
            return;

        if (icon != null)
            icon.sprite = data.Icon;

        if (rarityFrame != null)
            rarityFrame.color = data.RarityColor;

        if (displayName != null)
            displayName.text = data.DisplayName;

        if (description != null)
            description.text = data.Description;

        if (button != null)
            button.interactable = interactable;
    }
}

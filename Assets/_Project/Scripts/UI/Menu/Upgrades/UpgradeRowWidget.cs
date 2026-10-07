using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One row of the Upgrades list: icon, name, description, level pips and next cost. Click or gamepad
/// focus selects it (the detail panel follows); Submit steps into the detail panel.
/// </summary>
[RequireComponent(typeof(Button))]
public class UpgradeRowWidget : MonoBehaviour, ISelectHandler, ISubmitHandler
{
    [SerializeField] private Image background;
    [SerializeField] private Image iconFrame;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private UpgradePipsWidget pips;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text costText;

    [Header("Colors")]
    [SerializeField] private Color idleColor = new Color(0.93f, 0.94f, 0.96f);
    [SerializeField] private Color selectedColor = new Color(1f, 0.86f, 0.91f);

    public event Action<UpgradeRowWidget> Clicked;

    /// <summary>Gamepad/keyboard Submit (not a mouse click) - steps into the detail panel.</summary>
    public event Action<UpgradeRowWidget> Submitted;

    public PermanentUpgradeData Data { get; private set; }

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(RaiseClicked);
    }

    public void Bind(PermanentUpgradeData data, int level)
    {
        Data = data;

        icon.sprite = data.icon;
        icon.enabled = data.icon != null;
        Color frame = data.accentColor * 0.35f;
        frame.a = 1f;
        iconFrame.color = frame;

        nameText.text = data.Name.ToUpperInvariant();
        descriptionText.text = data.description;
        SetLevel(level);
    }

    public void SetLevel(int level)
    {
        pips.Set(Data.maxLevel, level, Data.accentColor);
        levelText.text = level + " / " + Data.maxLevel;
        costText.text = level >= Data.maxLevel ? "MAX" : Data.CostFor(level).ToString();
    }

    public void SetSelected(bool selected)
    {
        background.color = selected ? selectedColor : idleColor;
    }

    public void OnSelect(BaseEventData eventData)
    {
        RaiseClicked();
    }

    public void OnSubmit(BaseEventData eventData)
    {
        Submitted?.Invoke(this);
    }

    private void RaiseClicked()
    {
        Clicked?.Invoke(this);
    }
}

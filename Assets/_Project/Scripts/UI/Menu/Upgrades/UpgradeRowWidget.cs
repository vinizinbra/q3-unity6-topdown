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
    [SerializeField] private Image iconFrame;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private UpgradePipsWidget pips;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text costText;

    [Header("Locked (unlock requirement not met)")]
    [SerializeField, Tooltip("The cost pill's currency icon - swapped for Lock Sprite while locked.")]
    private Image costIcon;
    [SerializeField] private Sprite lockSprite;
    [SerializeField] private Color lockedTint = new Color(0.45f, 0.47f, 0.53f);
    [SerializeField, Tooltip("Colour of the lock reason shown in place of the description.")]
    private Color lockReasonColor = new Color(0.992f, 0.224f, 0.443f);

    [Header("Colors")]
    [SerializeField, Tooltip("What changes while this is the selected item: graphic colours (rest / selected) and objects shown only while selected.")]
    private SelectionStyle selectionStyle = new SelectionStyle();

    public event Action<UpgradeRowWidget> Clicked;

    /// <summary>Gamepad/keyboard Submit (not a mouse click) - steps into the detail panel.</summary>
    public event Action<UpgradeRowWidget> Submitted;

    public PermanentUpgradeData Data { get; private set; }

    private Sprite costIconSprite;
    private Color nameColor;
    private bool defaultsCaptured;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(RaiseClicked);
        CaptureDefaults();
    }

    // Template clones run Bind before their own Awake has a chance in some orders - capture lazily.
    private void CaptureDefaults()
    {
        if (defaultsCaptured)
            return;

        defaultsCaptured = true;
        costIconSprite = costIcon != null ? costIcon.sprite : null;
        nameColor = nameText.color;
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
        SetLevel(level, MetaProgressionSave.GetLockReason(data));
    }

    /// <param name="lockReason">Why the next level can't be bought yet (see MetaProgressionSave.GetLockReason), or null.</param>
    public void SetLevel(int level, string lockReason)
    {
        CaptureDefaults();
        bool maxed = level >= Data.maxLevel;
        bool locked = lockReason != null && !maxed;

        pips.Set(Data.maxLevel, level, Data.accentColor);
        levelText.text = level + " / " + Data.maxLevel;
        costText.text = maxed ? "MAX" : locked ? "LOCKED" : Data.CostFor(level).ToString();

        descriptionText.text = locked
            ? $"<color=#{ColorUtility.ToHtmlStringRGB(lockReasonColor)}>{lockReason}</color>"
            : Data.description;
        nameText.color = locked ? lockedTint : nameColor;
        icon.color = locked ? lockedTint : Color.white;

        if (costIcon != null)
            costIcon.sprite = locked && lockSprite != null ? lockSprite : costIconSprite;
    }

    public void SetSelected(bool selected)
    {
        selectionStyle.Apply(selected);
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

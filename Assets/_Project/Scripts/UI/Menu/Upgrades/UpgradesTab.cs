using System.Collections.Generic;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Upgrades tab content: permanent upgrades split into Power / Exploration, one scrolling list per
/// category (one <see cref="UpgradeRowWidget"/> prefab, instantiated per <see cref="PermanentUpgradeData"/>),
/// with the selected upgrade's details and an UPGRADE button on the right.
///
/// The lines come from <see cref="GameDB"/>; levels and the Rift Shard wallet from <see cref="MetaProgressionSave"/>
/// (UPGRADE spends shards there). Any save change - a purchase, a cheat, shards banked after a run - refreshes the
/// rows and the detail panel. See docs/talents.md, "Meta progression".
/// </summary>
public class UpgradesTab : TabContent
{
    private const string LogTag = "Upgrades";

    [Header("Lists (index = PermanentUpgradeCategory)")]
    [SerializeField, Tooltip("The row placed in the first list in the scene: cloned once per upgrade (into every list), hidden itself.")]
    private UpgradeRowWidget rowTemplate;
    [SerializeField] private Transform[] listRoots;
    [SerializeField] private ScrollRect[] scrolls;
    [SerializeField] private Sprite dividerSprite;
    [SerializeField] private TabStripWidget categoryTabs;
    [SerializeField] private TMP_Text subtitleText;
    [SerializeField, TextArea(1, 2)] private string[] subtitles =
    {
        "Permanent stat upgrades that make your Raiders stronger in every run.",
        "Unlock new systems and options during your runs, giving you more ways to explore, adapt and build."
    };

    [Header("Glitch")]
    [Tooltip("Plays a short glitch burst on the info panel whenever the viewed item changes.")]
    [SerializeField] private GlitchWidget infoGlitch;

    [Header("Navigation")]
    [Tooltip("The detail panel level: Submit on a row steps into it, Cancel steps back out.")]
    [SerializeField] private FocusScopeWidget infoScope;

    [Header("Detail panel")]
    [SerializeField] private TMP_Text detailName;
    [SerializeField] private TMP_Text detailKind;
    [SerializeField] private Image detailIcon;
    [SerializeField] private TMP_Text detailDescription;
    [SerializeField] private UpgradePipsWidget detailPips;
    [SerializeField] private TMP_Text detailLevel;
    [SerializeField] private TMP_Text currentValue;
    [SerializeField] private TMP_Text nextValue;

    [Header("Detail panel - per-level lines (upgrades with Level Infos, e.g. Exploration)")]
    [SerializeField, Tooltip("The side-by-side CURRENT / NEXT value cards; hidden for upgrades with per-level info.")]
    private GameObject currentNextBlock;
    [SerializeField, Tooltip("The stacked CURRENT / NEXT level cards (title + description); shown instead for upgrades with per-level info.")]
    private GameObject levelInfoBlock;
    [SerializeField] private TMP_Text currentLevelHeader;
    [SerializeField] private TMP_Text currentLevelText;
    [SerializeField, Tooltip("Small accent-coloured caption over the next level's title (NEXT · LV 3).")]
    private TMP_Text nextLevelLabel;
    [SerializeField] private TMP_Text nextLevelHeader;
    [SerializeField] private TMP_Text nextLevelText;
    [SerializeField, Tooltip("The NEXT card's background, tinted with the upgrade's accent colour.")]
    private Image nextLevelBackground;
    [SerializeField, Tooltip("The NEXT card's accent bar.")]
    private Image nextLevelAccent;
    [SerializeField, Range(0f, 1f)] private float nextLevelTint = 0.14f;
    [SerializeField] private Button upgradeButton;
    [SerializeField] private TMP_Text upgradeCost;
    [SerializeField] private TMP_Text upgradeLabel;
    [SerializeField] private Color nextColor = new Color(0.1f, 0.65f, 0.25f);
    [SerializeField, Tooltip("UPGRADE button tint once the upgrade is maxed. The button stays focusable (see RefreshDetail).")]
    private Color maxedButtonColor = new Color(0.45f, 0.47f, 0.53f);
    [SerializeField, Tooltip("UPGRADE button tint while the wallet can't pay the next level.")]
    private Color cantAffordButtonColor = new Color(0.55f, 0.3f, 0.35f);

    [Header("Locked lines (unlock requirement not met)")]
    [SerializeField, Tooltip("The UPGRADE button's currency icon - swapped for Lock Sprite while the line is locked.")]
    private Image upgradeCostIcon;
    [SerializeField] private Sprite lockSprite;
    [SerializeField] private Color lockReasonColor = new Color(0.992f, 0.224f, 0.443f);

    private readonly List<UpgradeRowWidget>[] rows =
        { new List<UpgradeRowWidget>(), new List<UpgradeRowWidget>() };
    private Color upgradeButtonColor;
    private Sprite upgradeCostIconSprite;
    private UpgradeRowWidget selected;
    private int category;
    private bool built;

    protected override void Awake()
    {
        base.Awake();
        upgradeButton.onClick.AddListener(OnUpgradePressed);
        upgradeButtonColor = upgradeButton.targetGraphic.color;
        upgradeCostIconSprite = upgradeCostIcon != null ? upgradeCostIcon.sprite : null;
        categoryTabs.Changed += OnCategoryChanged;
    }

    private void Start()
    {
        if (!built)
            Build();
    }

    private void OnEnable()
    {
        MetaProgressionSave.Changed += OnSaveChanged;
    }

    private void OnDisable()
    {
        MetaProgressionSave.Changed -= OnSaveChanged;
    }

    private void OnSaveChanged()
    {
        if (!built)
            return;

        foreach (List<UpgradeRowWidget> list in rows)
        {
            foreach (UpgradeRowWidget row in list)
                row.SetLevel(GetLevel(row.Data), MetaProgressionSave.GetLockReason(row.Data));
        }

        RefreshSubtitle();
        if (selected != null)
            RefreshDetail();
    }

    protected override void OnShow()
    {
        if (!built)
            Build();
        else
            OnSaveChanged(); // the save may have changed while hidden (run result, cheat)
    }

    protected override void OnHide()
    {
    }

    // The row being viewed in the active category.
    public override Selectable DefaultFocus => selected != null ? selected.GetComponent<Selectable>() : null;

    private static int GetLevel(PermanentUpgradeData data)
    {
        return Mathf.Clamp(MetaProgressionSave.GetLevel(data.id), 0, data.maxLevel);
    }

    private void Build()
    {
        if (rowTemplate == null || listRoots == null || listRoots.Length < rows.Length)
        {
            LogHelper.Warn(LogTag, "Row template / list roots not assigned - Upgrades list stays empty.");
            return;
        }

        ListTemplateUtility.Hide(rowTemplate);

        for (int c = 0; c < rows.Length; c++)
        {
            int previousGroup = int.MinValue;
            foreach (PermanentUpgradeData data in GameDB.Instance.PermanentUpgrades)
            {
                if (data == null || (int)data.category != c)
                    continue;

                if (previousGroup != int.MinValue && data.group != previousGroup)
                    AddDivider(listRoots[c]);
                previousGroup = data.group;

                UpgradeRowWidget row = ListTemplateUtility.Spawn(rowTemplate, listRoots[c]);
                row.name = "Upgrade_" + data.name;
                row.Bind(data, GetLevel(data));
                row.Clicked += OnRowClicked;
                row.Submitted += OnRowSubmitted;
                rows[c].Add(row);
            }
        }

        built = true;
        OnCategoryChanged(categoryTabs.SelectedIndex >= 0 ? categoryTabs.SelectedIndex : 0);
    }

    private void AddDivider(Transform root)
    {
        var divider = new GameObject("Divider", typeof(RectTransform), typeof(LayoutElement));
        divider.transform.SetParent(root, false);
        divider.GetComponent<LayoutElement>().preferredHeight = 16f;

        var line = new GameObject("Line", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        line.transform.SetParent(divider.transform, false);
        var rect = (RectTransform)line.transform;
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.offsetMin = new Vector2(8f, -1.5f);
        rect.offsetMax = new Vector2(-8f, 1.5f);

        var image = line.GetComponent<Image>();
        image.sprite = dividerSprite;
        image.type = dividerSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = new Color(0.3f, 0.32f, 0.38f, 0.45f);
        image.raycastTarget = false;
    }

    private void OnCategoryChanged(int index)
    {
        category = Mathf.Clamp(index, 0, rows.Length - 1);

        RefreshSubtitle();

        if (!built)
            return;

        if (scrolls != null && category < scrolls.Length && scrolls[category] != null)
            scrolls[category].verticalNormalizedPosition = 1f;

        Select(rows[category].Count > 0 ? rows[category][0] : null);
    }

    // Exploration lines unlock by how many Exploration levels are owned in total, so that count rides along.
    private void RefreshSubtitle()
    {
        if (subtitleText == null || subtitles == null || category >= subtitles.Length)
            return;

        string text = subtitles[category];
        if ((PermanentUpgradeCategory)category == PermanentUpgradeCategory.Exploration)
            text += $"  <b>Exploration upgrades owned: {MetaProgressionSave.CountLevels(PermanentUpgradeCategory.Exploration)}</b>";
        subtitleText.text = text;
    }

    private void OnRowClicked(UpgradeRowWidget row)
    {
        Select(row);
    }

    private void OnRowSubmitted(UpgradeRowWidget row)
    {
        if (infoScope != null)
            infoScope.Enter(row.GetComponent<Selectable>());
    }

    private void Select(UpgradeRowWidget row)
    {
        UpgradeRowWidget previous = selected;
        selected = row;
        foreach (List<UpgradeRowWidget> list in rows)
        {
            foreach (UpgradeRowWidget candidate in list)
                candidate.SetSelected(candidate == row);
        }

        if (row == null)
            return;

        if (scrolls != null && category < scrolls.Length)
            ScrollRectUtility.EnsureVisible(scrolls[category], (RectTransform)row.transform, 24f);

        RefreshDetail();

        // After the panel shows the new item, so the burst rests on what it now displays.
        if (previous != null && row != previous && infoGlitch != null && isActiveAndEnabled)
            infoGlitch.PlayGlitch();
    }

    private void RefreshDetail()
    {
        PermanentUpgradeData data = selected.Data;
        int level = GetLevel(data);
        bool maxed = level >= data.maxLevel;
        string lockReason = maxed ? null : MetaProgressionSave.GetLockReason(data);
        bool locked = lockReason != null;

        detailName.text = data.Name.ToUpperInvariant();
        detailKind.text = data.kindLabel;
        detailKind.color = data.accentColor;
        detailIcon.sprite = data.icon;
        detailIcon.enabled = data.icon != null;
        // Lines without per-level cards have nowhere else to say why they're locked.
        detailDescription.text = locked && !data.HasLevelInfo
            ? $"{data.description}\n<color=#{ColorUtility.ToHtmlStringRGB(lockReasonColor)}>LOCKED - {lockReason}</color>"
            : data.description;
        detailPips.Set(data.maxLevel, level, data.accentColor);
        detailLevel.text = level + " / " + data.maxLevel;
        currentValue.text = data.ValueLabel(level);
        nextValue.text = maxed ? "MAX" : data.ValueLabel(level + 1);
        nextValue.color = maxed ? currentValue.color : nextColor;
        RefreshLevelInfo(data, level, maxed, lockReason);
        upgradeCost.text = maxed || locked ? string.Empty : data.CostFor(level).ToString();
        upgradeLabel.text = maxed ? "MAXED" : locked ? "LOCKED" : "UPGRADE";
        if (upgradeCostIcon != null)
        {
            upgradeCostIcon.sprite = locked && lockSprite != null ? lockSprite : upgradeCostIconSprite;
            upgradeCostIcon.enabled = !maxed;
        }
        // Never interactable = false: UGUI clears the selection of a selectable that goes non-interactable,
        // which would strand gamepad focus on an empty scope. Maxed just greys it out; pressing does nothing.
        upgradeButton.targetGraphic.color = maxed || locked ? maxedButtonColor
            : MetaProgressionSave.CanAfford(data) ? upgradeButtonColor : cantAffordButtonColor;
    }

    // Upgrades with one title + text per level (Exploration lines) show what the owned level does and what the next
    // one adds, in place of the numeric CURRENT / NEXT values.
    private void RefreshLevelInfo(PermanentUpgradeData data, int level, bool maxed, string lockReason)
    {
        bool perLevel = data.HasLevelInfo && levelInfoBlock != null;
        if (currentNextBlock != null)
            currentNextBlock.SetActive(!perLevel);
        if (levelInfoBlock != null)
            levelInfoBlock.SetActive(perLevel);
        if (!perLevel)
            return;

        string muted = ColorUtility.ToHtmlStringRGB(new Color(0.45f, 0.47f, 0.53f));

        if (level <= 0)
        {
            currentLevelHeader.text = $"<size=70%><color=#{muted}>CURRENT</color></size>  LOCKED";
            currentLevelText.text = string.IsNullOrEmpty(data.levelZeroText) ? "Not unlocked yet." : data.levelZeroText;
        }
        else
        {
            PermanentUpgradeLevelInfo owned = data.LevelInfo(level);
            currentLevelHeader.text = $"<size=70%><color=#{muted}>CURRENT · LV {level}</color></size>  {owned.title.ToUpperInvariant()}";
            currentLevelText.text = owned.description;
        }

        if (maxed)
        {
            nextLevelLabel.text = "NEXT";
            nextLevelHeader.text = "MAX";
            nextLevelText.text = "Every level unlocked.";
        }
        else
        {
            PermanentUpgradeLevelInfo next = data.LevelInfo(level + 1);
            nextLevelHeader.text = next.title.ToUpperInvariant();
            if (lockReason != null)
            {
                nextLevelLabel.text = $"LOCKED · LV {level + 1}";
                nextLevelText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(lockReasonColor)}>{lockReason}.</color>\n{next.description}";
            }
            else
            {
                nextLevelLabel.text = $"NEXT · LV {level + 1}";
                nextLevelText.text = next.description;
            }
        }

        Color accentColor = maxed ? new Color(0.45f, 0.47f, 0.53f) : lockReason != null ? lockReasonColor : data.accentColor;
        nextLevelLabel.color = accentColor;
        if (nextLevelAccent != null)
            nextLevelAccent.color = accentColor;
        if (nextLevelBackground != null)
            nextLevelBackground.color = Color.Lerp(new Color(0.93f, 0.94f, 0.96f), accentColor, maxed ? 0f : nextLevelTint);
    }

    private void OnUpgradePressed()
    {
        if (selected == null)
            return;

        // A press that can't buy says why (the button stays focusable for gamepad, see RefreshDetail).
        PermanentUpgradeData data = selected.Data;
        int level = GetLevel(data);
        if (level >= data.maxLevel)
            return;

        string lockReason = MetaProgressionSave.GetLockReason(data);
        if (lockReason != null)
        {
            ToastManager.Instance?.Show($"Locked - {lockReason}");
            return;
        }

        if (!MetaProgressionSave.CanAfford(data))
        {
            int missing = data.CostFor(level) - MetaProgressionSave.RiftShards;
            ToastManager.Instance?.Show($"Not enough Rift Shards - you need {missing} more");
            return;
        }

        // Rows and the detail panel refresh from MetaProgressionSave.Changed.
        MetaProgressionSave.TryPurchase(data);
    }
}

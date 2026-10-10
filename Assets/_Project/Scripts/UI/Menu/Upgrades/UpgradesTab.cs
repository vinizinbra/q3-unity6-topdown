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
/// UI only: levels live in memory (seeded from each asset's mockLevel) and UPGRADE just raises the level -
/// no wallet, no save file yet. Replace <see cref="GetLevel"/> / <see cref="TryUpgrade"/> with the real
/// progress class once it exists.
/// </summary>
public class UpgradesTab : TabContent
{
    private const string LogTag = "Upgrades";

    [Header("Data")]
    [SerializeField] private List<PermanentUpgradeData> upgrades = new List<PermanentUpgradeData>();

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
    [SerializeField] private Button upgradeButton;
    [SerializeField] private TMP_Text upgradeCost;
    [SerializeField] private TMP_Text upgradeLabel;
    [SerializeField] private Color nextColor = new Color(0.1f, 0.65f, 0.25f);
    [SerializeField, Tooltip("UPGRADE button tint once the upgrade is maxed. The button stays focusable (see RefreshDetail).")]
    private Color maxedButtonColor = new Color(0.45f, 0.47f, 0.53f);

    private readonly Dictionary<PermanentUpgradeData, int> levels = new Dictionary<PermanentUpgradeData, int>();
    private readonly List<UpgradeRowWidget>[] rows =
        { new List<UpgradeRowWidget>(), new List<UpgradeRowWidget>() };
    private Color upgradeButtonColor;
    private UpgradeRowWidget selected;
    private int category;
    private bool built;

    protected override void Awake()
    {
        base.Awake();
        upgradeButton.onClick.AddListener(OnUpgradePressed);
        upgradeButtonColor = upgradeButton.targetGraphic.color;
        categoryTabs.Changed += OnCategoryChanged;
    }

    private void Start()
    {
        if (!built)
            Build();
    }

    protected override void OnShow()
    {
        if (!built)
            Build();
    }

    protected override void OnHide()
    {
    }

    // The row being viewed in the active category.
    public override Selectable DefaultFocus => selected != null ? selected.GetComponent<Selectable>() : null;

    // TODO: read from the player's saved progress once that class exists.
    private int GetLevel(PermanentUpgradeData data)
    {
        if (!levels.TryGetValue(data, out int level))
        {
            level = Mathf.Clamp(data.mockLevel, 0, data.maxLevel);
            levels[data] = level;
        }

        return level;
    }

    // TODO: spend currency + persist. For now it only raises the in-memory level.
    private bool TryUpgrade(PermanentUpgradeData data)
    {
        int level = GetLevel(data);
        if (level >= data.maxLevel)
            return false;

        levels[data] = level + 1;
        return true;
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
            foreach (PermanentUpgradeData data in upgrades)
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

        if (subtitleText != null && subtitles != null && category < subtitles.Length)
            subtitleText.text = subtitles[category];

        if (!built)
            return;

        if (scrolls != null && category < scrolls.Length && scrolls[category] != null)
            scrolls[category].verticalNormalizedPosition = 1f;

        Select(rows[category].Count > 0 ? rows[category][0] : null);
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

        detailName.text = data.Name.ToUpperInvariant();
        detailKind.text = data.kindLabel;
        detailKind.color = data.accentColor;
        detailIcon.sprite = data.icon;
        detailIcon.enabled = data.icon != null;
        detailDescription.text = data.description;
        detailPips.Set(data.maxLevel, level, data.accentColor);
        detailLevel.text = level + " / " + data.maxLevel;
        currentValue.text = data.ValueLabel(level);
        nextValue.text = maxed ? "MAX" : data.ValueLabel(level + 1);
        nextValue.color = maxed ? currentValue.color : nextColor;
        upgradeCost.text = maxed ? string.Empty : data.CostFor(level).ToString();
        upgradeLabel.text = maxed ? "MAXED" : "UPGRADE";
        // Never interactable = false: UGUI clears the selection of a selectable that goes non-interactable,
        // which would strand gamepad focus on an empty scope. Maxed just greys it out; pressing does nothing.
        upgradeButton.targetGraphic.color = maxed ? maxedButtonColor : upgradeButtonColor;
    }

    private void OnUpgradePressed()
    {
        if (selected == null || !TryUpgrade(selected.Data))
            return;

        selected.SetLevel(GetLevel(selected.Data));
        RefreshDetail();
    }
}

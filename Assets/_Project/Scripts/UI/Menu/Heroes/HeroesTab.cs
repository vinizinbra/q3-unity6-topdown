using System;
using System.Collections.Generic;
using Quantum;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum HeroListState
{
    Unlocked,
    Discovered,
    Locked
}

[Serializable]
public class HeroSkillEntryRefs
{
    public Image icon;
    public TMP_Text title;
    public TMP_Text description;
    public TMP_Text cooldown;
}

/// <summary>
/// Heroes tab content: hero list on the left (one <see cref="HeroRowWidget"/> instantiated per
/// <see cref="CharacterCatalog"/> entry), selected hero's info panel on the right, all read from the
/// hero's <see cref="CharacterData"/>.
///
/// Not backed by real data yet (placeholders below): list status line / mastery numbers (no per-hero
/// progression source exists yet) and the lock state (every hero reads as Unlocked).
/// </summary>
public class HeroesTab : TabContent
{
    private const string LogTag = "Heroes";

    [Header("List")]
    [SerializeField] private HeroRowWidget rowPrefab;
    [SerializeField] private Transform listRoot;

    [Header("Preview")]
    [Tooltip("The same lobby rig the main menu shows. Must have Follow Local Selection OFF - this tab drives it with the hero being viewed, which isn't necessarily the equipped one.")]
    [SerializeField] private CharacterPreviewWidget preview;

    [Header("Navigation")]
    [Tooltip("The info panel level: Submit on a hero row steps into it, Cancel steps back out.")]
    [SerializeField] private FocusScopeWidget infoScope;

    [Header("Info panel")]
    [SerializeField] private TMP_Text heroNameText;
    [SerializeField] private TMP_Text heroTitleText;
    [SerializeField] private TMP_Text heroDescriptionText;
    [SerializeField] private TabStripWidget infoTabs;
    [SerializeField] private HeroSkillEntryRefs skillEntry;
    [SerializeField] private HeroSkillEntryRefs passiveEntry;
    [SerializeField] private TMP_Text masteryLabel;
    [SerializeField] private TMP_Text masteryCount;
    [SerializeField] private RectTransform masteryFill;
    [SerializeField] private TMP_Text masteryPageText;

    [Header("Select button")]
    [SerializeField] private UnityEngine.UI.Button selectButton;
    [SerializeField] private Image selectBackground;
    [SerializeField] private TMP_Text selectLabel;
    [SerializeField] private Color selectColor = new Color(0.992f, 0.224f, 0.443f);
    [SerializeField] private Color selectedColor = new Color(0.992f, 0.224f, 0.443f);
    [SerializeField] private Color lockedColor = new Color(0.45f, 0.47f, 0.53f);

    [Header("Placeholders (no data source yet)")]
    [SerializeField] private int placeholderLevel = 1;
    [SerializeField] private int placeholderMasteryCurrent;
    [SerializeField] private int placeholderMasteryMax = 600;

    private readonly List<HeroRowWidget> rows = new List<HeroRowWidget>();
    private readonly List<string> ids = new List<string>();
    private CharacterCatalog catalog;
    private int viewedIndex = -1;
    private bool built;

    protected override void Awake()
    {
        base.Awake();
        selectButton.onClick.AddListener(OnSelectPressed);
    }

    private void OnEnable()
    {
        if (PartyManager.Instance != null)
            PartyManager.Instance.OnLocalCharacterChanged += OnLocalCharacterChanged;
    }

    private void OnDisable()
    {
        if (PartyManager.Instance != null)
            PartyManager.Instance.OnLocalCharacterChanged -= OnLocalCharacterChanged;
    }

    private void Start()
    {
        // PartyManager.Instance is assigned in its own Awake, so it may be absent in this tab's OnEnable.
        if (PartyManager.Instance != null)
        {
            PartyManager.Instance.OnLocalCharacterChanged -= OnLocalCharacterChanged;
            PartyManager.Instance.OnLocalCharacterChanged += OnLocalCharacterChanged;
        }

        if (!built)
            BuildList();
    }

    // The row of the hero being viewed (the equipped one on first open).
    public override Selectable DefaultFocus
    {
        get
        {
            if (!built || rows.Count == 0)
                return null;
            return rows[Mathf.Clamp(viewedIndex, 0, rows.Count - 1)].GetComponent<Selectable>();
        }
    }

    protected override void OnShow()
    {
        if (!built)
            BuildList();
    }

    protected override void OnHide()
    {
    }

    private string EquippedId
    {
        get
        {
            string id = PartyManager.Instance != null ? PartyManager.Instance.LocalCharacterId : null;
            if (!string.IsNullOrEmpty(id) && ids.Contains(id))
                return id;
            return ids.Count > 0 ? ids[0] : null;
        }
    }

    private void BuildList()
    {
        catalog = PartyManager.Instance != null ? PartyManager.Instance.characterCatalog : null;
        if (catalog == null || catalog.characters == null || catalog.characters.Length == 0)
        {
            LogHelper.Warn(LogTag, "No CharacterCatalog on PartyManager - hero list stays empty.");
            return;
        }

        foreach (HeroRowWidget row in rows)
        {
            row.Clicked -= OnRowClicked;
            row.Submitted -= OnRowSubmitted;
            Destroy(row.gameObject);
        }
        rows.Clear();
        ids.Clear();

        foreach (CharacterCatalog.Entry entry in catalog.characters)
        {
            catalog.TryResolveCharacterData(entry.id, out CharacterData data);

            HeroRowWidget row = Instantiate(rowPrefab, listRoot);
            row.name = "HeroRow_" + entry.id;
            row.Bind(rows.Count,
                HeroName(entry, data),
                "LV " + placeholderLevel,
                data != null && data.UIHead != null ? data.UIHead : catalog.ResolveIconSprite(entry.id),
                data != null ? data.RingColor : Color.white,
                HeroListState.Unlocked);
            row.Clicked += OnRowClicked;
            row.Submitted += OnRowSubmitted;

            rows.Add(row);
            ids.Add(entry.id);
        }

        built = true;
        View(Mathf.Max(0, ids.IndexOf(EquippedId)));
    }

    private static string HeroName(CharacterCatalog.Entry entry, CharacterData data)
    {
        if (data != null && !string.IsNullOrEmpty(data.DisplayName))
            return data.DisplayName;
        return string.IsNullOrEmpty(entry.displayName) ? entry.id : entry.displayName;
    }

    private void OnRowSubmitted(HeroRowWidget row)
    {
        if (infoScope != null)
            infoScope.Enter(row.GetComponent<Selectable>());
    }

    private void OnRowClicked(HeroRowWidget row)
    {
        View(row.Index);
    }

    private void OnLocalCharacterChanged(string id)
    {
        if (built && viewedIndex >= 0)
            RefreshSelectButton();
    }

    private void OnSelectPressed()
    {
        if (!built || viewedIndex < 0 || PartyManager.Instance == null)
            return;

        PartyManager.Instance.SetLocalCharacter(ids[viewedIndex]);
    }

    private void View(int index)
    {
        if (index < 0 || index >= ids.Count)
            return;

        viewedIndex = index;
        string id = ids[index];
        CharacterCatalog.Entry entry = catalog.characters[index];
        catalog.TryResolveCharacterData(id, out CharacterData data);

        for (int i = 0; i < rows.Count; i++)
            rows[i].SetSelected(i == index);

        if (preview != null)
            preview.ShowCharacterId(id);

        heroNameText.text = HeroName(entry, data);
        string title = data != null ? data.Title : null;
        heroTitleText.text = title;
        heroTitleText.gameObject.SetActive(!string.IsNullOrEmpty(title));
        heroDescriptionText.text = data != null ? data.Description : null;

        FillSkill(data);
        FillPassive(data);

        masteryLabel.text = "MASTERY  LV " + placeholderLevel;
        masteryCount.text = placeholderMasteryCurrent + " / " + placeholderMasteryMax;
        masteryPageText.text = "Mastery tree for " + heroNameText.text + " (placeholder)";
        float fill = placeholderMasteryMax > 0 ? Mathf.Clamp01((float)placeholderMasteryCurrent / placeholderMasteryMax) : 0f;
        masteryFill.anchorMax = new Vector2(fill, 1f);

        RefreshSelectButton();
    }

    // The hero's base skill (Dash is the shared generic base and deliberately not listed).
    private void FillSkill(CharacterData data)
    {
        SkillData skill = null;
        if (data != null)
            QuantumUnityDB.TryGetGlobalAsset(data.HeroSkill, out skill);

        skillEntry.icon.transform.parent.gameObject.SetActive(skill != null);
        skillEntry.title.gameObject.SetActive(skill != null);
        skillEntry.description.gameObject.SetActive(skill != null);
        skillEntry.cooldown.gameObject.SetActive(skill != null);
        if (skill == null)
            return;

        SetIcon(skillEntry.icon, skill.Icon);
        skillEntry.title.text = string.IsNullOrEmpty(skill.Name) ? skill.name : skill.Name;
        skillEntry.description.text = skill.GetFormattedDescription();
        skillEntry.cooldown.text = skill.Cooldown.AsFloat.ToString("0.#") + "s";
    }

    private void FillPassive(CharacterData data)
    {
        PassiveData passive = null;
        if (data != null)
            QuantumUnityDB.TryGetGlobalAsset(data.Passive, out passive);

        passiveEntry.icon.transform.parent.gameObject.SetActive(passive != null);
        passiveEntry.title.gameObject.SetActive(passive != null);
        passiveEntry.description.gameObject.SetActive(passive != null);
        passiveEntry.cooldown.gameObject.SetActive(passive != null);
        if (passive == null)
            return;

        // The right-hand slot that shows a skill's cooldown carries the kind tag instead - a passive has none.
        passiveEntry.cooldown.text = "PASSIVE";

        SetIcon(passiveEntry.icon, passive.Icon);
        passiveEntry.title.text = string.IsNullOrEmpty(passive.DisplayName) ? passive.name : passive.DisplayName;
        passiveEntry.description.text = passive.Description;
    }

    // No authored icon keeps the prefab's placeholder glyph (and its tint).
    private static void SetIcon(Image image, Sprite sprite)
    {
        if (sprite == null)
            return;
        image.sprite = sprite;
        image.color = Color.white;
    }

    private void RefreshSelectButton()
    {
        bool equipped = ids[viewedIndex] == EquippedId;
        selectButton.interactable = !equipped;
        selectLabel.text = equipped ? "SELECTED" : "SELECT";
        selectBackground.color = equipped ? selectedColor : selectColor;
    }
}

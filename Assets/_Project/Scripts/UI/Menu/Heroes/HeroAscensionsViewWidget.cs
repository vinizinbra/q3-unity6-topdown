using System;
using System.Collections.Generic;
using Quantum;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ASCENSIONS page of the Heroes tab: the viewed hero's whole Ascension codex, built from its CharacterData (see
/// <see cref="HeroAscensionCatalog"/>) - one section per kind (Skill / Passive / Dash) with a row per line, and the
/// selected line's detail with one row per rank (LV 1..3) and its description. Discovery / lock / owned ranks come from
/// <see cref="HeroAscensionProgress"/> (a stub for now: everything reads as Discovered). Sections and rows are cloned
/// from in-scene templates (see ListTemplateUtility).
/// </summary>
public class HeroAscensionsViewWidget : MonoBehaviour
{
    [Serializable]
    public struct LevelRow
    {
        public GameObject root;
        [Tooltip("The LV badge - tinted with the kind colour once that rank is owned (muted otherwise).")]
        public Image badge;
        public TMP_Text label;
        public TMP_Text text;
    }

    [Header("Templates (placed in the scene, cloned, hidden themselves)")]
    [SerializeField, Tooltip("A section placed in the list.")] private AscensionSectionWidget sectionTemplate;
    [SerializeField, Tooltip("A row inside the section template's rows root.")] private AscensionRowWidget rowTemplate;
    [SerializeField] private Transform listRoot;
    [SerializeField] private ScrollRect listScroll;

    [Header("Header")]
    [SerializeField, Tooltip("\"3 / 10\" - discovered over total.")] private TMP_Text discoveredText;
    [SerializeField, Tooltip("Optional line under the title; {0} is the hero's name.")] private TMP_Text subtitleText;
    [SerializeField, TextArea(1, 3)] private string subtitleFormat = "Discover and unlock powerful transformations for {0}. Each ascension has 3 tiers of upgrades.";

    [Header("Kind colours")]
    [SerializeField] private Color skillColor = new Color(0.992f, 0.224f, 0.443f);
    [SerializeField] private Color passiveColor = new Color(1f, 0.45f, 0.1f);
    [SerializeField] private Color dashColor = new Color(0.15f, 0.55f, 1f);
    [SerializeField] private Color mutedColor = new Color(0.8f, 0.82f, 0.86f);

    [Header("Detail")]
    [SerializeField, Tooltip("The detail card's title strip - tinted with the kind colour.")] private Image detailHeader;
    [SerializeField] private TMP_Text detailNumber;
    [SerializeField] private TMP_Text detailName;
    [SerializeField] private Image detailIcon;
    [SerializeField] private GameObject detailUnknownIcon;
    [SerializeField] private GameObject detailLockedIcon;
    [SerializeField] private TMP_Text detailDescription;
    [SerializeField, Tooltip("One per rank, LV 1 first.")] private LevelRow[] levelRows;
    [SerializeField, Tooltip("Optional: the levels' layout root (rows size to their text). With the card below set, the card's height follows it.")]
    private RectTransform levelsRoot;
    [SerializeField, Tooltip("Optional: the detail card, resized to end just under the levels.")]
    private RectTransform detailCard;
    [SerializeField] private float detailBottomPadding = 22f;

    [Header("Glitch")]
    [SerializeField] private GlitchWidget infoGlitch;

    private readonly List<AscensionRowWidget> rows = new List<AscensionRowWidget>();
    private AscensionRowWidget selected;
    private string heroId;

    public Selectable DefaultFocus => selected != null ? selected.GetComponent<Selectable>() : null;

    public Color ColorOf(AscensionKind kind) => kind switch
    {
        AscensionKind.Skill => skillColor,
        AscensionKind.Passive => passiveColor,
        _ => dashColor
    };

    private static string Title(AscensionKind kind) => kind switch
    {
        AscensionKind.Skill => "Skill",
        AscensionKind.Passive => "Passive",
        _ => "Dash"
    };

    /// <summary>Rebuilds the codex for this hero.</summary>
    public void Show(string id, CharacterData hero)
    {
        heroId = id;
        ListTemplateUtility.Hide(sectionTemplate, rowTemplate);
        ListTemplateUtility.Clear(listRoot, sectionTemplate);
        rows.Clear();
        selected = null;

        if (subtitleText != null)
            subtitleText.text = string.Format(subtitleFormat, hero != null && !string.IsNullOrEmpty(hero.DisplayName) ? hero.DisplayName : id);

        List<AscensionLine> lines = HeroAscensionCatalog.Lines(hero);
        int number = 0, discoveredTotal = 0;
        foreach (AscensionKind kind in (AscensionKind[])Enum.GetValues(typeof(AscensionKind)))
        {
            AscensionSectionWidget section = null;
            int discovered = 0, total = 0;
            foreach (AscensionLine line in lines)
            {
                if (line.Kind != kind)
                    continue;

                if (section == null)
                {
                    section = ListTemplateUtility.Spawn(sectionTemplate, listRoot);
                    ListTemplateUtility.Clear(section.RowsRoot); // the clone's copy of the row template
                    section.name = "Section_" + kind;
                    section.Bind(Title(kind), ColorOf(kind));
                }

                AscensionState state = HeroAscensionProgress.GetState(id, line, total, out string requirement, out int levels);
                AscensionRowWidget row = ListTemplateUtility.Spawn(rowTemplate, section.RowsRoot);
                row.name = "Ascension_" + line.Upgrade.name;
                row.Bind(++number, line, state, levels, requirement, ColorOf(kind));
                row.Clicked += Select;
                rows.Add(row);

                total++;
                discovered += state == AscensionState.Discovered ? 1 : 0;
            }

            if (section != null)
                section.SetCount(discovered, total);
            discoveredTotal += discovered;
        }

        if (discoveredText != null)
            discoveredText.text = discoveredTotal + " / " + rows.Count;

        if (listScroll != null)
            listScroll.verticalNormalizedPosition = 1f;

        Select(rows.Count > 0 ? rows[0] : null);
    }

    private void Select(AscensionRowWidget row)
    {
        AscensionRowWidget previous = selected;
        selected = row;
        foreach (AscensionRowWidget r in rows)
            r.SetSelected(r == row);

        if (row == null)
            return;

        if (listScroll != null)
            ScrollRectUtility.EnsureVisible(listScroll, (RectTransform)row.transform, 24f);

        RefreshDetail(row);

        if (previous != null && row != previous && infoGlitch != null && isActiveAndEnabled)
            infoGlitch.PlayGlitch();
    }

    private void RefreshDetail(AscensionRowWidget row)
    {
        AscensionLine line = row.Line;
        Color color = ColorOf(line.Kind);
        bool discovered = row.State == AscensionState.Discovered;

        if (detailHeader != null)
            detailHeader.color = color;
        detailNumber.text = row.Number.ToString("00");
        detailName.text = discovered ? line.Name : row.State == AscensionState.Locked ? "Locked" : "???";

        detailIcon.gameObject.SetActive(discovered);
        if (discovered)
            detailIcon.sprite = line.Upgrade.Icon;
        if (detailUnknownIcon != null)
            detailUnknownIcon.SetActive(row.State == AscensionState.Undiscovered);
        if (detailLockedIcon != null)
            detailLockedIcon.SetActive(row.State == AscensionState.Locked);

        detailDescription.text = row.State switch
        {
            AscensionState.Discovered => line.Upgrade.GetDescription(),
            AscensionState.Locked => string.IsNullOrEmpty(row.LockedRequirement) ? "Not unlocked yet." : row.LockedRequirement + " to unlock.",
            _ => "Not discovered yet. Find it in a run to reveal it."
        };

        int ranks = line.MaxRank;
        for (int i = 0; i < levelRows.Length; i++)
        {
            LevelRow level = levelRows[i];
            bool used = i < ranks;
            level.root.SetActive(used);
            if (!used)
                continue;

            int rank = i + 1;
            level.label.text = "LV " + rank;
            // Each level is discovered on its own: a found level shows its text in the kind colour, the others stay ???.
            bool known = rank <= row.DiscoveredLevels;
            level.badge.color = known ? color : mutedColor;
            level.text.text = known ? line.RankDescription(rank) : "???";
        }

        FitDetailToContent();
    }

    // The level rows size to their text, so the card's height follows them instead of being fixed. Measured again a
    // frame later (and when the page shows) since text sizes aren't final until TMP has built its mesh.
    private void FitDetailToContent()
    {
        if (levelsRoot == null || detailCard == null)
            return;

        Fit();
        if (isActiveAndEnabled)
            StartCoroutine(FitNextFrame());
    }

    private void OnEnable()
    {
        if (selected != null && levelsRoot != null && detailCard != null)
            StartCoroutine(FitNextFrame());
    }

    private System.Collections.IEnumerator FitNextFrame()
    {
        yield return null;
        Fit();
    }

    private void Fit()
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(levelsRoot);
        float levelsTop = -levelsRoot.anchoredPosition.y;
        float height = levelsTop + levelsRoot.rect.height + detailBottomPadding;
        detailCard.offsetMin = new Vector2(detailCard.offsetMin.x, detailCard.offsetMax.y - height);
    }
}

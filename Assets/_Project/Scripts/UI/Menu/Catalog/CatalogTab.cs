using System.Collections.Generic;
using Quantum;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Catalog tab content: everything the player can run into - Enemies, Bosses, Rift Mutations - as
/// grids of tiles grouped into sections (faction / rarity), with the selected entry's details on the right.
/// The Enemies page has a world filter and a world banner. Tiles come from <see cref="CatalogEntryData"/>
/// assets; one tile / section / chip prefab is instantiated per entry / group / world.
///
/// UI only: discovery and the encountered/defeated counters are mocked from each entry asset.
/// </summary>
public class CatalogTab : TabContent
{
    private const string LogTag = "Catalog";
    private const int KindCount = 3;

    private class Section
    {
        public CatalogKind kind;
        public int world;
        public string group;
        public CatalogSectionWidget widget;
        public readonly List<CatalogTileWidget> tiles = new List<CatalogTileWidget>();
    }

    [Header("Data")]
    [SerializeField] private List<CatalogEntryData> entries = new List<CatalogEntryData>();

    [Header("Templates (placed in the scene, cloned per item, hidden themselves)")]
    [SerializeField, Tooltip("A section placed in the first page's list.")] private CatalogSectionWidget sectionTemplate;
    [SerializeField, Tooltip("A tile inside the section template's grid.")] private CatalogTileWidget tileTemplate;
    [SerializeField, Tooltip("Bosses get big key-art tiles: a boss tile inside the section template's grid.")] private CatalogTileWidget bossTileTemplate;
    [SerializeField] private Vector2 bossCellSize = new Vector2(320f, 290f);
    [SerializeField, Tooltip("A chip placed in the Enemies page chip row.")] private FilterChipWidget chipTemplate;

    [Header("Pages (index = CatalogKind)")]
    [SerializeField] private Transform[] listRoots;
    [SerializeField] private ScrollRect[] scrolls;
    [SerializeField] private TabStripWidget kindTabs;
    [SerializeField] private TMP_Text discoveredText;

    [Header("World filter (Enemies page)")]
    [SerializeField] private Transform chipRoot;
    [SerializeField, Tooltip("The same world filter, repeated on the Bosses page.")] private Transform bossChipRoot;
    [SerializeField] private GameObject banner;
    [SerializeField] private TMP_Text bannerName;
    [SerializeField] private TMP_Text bannerCount;
    [SerializeField] private Image bannerIcon;

    [Header("Glitch")]
    [Tooltip("Plays a short glitch burst on the info panel whenever the viewed item changes.")]
    [SerializeField] private GlitchWidget infoGlitch;

    [Header("Detail panel")]
    [SerializeField] private TMP_Text detailNumber;
    [SerializeField] private TMP_Text detailName;
    [SerializeField] private TMP_Text detailSubtitle;
    [SerializeField] private Image detailIcon;
    [SerializeField] private TMP_Text detailDescription;
    [SerializeField] private TMP_Text[] statLabels;
    [SerializeField] private TMP_Text[] statValues;

    private readonly List<Section> sections = new List<Section>();
    private readonly List<FilterChipWidget> chips = new List<FilterChipWidget>();
    private CatalogTileWidget selected;
    private int kind;
    private int worldFilter = -1;
    private bool built;

    protected override void Awake()
    {
        base.Awake();
        kindTabs.Changed += OnKindChanged;
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

    // The tile being viewed on the active page.
    public override Selectable DefaultFocus => selected != null ? selected.GetComponent<Selectable>() : null;

    private static string WorldName(int index)
    {
        WorldCatalog catalog = WorldCatalog.Instance;
        WorldDefinition world = catalog != null ? catalog.Get(index) : null;
        return world != null && !string.IsNullOrEmpty(world.DisplayName) ? world.DisplayName : null;
    }

    private void Build()
    {
        if (tileTemplate == null || sectionTemplate == null || listRoots == null || listRoots.Length < KindCount)
        {
            LogHelper.Warn(LogTag, "Templates / list roots not assigned - Catalog stays empty.");
            return;
        }

        ListTemplateUtility.Hide(sectionTemplate, tileTemplate, bossTileTemplate, chipTemplate);

        for (int k = 0; k < KindCount; k++)
        {
            int number = 0;
            foreach (CatalogEntryData entry in EntriesOf((CatalogKind)k))
            {
                Section section = FindOrCreateSection(entry, k);
                CatalogTileWidget template = entry.kind == CatalogKind.Boss && bossTileTemplate != null ? bossTileTemplate : tileTemplate;
                CatalogTileWidget tile = ListTemplateUtility.Spawn(template, section.widget.GridRoot);
                tile.name = "Entry_" + entry.name;
                tile.Bind(entry, ++number, entry.mockDiscovered, entry.kind == CatalogKind.Boss ? WorldName(entry.worldIndex) : null);
                tile.Clicked += OnTileClicked;
                section.tiles.Add(tile);
            }
        }

        foreach (Section section in sections)
        {
            int found = 0;
            foreach (CatalogTileWidget tile in section.tiles)
                found += tile.Discovered ? 1 : 0;
            section.widget.SetCount(found, section.tiles.Count);
        }

        BuildWorldChips();
        built = true;
        RefreshDiscoveredTotal();
        ApplyWorldFilter(Mathf.Max(0, FirstWorldWithEnemies()));
        OnKindChanged(kindTabs.SelectedIndex >= 0 ? kindTabs.SelectedIndex : 0);
    }

    // List order for bosses and mutations. Enemies are ordered from their own data instead: world, then the
    // faction's SortOrder, then tier (Filler first, Elite last), then name.
    private IEnumerable<CatalogEntryData> EntriesOf(CatalogKind of)
    {
        var result = new List<CatalogEntryData>();
        foreach (CatalogEntryData entry in entries)
        {
            if (entry != null && entry.kind == of)
                result.Add(entry);
        }

        if (of == CatalogKind.Enemy)
            result.Sort(CompareEnemies);

        return result;
    }

    private static int CompareEnemies(CatalogEntryData a, CatalogEntryData b)
    {
        int result = a.worldIndex.CompareTo(b.worldIndex);
        if (result != 0)
            return result;

        result = FactionOrder(a).CompareTo(FactionOrder(b));
        if (result != 0)
            return result;

        result = string.CompareOrdinal(a.ResolvedGroup, b.ResolvedGroup);
        if (result != 0)
            return result;

        result = TierOrder(a).CompareTo(TierOrder(b));
        return result != 0 ? result : string.CompareOrdinal(a.ResolvedName, b.ResolvedName);
    }

    private static int FactionOrder(CatalogEntryData entry)
    {
        return entry.source is EnemyDataAsset enemy && enemy.Faction != null ? enemy.Faction.SortOrder : int.MaxValue;
    }

    private static int TierOrder(CatalogEntryData entry)
    {
        return entry.source is EnemyDataAsset enemy ? (int)enemy.Tier : int.MaxValue;
    }

    private Section FindOrCreateSection(CatalogEntryData entry, int k)
    {
        // Only the Enemies page has a section per world (and a banner); bosses and mutations are one flat list
        // whose tiles the world filter shows or hides.
        int sectionWorld = entry.kind == CatalogKind.Enemy ? entry.worldIndex : -1;
        foreach (Section existing in sections)
        {
            if (existing.kind == entry.kind && existing.world == sectionWorld && existing.group == entry.ResolvedGroup)
                return existing;
        }

        var section = new Section { kind = entry.kind, world = sectionWorld, group = entry.ResolvedGroup };
        section.widget = ListTemplateUtility.Spawn(sectionTemplate, listRoots[k]);
        // The clone carries copies of the tile templates that live in the template's grid.
        ListTemplateUtility.Clear(section.widget.GridRoot);
        if (entry.kind == CatalogKind.Boss)
            section.widget.GridRoot.GetComponent<GridLayoutGroup>().cellSize = bossCellSize;
        section.widget.name = "Section_" + entry.ResolvedGroup;
        section.widget.Bind(entry.ResolvedGroup, entry.ResolvedGroupColor);
        sections.Add(section);
        return section;
    }

    private int FirstWorldWithEnemies()
    {
        foreach (Section section in sections)
        {
            if (section.kind == CatalogKind.Enemy && section.world >= 0)
                return section.world;
        }

        return -1;
    }

    private void BuildWorldChips()
    {
        if (chipTemplate == null)
            return;

        foreach (Transform root in new[] { chipRoot, bossChipRoot })
        {
            if (root == null)
                continue;

            AddChip(root, "ALL", -1);
            WorldCatalog catalog = WorldCatalog.Instance;
            for (int i = 0; catalog != null && i < catalog.Worlds.Count; i++)
            {
                string name = WorldName(i);
                if (name != null)
                    AddChip(root, name.ToUpperInvariant(), i);
            }
        }
    }

    private void AddChip(Transform root, string text, int id)
    {
        FilterChipWidget chip = ListTemplateUtility.Spawn(chipTemplate, root);
        chip.name = "Chip_" + text;
        chip.Bind(text, id);
        chip.Clicked += OnChipClicked;
        chips.Add(chip);

        // World names are longer than the weapon-family labels the chip was sized for.
        var layout = chip.GetComponent<LayoutElement>();
        if (layout != null)
            layout.preferredWidth = 250f;

        foreach (TMP_Text label in chip.GetComponentsInChildren<TMP_Text>(true))
            label.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private void OnChipClicked(FilterChipWidget chip)
    {
        ApplyWorldFilter(chip.Id);
        if (kind == (int)CatalogKind.Enemy || kind == (int)CatalogKind.Boss)
            Select(FirstVisibleTile((CatalogKind)kind));
    }

    private void ApplyWorldFilter(int world)
    {
        worldFilter = world;
        foreach (FilterChipWidget chip in chips)
            chip.SetSelected(chip.Id == world);

        int found = 0, total = 0;
        foreach (Section section in sections)
        {
            if (section.kind == CatalogKind.Boss)
            {
                foreach (CatalogTileWidget tile in section.tiles)
                    tile.gameObject.SetActive(world < 0 || tile.Entry.worldIndex == world);
                continue;
            }

            if (section.kind != CatalogKind.Enemy)
                continue;

            bool visible = world < 0 || section.world == world;
            section.widget.gameObject.SetActive(visible);

            // With every world listed, the section title says which world it belongs to.
            string worldName = WorldName(section.world);
            section.widget.SetTitle(world < 0 && worldName != null ? worldName + " - " + section.group : section.group);

            if (visible)
            {
                foreach (CatalogTileWidget tile in section.tiles)
                {
                    total++;
                    found += tile.Discovered ? 1 : 0;
                }
            }
        }

        if (banner != null)
        {
            banner.SetActive(world >= 0);
            if (world >= 0)
            {
                bannerName.text = (WorldName(world) ?? "World " + (world + 1)).ToUpperInvariant();
                bannerCount.text = found + " / " + total;
                WorldDefinition def = WorldCatalog.Instance != null ? WorldCatalog.Instance.Get(world) : null;
                bannerIcon.sprite = def != null ? def.Icon : null;
                bannerIcon.enabled = bannerIcon.sprite != null;
            }
        }

        foreach (CatalogKind of in new[] { CatalogKind.Enemy, CatalogKind.Boss })
        {
            if (scrolls != null && scrolls.Length > (int)of && scrolls[(int)of] != null)
                scrolls[(int)of].verticalNormalizedPosition = 1f;
        }
    }

    private void RefreshDiscoveredTotal()
    {
        int found = 0, total = 0;
        foreach (CatalogEntryData entry in entries)
        {
            if (entry == null)
                continue;
            total++;
            found += entry.mockDiscovered ? 1 : 0;
        }

        if (discoveredText != null)
            discoveredText.text = "<color=#FD3971>" + found + "</color> / " + total + " DISCOVERED";
    }

    private void OnKindChanged(int index)
    {
        kind = Mathf.Clamp(index, 0, KindCount - 1);
        if (!built)
            return;

        if (scrolls != null && kind < scrolls.Length && scrolls[kind] != null)
            scrolls[kind].verticalNormalizedPosition = 1f;

        Select(FirstVisibleTile((CatalogKind)kind));
    }

    private CatalogTileWidget FirstVisibleTile(CatalogKind of)
    {
        foreach (Section section in sections)
        {
            if (section.kind != of || !section.widget.gameObject.activeSelf)
                continue;

            foreach (CatalogTileWidget tile in section.tiles)
            {
                if (tile.gameObject.activeSelf)
                    return tile;
            }
        }

        return null;
    }

    private void OnTileClicked(CatalogTileWidget tile)
    {
        Select(tile);
    }

    private void Select(CatalogTileWidget tile)
    {
        CatalogTileWidget previous = selected;
        selected = tile;
        foreach (Section section in sections)
        {
            foreach (CatalogTileWidget candidate in section.tiles)
                candidate.SetSelected(candidate == tile);
        }

        if (tile == null)
            return;

        int k = (int)tile.Entry.kind;
        if (scrolls != null && k < scrolls.Length)
            ScrollRectUtility.EnsureVisible(scrolls[k], (RectTransform)tile.transform, 24f);

        RefreshDetail(tile);

        // After the panel shows the new item, so the burst rests on what it now displays.
        if (previous != null && tile != previous && infoGlitch != null && isActiveAndEnabled)
            infoGlitch.PlayGlitch();
    }

    private void RefreshDetail(CatalogTileWidget tile)
    {
        CatalogEntryData entry = tile.Entry;
        bool known = tile.Discovered;

        detailNumber.text = tile.NumberLabel;
        detailName.text = known ? entry.ResolvedName.ToUpperInvariant() : "???";
        string heading = entry.kind == CatalogKind.Boss ? (WorldName(entry.worldIndex) ?? string.Empty) : entry.ResolvedGroup;
        detailSubtitle.text = known ? (heading + (string.IsNullOrEmpty(entry.subtitle) ? string.Empty : "  •  " + entry.subtitle)).ToUpperInvariant() : string.Empty;
        Sprite sprite = entry.ResolvedIcon;
        detailIcon.sprite = sprite;
        detailIcon.enabled = sprite != null;
        detailIcon.color = known ? entry.iconTint : new Color(0.08f, 0.09f, 0.12f, 0.9f);
        detailDescription.text = known ? entry.ResolvedDescription : "Not discovered yet.";

        var labels = new List<string>();
        var values = new List<string>();
        switch (entry.kind)
        {
            case CatalogKind.Boss:
                labels.AddRange(new[] { "ENCOUNTERED", "DEFEATED", "BEST CLEAR TIME", "WORLD" });
                values.AddRange(new[] { Count(entry.mockEncountered, known), Count(entry.mockDefeated, known), known ? ClearTime(entry.mockBestClearSeconds) : "-", (WorldName(entry.worldIndex) ?? "-").ToUpperInvariant() });
                break;
            case CatalogKind.Enemy:
                labels.AddRange(new[] { "ENCOUNTERED", "DEFEATED", "WORLD", "FACTION" });
                values.AddRange(new[] { Count(entry.mockEncountered, known), Count(entry.mockDefeated, known), (WorldName(entry.worldIndex) ?? "-").ToUpperInvariant(), entry.ResolvedGroup.ToUpperInvariant() });
                break;
            default:
                labels.AddRange(new[] { "RARITY", "PICKED" });
                values.AddRange(new[] { entry.ResolvedGroup.ToUpperInvariant(), Count(entry.mockEncountered, known) });
                break;
        }

        for (int i = 0; i < statLabels.Length; i++)
        {
            bool used = i < labels.Count;
            statLabels[i].transform.parent.gameObject.SetActive(used);
            if (!used)
                continue;
            statLabels[i].text = labels[i];
            statValues[i].text = values[i];
        }
    }

    private static string ClearTime(int seconds)
    {
        return seconds > 0 ? (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00") : "--:--";
    }

    private static string Count(int value, bool known)
    {
        return known ? value.ToString() : "-";
    }
}

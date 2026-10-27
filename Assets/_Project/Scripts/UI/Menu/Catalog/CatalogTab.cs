using System.Collections.Generic;
using Quantum;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Catalog tab content: everything the player can run into - Enemies, Bosses, Rift Mutations - as
/// grids of tiles grouped into sections (faction / rarity), with the selected entry's details on the right.
/// The Enemies page has a world filter and a world banner. Everything is listed from the game's own configuration, never
/// a second list: enemies and bosses from what each world actually spawns (WorldCatalog -> SurvivalConfig phases, see
/// <see cref="BuildWorldEntries"/>), mutations from the level-up pool (<see cref="BuildMutationEntries"/>). What the
/// Catalog needs beyond that (an enemy's subtitle, every entry's <see cref="CatalogDiscovery"/>) lives on the gameplay
/// asset itself. One tile / section / chip is cloned per entry / group / world.
///
/// Discovery and the encountered/defeated counters come from <see cref="CatalogProgress"/>, which nothing records yet.
/// </summary>
public class CatalogTab : TabContent
{
    private const string LogTag = "Catalog";
    private const int KindCount = 3;
    private const string GameConfigPath = "Configs/OverallGameConfig";

    private class Section
    {
        public CatalogKind kind;
        public int world;
        public string group;
        public CatalogSectionWidget widget;
        public readonly List<CatalogTileWidget> tiles = new List<CatalogTileWidget>();
    }

    [Header("Data")]
    [SerializeField, Tooltip("Shows every entry as discovered while there is no saved Catalog progress yet. Turn off to see the real CatalogDiscovery rules.")]
    private bool mockAllDiscovered = true;

    [SerializeField, Tooltip("Rift Mutation section header color per rarity, in UpgradeRarity order: Common, Rare, Epic, Legendary.")]
    private Color[] rarityColors =
    {
        new Color(0.75f, 0.75f, 0.78f),
        new Color(0.1f, 0.6f, 1f),
        new Color(0.65f, 0.3f, 1f),
        new Color(1f, 0.75f, 0.1f)
    };

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
    // What the tab lists: one runtime entry per enemy / boss / mutation the game can produce, minus hidden undiscovered ones.
    private readonly List<CatalogEntry> listed = new List<CatalogEntry>();
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

        listed.Clear();
        var candidates = BuildWorldEntries();
        candidates.AddRange(BuildMutationEntries());
        foreach (CatalogEntry entry in candidates)
        {
            CatalogDiscovery rule = CatalogProgress.RuleOf(entry.source);
            entry.record = CatalogProgress.Get(entry.source.Guid);
            entry.discovered = mockAllDiscovered || CatalogProgress.IsDiscovered(rule, entry.record);
            if (rule != CatalogDiscovery.HiddenUntilFound || entry.discovered)
                listed.Add(entry);
        }

        for (int k = 0; k < KindCount; k++)
        {
            int number = 0;
            foreach (CatalogEntry entry in EntriesOf((CatalogKind)k))
            {
                Section section = FindOrCreateSection(entry, k);
                CatalogTileWidget template = entry.kind == CatalogKind.Boss && bossTileTemplate != null ? bossTileTemplate : tileTemplate;
                CatalogTileWidget tile = ListTemplateUtility.Spawn(template, section.widget.GridRoot);
                tile.name = "Entry_" + entry.Name;
                tile.Bind(entry, ++number, entry.discovered, entry.kind == CatalogKind.Boss ? WorldName(entry.worldIndex) : null);
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

    // Enemies and bosses come from each world's own spawn data, so the lists always match what the game spawns:
    // enemies from every phase's AllowedEnemies, AllowedGroups / GuaranteedGroup members and GuaranteedEnemyData;
    // bosses from a Boss phase's BossPrototype (its Enemy component's EnemyData - the same path RunPhaseUtility spawns
    // from). Each is listed under the first world that spawns it.
    private List<CatalogEntry> BuildWorldEntries()
    {
        var result = new List<CatalogEntry>();
        var seen = new HashSet<EnemyDataAsset>();
        WorldCatalog catalog = WorldCatalog.Instance;
        for (int w = 0; catalog != null && w < catalog.Worlds.Count; w++)
        {
            WorldDefinition world = catalog.Get(w);
            if (world == null || !QuantumUnityDB.TryGetGlobalAsset(world.SurvivalConfig, out SurvivalConfig survival) || survival.Phases == null)
                continue;

            foreach (SurvivalPhase phase in survival.Phases)
            {
                if (phase.AllowedEnemies != null)
                {
                    foreach (EnemySpawnEntry spawn in phase.AllowedEnemies)
                        AddEnemy(spawn.EnemyData, w);
                }

                if (phase.AllowedGroups != null)
                {
                    foreach (AssetRef<EnemyGroupConfig> group in phase.AllowedGroups)
                        AddGroup(group, w);
                }

                AddGroup(phase.GuaranteedGroup, w);
                AddEnemy(phase.GuaranteedEnemyData, w);
                AddBoss(phase.BossPrototype, w);
            }
        }

        return result;

        void AddBoss(AssetRef<EntityPrototype> prototypeRef, int worldIndex)
        {
            if (!QuantumUnityDB.TryGetGlobalAsset(prototypeRef, out EntityPrototype prototype) || prototype.Container.Components == null)
                return;

            foreach (ComponentPrototype component in prototype.Container.Components)
            {
                if (component is Quantum.Prototypes.EnemyPrototype enemy
                    && QuantumUnityDB.TryGetGlobalAsset(enemy.EnemyData, out EnemyDataAsset data) && seen.Add(data))
                    result.Add(CreateEntry(data, CatalogKind.Boss, worldIndex));
            }
        }

        void AddGroup(AssetRef<EnemyGroupConfig> groupRef, int worldIndex)
        {
            if (!QuantumUnityDB.TryGetGlobalAsset(groupRef, out EnemyGroupConfig group) || group.Members == null)
                return;
            foreach (GroupMemberEntry member in group.Members)
                AddEnemy(member.EnemyData, worldIndex);
        }

        void AddEnemy(AssetRef<EnemyDataAsset> enemyRef, int worldIndex)
        {
            if (QuantumUnityDB.TryGetGlobalAsset(enemyRef, out EnemyDataAsset enemy) && !(enemy is BossDataAsset) && seen.Add(enemy))
                result.Add(CreateEntry(enemy, CatalogKind.Enemy, worldIndex));
        }

        CatalogEntry CreateEntry(EnemyDataAsset source, CatalogKind entryKind, int worldIndex)
        {
            return new CatalogEntry { kind = entryKind, source = source, worldIndex = worldIndex };
        }
    }

    // Mutations come from the same pool level-ups, Chests and the Cursed Rift roll from (LevelUpConfig.RiftMutations),
    // so a mutation added there shows up here with nothing else to author.
    private List<CatalogEntry> BuildMutationEntries()
    {
        var result = new List<CatalogEntry>();
        var config = Resources.Load<OverallGameConfig>(GameConfigPath);
        if (config == null || !QuantumUnityDB.TryGetGlobalAsset(config.LevelUpConfig, out LevelUpConfig levelUp) || levelUp.RiftMutations == null)
        {
            LogHelper.Warn(LogTag, "No LevelUpConfig on Resources/" + GameConfigPath + " - Rift Mutations page stays empty.");
            return result;
        }

        var seen = new HashSet<RiftMutationData>();
        foreach (AssetRef<RiftMutationData> mutationRef in levelUp.RiftMutations)
        {
            if (!QuantumUnityDB.TryGetGlobalAsset(mutationRef, out RiftMutationData mutation) || !seen.Add(mutation))
                continue;

            int rarity = (int)mutation.Rarity;
            Color color = rarityColors != null && rarity < rarityColors.Length ? rarityColors[rarity] : Color.white;
            result.Add(new CatalogEntry { kind = CatalogKind.RiftMutation, source = mutation, rarityColor = color });
        }

        return result;
    }

    // List order for bosses: as the worlds spawn them. Enemies are ordered from their own data: world, then the
    // faction's SortOrder, then tier (Filler first, Elite last), then name. Mutations: rarity, then name.
    private IEnumerable<CatalogEntry> EntriesOf(CatalogKind of)
    {
        var result = new List<CatalogEntry>();
        foreach (CatalogEntry entry in listed)
        {
            if (entry.kind == of)
                result.Add(entry);
        }

        if (of == CatalogKind.Enemy)
            result.Sort(CompareEnemies);
        else if (of == CatalogKind.RiftMutation)
            result.Sort(CompareMutations);

        return result;
    }

    private static int CompareMutations(CatalogEntry a, CatalogEntry b)
    {
        int result = ((RiftMutationData)a.source).Rarity.CompareTo(((RiftMutationData)b.source).Rarity);
        return result != 0 ? result : string.CompareOrdinal(a.ResolvedName, b.ResolvedName);
    }

    private static int CompareEnemies(CatalogEntry a, CatalogEntry b)
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

    private static int FactionOrder(CatalogEntry entry)
    {
        return entry.source is EnemyDataAsset enemy && enemy.Faction != null ? enemy.Faction.SortOrder : int.MaxValue;
    }

    private static int TierOrder(CatalogEntry entry)
    {
        return entry.source is EnemyDataAsset enemy ? (int)enemy.Tier : int.MaxValue;
    }

    private Section FindOrCreateSection(CatalogEntry entry, int k)
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
        foreach (CatalogEntry entry in listed)
        {
            total++;
            found += entry.discovered ? 1 : 0;
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
        CatalogEntry entry = tile.Entry;
        bool known = tile.Discovered;

        detailNumber.text = tile.NumberLabel;
        detailName.text = known ? entry.ResolvedName.ToUpperInvariant() : "???";
        string heading = entry.kind == CatalogKind.Boss ? (WorldName(entry.worldIndex) ?? string.Empty) : entry.ResolvedGroup;
        string subtitle = entry.ResolvedSubtitle;
        detailSubtitle.text = known ? (heading + (string.IsNullOrEmpty(subtitle) ? string.Empty : "  •  " + subtitle)).ToUpperInvariant() : string.Empty;
        Sprite sprite = entry.ResolvedIcon;
        detailIcon.sprite = sprite;
        detailIcon.enabled = sprite != null;
        detailIcon.color = known ? Color.white : new Color(0.08f, 0.09f, 0.12f, 0.9f);
        detailDescription.text = known ? entry.ResolvedDescription : "Not discovered yet.";

        var labels = new List<string>();
        var values = new List<string>();
        switch (entry.kind)
        {
            case CatalogKind.Boss:
                labels.AddRange(new[] { "ENCOUNTERED", "DEFEATED", "BEST CLEAR TIME", "WORLD" });
                values.AddRange(new[] { Count(entry.record.Encountered, known), Count(entry.record.Defeated, known), known ? ClearTime(entry.record.BestClearSeconds) : "-", (WorldName(entry.worldIndex) ?? "-").ToUpperInvariant() });
                break;
            case CatalogKind.Enemy:
                labels.AddRange(new[] { "ENCOUNTERED", "DEFEATED", "WORLD", "FACTION" });
                values.AddRange(new[] { Count(entry.record.Encountered, known), Count(entry.record.Defeated, known), (WorldName(entry.worldIndex) ?? "-").ToUpperInvariant(), entry.ResolvedGroup.ToUpperInvariant() });
                break;
            default:
                labels.AddRange(new[] { "RARITY", "PICKED" });
                values.AddRange(new[] { entry.ResolvedGroup.ToUpperInvariant(), Count(entry.record.Encountered, known) });
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

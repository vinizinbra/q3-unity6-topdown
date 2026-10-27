using System.Collections.Generic;
using Quantum;
using QuantumUser.View;
using UnityEngine;
using UnityEngine.UI;

// Tab-hold "everything I'm currently running" overlay for one player, top to bottom:
//   1. HeroInfoWidget  - head icon, health/shield readouts, Base Skill and Passive Skill rows.
//   2. CurrentWeaponUiWidget - the equipped weapon plus one row per granted perk.
//   3. The upgrade history lists below - every upgrade the bound entity has picked, read off
//      UpgradeHistory (see LevelUp.qtn/LevelUpUtility.RecordHistory), split into vertical-scroll
//      lists by LevelUpPoolKind: hero (SkillUpgrade+PassiveUpgrade, the "Hero Ascension" nickname
//      docs/level-up-upgrades.md uses), global (GlobalUpgrade), rift (RiftMutation). The riftMark
//      list/pool below is retired (its LevelUpPoolKind value no longer exists) and stays perpetually
//      empty - left in place rather than ripped out since it's prefab/UI-side wiring, not simulation.
// WeaponPerk/ChooseWeapon never appear in UpgradeHistory (already visible on the weapon itself - see
// RecordHistory's own early-out), which is exactly what section 2 above covers instead.
//
// Sections 1 and 2 are existing widgets reused wholesale rather than reimplemented here (see
// PartyHudWidget for the same compose-and-forward-Initialize shape) - this class only owns the
// Tab toggle, the entity binding it pushes down to them, and the history lists. Pure visual toggle -
// shown while Tab is held, hidden on release; hardcoded for now, no window-manager/input-remapping
// wiring yet. Same self-bind-to-local-slot-0 shape as CurrentWeaponUiWidget/
// PartyHistoryUpgradeContainer.
public class HeroInfoPopupWidget : QuantumGlobalMonoBehaviour
{
    // Android Back (KeyCode.Escape) toggles instead of holds: a pad's Select arrives as Back on
    // Android (MOGA Pro 2) and fires down+up in the same frame, so GetKey never reads it as held.
    private bool _toggledOpen;

    [SerializeField] private GameObject root;

    [Header("Hero")]
    [SerializeField, Tooltip("Head icon, health/shield readouts, Base Skill and Passive Skill rows. Optional - left unassigned, this section is simply absent.")]
    private HeroInfoWidget heroInfoWidget;

    [Header("Build Stats")]
    [SerializeField, Tooltip("Colored per-stat readout (Damage, Crit, Move Speed... with +15% style deltas). Optional - left unassigned, this section is simply absent.")]
    private HeroBuildStatsWidget buildStatsWidget;

    [Header("Weapon")]
    [SerializeField, Tooltip("The same always-on equipped-weapon readout used on the player's own HUD cluster, reused here so a perk reads identically in both places. Optional - left unassigned, this section is simply absent.")]
    private CurrentWeaponUiWidget currentWeaponWidget;

    [Header("Upgrades")]
    [SerializeField, Tooltip("Live template already placed inside this window's own hierarchy - kept inactive and NEVER destroyed, cloned (Instantiate) into whichever list matches each entry's Kind. Rebuilds reuse those clones via activate/deactivate instead of Destroy/Instantiate.")]
    private UpgradeWidget upgradeWidgetPrefab;

    [SerializeField, Tooltip("Parent for SkillUpgrade/PassiveUpgrade entries - typically a ScrollRect Content with a VerticalLayoutGroup.")]
    private Transform heroContent;
    [SerializeField, Tooltip("Parent for GlobalUpgrade entries - typically a ScrollRect Content with a VerticalLayoutGroup.")]
    private Transform globalContent;
    [SerializeField, Tooltip("Parent for RiftMutation entries - typically a ScrollRect Content with a VerticalLayoutGroup.")]
    private Transform riftContent;
    [SerializeField, Tooltip("Parent for RiftMarkMutation entries - typically a ScrollRect Content with a VerticalLayoutGroup.")]
    private Transform riftMarkContent;

    [Header("Rift Mutation icons")]
    [SerializeField, Tooltip("Optional row (e.g. next to the hero name) showing just the icon of every Rift Mutation picked - the build's defining picks at a glance.")]
    private Transform riftIconsRoot;
    [SerializeField, Tooltip("Inactive Image under riftIconsRoot, cloned per mutation and never destroyed.")]
    private Image riftIconTemplate;
    [SerializeField, Tooltip("Optional hover panel - each icon's UpgradeTooltipTriggerWidget (if the template has one) shows the mutation's name and description in it.")]
    private UpgradeTooltipWidget riftIconTooltip;

    [SerializeField, Tooltip("Optional - shown while the bound player has picked no upgrades yet, instead of an empty list.")]
    private GameObject emptyUpgradesLabel;

    [SerializeField, Tooltip("On: binds itself to local slot 0 (player 1) automatically. Off: stays unbound until something else calls Initialize (e.g. the party HUD).")]
    private bool autoBindLocalPlayerOne = true;

    [SerializeField] private EntityRef _entityRef;

    // -1 never matches a real ComputeSignature result (which starts folding from a fixed seed) -
    // see PartyHistoryUpgradeContainer's own comment on the same trick.
    private int _signature = -1;
    private bool _shown;

    // Pooled clones per list, reused across rebuilds via activate/deactivate - upgradeWidgetPrefab
    // is a hand-placed object living inside this window's own hierarchy, not a Project asset, so
    // it (and every clone spawned from it) must never be Destroy()'d.
    private readonly List<UpgradeWidget> _heroPool = new List<UpgradeWidget>();
    private readonly List<UpgradeWidget> _globalPool = new List<UpgradeWidget>();
    private readonly List<UpgradeWidget> _riftPool = new List<UpgradeWidget>();
    private readonly List<UpgradeWidget> _riftMarkPool = new List<UpgradeWidget>();

    private readonly List<Image> _riftIcons = new List<Image>();

    private readonly List<UpgradeHistoryEntry> _heroEntries = new List<UpgradeHistoryEntry>();
    private readonly List<UpgradeHistoryEntry> _globalEntries = new List<UpgradeHistoryEntry>();
    private readonly List<UpgradeHistoryEntry> _riftEntries = new List<UpgradeHistoryEntry>();
    private readonly List<UpgradeHistoryEntry> _riftMarkEntries = new List<UpgradeHistoryEntry>();

    // Awake, not Start: this popup is always the one deciding which entity its children show
    // (Initialize, below), so a child's own autoBindLocalPlayerOne default must be off before its
    // own Start ever runs - same reasoning as PartyHudWidget.DisableChildAutoBind.
    private void Awake()
    {
        if (currentWeaponWidget != null)
            currentWeaponWidget.DisableAutoBind();
    }

    private void Start()
    {
        if (upgradeWidgetPrefab != null)
            upgradeWidgetPrefab.gameObject.SetActive(false);

        SetShown(false);

        if (autoBindLocalPlayerOne)
            MyLocalPlayer.Instance.BindToSlot(0, Initialize);
    }

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
        _signature = -1;

        if (heroInfoWidget != null)
            heroInfoWidget.Initialize(entityRef);

        if (buildStatsWidget != null)
            buildStatsWidget.Initialize(entityRef);

        if (currentWeaponWidget != null)
            currentWeaponWidget.Initialize(entityRef);
    }

    // Called by PartyHudWidget on every widget it owns, so an externally-driven slot
    // never fights its own children's default self-binding - see SkillCooldownUiWidget's own comment.
    public void DisableAutoBind()
    {
        autoBindLocalPlayerOne = false;
    }

    public override void QUpdate(QuantumGame game)
    {
        // Pure visual show/hide, not simulation-driven - still polled from QUpdate (not a raw
        // Update()) since QuantumGlobalMonoBehaviour already owns Update() itself and forwards
        // here; redeclaring Update() in a subclass fights that base wrapper. Fully-qualified:
        // Quantum.Input (this file's "using Quantum;") also has this name.
        if (Application.isMobilePlatform && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            _toggledOpen = !_toggledOpen;

        bool held = _toggledOpen || UnityEngine.Input.GetKey(KeyCode.Tab) || Quantum.GamepadControls.OpenHeroInfoHeld;

        if (held != _shown)
        {
            _shown = held;

            if (held)
            {
                _signature = -1; // force a rebuild against current history the moment it's shown

                // The hero head icon is snapshotted off the live CharView (see
                // PlayerPortraitUiWidget), which may not have existed yet back when this popup was
                // first bound - re-resolving on every show costs nothing and can't miss it.
                if (heroInfoWidget != null)
                    heroInfoWidget.Refresh();
            }

            SetShown(held);

            // Playing alone, the panel pauses the game while it is up - only the instance bound to the local
            // player (the party HUD's copies for teammates are not "me looking at my build").
            if (autoBindLocalPlayerOne)
            {
                if (held)
                    SoloPauseUtility.Request(this);
                else
                    SoloPauseUtility.Release(this);
            }
        }

        if (_shown == false || root == null || upgradeWidgetPrefab == null)
            return;

        Frame frame = game.Frames.Predicted;

        // No UpgradeHistory yet simply means nothing has been picked - still an empty list to show.
        if (frame.TryGet<UpgradeHistory>(_entityRef, out var history) == false)
        {
            if (emptyUpgradesLabel != null && emptyUpgradesLabel.activeSelf == false)
                emptyUpgradesLabel.SetActive(true);

            return;
        }

        int signature = ComputeSignature(history);

        if (signature == _signature)
            return;

        _signature = signature;
        Rebuild(frame, history);
    }

    private static int ComputeSignature(UpgradeHistory history)
    {
        int signature = 17;
        var entries = history.Entries;

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Upgrade.IsValid == false)
                continue;

            signature = signature * 31 + entries[i].Upgrade.GetHashCode();
            signature = signature * 31 + entries[i].Count;
        }

        return signature;
    }

    private void Rebuild(Frame frame, UpgradeHistory history)
    {
        _heroEntries.Clear();
        _globalEntries.Clear();
        _riftEntries.Clear();
        _riftMarkEntries.Clear();

        var entries = history.Entries;

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Upgrade.IsValid == false)
                continue;

            switch (entries[i].Kind)
            {
                case LevelUpPoolKind.SkillUpgrade:
                case LevelUpPoolKind.PassiveUpgrade:
                    _heroEntries.Add(entries[i]);
                    break;
                case LevelUpPoolKind.GlobalUpgrade:
                    _globalEntries.Add(entries[i]);
                    break;
                case LevelUpPoolKind.RiftMutation:
                    _riftEntries.Add(entries[i]);
                    break;
            }
        }

        // Strongest first within each group: highest owned rank on top, pick order breaking ties.
        SortByRank(_heroEntries);
        SortByRank(_globalEntries);

        RebuildList(frame, heroContent, _heroPool, _heroEntries);
        RebuildList(frame, globalContent, _globalPool, _globalEntries);
        RebuildList(frame, riftContent, _riftPool, _riftEntries);
        RebuildList(frame, riftMarkContent, _riftMarkPool, _riftMarkEntries);

        // The lists can share one Content (the popup's single grid does), where card order would
        // otherwise be whichever pool happened to spawn first - Rift Mutations lead, being the rarest,
        // most build-defining picks.
        OrderSiblings(_riftPool);
        OrderSiblings(_riftMarkPool);
        OrderSiblings(_heroPool);
        OrderSiblings(_globalPool);
        RebuildRiftIcons(frame);

        bool empty = _heroEntries.Count + _globalEntries.Count + _riftEntries.Count + _riftMarkEntries.Count == 0;

        if (emptyUpgradesLabel != null && emptyUpgradesLabel.activeSelf != empty)
            emptyUpgradesLabel.SetActive(empty);
    }

    private void RebuildList(Frame frame, Transform content, List<UpgradeWidget> pool, List<UpgradeHistoryEntry> entries)
    {
        if (content == null)
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            UpgradeWidget widget = i < pool.Count ? pool[i] : Spawn(content, pool);
            UpgradeData data = frame.FindAsset(entries[i].Upgrade);
            int count = entries[i].Count;

            // Ranked Ascensions (Cluster Bomb, Momentum, etc.) build their description from their own
            // per-rank arrays rather than the plain Description field - same rank-aware resolution
            // GameplayUiController.BuildCardData already uses for the level-up card itself. count here
            // is the rank ALREADY owned (unlike the draft card, which shows the NEXT rank), so no +1.
            string description = data is IRankedUpgrade ranked && ranked.MaxRank > 1
                ? ranked.GetDescription(count)
                : data.GetDescription();

            // Only an upgrade that can actually be owned more than once gets a rank numeral in its
            // title - a Rift Mutation is non-stackable by design, so "- I" on it would just be noise
            // on every single row. Passing 0 is UpgradeWidget's own "no rank" input, the same thing
            // HeroInfoWidget passes for the Base/Passive Skill rows.
            widget.gameObject.SetActive(true);
            widget.Setup(data.Icon, data.DisplayName, description, GameplayUiController.CanStack(data) ? count : 0, GetMaxRank(data));
        }

        for (int i = entries.Count; i < pool.Count; i++)
            pool[i].gameObject.SetActive(false);
    }

    private void RebuildRiftIcons(Frame frame)
    {
        if (riftIconsRoot == null || riftIconTemplate == null)
            return;

        riftIconTemplate.gameObject.SetActive(false);

        for (int i = 0; i < _riftEntries.Count; i++)
        {
            if (i == _riftIcons.Count)
                _riftIcons.Add(Instantiate(riftIconTemplate, riftIconsRoot));

            UpgradeData data = frame.FindAsset(_riftEntries[i].Upgrade);
            _riftIcons[i].sprite = data.Icon;
            _riftIcons[i].gameObject.SetActive(true);

            if (_riftIcons[i].TryGetComponent(out UpgradeTooltipTriggerWidget trigger))
                trigger.Setup(riftIconTooltip, data.DisplayName, data.GetDescription());
        }

        for (int i = _riftEntries.Count; i < _riftIcons.Count; i++)
            _riftIcons[i].gameObject.SetActive(false);
    }

    private readonly List<UpgradeHistoryEntry> _sortScratch = new List<UpgradeHistoryEntry>();

    // List.Sort isn't stable, so ties fall back to the original (pick-order) index explicitly.
    private void SortByRank(List<UpgradeHistoryEntry> entries)
    {
        _sortScratch.Clear();
        _sortScratch.AddRange(entries);
        entries.Sort((a, b) =>
        {
            int byRank = b.Count.CompareTo(a.Count);
            return byRank != 0 ? byRank : _sortScratch.IndexOf(a).CompareTo(_sortScratch.IndexOf(b));
        });
    }

    private static void OrderSiblings(List<UpgradeWidget> pool)
    {
        for (int i = 0; i < pool.Count; i++)
            pool[i].transform.SetAsLastSibling();
    }

    // 0 = single-pick or unlimited - UpgradeWidget then shows no rank pips.
    private static int GetMaxRank(UpgradeData data)
    {
        if (data is IRankedUpgrade ranked)
            return ranked.MaxRank;

        if (data is GlobalUpgradeData globalUpgrade)
            return globalUpgrade.MaxPicks;

        return 0;
    }

    private UpgradeWidget Spawn(Transform content, List<UpgradeWidget> pool)
    {
        UpgradeWidget widget = Instantiate(upgradeWidgetPrefab, content);
        pool.Add(widget);
        return widget;
    }

    private void SetShown(bool shown)
    {
        if (shown == false && riftIconTooltip != null)
            riftIconTooltip.Hide();

        if (root != null && root.activeSelf != shown)
            root.SetActive(shown);
    }
}

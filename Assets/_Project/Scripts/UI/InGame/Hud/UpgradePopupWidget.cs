using System.Collections.Generic;
using Quantum;
using QuantumUser.View;
using UnityEngine;

// Tab-hold upgrade summary overlay - shows every upgrade the bound entity has picked, read off
// UpgradeHistory (see LevelUp.qtn/LevelUpUtility.RecordHistory), split into 3 vertical-scroll lists
// by LevelUpPoolKind: hero (SkillUpgrade+PassiveUpgrade, the "Hero Ascension" nickname docs/level-up-
// upgrades.md uses), global (GlobalUpgrade), and rift (RiftMutation). WeaponPerk/ChooseWeapon never
// appear in UpgradeHistory (already visible on the weapon itself - see RecordHistory's own early-out),
// so there's no weapon list here. Pure visual toggle - shown while Tab is held, hidden on release;
// hardcoded for now, no window-manager/input-remapping wiring yet. Same self-bind-to-local-slot-0
// shape as CurrentWeaponUiWidget/PartyHistoryUpgradeContainer.
public class UpgradePopupWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private GameObject root;

    [SerializeField, Tooltip("Live template already placed inside this window's own hierarchy - kept inactive and NEVER destroyed, cloned (Instantiate) into whichever list matches each entry's Kind. Rebuilds reuse those clones via activate/deactivate instead of Destroy/Instantiate.")]
    private UpgradeWidget upgradeWidgetPrefab;

    [SerializeField, Tooltip("Parent for SkillUpgrade/PassiveUpgrade entries - typically a ScrollRect Content with a VerticalLayoutGroup.")]
    private Transform heroContent;
    [SerializeField, Tooltip("Parent for GlobalUpgrade entries - typically a ScrollRect Content with a VerticalLayoutGroup.")]
    private Transform globalContent;
    [SerializeField, Tooltip("Parent for RiftMutation entries - typically a ScrollRect Content with a VerticalLayoutGroup.")]
    private Transform riftContent;

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

    private readonly List<UpgradeHistoryEntry> _heroEntries = new List<UpgradeHistoryEntry>();
    private readonly List<UpgradeHistoryEntry> _globalEntries = new List<UpgradeHistoryEntry>();
    private readonly List<UpgradeHistoryEntry> _riftEntries = new List<UpgradeHistoryEntry>();

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
    }

    // Called by PartyHudWidget on every widget it owns, so an externally-driven slot
    // never fights its own children's default self-binding - see ScrapUiWidget's own comment.
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
        bool held = UnityEngine.Input.GetKey(KeyCode.Tab);

        if (held != _shown)
        {
            _shown = held;

            if (held)
                _signature = -1; // force a rebuild against current history the moment it's shown

            SetShown(held);
        }

        if (_shown == false || root == null || upgradeWidgetPrefab == null)
            return;

        Frame frame = game.Frames.Predicted;

        if (frame.TryGet<UpgradeHistory>(_entityRef, out var history) == false)
            return;

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

        RebuildList(frame, heroContent, _heroPool, _heroEntries);
        RebuildList(frame, globalContent, _globalPool, _globalEntries);
        RebuildList(frame, riftContent, _riftPool, _riftEntries);
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

            widget.gameObject.SetActive(true);
            widget.Setup(data.Icon, data.DisplayName, description, count);
        }

        for (int i = entries.Count; i < pool.Count; i++)
            pool[i].gameObject.SetActive(false);
    }

    private UpgradeWidget Spawn(Transform content, List<UpgradeWidget> pool)
    {
        UpgradeWidget widget = Instantiate(upgradeWidgetPrefab, content);
        pool.Add(widget);
        return widget;
    }

    private void SetShown(bool shown)
    {
        if (root != null && root.activeSelf != shown)
            root.SetActive(shown);
    }
}

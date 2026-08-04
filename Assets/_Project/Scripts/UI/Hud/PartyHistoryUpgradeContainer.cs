using Quantum;
using QuantumUser.View;
using UnityEngine;

// Grid of PartyHistoryUpgradeWidget icons in a PartyHudWidget slot - one icon per distinct upgrade
// the bound entity has ever picked, read off UpgradeHistory (see LevelUp.qtn and LevelUpUtility.
// RecordHistory) across Skill Upgrade/Global Upgrade/Passive Upgrade/Rift Mutation - including the
// stat-only kinds (Global/Passive) that otherwise leave no visible trace on the entity. Weapon Perk
// is excluded (already visible on the weapon itself). Same "leaf widget bound externally via
// Initialize, self-hides via root, autoBindLocalPlayerOne default" shape as its PartyHudWidget
// siblings (ScrapUiWidget etc.) - none of them share a base class, so this one doesn't either.
public class PartyHistoryUpgradeContainer : QuantumGlobalMonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField, Tooltip("Parent every icon is instantiated into - typically a GridLayoutGroup.")]
    private Transform grid;
    [SerializeField, Tooltip("Live template, NOT a child of grid - hidden at Start, cloned into grid on rebuild.")]
    private PartyHistoryUpgradeWidget iconPrefab;

    [SerializeField, Tooltip("On: binds itself to local slot 0 (player 1) automatically. Off: stays unbound until something else calls Initialize (e.g. the party HUD).")]
    private bool autoBindLocalPlayerOne = true;

    [SerializeField] private EntityRef _entityRef;

    // -1 never matches a real ComputeSignature result (which starts folding from a fixed seed), so
    // the first QUpdate after a fresh Initialize always rebuilds against that entity's own history
    // rather than reusing whatever the previously-bound entity last left behind.
    private int _signature = -1;

    private void Start()
    {
        if (iconPrefab != null)
            iconPrefab.gameObject.SetActive(false);

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

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override void QUpdate(QuantumGame game)
    {
        if (grid == null || iconPrefab == null)
            return;

        Frame frame = game.Frames.Predicted;
        bool hasHistory = frame.TryGet<UpgradeHistory>(_entityRef, out var history);

        SetShown(hasHistory);

        if (hasHistory == false)
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
        for (int i = grid.childCount - 1; i >= 0; i--)
            Destroy(grid.GetChild(i).gameObject);

        var entries = history.Entries;

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Upgrade.IsValid == false)
                continue;

            UpgradeData data = frame.FindAsset(entries[i].Upgrade);
            PartyHistoryUpgradeWidget icon = Instantiate(iconPrefab, grid);
            icon.gameObject.SetActive(true);
            icon.Setup(data.Icon, entries[i].Count);
        }
    }

    private void SetShown(bool shown)
    {
        if (root != null && root.activeSelf != shown)
            root.SetActive(shown);
    }
}

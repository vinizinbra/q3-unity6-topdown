using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Scrap's stack display: same shape as AdrenalineUiWidget - a single fixed icon (Scrap Collector's
// own, assigned once in the Inspector - PassiveData has no Icon field to read at runtime) plus a
// text for current ScrapStacks and a "MAX" badge object that just shows/hides once ScrapStacks
// reaches StacksRequired. Whole widget self-hides via root when LuxScrapCollector isn't present
// (Scrap Collector not taken). By default self-binds to local slot 0 (player 1), same as
// SkillCooldownUiWidget - see autoBindLocalPlayerOne.
public class ScrapUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text stacksText;
    [SerializeField] private GameObject maxStacksObject;

    [SerializeField, Tooltip("On: binds itself to local slot 0 (player 1) automatically. Off: stays unbound until something else calls Initialize (e.g. the party HUD).")]
    private bool autoBindLocalPlayerOne = true;

    [SerializeField] private EntityRef _entityRef;

    private void Start()
    {
        if (maxStacksObject != null)
            maxStacksObject.SetActive(false);

        if (autoBindLocalPlayerOne)
            MyLocalPlayer.Instance.BindToSlot(0, Initialize);
    }

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
    }

    // Called by PartyHudWidget on every widget it owns, so an externally-driven slot
    // never fights its own children's default self-binding - see the class comment above.
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
        var frame = game.Frames.Predicted;

        if (frame.Has<LuxScrapCollector>(_entityRef) == false)
        {
            SetShown(false);
            return;
        }

        SetShown(true);

        LuxScrapCollector collector = frame.Get<LuxScrapCollector>(_entityRef);

        if (stacksText != null)
            stacksText.text = collector.ScrapStacks.ToString();

        if (maxStacksObject != null)
            maxStacksObject.SetActive(collector.ScrapStacks >= collector.StacksRequired);
    }

    private void SetShown(bool shown)
    {
        if (root != null && root.activeSelf != shown)
            root.SetActive(shown);
    }
}

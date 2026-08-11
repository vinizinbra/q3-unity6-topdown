using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Aftershock's "Building Pressure" stack display: same shape as AdrenalineUiWidget/ScrapUiWidget/
// RemixUiWidget - a single fixed icon (assigned once in the Inspector - AftershockUpgrade has no Icon
// field to read at runtime) plus a text for the current stack count, clamped to AftershockUpgrade.
// MaxStacks (JuggernautCharge.UnitsHit itself is cumulative/unclamped - see that component's own
// comment - the display clamp is what Aftershock's own end-of-channel damage calculation applies too,
// see JuggernautSkillData.TryEndExplosion).
//
// Gated on BOTH JuggernautCharge (only exists while Juggernaut is actively channeling - added at
// Begin, removed at End, see JuggernautSkillData) AND AftershockUpgrade (the Ascension has to
// actually be equipped for stacks to do anything) - showing a stack count that isn't feeding a real
// damage bonus would be misleading. Whole widget self-hides via root whenever either is missing. By
// default self-binds to local slot 0 (player 1), same as SkillCooldownUiWidget - see
// autoBindLocalPlayerOne.
public class JuggernautStackDamageUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text stacksText;

    [SerializeField, Tooltip("On: binds itself to local slot 0 (player 1) automatically. Off: stays unbound until something else calls Initialize (e.g. the party HUD).")]
    private bool autoBindLocalPlayerOne = true;

    [SerializeField] private EntityRef _entityRef;

    private void Start()
    {
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

        if (frame.Has<JuggernautCharge>(_entityRef) == false || frame.Has<AftershockUpgrade>(_entityRef) == false)
        {
            SetShown(false);
            return;
        }

        SetShown(true);

        JuggernautCharge charge = frame.Get<JuggernautCharge>(_entityRef);
        AftershockUpgrade upgrade = frame.Get<AftershockUpgrade>(_entityRef);
        int stacks = Mathf.Min(charge.UnitsHit, upgrade.MaxStacks);

        if (stacksText != null)
            stacksText.text = $"{stacks}/{upgrade.MaxStacks}";
    }

    private void SetShown(bool shown)
    {
        if (root != null && root.activeSelf != shown)
            root.SetActive(shown);
    }
}

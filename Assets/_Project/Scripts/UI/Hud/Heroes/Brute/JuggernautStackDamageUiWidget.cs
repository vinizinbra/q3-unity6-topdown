using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Juggernaut Stack Damage's stack display: same shape as AdrenalineUiWidget/ScrapUiWidget/
// RemixUiWidget - a single fixed icon (assigned once in the Inspector - JuggernautStackDamageUpgrade
// has no Icon field to read at runtime) plus a text for the current stack count. No "MAX" badge,
// unlike those three - JuggernautCharge.UnitsHit has no MaxStacks/cap counterpart to compare against
// (see that component's own comment: cumulative over the whole activation, never clamped), so there's
// nothing honest to gate a MAX state on.
//
// Gated on BOTH JuggernautCharge (only exists while Juggernaut is actively channeling - added at
// Begin, removed at End, see JuggernautSkillData) AND JuggernautStackDamageUpgrade (the upgrade has
// to actually be equipped for UnitsHit to do anything - see JuggernautSkillData.
// ResolveStackDamageBonus) - showing a stack count that isn't feeding a real damage bonus would be
// misleading. Whole widget self-hides via root whenever either is missing. By default self-binds to
// local slot 0 (player 1), same as SkillCooldownUiWidget - see autoBindLocalPlayerOne.
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

        if (frame.Has<JuggernautCharge>(_entityRef) == false || frame.Has<JuggernautStackDamageUpgrade>(_entityRef) == false)
        {
            SetShown(false);
            return;
        }

        SetShown(true);

        JuggernautCharge charge = frame.Get<JuggernautCharge>(_entityRef);

        if (stacksText != null)
            stacksText.text = charge.UnitsHit.ToString();
    }

    private void SetShown(bool shown)
    {
        if (root != null && root.activeSelf != shown)
            root.SetActive(shown);
    }
}

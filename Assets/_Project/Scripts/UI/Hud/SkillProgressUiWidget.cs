using Quantum;
using QuantumUser.View;
using UnityEngine;
using UnityEngine.UI;

// Shared fixed HUD element for "how close is my Hero Skill to its bonus state" - a fill slider,
// hidden whenever a subclass can't resolve a live progress value (upgrade not equipped, skill not
// active). Meant to sit directly on/around HeroSkillUiWidget's icon rather than stand alone.
// Subclasses only supply what "progress" means for their own skill - e.g. RageOverdriveUiWidget reads
// RageOverdrive.Stacks/MaxStacks directly off the entity, while JuggernautChargeUiWidget resolves
// JuggernautCharge.ChargePoints against the active skill asset's MaxCharge (MaxCharge lives on the
// asset, not the component). By default self-binds to local slot 0 (player 1), same as
// SkillCooldownUiWidget - see autoBindLocalPlayerOne.
public abstract class SkillProgressUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField, Tooltip("Whole visual group toggled by resolved progress - separate from this component's own GameObject so it can wrap more than just the slider below.")]
    private GameObject root;

    [SerializeField] private Slider fillSlider;

    [Header("Complete")]
    [SerializeField, Tooltip("Toggled on/off as progress crosses complete - author the shine itself (Animator/Particle System/etc.) on this object in the Editor rather than here in code.")]
    private GameObject shineEffect;

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
        bool active = TryGetProgress(frame, _entityRef, out int current, out int max, out bool complete);

        SetShown(active);

        if (active == false)
            return;

        UpdateFill(current, max, complete);
    }

    protected abstract bool TryGetProgress(Frame frame, EntityRef entity, out int current, out int max, out bool complete);

    private void UpdateFill(int current, int max, bool complete)
    {
        if (fillSlider != null)
            fillSlider.value = max > 0 ? (float)current / max : 0f;

        if (shineEffect != null && shineEffect.activeSelf != complete)
            shineEffect.SetActive(complete);
    }

    private void SetShown(bool shown)
    {
        if (root != null && root.activeSelf != shown)
            root.SetActive(shown);
    }
}

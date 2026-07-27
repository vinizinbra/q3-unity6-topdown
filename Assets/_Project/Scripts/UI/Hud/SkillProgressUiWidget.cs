using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

// Shared fixed HUD element for "how close is my Hero Skill to its bonus state" - fill slider + count
// text, hidden whenever a subclass can't resolve a live progress value (upgrade not equipped, skill
// not active). Meant to sit directly on/around HeroSkillUiWidget's icon rather than stand alone.
// Subclasses only supply what "progress" means for their own skill - e.g. RageOverdriveUiWidget reads
// RageOverdrive.Stacks/MaxStacks directly off the entity, while JuggernautChargeUiWidget resolves
// JuggernautCharge.ChargePoints against the active skill asset's MaxCharge (MaxCharge lives on the
// asset, not the component).
public abstract class SkillProgressUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField, Tooltip("Whole visual group toggled by resolved progress - separate from this component's own GameObject so it can wrap more than just the slider/text below.")]
    private GameObject root;

    [SerializeField] private Slider fillSlider;
    [SerializeField, Tooltip("The slider's own fill Image, recolored on completion - Slider itself has no color property to read/set directly.")]
    private Image fillGraphic;
    [SerializeField, FormerlySerializedAs("stacksText")] private TMP_Text progressText;

    [Header("Complete")]
    [SerializeField, FormerlySerializedAs("overdriveColor"), Tooltip("Applied to fillGraphic once progress is complete, so reaching max reads as an obvious state change rather than just a full bar.")]
    private Color completeColor = Color.red;
    [SerializeField] private Color normalColor = Color.white;

    [SerializeField] private EntityRef _entityRef;

    private void Start()
    {
        MyLocalPlayer.Instance.AddOnLocalPlayerSetup(OnLocalPlayerSetup);
    }

    private void OnLocalPlayerSetup(EntityRef entityRef)
    {
        _entityRef = entityRef;
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
        if (progressText != null)
            progressText.text = $"{current}/{max}";

        if (fillSlider != null)
            fillSlider.value = max > 0 ? (float)current / max : 0f;

        if (fillGraphic != null)
            fillGraphic.color = complete ? completeColor : normalColor;
    }

    private void SetShown(bool shown)
    {
        if (root != null && root.activeSelf != shown)
            root.SetActive(shown);
    }
}

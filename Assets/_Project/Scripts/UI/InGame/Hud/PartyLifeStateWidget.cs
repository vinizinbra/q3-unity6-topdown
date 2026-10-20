using Quantum;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows a party slot's player life state (see docs/revive.md) - the one thing a teammate's slot
// most needs to say at a glance. Downed: the card pulses in downedColor (same red as the
// off-screen teammate indicators) and the optional label reads the bleed-out time. KO: the whole
// slot dims and the portrait greys out. Bound externally via Initialize by PartyHudWidget.
public class PartyLifeStateWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField, Tooltip("Graphics tinted downedColor while Downed - typically the card background.")]
    private Graphic[] downedTintTargets;
    [SerializeField, Tooltip("Portrait greyed out once KO.")]
    private Graphic portrait;
    [SerializeField, Tooltip("Optional - shows DOWNED + bleed-out seconds while Downed, hidden otherwise.")]
    private TMP_Text stateLabel;
    [SerializeField, Tooltip("Dimmed once KO. Left empty, uses (or adds) one on this GameObject.")]
    private CanvasGroup canvasGroup;

    [SerializeField] private Color downedColor = new Color(1f, 0.23f, 0.23f);
    [SerializeField, Tooltip("Pulses per second while Downed.")]
    private float pulseSpeed = 2f;
    [SerializeField, Range(0f, 1f)] private float koAlpha = 0.45f;
    [SerializeField] private Color koPortraitColor = new Color(0.35f, 0.35f, 0.35f);

    [SerializeField] private EntityRef _entityRef;

    private Color[] _normalTints;
    private Color _normalPortraitColor;
    private PlayerLifeStateKind _shownState;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        _normalTints = new Color[downedTintTargets != null ? downedTintTargets.Length : 0];

        for (int i = 0; i < _normalTints.Length; i++)
            _normalTints[i] = downedTintTargets[i] != null ? downedTintTargets[i].color : Color.white;

        if (portrait != null)
            _normalPortraitColor = portrait.color;

        // Forces the first paint - _shownState already defaults to Alive.
        _shownState = (PlayerLifeStateKind)byte.MaxValue;
        Apply(PlayerLifeStateKind.Alive, 0f);
    }

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
        Apply(PlayerLifeStateKind.Alive, 0f);
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

        if (frame.TryGet<PlayerLifeState>(_entityRef, out var life) == false)
            return;

        Apply(life.State, life.BleedOutRemaining.AsFloat);
    }

    private void Apply(PlayerLifeStateKind state, float bleedOutRemaining)
    {
        // The Downed pulse animates every frame; the other states only need painting on change.
        if (state == _shownState && state != PlayerLifeStateKind.Downed)
            return;

        _shownState = state;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f);

        for (int i = 0; i < _normalTints.Length; i++)
        {
            if (downedTintTargets[i] == null)
                continue;

            downedTintTargets[i].color = state == PlayerLifeStateKind.Downed
                ? Color.Lerp(_normalTints[i], downedColor, 0.55f + 0.45f * pulse)
                : _normalTints[i];
        }

        if (portrait != null)
            portrait.color = state == PlayerLifeStateKind.KO ? koPortraitColor : _normalPortraitColor;

        if (canvasGroup != null)
            canvasGroup.alpha = state == PlayerLifeStateKind.KO ? koAlpha : 1f;

        if (stateLabel != null)
        {
            bool showLabel = state != PlayerLifeStateKind.Alive;

            if (stateLabel.gameObject.activeSelf != showLabel)
                stateLabel.gameObject.SetActive(showLabel);

            if (state == PlayerLifeStateKind.Downed)
                stateLabel.text = $"DOWNED {Mathf.CeilToInt(bleedOutRemaining)}";
            else if (state == PlayerLifeStateKind.KO)
                stateLabel.text = "KO";
        }
    }
}

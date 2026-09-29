using NaughtyAttributes;
using PrimeTween;
using Quantum;
using TMPro;
using UnityEngine;

// One floating label above a hero (child of CharacterUiWidget) that pops a short skill-status
// callout - "Skill not ready" on a refused press, "Skill ready"/"Dash ready" the moment a slot
// recovers from 0 stacks. Driven purely by SkillSystem's EventSkillNotReady/EventSkillReady, so it
// needs no per-frame polling. A new message restarts the animation rather than queueing: spamming
// the button should keep re-punching the same label, not stack a column of copies.
//
// Only shown for LOCAL players by default - a teammate's cooldowns are their own business, and a
// remote player's input is only predicted here, so their "not ready" presses can be phantoms.
public class SkillStatusLabelWidget : MonoBehaviour
{
    [SerializeField] private RectTransform selfRect;
    [SerializeField] private TMP_Text label;
    [SerializeField, Tooltip("Faded in/out as a whole. Auto-added to this GameObject if left unassigned.")]
    private CanvasGroup canvasGroup;

    [SerializeField, Tooltip("On: only local players' own heroes show callouts. Off: every hero does, remote teammates included.")]
    private bool localPlayersOnly = true;

    [Header("Text")]
    [SerializeField] private string heroSkillNotReadyText = "SKILL NOT READY";
    [SerializeField] private string dashNotReadyText = "DASH NOT READY";
    [SerializeField] private string heroSkillReadyText = "SKILL READY";
    [SerializeField] private string dashReadyText = "DASH READY";
    [SerializeField] private Color notReadyColor = new Color(1f, 0.12f, 0.32f);
    [SerializeField] private Color readyColor = new Color(0.2f, 1f, 0.3f);

    [Header("Animation")]
    [SerializeField, Tooltip("How far (canvas units) the label rises over its whole lifetime.")]
    private float flyDistance = 70f;
    [SerializeField] private float lifetime = 1.1f;
    [SerializeField, Tooltip("Scale the pop starts from before overshooting back to 1.")]
    private float popFromScale = 0.2f;
    [SerializeField] private float popDuration = 0.28f;
    [SerializeField, Range(0f, 1f), Tooltip("Fraction of the lifetime spent fully opaque before the fade-out starts.")]
    private float opaquePercent = 0.55f;
    [SerializeField, Tooltip("Horizontal shake strength on \"not ready\" only - a refused press should read as a head-shake \"no\".")]
    private float notReadyShakeStrength = 14f;
    [SerializeField] private float notReadyShakeDuration = 0.3f;

    private QuantumGame _game;
    private EntityRef _entityRef;
    private Vector2 _restPosition;
    private Sequence _sequence;
    private Tween _shakeTween;

    private void Awake()
    {
        if (selfRect == null)
            selfRect = (RectTransform)transform;

        if (canvasGroup == null && TryGetComponent(out canvasGroup) == false)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        _restPosition = selfRect.anchoredPosition;
        canvasGroup.alpha = 0f;
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
        _sequence.Stop();
        _shakeTween.Stop();
    }

    // Called from CharacterUiWidget.Setup - same once-per-instance timing the rest of that widget
    // relies on (it runs before this object's own Awake on a freshly spawned clone).
    public void Setup(QuantumGame game, EntityRef entityRef)
    {
        _game = game;
        _entityRef = entityRef;

        QuantumEvent.UnsubscribeListener(this);
        QuantumEvent.Subscribe<EventSkillNotReady>(this, OnSkillNotReady);
        QuantumEvent.Subscribe<EventSkillReady>(this, OnSkillReady);
    }

    private void OnSkillNotReady(EventSkillNotReady e)
    {
        if (ShouldShow(e.Entity) == false)
            return;

        Play(e.Slot == SkillSlotId.DashSkill ? dashNotReadyText : heroSkillNotReadyText, notReadyColor, shake: true);
    }

    private void OnSkillReady(EventSkillReady e)
    {
        if (ShouldShow(e.Entity) == false)
            return;

        Play(e.Slot == SkillSlotId.DashSkill ? dashReadyText : heroSkillReadyText, readyColor, shake: false);
    }

    private unsafe bool ShouldShow(EntityRef entity)
    {
        if (entity != _entityRef || _game == null)
            return false;

        if (localPlayersOnly == false)
            return true;

        Frame frame = _game.Frames.Predicted;

        return frame != null
               && frame.Unsafe.TryGetPointer<PlayerLink>(entity, out var link) == true
               && _game.PlayerIsLocal(link->Player) == true;
    }

    // Pop in with an overshoot, rise while easing out, then fade near the top. Unscaled time, same
    // as the rest of the HUD's punches - a hit-stop freeze shouldn't hold the label mid-flight.
    private void Play(string text, Color color, bool shake)
    {
        if (label == null)
            return;

        _sequence.Stop();
        _shakeTween.Stop();

        label.text = text;
        label.color = color;
        canvasGroup.alpha = 1f;
        selfRect.anchoredPosition = _restPosition;
        selfRect.localScale = Vector3.one * popFromScale;

        Vector2 top = _restPosition + Vector2.up * flyDistance;

        _sequence = Sequence.Create(useUnscaledTime: true)
            .Group(Tween.Scale(selfRect, Vector3.one, popDuration, Ease.OutBack, useUnscaledTime: true))
            .Group(Tween.UIAnchoredPosition(selfRect, _restPosition, top, lifetime, Ease.OutCubic, useUnscaledTime: true))
            .Group(Tween.Alpha(canvasGroup, 1f, 0f, lifetime * (1f - opaquePercent), Ease.InQuad,
                startDelay: lifetime * opaquePercent, useUnscaledTime: true));

        // Shakes the label's own text rect, not selfRect, so it can't fight the rise tween above.
        if (shake == true)
        {
            label.rectTransform.anchoredPosition = Vector2.zero;
            _shakeTween = Tween.ShakeLocalPosition(label.transform, new Vector3(notReadyShakeStrength, 0f, 0f),
                notReadyShakeDuration, frequency: 18f, useUnscaledTime: true);
        }
    }

    [Button]
    private void TestNotReady() => Play(heroSkillNotReadyText, notReadyColor, shake: true);

    [Button]
    private void TestSkillReady() => Play(heroSkillReadyText, readyColor, shake: false);

    [Button]
    private void TestDashReady() => Play(dashReadyText, readyColor, shake: false);
}

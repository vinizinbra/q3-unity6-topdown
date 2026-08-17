using PrimeTween;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Screen-space HUD widget for the Base-Skill-button redirect's world-space prompt (see
// docs/breathing-poi.md) - same "manager-pooled widget under the HUD Canvas, not parented under
// the entity's own view prefab" pattern CharacterUiWidget/EnemyUiWidgetManager already use,
// rather than a CustomQuantumEntityViewComponent living in the 3D world rig (keeps it out of any
// world rig hierarchy that gets squashed/rotated for animation, same reasoning CharacterUiWidget
// itself already documents).
//
// Spawned once per Interactable entity's whole lifetime (by PoiView.Initialize/DeInitialize,
// mirroring how EnemyView/CharView/SentryView spawn their own CharacterUiWidget) rather than
// spawned/despawned every time targeting changes - the widget's own GameObject stays active the
// whole time so LateUpdate keeps running to re-check ContextInteraction.State and update
// titleText/descriptionText accordingly. title is constant (the POI's own name, e.g. "CURSED
// RIFT") - description varies per state (Available/PhaseUnavailable/AlreadyUsed/NotNeeded) and is
// OPTIONAL, hidden entirely when empty (e.g. Available's own description is empty by default - the
// button icon swap already says "press to interact"). Busy hides the whole widget, not just the
// description, since the real Choice Window is already open at that point. NotNeeded's own
// description is ALSO fired as a ToastManager popup (e.g. "FULL HEALTH") whenever the player
// actually PRESSES the Base Skill button while NotNeeded (EventContextInteractionRejected, fired
// by whichever utility's own TryInteract/TryBeginInteraction rejected the attempt) - deliberately
// NOT fired just from standing near a NotNeeded POI, only from a real attempted interaction.
public class InteractionPromptWidget : MonoBehaviour
{
    [SerializeField] private RectTransform selfRect;
    [SerializeField, Tooltip("Scaled to zero/one for the pop in/out - the widget's own root stays active so LateUpdate keeps re-checking ContextInteraction.State.")]
    private GameObject visualRoot;
    [SerializeField, Tooltip("The POI's own name (e.g. \"CURSED RIFT\") - constant, set once in Setup, shown whenever the widget is.")]
    private TMP_Text titleText;
    [SerializeField, Tooltip("Optional per-state line (e.g. \"COME BACK ON BREAK\") - hidden entirely (both this and descriptionRoot) whenever the current state's description is empty.")]
    private TMP_Text descriptionText;
    [SerializeField, Tooltip("On a rejected press while NotNeeded (e.g. pressing at a Healing Shrine already at full Health), also fire this same description as a ToastManager popup - only on an actual press (EventContextInteractionRejected), never just from standing nearby. No-ops if this state's own description is empty or ToastManager.Instance is unset.")]
    private bool toastOnNotNeeded = true;
    [SerializeField, Tooltip("Container for descriptionText - left unassigned, only descriptionText itself is toggled.")]
    private GameObject descriptionRoot;

    [Header("Scale In/Out")]
    [SerializeField, Tooltip("Springy pop on entering range - matches ColliderVisualScaleView/DamageNumberUiWidget's own default.")]
    private float scaleInDuration = 0.2f;
    [SerializeField]
    private Ease scaleInEase = Ease.OutBack;
    [SerializeField, Tooltip("Plain shrink on leaving range/losing eligibility - shorter than the pop-in, same asymmetry ChooseWindow's own timescale ramp uses (fast out reads as responsive, not laggy).")]
    private float scaleOutDuration = 0.15f;
    [SerializeField]
    private Ease scaleOutEase = Ease.InQuad;

    private Canvas _canvas;
    private Camera _worldCamera;
    private QuantumGame _game;
    private EntityRef _entityRef;
    private Transform _followTarget;
    private Vector3 _worldOffset;
    private string _activeDescription;
    private string _phaseUnavailableDescription;
    private string _alreadyUsedDescription;
    private string _notNeededDescription;
    private bool _isShown;
    private Tween _scaleTween;

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        QuantumEvent.Subscribe<EventContextInteractionRejected>(this, OnContextInteractionRejected);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    // Fired by whichever utility's own TryInteract/TryBeginInteraction rejected an actual button
    // press while NotNeeded (see HealingShrineUtility.TryInteract) - the toast trigger, distinct
    // from ApplyDescription below (which drives the passive world-space label off live State every
    // LateUpdate regardless of whether a press ever happens). Filters to presses against THIS
    // entity by one of THIS client's own local players - a remote/other local player's rejected
    // press elsewhere is not this client's business.
    private void OnContextInteractionRejected(EventContextInteractionRejected e)
    {
        if (toastOnNotNeeded == false || e.Target != _entityRef || string.IsNullOrEmpty(_notNeededDescription))
            return;

        if (MyLocalPlayer.Instance == null)
            return;

        var slots = MyLocalPlayer.Instance.Slots;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsSet && slots[i].EntityRef == e.Player)
            {
                ToastManager.Instance?.Show(_notNeededDescription);
                return;
            }
        }
    }

    // title/descriptions are captured once here, not re-applied every LateUpdate for title (it
    // never changes for the lifetime of a given POI instance) - only WHICH description is
    // currently displayed changes, every LateUpdate, off ContextInteraction.State. The manager's
    // widgetPrefab is a disabled scene object (see InteractionPromptWidgetManager.Awake) - clones
    // stay inactive until SetActive(true) right after this call, same "Setup runs before the clone
    // is ever enabled" ordering CharacterUiWidget's own Setup relies on.
    public void Setup(QuantumGame game, EntityRef entityRef, Transform followTarget, string title,
        string activeDescription, string phaseUnavailableDescription, string alreadyUsedDescription, string notNeededDescription, Vector3 worldOffset = default)
    {
        _game = game;
        _entityRef = entityRef;
        _followTarget = followTarget;
        _worldOffset = worldOffset;
        _worldCamera = Camera.main;
        _activeDescription = activeDescription;
        _phaseUnavailableDescription = phaseUnavailableDescription;
        _alreadyUsedDescription = alreadyUsedDescription;
        _notNeededDescription = notNeededDescription;

        if (titleText != null)
            titleText.text = title;

        _isShown = false;

        if (visualRoot != null)
        {
            visualRoot.transform.localScale = Vector3.zero;
            visualRoot.SetActive(false);
        }
    }

    private unsafe void LateUpdate()
    {
        if (_game == null || _followTarget == null)
            return;

        FollowTarget();
        UpdateFromState();
    }

    private void FollowTarget()
    {
        Vector3 worldPosition = _followTarget.position + _worldOffset;

        if (UIHelper.TryWorldToAnchoredPosition(selfRect, _canvas, _worldCamera, worldPosition, out var anchoredPosition))
            selfRect.anchoredPosition = anchoredPosition;
    }

    // Reads whichever LOCAL player currently has this entity as their own ContextInteraction.
    // ActiveTarget (couch co-op: two local players can independently be in/out of range) and
    // shows/hides + re-labels the prompt off that player's own State.
    private unsafe void UpdateFromState()
    {
        ContextInteractionState state = ContextInteractionState.None;

        if (MyLocalPlayer.Instance != null)
        {
            Frame frame = _game.Frames.Predicted;
            var slots = MyLocalPlayer.Instance.Slots;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsSet == false)
                    continue;

                if (frame.Unsafe.TryGetPointer<ContextInteraction>(slots[i].EntityRef, out var context) == false)
                    continue;

                if (context->ActiveTarget == _entityRef)
                {
                    state = context->State;
                    break;
                }
            }
        }

        // Busy (this player already has the real Choice Window open) hides the world prompt
        // entirely rather than showing a redundant message on top of that screen.
        bool shown = state == ContextInteractionState.Available
            || state == ContextInteractionState.PhaseUnavailable
            || state == ContextInteractionState.AlreadyUsed
            || state == ContextInteractionState.NotNeeded;

        if (shown)
            ApplyDescription(ResolveDescription(state));

        SetShown(shown);
    }

    private string ResolveDescription(ContextInteractionState state)
    {
        switch (state)
        {
            case ContextInteractionState.Available: return _activeDescription;
            case ContextInteractionState.PhaseUnavailable: return _phaseUnavailableDescription;
            case ContextInteractionState.AlreadyUsed: return _alreadyUsedDescription;
            case ContextInteractionState.NotNeeded: return _notNeededDescription;
            default: return string.Empty;
        }
    }

    // Optional - hidden entirely (root + text) whenever this state's own description is empty,
    // e.g. Available's default is blank since the Base Skill icon swap already communicates
    // "press to interact" on its own.
    private void ApplyDescription(string description)
    {
        bool hasDescription = string.IsNullOrEmpty(description) == false;

        if (descriptionRoot != null)
            descriptionRoot.SetActive(hasDescription);

        if (descriptionText != null)
        {
            descriptionText.gameObject.SetActive(hasDescription);
            descriptionText.text = description;
        }
    }

    // Scales the prompt in/out instead of an instant SetActive snap - useUnscaledTime so it stays
    // responsive even if some OTHER player's Level-Up screen has ramped Time.timeScale down
    // match-wide, consistent with Cursed Rift's own "doesn't pause for anyone" design.
    private void SetShown(bool shown)
    {
        if (_isShown == shown || visualRoot == null)
            return;

        _isShown = shown;
        _scaleTween.Stop();

        if (shown)
        {
            visualRoot.SetActive(true);
            _scaleTween = Tween.Scale(visualRoot.transform, Vector3.one, scaleInDuration, scaleInEase, useUnscaledTime: true);
        }
        else
        {
            GameObject root = visualRoot;
            _scaleTween = Tween.Scale(root.transform, Vector3.zero, scaleOutDuration, scaleOutEase, useUnscaledTime: true)
                .OnComplete(() => root.SetActive(false));
        }
    }
}

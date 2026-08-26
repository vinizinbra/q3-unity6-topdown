using PrimeTween;
using Photon.Deterministic;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("Hold Progress (Revive - see docs/revive.md)")]
    [SerializeField, Tooltip("Optional - Slider.value (0-1) driven by a Revive channel's own live progress/duration while SOMEONE is holding to revive the entity this widget is following - either one of THIS client's local players (reviver's view) or a teammate reviving one of this client's own local players (the downed player's own view). Left unassigned, this feature is simply off.")]
    private Slider progressFillSlider;
    [SerializeField, Tooltip("Title shown instead of \"REVIVE\" when the Downed entity this widget follows is one of THIS client's own local players - they're not the one pressing anything, they're the one being picked up.")]
    private string selfDownedTitle = "BEING REVIVED";

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
    private string _occupiedDescription;
    // Downed's own live bleed-out countdown ("15s"), reusing descriptionText/ApplyDescription
    // instead of a dedicated field - see RefreshReviveTitle. Empty whenever there's nothing to
    // show (Alive - the only case that reaches this widget at all, since KO has no revive
    // prompt/path anymore), in which case the generic per-ContextInteractionState description
    // (ResolveDescription) takes over instead.
    private string _bleedOutDescription = string.Empty;
    private bool _isShown;
    // Whether the entity this widget follows is one of THIS client's own local players - i.e. we're
    // rendering the DOWNED player's own view of their revive, not a nearby reviver's. Refreshed
    // every frame in RefreshReviveTitle (local slots are bound asynchronously, so this can't be
    // resolved once in Setup).
    private bool _isLocalTarget;
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
        string activeDescription, string phaseUnavailableDescription, string alreadyUsedDescription, string notNeededDescription,
        Vector3 worldOffset = default, string occupiedDescription = "")
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
        _occupiedDescription = occupiedDescription;

        SetTitle(title);

        _isShown = false;

        if (visualRoot != null)
        {
            visualRoot.transform.localScale = Vector3.zero;
            visualRoot.SetActive(false);
        }

        // Revive-only elements (see docs/revive.md) - this same prefab/widget is shared across
        // EVERY Interactable kind (InteractionPromptWidgetManager pools one widgetPrefab for Cursed
        // Rift/Healing Shrine/Store/Blacksmith/Traversal Challenge/Revive alike), so a non-revive
        // instance would otherwise show these at whatever state the shared prefab was left in.
        // RefreshReviveTitle's own early-return re-hides them every frame for the Alive/no-
        // PlayerLifeState case too - this Setup-time hide only covers the gap before that first runs.
        SetActive(progressFillSlider, false);
    }

    // Only writes if changed (avoids per-tick TMP layout thrash).
    public void SetTitle(string title)
    {
        if (titleText != null && titleText.text != title)
            titleText.text = title;
    }

    private unsafe void LateUpdate()
    {
        if (_game == null || _followTarget == null)
            return;

        FollowTarget();

        // Applies the Downed title/color and live bleed-out countdown every frame this entity's
        // PlayerLifeState is Downed - the only state this widget ever shows for now (KO removes its
        // own Interactable, see PlayerLifeStateUtility.EnterKO, despawning this whole widget via
        // ReviveInteractionPromptView's own edge-detect before this could ever run for it).
        RefreshReviveTitle();

        // Revive (see docs/revive.md) - checked BEFORE the generic ContextInteraction-driven
        // switch below and returns early when handled. ContextInteraction.ActiveTarget is fully
        // re-resolved fresh every tick with no stickiness, so once a channel is active this reads
        // PlayerLifeState/ReviveChannel directly instead - a reviver drifting near some other POI
        // mid-hold must never silently blank this prompt on its real (locked) target. Returns false
        // (nobody holding a channel this client should be showing) whenever there's nothing to show
        // progress for, letting the generic path below drive the passive Available/Occupied display
        // off the already-fresh title instead.
        if (UpdateFromReviveState() == true)
            return;

        UpdateFromState();
    }

    private unsafe void RefreshReviveTitle()
    {
        Frame frame = _game.Frames.Predicted;

        if (frame.Unsafe.TryGetPointer<PlayerLifeState>(_entityRef, out var lifeState) == false
            || lifeState->State != PlayerLifeStateKind.Downed)
        {
            _bleedOutDescription = string.Empty;
            _isLocalTarget = false;

            // Runs every frame for every widget instance regardless of Interactable kind - the
            // one guaranteed choke point that keeps these revive-only elements hidden for every
            // non-revive POI (and for a Revive-kind widget on the rare frame its target reads back
            // Alive/KO), independent of Setup-time pooling/reuse quirks.
            SetActive(progressFillSlider, false);
            return;
        }

        _isLocalTarget = IsLocalPlayer(_entityRef);

        // A player being revived sees their own prompt too (it's anchored above their own head),
        // so "REVIVE" - an instruction aimed at whoever is holding the button - would read wrong
        // there. Same widget, same progress bar and bleed-out clock, just the other side of it.
        SetTitle(_isLocalTarget == true ? selfDownedTitle : "REVIVE");

        // Reads the live value directly, so it automatically reflects the simulation's own
        // pause-while-held behavior (PlayerLifeStateSystem) with no extra UI logic. Shown via the
        // existing descriptionText (ApplyDescription) rather than a dedicated field - see
        // UpdateFromReviveState/UpdateFromState, both of which now prefer this over the plain
        // per-ContextInteractionState description whenever it's non-empty.
        _bleedOutDescription = FormatBleedOutTimer(lifeState->BleedOutRemaining);
    }

    private static string FormatBleedOutTimer(FP secondsRemaining)
    {
        int seconds = Mathf.Max(0, Mathf.CeilToInt(secondsRemaining.AsFloat));
        return $"{seconds}s";
    }

    // Returns true if there is a live Revive channel on this entity THIS client should be showing -
    // either one of its own local players is holding to revive someone (the reviver's view), or one
    // of its own local players is the DOWNED entity being revived by a teammate (the target's own
    // view - they never resolve a ContextInteraction of their own while incapacitated, so without
    // this branch the whole prompt would simply never show for them). In both cases the prompt
    // shows live hold progress instead of falling through to the generic ContextInteraction-driven
    // display below.
    private unsafe bool UpdateFromReviveState()
    {
        Frame frame = _game.Frames.Predicted;

        // KO no longer has a revive path at all (see PlayerLifeStateUtility.EnterKO) - this whole
        // widget won't even exist for a KO'd entity (ReviveInteractionPromptView despawns it the
        // instant Interactable is removed), but the check stays explicit (State != Downed, not
        // just == Alive) for the same "never trust it" reason every other resolver here follows.
        if (frame.Unsafe.TryGetPointer<PlayerLifeState>(_entityRef, out var lifeState) == false
            || lifeState->State != PlayerLifeStateKind.Downed)
        {
            return false;
        }

        EntityRef holder = lifeState->ReviveHolder;
        ReviveChannel* channel = null;

        // _isLocalTarget is refreshed by RefreshReviveTitle, which LateUpdate always runs first.
        if (holder != EntityRef.None && (IsLocalPlayer(holder) == true || _isLocalTarget == true))
            frame.Unsafe.TryGetPointer<ReviveChannel>(holder, out channel);

        if (channel == null)
        {
            // Nothing actively channeling right now - hidden entirely, not just reset to
            // 0, so standing near a Downed teammate without holding yet doesn't show a stray empty
            // progress bar; also covers stale progress from an earlier hold never lingering visible
            // underneath the generic idle prompt below.
            SetActive(progressFillSlider, false);

            return false;
        }

        ReviveConfig config = PlayerLifeStateUtility.GetConfig(frame);
        FP duration = config != null ? config.DownedReviveDuration : (FP._2 + FP._0_50);

        // Only ever shown here, while a real Revive channel is actively progressing - see Setup/
        // RefreshReviveTitle/the idle branch above for every other case that keeps it hidden.
        SetActive(progressFillSlider, true);

        if (progressFillSlider != null)
            progressFillSlider.value = duration > FP._0 ? (lifeState->ReviveProgress / duration).AsFloat : 0f;

        // Live bleed-out countdown even while actively being revived (reinforces that it's
        // currently frozen, alongside the progress bar).
        ApplyDescription(_bleedOutDescription);
        SetShown(true);
        return true;
    }

    private unsafe bool IsLocalPlayer(EntityRef entity)
    {
        if (MyLocalPlayer.Instance == null)
            return false;

        var slots = MyLocalPlayer.Instance.Slots;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsSet && slots[i].EntityRef == entity)
                return true;
        }

        return false;
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
            || state == ContextInteractionState.NotNeeded
            || state == ContextInteractionState.Occupied;

        // Downed's live bleed-out countdown (see RefreshReviveTitle) takes priority over the plain
        // per-state description whenever it's non-empty - a nearby teammate should always see the
        // clock, whether they're simply in range (Available) or someone else already claimed the
        // revive (Occupied).
        if (shown)
            ApplyDescription(string.IsNullOrEmpty(_bleedOutDescription) == false ? _bleedOutDescription : ResolveDescription(state));

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
            case ContextInteractionState.Occupied: return _occupiedDescription;
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

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }

    private static void SetActive(Slider slider, bool active)
    {
        if (slider != null)
            SetActive(slider.gameObject, active);
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

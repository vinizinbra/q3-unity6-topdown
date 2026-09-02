using PrimeTween;
using Quantum;
using QuantumUser.View;
using QuantumUser.View.Managers;
using UnityEngine;

// Screen-space HUD arrow pointing at the CURRENT TARGET'S OWN HEALTH BAR - replaces the old
// ground-plane reticle (TargetView, deleted) that rendered directly on the target in 3D.
//
// Reads Aim.Target every tick the same way TargetView/MovementRingView's own target arrow already
// do, then resolves the target's CharacterUiWidget via EnemyUiWidgetManager/SentryUiWidgetManager
// (Aim never targets a player, so that lookup only ever finds an enemy/sentry) and follows its
// HealthAnchor, offset above it (anchorOffset) so it points down onto the bar. "Home" - where the
// arrow starts on first acquiring a target, and where it flies back to before disappearing once
// the target is lost - is the LOCAL PLAYER'S OWN WORLD POSITION (via EntityViewManager's
// EntityRef->Transform cache, the same lookup TargetView/MovementRingView already use), converted
// to screen space with UIHelper.TryWorldToAnchoredPosition - deliberately the player's actual
// center, not their health bar, and with no anchorOffset applied, so it always reads as "from/to
// the player" rather than "from/to the player's UI."
//
// Travel is a straight port of TargetView's own follow-and-bounce feel into anchored-position
// space: an exponential position ease toward whatever the current destination is (a live target,
// or home), shrinking toward travelScaleMultiplier while in flight, then popping back to full size
// with an overshoot (bounceEase) the instant it arrives - replayed on every switch, not just the
// first acquire, so target-to-target swaps get the same travel+bounce beat. Reaching home ends the
// trip by hiding outright rather than bouncing, since "arriving at the player" should read as the
// indicator going away, not locking onto them.
//
// One instance per local player slot (couch co-op), same self-bind idiom CurrencyUiWidget/
// SkillCooldownUiWidget already use. Known simplification: two local players targeting the same
// enemy overlap their arrows exactly rather than offsetting apart - acceptable for now.
public class TargetArrowWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private RectTransform selfRect;
    [SerializeField, Tooltip("Added on top of the destination's health bar anchor position (anchored units) - positive Y sits the arrow above the bar, pointing down onto it. Default for any enemy tier not listed in tierOffsetOverrides below, and always used for the trip home.")]
    private Vector2 anchorOffset = new Vector2(0f, 30f);

    [SerializeField, Tooltip("Per-EnemyTier override for anchorOffset - a bigger tier (Elite, Boss) tends to carry a bigger/taller health bar, so the arrow needs to sit further up to still read as pointing at it rather than overlapping it. Any tier not listed here falls back to anchorOffset.")]
    private TierOffsetOverride[] tierOffsetOverrides;

    [Header("Travel")]
    [SerializeField, Tooltip("How fast the arrow eases toward its current destination (target or home). Higher = snappier/closer to instant.")]
    private float followLerpSpeed = 12f;
    [SerializeField, Tooltip("How fast the arrow eases toward travelScaleMultiplier while still in flight.")]
    private float scaleLerpSpeed = 10f;
    [SerializeField, Range(0.1f, 1f), Tooltip("Fraction of the arrow's normal size it shrinks toward while traveling.")]
    private float travelScaleMultiplier = 0.5f;
    [SerializeField, Tooltip("How long a fly-out to a new destination lasts before the arrow is considered arrived. Time-based rather than distance-based - the destination is a LIVE moving target's screen position, so a tight distance threshold would rarely be reached while it keeps walking, which read as the arrow never arriving.")]
    private float travelDuration = 0.25f;
    [SerializeField, Tooltip("How long the arrival scale-up takes when landing on a target. Paired with bounceEase's overshoot, this is the whole \"bounce\" - no separate punch after.")]
    private float bounceDuration = 0.3f;
    [SerializeField] private Ease bounceEase = Ease.OutBack;

    [Header("Idle bob (locked on)")]
    [SerializeField, Tooltip("Vertical bob amplitude (anchored units) once the arrow has arrived and is sitting locked on the target - reads as a gentle pointing-down bounce rather than a static icon. 0 disables it.")]
    private float idleBobAmplitude = 6f;
    [SerializeField, Tooltip("Bob speed in radians/sec.")]
    private float idleBobSpeed = 4f;

    [SerializeField, Tooltip("On: binds itself to localSlotIndex automatically. Off: stays unbound until something else calls Initialize.")]
    private bool autoBindLocalSlot = true;
    [SerializeField, Tooltip("Local slot index to bind to when autoBindLocalSlot is on - 0 for player 1, 1 for a second local (couch co-op) player.")]
    private int localSlotIndex;

    private Canvas _canvas;
    private CanvasGroup _canvasGroup;
    private Camera _worldCamera;
    private EntityRef _entityRef;
    private EntityRef _lastTarget;
    private Vector3 _baseScale;
    private Vector2 _currentPosition;

    // True while the arrow is sitting hidden at "home" with nothing to fly toward - the very
    // start state, and the state it returns to once a return-home trip completes.
    private bool _isHome = true;
    private bool _isReturningHome;
    private bool _arrived;
    private float _travelTimer;
    private Tween _bounceTween;

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        _baseScale = selfRect.localScale;

        _canvasGroup = selfRect.GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = selfRect.gameObject.AddComponent<CanvasGroup>();

        SetShown(false);
    }

    private void Start()
    {
        if (autoBindLocalSlot)
            MyLocalPlayer.Instance.BindToSlot(localSlotIndex, Initialize);
    }

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
    }

    // Called externally (e.g. a future party HUD) so an externally-driven instance never fights its
    // own default self-binding - same convention CurrencyUiWidget.DisableAutoBind uses.
    public void DisableAutoBind()
    {
        autoBindLocalSlot = false;
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override void QUpdate(QuantumGame game)
    {
        if (_entityRef == EntityRef.None)
        {
            SnapHome();
            return;
        }

        Frame frame = game.Frames.Predicted;

        EntityRef target = frame.Exists(_entityRef) && frame.Has<Aim>(_entityRef)
            ? frame.Get<Aim>(_entityRef).Target
            : EntityRef.None;

        RectTransform targetAnchor = null;
        bool hasTarget = target != EntityRef.None && TryResolveTargetAnchor(target, out targetAnchor);

        if (target != _lastTarget)
        {
            if (hasTarget)
                OnTargetAcquired();
            else
                BeginReturnHome();

            _lastTarget = target;
        }

        if (hasTarget == false && _isReturningHome == false)
            return; // Never had a target yet, or already finished the trip home - nothing to animate.

        Vector2 desiredPosition = default;
        bool resolved = hasTarget
            ? UIHelper.TryRectTransformToAnchoredPosition(selfRect, _canvas, targetAnchor, out desiredPosition)
            : TryResolveHomePosition(out desiredPosition);

        if (resolved == false)
        {
            SnapHome();
            return;
        }

        if (hasTarget)
            desiredPosition += ResolveAnchorOffset(frame, target);

        float dt = Time.deltaTime;

        if (_arrived)
        {
            // Locked on - track the destination 1:1 every frame, same as CharacterUiWidget's own
            // FollowTarget, so the arrow never visibly lags a moving target once it's landed. The
            // eased approach below only applies to the travel leg itself. A small sine bob is
            // layered on top so a stationary target doesn't read as a static, forgotten icon.
            _currentPosition = desiredPosition;
            _currentPosition.y += Mathf.Sin(Time.time * idleBobSpeed) * idleBobAmplitude;
        }
        else
        {
            _currentPosition = Vector2.Lerp(_currentPosition, desiredPosition, 1f - Mathf.Exp(-followLerpSpeed * dt));
            _travelTimer += dt;
            UpdateApproach(dt);
        }

        selfRect.anchoredPosition = _currentPosition;
    }

    // First-ever acquire (arrow currently sitting hidden at home) starts the flight from the
    // player's own position; a direct target-to-target switch keeps flying from wherever the arrow
    // already is instead of resetting to the player - same "switching skips the intro" behavior
    // TargetView's own reticle had.
    private void OnTargetAcquired()
    {
        if (_isHome)
        {
            if (TryResolveHomePosition(out Vector2 homePosition))
            {
                _currentPosition = homePosition;
                selfRect.anchoredPosition = _currentPosition;
            }

            _isHome = false;
        }

        _isReturningHome = false;
        _arrived = false;
        _travelTimer = 0f;
        _bounceTween.Stop();
        SetShown(true);
    }

    private void BeginReturnHome()
    {
        _isReturningHome = true;
        _arrived = false;
        _travelTimer = 0f;
        _bounceTween.Stop();
    }

    private void UpdateApproach(float dt)
    {
        Vector3 travelScale = _baseScale * travelScaleMultiplier;
        selfRect.localScale = Vector3.Lerp(selfRect.localScale, travelScale, 1f - Mathf.Exp(-scaleLerpSpeed * dt));

        if (_travelTimer < travelDuration)
            return;

        _arrived = true;

        if (_isReturningHome)
        {
            // Arriving home ends the trip by disappearing rather than bouncing - this is "you have
            // no target" landing on the player, not a lock-on.
            _isReturningHome = false;
            _isHome = true;
            SetShown(false);
            return;
        }

        _bounceTween = Tween.Scale(selfRect, selfRect.localScale, _baseScale, bounceDuration, bounceEase);
    }

    // Falls back to the flat anchorOffset for anything without a listed override - includes every
    // enemy tier nobody bothered to author an entry for, and a Sentry target (no EnemyTier at all).
    private Vector2 ResolveAnchorOffset(Frame frame, EntityRef target)
    {
        if (tierOffsetOverrides == null || tierOffsetOverrides.Length == 0 || frame.TryGet<Enemy>(target, out var enemy) == false)
            return anchorOffset;

        EnemyDataAsset enemyData = frame.FindAsset(enemy.EnemyData);
        if (enemyData == null)
            return anchorOffset;

        foreach (var tierOverride in tierOffsetOverrides)
        {
            if (tierOverride.Tier == enemyData.Tier)
                return tierOverride.Offset;
        }

        return anchorOffset;
    }

    private bool TryResolveHomePosition(out Vector2 anchoredPosition)
    {
        anchoredPosition = default;

        Transform playerTransform = EntityViewManager.Instance != null
            ? EntityViewManager.Instance.GetEntityTransform(_entityRef)
            : null;

        if (playerTransform == null)
            return false;

        if (_worldCamera == null)
            _worldCamera = Camera.main;

        if (_worldCamera == null)
            return false;

        return UIHelper.TryWorldToAnchoredPosition(selfRect, _canvas, _worldCamera, playerTransform.position, out anchoredPosition);
    }

    private static bool TryResolveTargetAnchor(EntityRef target, out RectTransform anchor)
    {
        if (EnemyUiWidgetManager.Instance != null && EnemyUiWidgetManager.Instance.TryGetWidget(target, out var enemyWidget))
        {
            anchor = enemyWidget.HealthAnchor;
            return anchor != null;
        }

        if (SentryUiWidgetManager.Instance != null && SentryUiWidgetManager.Instance.TryGetWidget(target, out var sentryWidget))
        {
            anchor = sentryWidget.HealthAnchor;
            return anchor != null;
        }

        anchor = null;
        return false;
    }

    private void SnapHome()
    {
        _isReturningHome = false;
        _isHome = true;
        _arrived = false;
        _lastTarget = EntityRef.None;
        _bounceTween.Stop();
        SetShown(false);
    }

    private void SetShown(bool shown)
    {
        if (_canvasGroup != null)
            _canvasGroup.alpha = shown ? 1f : 0f;
    }

    [System.Serializable]
    private struct TierOffsetOverride
    {
        public EnemyTier Tier;
        public Vector2 Offset;
    }
}

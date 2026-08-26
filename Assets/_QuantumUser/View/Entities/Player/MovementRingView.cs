using PrimeTween;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.Serialization;

namespace Quantum
{
    // Local-player-only ground marker: a ring (plus an additive RingGlow overlay that pulses
    // subtly) that stays visible the whole time as a "this is you" highlight, plus three child
    // arrows - a lead/trail pair shows up while moving, both rotated to face the current
    // movement direction (read off KCC.Data.RealVelocity, same convention as
    // BlobAnimationView/PlayerGunAimView) but sitting at different offsets/alphas for a fading
    // chevron-trail look; the third shows up while there's a current Aim.Target (see AimSystem,
    // same source TargetView's reticle uses) and points from the character toward it.
    // Ring/RingGlow/MovementArrow(+trail) are tinted from the hero's own CharacterData.RingColor;
    // TargetArrow keeps its own authored color, since a "you're locked on" indicator isn't a
    // per-hero identity marker. Unlike TargetView's reticle, all these sprites always track this
    // entity's own position, so they just stay parented under the character instead of
    // unparenting.
    //
    // A FOURTH arrow - fully optional, off unless its sprite is assigned - points at this player's
    // own dropped Signature Accessory (see docs/accessory-guard.md). It lives here rather than in a
    // component of its own precisely because it is the same job as the target arrow: a flat ground
    // arrow orbiting the character, aimed at a world entity, sharing this ring's grounded fade and
    // PositionAndRotateArrow. A second near-identical component would just be one more thing to keep
    // in sync by hand.
    public class MovementRingView : CustomQuantumEntityViewComponent
    {
        [Header("Ring")]
        [SerializeField, Tooltip("Ring sprite shown continuously while this is the local player.")]
        private SpriteRenderer ringSprite;
        [SerializeField, Tooltip("Seconds for the ring to fade fully in/out when the grounded state changes - same fade-driven approach as WeaponRangeIndicatorView, but symmetric (fades out on leaving the ground instead of vanishing instantly).")]
        private float ringFadeDuration = 0.2f;

        [Header("Ring glow (additive)")]
        [SerializeField, Tooltip("Additive glow overlay on the ring - fades with the same grounded state as the ring, and pulses subtly on top of that.")]
        private SpriteRenderer ringGlowSprite;
        [SerializeField, Tooltip("Alpha range for the glow pulse - kept close together for a barely-noticeable breathing effect.")]
        private float glowPulseMinAlpha = 0.6f;
        [SerializeField]
        private float glowPulseMaxAlpha = 1f;
        [SerializeField, Tooltip("Seconds for one half-cycle (min-to-max or max-to-min) of the glow pulse.")]
        private float glowPulseDuration = 1.5f;
        [SerializeField]
        private Ease glowPulseEase = Ease.InOutSine;

        [Header("Movement arrow")]
        [SerializeField, FormerlySerializedAs("arrowSprite"), Tooltip("Arrow sprite shown only while moving, rotated to face the current movement direction.")]
        private SpriteRenderer moveArrowSprite;
        [SerializeField, Tooltip("Minimum ground speed before the arrow is considered moving and shown.")]
        private float moveSpeedThreshold = 0.1f;
        [SerializeField, FormerlySerializedAs("turnSpeed"), Tooltip("Degrees per second the arrow turns to catch up to a new movement heading.")]
        private float moveTurnSpeed = 720f;
        [SerializeField, FormerlySerializedAs("arrowOffsetDistance"), Tooltip("Distance from the character's center the arrow sits along the movement direction.")]
        private float moveArrowOffsetDistance = 0.5f;
        [SerializeField, FormerlySerializedAs("arrowRotationOffset"), Tooltip("Rotation correction added only to this arrow's visual spin, not its position - compensates for the arrow art's authored facing direction (e.g. -90 if the sprite points right instead of up).")]
        private float moveArrowRotationOffset = -90f;

        [Header("Movement arrow (trail)")]
        [SerializeField, Tooltip("Second arrow shown alongside the movement arrow, sitting further out along the same heading, for a fading chevron-trail look.")]
        private SpriteRenderer moveArrowTrailSprite;
        [SerializeField, Tooltip("Distance from the character's center the trail arrow sits along the movement direction - greater than moveArrowOffsetDistance so it trails behind/ahead of the lead arrow.")]
        private float moveArrowTrailOffsetDistance = 0.9f;
        [SerializeField, Tooltip("Rotation correction added only to the trail arrow's visual spin - same convention as moveArrowRotationOffset.")]
        private float moveArrowTrailRotationOffset = -90f;
        [SerializeField, Range(0f, 1f), Tooltip("Multiplies the trail arrow's alpha relative to the lead arrow, so it reads as dimmer/fading.")]
        private float moveArrowTrailAlphaScale = 0.6f;

        [Header("Target arrow")]
        [SerializeField, Tooltip("Arrow sprite shown only while there's a current Aim target, rotated to face that target's direction.")]
        private SpriteRenderer targetArrowSprite;
        [SerializeField, Tooltip("Degrees per second the arrow turns to catch up to a new target direction.")]
        private float targetTurnSpeed = 720f;
        [SerializeField, Tooltip("Distance from the character's center the arrow sits along the direction to the target.")]
        private float targetArrowOffsetDistance = 0.5f;
        [SerializeField, Tooltip("Rotation correction added only to this arrow's visual spin, not its position - compensates for the arrow art's authored facing direction (e.g. -90 if the sprite points right instead of up).")]
        private float targetArrowRotationOffset = -90f;

        [Header("Accessory arrow (see docs/accessory-guard.md)")]
        [SerializeField, Tooltip("Optional - arrow shown only while this player's Signature Accessory is lying out in the level (AccessoryGuard.Accessory), rotated to face it. A dropped accessory has no lifetime and is only ever recovered by walking back to it, so this is the pointer that keeps that retrieval from becoming a hunt. Left unassigned the whole feature is simply off, so an existing hero prefab is unaffected until it gets one.")]
        private SpriteRenderer accessoryArrowSprite;
        [SerializeField, Tooltip("Optional companion sprite sitting further out along the same heading, painted with THIS hero's own CharacterData.Accessory.CollectibleSprite - the same sprite DroppedAccessoryView puts on the world pickup, so the pointer and the thing it points at can never show different art. Never turns with the heading (a rotated hat reads as broken art, not as a direction) - it billboards to the camera instead, see accessoryIconBillboard below.")]
        private SpriteRenderer accessoryIconSprite;
        [SerializeField, Tooltip("Seconds the accessory arrow/icon take to pop in when they appear. 0 skips the animation and they simply show at full size.")]
        private float accessoryAppearDuration = 0.3f;

        [SerializeField, Tooltip("Ease for that pop-in. OutBack overshoots slightly past full size and settles back, so the pointer arrives with a snap instead of fading in unnoticed.")]
        private Ease accessoryAppearEase = Ease.OutBack;

        [SerializeField, Tooltip("Seconds the accessory arrow/icon take to shrink away once there is nothing to point at (recovered, or you walked inside accessoryHideWithinDistance). The sprites stay rendered for exactly this long so the animation can play. 0 hides them instantly.")]
        private float accessoryDisappearDuration = 0.2f;

        [SerializeField, Tooltip("Ease for that shrink-away. InBack winds up slightly larger before collapsing, the natural counterpart to the pop-in's overshoot.")]
        private Ease accessoryDisappearEase = Ease.InBack;

        [SerializeField, Tooltip("Size multiplier for the accessory ICON. The sprite is first normalised to 1x1 world units - its own pixel size and Pixels Per Unit are divided out, so a 64px cap and a 256px headset read exactly the same size - and then multiplied by this. The prefab's own scale on that sprite is REPLACED, not multiplied. 0 keeps the prefab scale and skips normalisation entirely.")]
        private float accessoryIconScale = 1f;

        [SerializeField, Tooltip("Face the accessory ICON at the camera instead of leaving it flat on the ground, so its artwork reads like the world pickup rather than a floor decal. This component applies the camera-facing rotation itself, so a Billboard on that same sprite is redundant and is disabled automatically. The arrow is unaffected either way - it stays a flat ground arrow like the other three.")]
        private bool accessoryIconBillboard = true;

        [SerializeField, Tooltip("Degrees per second the arrow turns to catch up to a new accessory direction.")]
        private float accessoryTurnSpeed = 720f;
        [SerializeField, Tooltip("Distance from the character's center the arrow sits along the direction to the accessory.")]
        private float accessoryArrowOffsetDistance = 0.5f;
        [SerializeField, Tooltip("Distance from the character's center the icon sits - usually further out than the arrow, so the pair reads as \"arrow, then what it's pointing at\".")]
        private float accessoryIconOffsetDistance = 0.9f;
        [SerializeField, Tooltip("Rotation correction added only to this arrow's visual spin, not its position - same convention as the other arrows here.")]
        private float accessoryArrowRotationOffset = -90f;
        [SerializeField, Tooltip("Point at it during the pop arc too (AccessoryGuardState.Airborne), not just once it has landed - that's exactly when you most want to see where it's heading.")]
        private bool accessoryShowWhileAirborne = true;
        [SerializeField, Tooltip("Hide the arrow once the accessory is within this many world units - close enough that it's on screen and the pickup speaks for itself. 0 keeps it shown right up until collection.")]
        private float accessoryHideWithinDistance = 6f;

        private float _baseRingAlpha;
        private float _baseGlowAlpha;
        private float _baseMoveArrowAlpha;
        private float _baseMoveArrowTrailAlpha;

        private Color _baseRingColor;
        private Color _baseGlowColor;
        private Color _baseMoveArrowColor;
        private Color _baseMoveArrowTrailColor;
        private Color _baseTargetArrowColor;
        private float _ringAlpha;
        private float _glowPulseAlpha;
        private Tween _glowTween;

        private Vector3 _baseMoveArrowLocalPosition;
        private Vector3 _baseMoveArrowTrailLocalPosition;
        private float _currentMoveHeading;
        private bool _hasMoveHeading;

        private Vector3 _baseTargetArrowLocalPosition;
        private float _currentTargetHeading;
        private bool _hasTargetHeading;


        // The arrow takes the hero's RingColor like the ring/move arrows do (built in Initialize, once
        // CharacterData is resolvable); the icon keeps whatever the prefab painted, since it's the
        // accessory's own artwork rather than an indicator. Only the arrow's authored ALPHA survives
        // the tint, so its prefab opacity still means something.
        private float _baseAccessoryArrowAlpha = 1f;
        private Color _baseAccessoryArrowColor = Color.white;
        private Color _baseAccessoryIconColor = Color.white;

        // Captured before the pop-in tween ever scales them, so a re-appear always lands back on the
        // prefab's own size rather than compounding off whatever the last tween left behind.
        private Vector3 _baseAccessoryArrowScale = Vector3.one;
        private Vector3 _baseAccessoryIconScale = Vector3.one;

        // How far the icon sprite's CENTRE sits from its PIVOT, in UNSCALED local units - multiplied by
        // the icon's live localScale when applied, so the correction stays right mid-pop-in rather than
        // over-shifting a sprite that is only half grown. Subtracted from its position every frame so
        // an off-centre or bottom-pivoted sprite orbits centred on the point instead of hanging off it.
        private Vector2 _accessoryIconCenterOffset;

        private Tween _accessoryArrowScaleTween;
        private Tween _accessoryIconScaleTween;
        private bool _accessoryShown;

        // "Is anything still on screen", as opposed to _accessoryShown's "is there something to point
        // at". They differ for exactly the length of the shrink-away: the sprites have to outlive the
        // tracked accessory by that long, or the renderers would switch off on the same frame and the
        // animation would never be seen.
        private bool _accessoryRendering;

        // Cached the same lazy way Billboard/DroppedAccessoryView do it - Camera.main is a tagged
        // lookup, not free.
        private Camera _accessoryIconCamera;
        private Vector3 _baseAccessoryArrowLocalPosition;
        private Vector3 _baseAccessoryIconLocalPosition;
        private float _currentAccessoryHeading;
        private bool _hasAccessoryHeading;

        public override void Awake()
        {
            base.Awake();

            executeOnlyOnLocal = true;

            // Lie flat on the ground facing up, same convention as PlayerShadow/TargetView.
            ringSprite.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ringGlowSprite.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            moveArrowSprite.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            moveArrowTrailSprite.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            targetArrowSprite.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Captured before RingColor tinting overwrites RGB below - these authored alphas stay
            // the ceiling each sprite fades/pulses toward, so per-hero opacity choices survive.
            _baseRingAlpha = ringSprite.color.a;
            _baseGlowAlpha = ringGlowSprite.color.a;
            _baseMoveArrowAlpha = moveArrowSprite.color.a;
            _baseMoveArrowTrailAlpha = moveArrowTrailSprite.color.a;
            // Not tinted from RingColor, so its full authored color is already final - only the
            // alpha gets modulated per-frame, same as the tinted sprites.
            _baseTargetArrowColor = targetArrowSprite.color;

            // Keeps whatever ground lift/depth the artist authored on each arrow - only its X/Z
            // gets overridden per-frame to orbit around that same resting height.
            _baseMoveArrowLocalPosition = moveArrowSprite.transform.localPosition;
            _baseMoveArrowTrailLocalPosition = moveArrowTrailSprite.transform.localPosition;
            _baseTargetArrowLocalPosition = targetArrowSprite.transform.localPosition;

            // Null-guarded, unlike the five above: the accessory pair is optional, so a hero prefab
            // authored before it existed keeps working untouched.
            if (accessoryArrowSprite != null)
            {
                accessoryArrowSprite.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                _baseAccessoryArrowLocalPosition = accessoryArrowSprite.transform.localPosition;
                _baseAccessoryArrowScale = accessoryArrowSprite.transform.localScale;
                // Alpha only - the RGB is overwritten by the RingColor tint in Initialize.
                _baseAccessoryArrowAlpha = accessoryArrowSprite.color.a;
            }

            if (accessoryIconSprite != null)
            {
                accessoryIconSprite.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                _baseAccessoryIconLocalPosition = accessoryIconSprite.transform.localPosition;
                _baseAccessoryIconScale = accessoryIconSprite.transform.localScale;
                _baseAccessoryIconColor = accessoryIconSprite.color;

                // Exactly one thing may own this rotation. Billboard hard-sets a world rotation every
                // LateUpdate, so leaving it enabled alongside our own write makes the result depend on
                // script execution order. We reproduce it verbatim (same LookRotation) in
                // ApplyAccessoryIconBillboard, so disabling it loses nothing - the same call
                // DroppedAccessoryView already makes for the world pickup's own spin.
                Billboard billboard = accessoryIconSprite.GetComponent<Billboard>();

                if (billboard != null && accessoryIconBillboard == true)
                    billboard.enabled = false;
            }
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            bool isLocalPlayer = QuantumHelper.IsLocalPlayer(_playerRef);
            ringSprite.gameObject.SetActive(isLocalPlayer);
            ringGlowSprite.gameObject.SetActive(isLocalPlayer);
            moveArrowSprite.gameObject.SetActive(isLocalPlayer);
            moveArrowTrailSprite.gameObject.SetActive(isLocalPlayer);
            targetArrowSprite.gameObject.SetActive(isLocalPlayer);
            SetActiveIfPresent(accessoryArrowSprite, isLocalPlayer);
            SetActiveIfPresent(accessoryIconSprite, isLocalPlayer);

            _ringAlpha = 0f;
            _hasAccessoryHeading = false;

            if (isLocalPlayer == false)
                return;

            CharacterData characterData = ResolveCharacterData(game.Frames.Verified);

            Color ringColor = ResolveRingColor(characterData);
            _baseRingColor = new Color(ringColor.r, ringColor.g, ringColor.b, _baseRingAlpha);
            _baseGlowColor = new Color(ringColor.r, ringColor.g, ringColor.b, _baseGlowAlpha);
            _baseMoveArrowColor = new Color(ringColor.r, ringColor.g, ringColor.b, _baseMoveArrowAlpha);
            _baseMoveArrowTrailColor = new Color(ringColor.r, ringColor.g, ringColor.b, _baseMoveArrowTrailAlpha);
            _baseAccessoryArrowColor = new Color(ringColor.r, ringColor.g, ringColor.b, _baseAccessoryArrowAlpha);

            // Painted once here rather than every frame: the accessory a hero wears is fixed for the
            // whole run, and this is the same CollectibleSprite DroppedAccessoryView resolves for the
            // world pickup. An unauthored hero keeps whatever the prefab baked in rather than blanking
            // out - the same keepPlaceholderWhenUnauthored default the pickup itself uses.
            if (accessoryIconSprite != null && characterData != null && characterData.Accessory.CollectibleSprite != null)
                accessoryIconSprite.sprite = characterData.Accessory.CollectibleSprite;

            // After the paint above, since both the size normalisation and the centring depend on
            // WHICH sprite ended up on the renderer.
            ResolveAccessoryIconLayout();

            StartGlowPulse();
        }

        private static void SetActiveIfPresent(SpriteRenderer sprite, bool active)
        {
            if (sprite != null)
                sprite.gameObject.SetActive(active);
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);

            if (_glowTween.isAlive)
                _glowTween.Stop();

            // Same reason the glow tween is stopped: these drive a Transform that is about to be
            // pooled/destroyed, and a live tween writing into it afterwards is a stale-target error.
            if (_accessoryArrowScaleTween.isAlive)
                _accessoryArrowScaleTween.Stop();

            if (_accessoryIconScaleTween.isAlive)
                _accessoryIconScaleTween.Stop();

            _accessoryShown = false;
            _accessoryRendering = false;
        }

        // Resolved once and shared by the ring tint and the accessory icon, rather than each
        // FindAsset-ing the same asset independently.
        private CharacterData ResolveCharacterData(Frame frame)
        {
            if (frame.Has<CharacterStats>(_entityRef) == false)
                return null;

            return frame.FindAsset(frame.Get<CharacterStats>(_entityRef).CharacterData);
        }

        private static Color ResolveRingColor(CharacterData data)
        {
            return data != null ? data.RingColor : Color.white;
        }

        private void StartGlowPulse()
        {
            if (_glowTween.isAlive)
                _glowTween.Stop();

            _glowPulseAlpha = glowPulseMaxAlpha;
            _glowTween = Tween.Custom(this, glowPulseMinAlpha, glowPulseMaxAlpha, glowPulseDuration,
                (view, value) => view._glowPulseAlpha = value,
                glowPulseEase, cycles: -1, cycleMode: CycleMode.Yoyo);
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (ringSprite == null || ringGlowSprite == null || moveArrowSprite == null || moveArrowTrailSprite == null || targetArrowSprite == null)
                return;

            var frame = game.Frames.Predicted;
            if (frame.Has<KCC>(_entityRef) == false)
                return;

            var kcc = frame.Get<KCC>(_entityRef);
            UpdateRing(kcc.Data.IsGrounded);

            Vector3 velocity = kcc.Data.RealVelocity.ToUnityVector3();
            velocity.y = 0f;
            UpdateMoveArrow(velocity);

            UpdateTargetArrow(frame);
            UpdateAccessoryArrow(frame);
        }

        private void UpdateRing(bool isGrounded)
        {
            float fadeSpeed = ringFadeDuration > 0f ? 1f / ringFadeDuration : float.MaxValue;
            _ringAlpha = Mathf.MoveTowards(_ringAlpha, isGrounded ? 1f : 0f, fadeSpeed * Time.deltaTime);

            Color ringColor = _baseRingColor;
            ringColor.a = _baseRingColor.a * _ringAlpha;
            ringSprite.color = ringColor;
            ringSprite.enabled = _ringAlpha > 0f;

            // _glowPulseAlpha is driven by StartGlowPulse's PrimeTween loop - composed here rather
            // than tweening color.a directly, so the grounded fade and the pulse don't fight over
            // the same channel.
            Color glowColor = _baseGlowColor;
            glowColor.a = _baseGlowColor.a * _ringAlpha * _glowPulseAlpha;
            ringGlowSprite.color = glowColor;
            ringGlowSprite.enabled = glowColor.a > 0f;
        }

        private void UpdateMoveArrow(Vector3 velocity)
        {
            bool isMoving = velocity.sqrMagnitude > moveSpeedThreshold * moveSpeedThreshold;

            // Same grounded fade as the ring - fades out on leaving the ground, in on landing,
            // instead of just popping on/off with isMoving.
            ApplyMoveArrowColor(moveArrowSprite, _baseMoveArrowColor, 1f, isMoving);
            ApplyMoveArrowColor(moveArrowTrailSprite, _baseMoveArrowTrailColor, moveArrowTrailAlphaScale, isMoving);

            if (isMoving == false)
            {
                _hasMoveHeading = false;
                return;
            }

            float targetHeading = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
            _currentMoveHeading = _hasMoveHeading
                ? Mathf.MoveTowardsAngle(_currentMoveHeading, targetHeading, moveTurnSpeed * Time.deltaTime)
                : targetHeading;
            _hasMoveHeading = true;

            // Both arrows share the same heading state - they're a lead/trail pair on one
            // direction, not two independently-turning indicators.
            PositionAndRotateArrow(moveArrowSprite, _baseMoveArrowLocalPosition, _currentMoveHeading, moveArrowOffsetDistance, moveArrowRotationOffset);
            PositionAndRotateArrow(moveArrowTrailSprite, _baseMoveArrowTrailLocalPosition, _currentMoveHeading, moveArrowTrailOffsetDistance, moveArrowTrailRotationOffset);
        }

        private void ApplyMoveArrowColor(SpriteRenderer arrow, Color baseColor, float alphaScale, bool isMoving)
        {
            Color color = baseColor;
            color.a = baseColor.a * _ringAlpha * alphaScale;
            arrow.color = color;
            arrow.enabled = isMoving && color.a > 0f;
        }

        private void UpdateTargetArrow(Frame frame)
        {
            EntityRef target = frame.Has<Aim>(_entityRef) == true ? frame.Get<Aim>(_entityRef).Target : EntityRef.None;
            Transform targetTransform = target != EntityRef.None ? EntityViewManager.Instance.GetEntityTransform(target) : null;
            bool hasTarget = targetTransform != null;

            // Same grounded fade as the ring - fades out on leaving the ground, in on landing,
            // instead of just popping on/off with hasTarget.
            Color color = _baseTargetArrowColor;
            color.a = _baseTargetArrowColor.a * _ringAlpha;
            targetArrowSprite.color = color;
            targetArrowSprite.enabled = hasTarget && color.a > 0f;

            if (hasTarget == false)
            {
                _hasTargetHeading = false;
                return;
            }

            Vector3 toTarget = targetTransform.position - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude < 0.0001f)
                return;

            float targetHeading = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            _currentTargetHeading = _hasTargetHeading
                ? Mathf.MoveTowardsAngle(_currentTargetHeading, targetHeading, targetTurnSpeed * Time.deltaTime)
                : targetHeading;
            _hasTargetHeading = true;

            PositionAndRotateArrow(targetArrowSprite, _baseTargetArrowLocalPosition, _currentTargetHeading, targetArrowOffsetDistance, targetArrowRotationOffset);
        }

        // Same shape as UpdateTargetArrow above, pointed at this player's own dropped accessory
        // instead of an Aim target (see docs/accessory-guard.md). AccessoryGuard.Accessory is
        // EntityRef.None while the accessory is worn or broken, and broken debris is deliberately
        // never tracked by it, so "is there something to walk back to" needs no extra state here.
        private void UpdateAccessoryArrow(Frame frame)
        {
            if (accessoryArrowSprite == null && accessoryIconSprite == null)
                return;

            bool shown = TryResolveAccessoryDirection(frame, out Vector3 toAccessory);

            // Edge-triggered, not per-frame: the pointer only pops in on the transition into shown -
            // a block dropping the accessory, or walking back out past accessoryHideWithinDistance.
            if (shown != _accessoryShown)
            {
                _accessoryShown = shown;

                if (shown == true)
                {
                    _accessoryRendering = true;
                    PlayAccessoryAppear();
                }
                else
                {
                    PlayAccessoryDisappear();
                }
            }

            // Same grounded fade as the ring, same as every other arrow here - gated on
            // _accessoryRendering rather than `shown` so the shrink-away above gets the frames it
            // needs before the renderers switch off. NOTE this writes sprite.color every frame, so
            // the colours that matter are _baseAccessoryArrowColor/_baseAccessoryIconColor above -
            // editing the SpriteRenderer's own colour mid-play is overwritten on the next frame.
            ApplyAccessoryColor(accessoryArrowSprite, _baseAccessoryArrowColor, _accessoryRendering);
            ApplyAccessoryColor(accessoryIconSprite, _baseAccessoryIconColor, _accessoryRendering);

            if (shown == false)
            {
                _hasAccessoryHeading = false;
                return;
            }

            float targetHeading = Mathf.Atan2(toAccessory.x, toAccessory.z) * Mathf.Rad2Deg;
            _currentAccessoryHeading = _hasAccessoryHeading
                ? Mathf.MoveTowardsAngle(_currentAccessoryHeading, targetHeading, accessoryTurnSpeed * Time.deltaTime)
                : targetHeading;
            _hasAccessoryHeading = true;

            PositionAndRotateArrow(accessoryArrowSprite, _baseAccessoryArrowLocalPosition, _currentAccessoryHeading, accessoryArrowOffsetDistance, accessoryArrowRotationOffset);

            // The icon rides the same heading but never takes the arrow's rotation - it's a picture
            // of the accessory, not a direction indicator, so spinning it would just look broken.
            PositionKeepingRotation(accessoryIconSprite, _baseAccessoryIconLocalPosition, _currentAccessoryHeading, accessoryIconOffsetDistance);
            ApplyAccessoryIconBillboard();
            ApplyAccessoryIconCentering();
        }

        private bool TryResolveAccessoryDirection(Frame frame, out Vector3 toAccessory)
        {
            toAccessory = default;

            // No AccessoryGuard at all means the mechanic is off entirely (RuntimeConfig
            // .AccessoryGuardConfig unassigned - see CharacterSystem.SeedAccessoryGuard).
            if (frame.TryGet<AccessoryGuard>(_entityRef, out var guard) == false)
                return false;

            EntityRef accessory = guard.Accessory;

            bool tracked = accessory != EntityRef.None
                && frame.Exists(accessory)
                && (guard.State == AccessoryGuardState.Dropped
                    || (guard.State == AccessoryGuardState.Airborne && accessoryShowWhileAirborne));

            if (tracked == false)
                return false;

            if (TryResolveEntityPosition(frame, accessory, out Vector3 accessoryPosition) == false)
                return false;

            toAccessory = accessoryPosition - transform.position;
            toAccessory.y = 0f;

            // XZ only, deliberately: an accessory that landed on a ledge above shouldn't read as
            // further away than one at your feet (it may land higher - see PopVelocity.CanLandHigher).
            float distance = toAccessory.magnitude;

            if (distance < 0.0001f)
                return false;

            return accessoryHideWithinDistance <= 0f || distance > accessoryHideWithinDistance;
        }

        // Prefers the entity's own interpolated view Transform (the same EntityViewManager lookup the
        // target arrow uses) so the arrow can't shimmer against the sprite it's pointing at, and falls
        // back to the simulation's Transform3D - which is what actually runs unless the dropped-
        // accessory prototype carries an EntityViewCacheInit.
        private static bool TryResolveEntityPosition(Frame frame, EntityRef entity, out Vector3 position)
        {
            Transform view = EntityViewManager.Instance != null ? EntityViewManager.Instance.GetEntityTransform(entity) : null;

            if (view != null)
            {
                position = view.position;
                return true;
            }

            if (frame.TryGet<Transform3D>(entity, out var transform3D))
            {
                position = transform3D.Position.ToUnityVector3();
                return true;
            }

            position = default;
            return false;
        }

        private void ApplyAccessoryColor(SpriteRenderer sprite, Color baseColor, bool shown)
        {
            if (sprite == null)
                return;

            Color color = baseColor;
            color.a = baseColor.a * _ringAlpha;
            sprite.color = color;
            sprite.enabled = shown && color.a > 0f;
        }

        // Positions along the heading exactly like PositionAndRotateArrow, but leaves rotation alone.
        private static void PositionKeepingRotation(SpriteRenderer sprite, Vector3 baseLocalPosition, float heading, float offsetDistance)
        {
            if (sprite == null)
                return;

            float headingRad = heading * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(headingRad), 0f, Mathf.Cos(headingRad));
            sprite.transform.localPosition = baseLocalPosition + direction * offsetDistance;
        }

        // Pop-in for both accessory sprites the moment there is something to point at. Scale rather
        // than alpha: alpha is already spoken for by the ring's own grounded fade (ApplyAccessoryColor
        // multiplies into it every frame), so a fade-in here would be fighting it - and OutBack's
        // overshoot is what makes the pointer register in peripheral vision, which is the whole job.
        private void PlayAccessoryAppear()
        {
            PlayAppearTween(accessoryArrowSprite, _baseAccessoryArrowScale, ref _accessoryArrowScaleTween);
            PlayAppearTween(accessoryIconSprite, _baseAccessoryIconScale, ref _accessoryIconScaleTween);
        }

        private void PlayAppearTween(SpriteRenderer sprite, Vector3 baseScale, ref Tween tween)
        {
            if (sprite == null)
                return;

            if (tween.isAlive)
                tween.Stop();

            if (accessoryAppearDuration <= 0f)
            {
                sprite.transform.localScale = baseScale;
                return;
            }

            tween = Tween.Scale(sprite.transform, Vector3.zero, baseScale, accessoryAppearDuration,
                accessoryAppearEase);
        }

        // The mirror of the pop-in, played when the accessory stops being something to walk to -
        // recovered, or close enough that accessoryHideWithinDistance takes over. Shrinking from
        // whatever scale it currently sits at (rather than from the authored one) is what lets a
        // hide interrupt an appear mid-tween without a visible jump.
        private void PlayAccessoryDisappear()
        {
            bool arrowStarted = PlayDisappearTween(accessoryArrowSprite, ref _accessoryArrowScaleTween);
            bool iconStarted = PlayDisappearTween(accessoryIconSprite, ref _accessoryIconScaleTween);

            // Both run the same duration, so one completion callback ends the render window for the
            // pair. Stop() never fires OnComplete, so an appear interrupting a shrink can't turn the
            // renderers off behind its own back.
            if (arrowStarted == true)
                _accessoryArrowScaleTween.OnComplete(this, view => view._accessoryRendering = false);
            else if (iconStarted == true)
                _accessoryIconScaleTween.OnComplete(this, view => view._accessoryRendering = false);
            else
                _accessoryRendering = false;
        }

        private bool PlayDisappearTween(SpriteRenderer sprite, ref Tween tween)
        {
            if (sprite == null)
                return false;

            if (tween.isAlive)
                tween.Stop();

            if (accessoryDisappearDuration <= 0f)
            {
                sprite.transform.localScale = Vector3.zero;
                return false;
            }

            tween = Tween.Scale(sprite.transform, Vector3.zero, accessoryDisappearDuration,
                accessoryDisappearEase);
            return true;
        }

        // Normalises the icon to 1x1 world units * accessoryIconScale regardless of the sprite's own
        // pixel size and PPU - the same job ChunkDetailScatter.ResolveUnitScale does for hand-placed
        // detail props, and for the same reason: one hero's accessory art has no idea how big another
        // hero's is, so without this the pointer changes size per hero for no design reason.
        //
        // Also captures the sprite's pivot->centre offset at that final scale, since a sprite imported
        // with a bottom or off-centre pivot would otherwise orbit hanging off the point rather than
        // centred on it (Sprite.bounds is in local units, pivot-relative, PPU already divided out).
        private void ResolveAccessoryIconLayout()
        {
            _accessoryIconCenterOffset = Vector2.zero;

            if (accessoryIconSprite == null || accessoryIconSprite.sprite == null)
                return;

            Sprite sprite = accessoryIconSprite.sprite;
            Vector2 size = sprite.bounds.size;
            float largest = Mathf.Max(size.x, size.y);

            // 0 (or a degenerate sprite) opts out: keep whatever the prefab authored, and measure the
            // centre offset against that same scale so the centring stays correct either way.
            float scale = accessoryIconScale > 0f && largest > 0.0001f
                ? accessoryIconScale / largest
                : accessoryIconSprite.transform.localScale.x;

            _baseAccessoryIconScale = Vector3.one * scale;
            accessoryIconSprite.transform.localScale = _baseAccessoryIconScale;
            _accessoryIconCenterOffset = sprite.bounds.center;
        }

        // Shifts the icon so the SPRITE'S CENTRE lands on the orbit point rather than its pivot.
        // Applied after the billboard write and in that rotation's own right/up axes, so it stays
        // correct however the sprite is currently facing - and it moves the icon within its own
        // billboard plane only, so the authored local Y still decides how high off the ground the
        // icon's centre rides.
        private void ApplyAccessoryIconCentering()
        {
            if (accessoryIconSprite == null || _accessoryIconCenterOffset == Vector2.zero)
                return;

            Transform icon = accessoryIconSprite.transform;
            Vector2 offset = _accessoryIconCenterOffset * icon.localScale.x;
            icon.position -= icon.right * offset.x + icon.up * offset.y;
        }

        // Camera-facing rotation for the icon, applied AFTER PositionKeepingRotation has placed it -
        // it is a picture of the accessory, so it should stand up and read like the world pickup does
        // rather than lie flat like the arrows around it. Written in world space, so the ring root's
        // own orientation doesn't tilt it. Same LookRotation the Billboard component uses, reproduced
        // rather than delegated for the one-writer reason documented at the capture site; with this
        // off the icon simply keeps the flat authored rotation stamped there instead.
        private void ApplyAccessoryIconBillboard()
        {
            if (accessoryIconBillboard == false || accessoryIconSprite == null)
                return;

            if (_accessoryIconCamera == null)
                _accessoryIconCamera = Camera.main;

            if (_accessoryIconCamera == null)
                return;

            accessoryIconSprite.transform.rotation =
                Quaternion.LookRotation(_accessoryIconCamera.transform.forward, Vector3.up);
        }

        // heading/rotationOffset stay separate - the position needs to sit along the real
        // direction regardless of which way the arrow art faces, only the visual spin needs the
        // correction.
        private static void PositionAndRotateArrow(SpriteRenderer arrow, Vector3 baseLocalPosition, float heading, float offsetDistance, float rotationOffset)
        {
            // Null-tolerant for the optional accessory arrow - every other caller passes a required one.
            if (arrow == null)
                return;

            arrow.transform.localRotation = Quaternion.Euler(90f, heading + rotationOffset, 0f);

            float headingRad = heading * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(headingRad), 0f, Mathf.Cos(headingRad));
            arrow.transform.localPosition = baseLocalPosition + direction * offsetDistance;
        }
    }
}

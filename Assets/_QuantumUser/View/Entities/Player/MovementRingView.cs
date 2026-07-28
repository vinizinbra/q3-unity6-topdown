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

            _ringAlpha = 0f;

            if (isLocalPlayer == false)
                return;

            Color ringColor = ResolveRingColor(game.Frames.Verified);
            _baseRingColor = new Color(ringColor.r, ringColor.g, ringColor.b, _baseRingAlpha);
            _baseGlowColor = new Color(ringColor.r, ringColor.g, ringColor.b, _baseGlowAlpha);
            _baseMoveArrowColor = new Color(ringColor.r, ringColor.g, ringColor.b, _baseMoveArrowAlpha);
            _baseMoveArrowTrailColor = new Color(ringColor.r, ringColor.g, ringColor.b, _baseMoveArrowTrailAlpha);

            StartGlowPulse();
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);

            if (_glowTween.isAlive)
                _glowTween.Stop();
        }

        private Color ResolveRingColor(Frame frame)
        {
            if (frame.Has<CharacterStats>(_entityRef) == false)
                return Color.white;

            CharacterData data = frame.FindAsset(frame.Get<CharacterStats>(_entityRef).CharacterData);
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

        // heading/rotationOffset stay separate - the position needs to sit along the real
        // direction regardless of which way the arrow art faces, only the visual spin needs the
        // correction.
        private static void PositionAndRotateArrow(SpriteRenderer arrow, Vector3 baseLocalPosition, float heading, float offsetDistance, float rotationOffset)
        {
            arrow.transform.localRotation = Quaternion.Euler(90f, heading + rotationOffset, 0f);

            float headingRad = heading * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(headingRad), 0f, Mathf.Cos(headingRad));
            arrow.transform.localPosition = baseLocalPosition + direction * offsetDistance;
        }
    }
}

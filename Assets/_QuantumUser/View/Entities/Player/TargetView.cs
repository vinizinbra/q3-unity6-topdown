using PrimeTween;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Ground-level targeting reticle. Local-player-only (forced in Awake - this only makes sense
    // on the player's own screen, not remote players' views). Unparents itself on Awake since it
    // needs to move independently of the player - tracks Aim.Target each frame (see AimSystem) and
    // follows that entity's feet via EntityViewManager's EntityRef->Transform cache, spinning
    // continuously for a "locked on" read. On acquiring a new target (from no target at all) the
    // reticle starts at the player's own position and eases toward the target (instead of
    // snapping straight there), shrinking while it travels, then scales back up with an overshoot
    // once it arrives - reads as the lock "flying out" from the player and snapping onto the
    // target. Switching directly between two targets skips that intro and just keeps flying from
    // wherever the reticle already is. Hidden immediately whenever there's no target.
    public class TargetView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Sprite shown/hidden based on whether there's currently a target. This object is repositioned to the target's feet every frame.")]
        private SpriteRenderer reticleSprite;
        [SerializeField] private Transform targetTransform;
        [SerializeField, Tooltip("Degrees per second the reticle spins in place.")]
        private float spinSpeed = 90f;
        [SerializeField, Tooltip("Small lift above the target's base position, avoids z-fighting with the ground.")]
        private float groundOffset = 0.02f;
        [SerializeField, Tooltip("Depth offset applied to the reticle's world Z position.")]
        private float depthOffset = 0f;

        [Header("Travel (player -> target)")]
        [SerializeField, Tooltip("How fast the reticle eases toward the target's position. Higher = snappier/closer to instant.")]
        private float followLerpSpeed = 12f;
        [SerializeField, Tooltip("How fast the reticle eases toward travelScaleMultiplier while it's still closing in on the target.")]
        private float scaleLerpSpeed = 10f;
        [SerializeField, Range(0.1f, 1f), Tooltip("Fraction of the reticle's normal size it shrinks toward while traveling to the target.")]
        private float travelScaleMultiplier = 0.5f;
        [SerializeField, Tooltip("Once within this distance of the target, the reticle is considered arrived and scales back up to full size.")]
        private float arriveDistance = 0.15f;
        [SerializeField, Tooltip("How long the arrival scale-up takes. Paired with bounceEase's overshoot, this is the whole \"bounce\" - no separate punch after.")]
        private float bounceDuration = 0.3f;
        [SerializeField] private Ease bounceEase = Ease.OutBack;

        [Header("Size by target radius")]
        [SerializeField, Tooltip("Target collider radius that maps to the reticle's normal (1x) size. Enemies at or below this stay normal size - only bigger ones scale the reticle up, so it stays as visible as the enemy it's covering instead of getting lost under a big body.")]
        private float referenceRadius = 0.5f;
        [SerializeField, Tooltip("Upper bound on how much a huge target (e.g. a boss) can scale the reticle up.")]
        private float maxRadiusScale = 2.5f;
        [SerializeField, Tooltip("Extra radius (world units) added on top of the target's collider radius before sizing the reticle, so it sits slightly outside the enemy's body rather than flush with it. Mirrors EnemyView's own viewRadiusPadding.")]
        private float radiusPadding = 0.1f;

        private Vector3 _baseScale;
        private Vector3 _lockScale;
        private EntityRef _lastTarget;
        private bool _arrived;

        public override void Awake()
        {
            base.Awake();

            // This reticle only makes sense on the local player's own screen.
            executeOnlyOnLocal = true;

            // Needs to move independently of the player's own transform to follow the target.
            reticleSprite.transform.SetParent(null);

            // Lie flat on the ground facing up, same convention as PlayerShadow - spinning below
            // only ever rotates around local Z (now the up axis) so it stays flat.
            reticleSprite.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // Captured after SetParent - unparenting with worldPositionStays can rescale the local
            // transform, so this is the reticle's true resting (full-size) scale.
            _baseScale = reticleSprite.transform.localScale;
        }

        // QUpdate (and therefore the "hasTarget" enable/disable toggle below) never runs for
        // remote players - executeOnlyOnLocal short-circuits it. Without this, a remote view's
        // reticle would just sit at whatever enabled state the prefab was authored with.
        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            reticleSprite.gameObject.SetActive(QuantumHelper.IsLocalPlayer(_playerRef));
        }

        // reticleSprite was unparented in Awake so it can move independently of the player -
        // that also means it's no longer a child of this view's hierarchy, so destroying the
        // view (e.g. on death/respawn) never cascades to it. Without this it leaks as an
        // orphaned sprite frozen at its last position, and the next spawn's TargetView creates
        // another one on top of it.
        public override void OnDestroy()
        {
            base.OnDestroy();

            if (reticleSprite != null)
            {
                Tween.StopAll(reticleSprite.transform);
                Destroy(reticleSprite.gameObject);
            }
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (reticleSprite == null)
                return;

            var frame = game.Frames.Predicted;

            EntityRef target = frame.Has<Aim>(_entityRef) == true ? frame.Get<Aim>(_entityRef).Target : EntityRef.None;
            targetTransform = target != EntityRef.None ? EntityViewManager.Instance.GetEntityTransform(target) : null;
            bool hasTarget = targetTransform != null;

            if (target != _lastTarget)
            {
                if (hasTarget == true)
                    OnTargetAcquired(frame, target);

                _lastTarget = target;
            }

            reticleSprite.enabled = hasTarget;

            if (hasTarget == false)
                return;

            float dt = Time.deltaTime;

            Vector3 desiredPosition = targetTransform.position;

            // targetTransform.position is the target's live Transform3D.Position - for a Grounded
            // enemy that's its collider CENTER, sitting a full radius above the ground (same
            // reasoning EnemyMovementUtility.IsGrounded's own ResolveShapeHalfHeight use is built
            // on), not its feet. Locked down to real ground level there so the reticle reads as
            // resting under the target instead of floating at chest height. A Flying enemy or one
            // currently mid climb/gap traversal hop (EnemyMovementUtility.BeginTraversalJump) keeps
            // today's behavior instead - aims at the raw, genuinely-elevated position, same as
            // always, since locking that to the ground would leave the reticle behind while the
            // target is visibly airborne.
            if (IsTargetAirborne(frame, target) == false)
                desiredPosition.y = ResolveGroundedY(frame, target, desiredPosition.y);

            desiredPosition.y += groundOffset;
            desiredPosition.z += depthOffset;

            Vector3 position = Vector3.Lerp(reticleSprite.transform.position, desiredPosition, 1f - Mathf.Exp(-followLerpSpeed * dt));
            reticleSprite.transform.position = position;

            if (_arrived == false)
                UpdateApproach(position, desiredPosition, dt);

            reticleSprite.transform.Rotate(0f, 0f, spinSpeed * dt, Space.Self);
        }

        // Only snaps to the player's position when there was no previous target - switching
        // directly between two targets should keep flying from wherever the reticle already is,
        // not restart the trip from the player. _lockScale is resolved fresh per target so a
        // switch from a small enemy to a big one also grows the reticle, not just a fresh lock.
        private void OnTargetAcquired(Frame frame, EntityRef target)
        {
            if (_lastTarget == EntityRef.None)
                reticleSprite.transform.position = transform.position;

            _lockScale = _baseScale * ResolveRadiusScale(frame, target);
            reticleSprite.transform.localScale = _lockScale;
            _arrived = false;
        }

        // Bigger targets get a bigger reticle so it doesn't get visually lost under a big body -
        // uses the target's real collider radius (same resolution EnemyMovementUtility.
        // ResolveEntityRadius uses on the Simulation side).
        private float ResolveRadiusScale(Frame frame, EntityRef target)
        {
            if (referenceRadius <= 0f || frame.TryGet<PhysicsCollider3D>(target, out var collider) == false)
                return 1f;

            float radius = EnemyMovementUtility.ResolveShapeRadius(collider.Shape).AsFloat + radiusPadding;
            return Mathf.Clamp(radius / referenceRadius, 1f, maxRadiusScale);
        }

        // No Enemy component at all (e.g. a Decoy) - nothing to ground-lock against, so this keeps
        // today's raw-position behavior unchanged rather than guessing.
        private static bool IsTargetAirborne(Frame frame, EntityRef target)
        {
            if (frame.TryGet<Enemy>(target, out Enemy enemy) == false)
                return true;

            EnemyDataAsset enemyData = frame.FindAsset(enemy.EnemyData);
            return enemyData.Stats.Height.InitialState == EnemyHeightState.Flying
                || enemy.TraversalJumpDuration > Photon.Deterministic.FP._0;
        }

        // Same per-shape half-height math EnemyMovementUtility.IsGrounded already uses to find how
        // far below a Grounded entity's own collider-center pivot the real ground sits - reused here
        // (already public for exactly this kind of outside caller) instead of a raycast, since every
        // enemy's collider is seeded as a plain sphere at spawn (EnemySystem.SeedRadius) and a
        // sphere's radius already IS its own half-height.
        private static float ResolveGroundedY(Frame frame, EntityRef target, float rawY)
        {
            if (frame.TryGet<PhysicsCollider3D>(target, out var collider) == false)
                return rawY;

            return rawY - EnemyMovementUtility.ResolveShapeHalfHeight(collider.Shape).AsFloat;
        }

        private void UpdateApproach(Vector3 position, Vector3 desiredPosition, float dt)
        {
            Vector3 travelScale = _lockScale * travelScaleMultiplier;
            reticleSprite.transform.localScale = Vector3.Lerp(reticleSprite.transform.localScale, travelScale, 1f - Mathf.Exp(-scaleLerpSpeed * dt));

            if (Vector3.Distance(position, desiredPosition) > arriveDistance)
                return;

            _arrived = true;
            Tween.Scale(reticleSprite.transform, reticleSprite.transform.localScale, _lockScale, bounceDuration, bounceEase);
        }
    }
}

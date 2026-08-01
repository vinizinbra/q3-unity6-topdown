using Photon.Deterministic;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Computes the gun's aim rotation and secondary-motion offsets from the entity's Aim.Angle
    // (see AimSystem), which is already resolved once in simulation and holds its last direction
    // while stationary - so this doesn't need its own speed threshold or a MonoBehaviour movement
    // reference the way a client-side equivalent would. Reconstructs a world-space direction from
    // that angle (tilted toward Aim.Target's actual elevation when one is locked, since Angle
    // itself is always flat - see ResolveAimWorldDirection) and projects it onto the camera's
    // screen plane (right/up) so it reads correctly on screen regardless of camera pitch/yaw -
    // the same trick BlobAnimationView uses for tilt.
    //
    // Doesn't touch any transform itself: this only computes the generic, weapon-agnostic pose
    // (rotation, aim direction, flip, camera basis, and the summed sway/follow/recoil offset) and
    // hands it to WeaponView.ApplyAim() every frame. WeaponView owns the per-weapon tuning (the
    // cardinal position offsets, hand grips) and actually writes to the transform - so this stays
    // reusable across whatever weapon prefab WeaponViewController spawns, without needing to
    // know anything about that weapon's specific art/offsets.
    //
    // Two secondary-motion layers ride on top of the base aim rotation, each with a single
    // "how much" and "how loose" knob rather than a tunable per axis/component:
    //  - Body Follow: rocks/bobs with BlobAnimationView's stride, and lags a beat behind its
    //    slower lean. Internal per-axis ratios are fixed constants, tuned once for a natural feel.
    //  - Speed Sway: a damped spring offsets the gun opposite the entity's current velocity, so
    //    it trails while accelerating and swings past rest when it suddenly stops or lands.
    public class PlayerGunAimView : CustomQuantumEntityViewComponent
    {
        [Header("References")]
        [SerializeField, Tooltip("Falls back to Camera.main if left empty.")]
        private Transform cameraTransform;
        [SerializeField, Tooltip("Falls back to a BlobAnimationView anywhere under the rig root if left empty. Source of the Body Follow motion below.")]
        private BlobAnimationView torsoFollow;
        [SerializeField, Tooltip("Assign explicitly - the WeaponViewController on this same rig. Its CurrentWeaponView is read fresh every frame (see WeaponHandGripView) so this keeps tracking correctly across a future weapon swap, and receives the computed pose to apply to the transform.")]
        private WeaponViewController weaponInstantiator;

        [Header("Aim")]
        [SerializeField, Tooltip("Degrees added so the sprite's own rest orientation lines up with angle 0 (screen-right). -90 if the gun art is drawn pointing up.")]
        private float angleOffset = -90f;
        [SerializeField, Tooltip("How quickly the gun turns to face a new direction. Higher = snappier.")]
        private float rotationSmoothing = 20f;
        [SerializeField, Tooltip("Mirror on the local Y axis when facing left, instead of continuing to rotate upside-down. Flips this whole object's subtree (sprite + any child fire points/muzzles), not just the SpriteRenderer. Only used as a fallback when no BlobAnimationView is found - normally the gun mirrors BlobAnimationView.FacingSign directly, so it always stays in sync with the body instead of flipping independently based on aim.")]
        private bool flipWhenAimingLeft = true;

        [Header("Body Follow (idle sway / walk-run stride)")]
        [SerializeField, Tooltip("Turn this whole layer on/off - useful for isolating it from Speed Sway below while tuning either one.")]
        private bool enableBodyFollow = true;
        [SerializeField, Range(0f, 1f), Tooltip("Overall strength of the torso's lean/rock/bob bleeding into the gun.")]
        private float followAmount = 0.5f;
        [SerializeField, Tooltip("How slowly the gun catches up to the torso's slow lean (acceleration tilt). The per-stride rock/bob rides along mostly unlagged - smoothing that out just makes running look static instead of delayed.")]
        private float followLag = 6f;
        [SerializeField, Tooltip("Screen-up offset per unit the torso's jump squash/stretch deviates from rest (BlobAnimationView.CurrentRootVerticalScale - 1, e.g. 0.2 at a 1.2x stretch). Positive pushes the gun up when the body stretches taller. Set to 0 to disable.")]
        private float squashPositionCompensation = 1f;

        [Header("Speed Sway (weight/inertia feel)")]
        [SerializeField, Tooltip("Turn this whole layer on/off - useful for isolating it from Body Follow above while tuning either one.")]
        private bool enableSpeedSway = true;
        [SerializeField, Range(0f, 1f), Tooltip("Overall strength of the velocity-based inertia sway.")]
        private float swayAmount = 0.5f;
        [SerializeField, Range(0f, 1f), Tooltip("How lively the sway spring is: low = soft and slow to settle, high = snappy and overshoots past rest before settling (the 'kicks the other way when you stop/land' feel).")]
        private float swaySpringiness = 0.5f;
        [SerializeField, Range(1f, 4f), Tooltip("How much faster vertical sway (jump/fall + forward run) settles compared to horizontal. 1 = same speed as horizontal; higher stops jump sway from lingering through the whole airtime.")]
        private float verticalSwaySpringMultiplier = 1.8f;

        // Fixed internal ratios for Body Follow - tuned once so followAmount alone stays coherent
        // instead of exposing a coefficient per axis/component.
        private const float LeanRotationScale = 1f;
        private const float RockRotationScale = 1f;
        private const float BobPositionScale = 1f;
        private const float LeanPositionPerDegree = -0.01f; // negative: swings right when leaning right, matching run direction

        // Fixed internal ratios for Speed Sway - swayAmount scales these uniformly.
        private const float HorizontalSwayPerSpeed = 0.015f;
        private const float VerticalSwayPerSpeed = 0.02f;
        private const float MaxSway = 0.3f;
        private const float SwaySpringFrequencyMin = 2.5f;
        private const float SwaySpringFrequencyMax = 6.5f;
        private const float SwaySpringDampingMin = 0.28f; // springiness = 1: snappy and bouncy
        private const float SwaySpringDampingMax = 0.6f;  // springiness = 0: soft, barely overshoots

        private float currentAngle;
        private Vector2 currentAimDir = Vector2.right; // persists if projection ever degenerates, so offset still applies
        private bool isFlipped;
        private float laggedLeanDegrees;
        private Vector2 swayOffset;
        private Vector2 swayVelocity;

        public override void Awake()
        {
            base.Awake();

            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;

            // Mirrors CustomQuantumEntityViewComponent's own entityView lookup - the gun is
            // often parented under a socket/bone that isn't a direct ancestor chain up to the
            // rig root, so GetComponentInParent alone can miss BlobAnimationView entirely.
            if (torsoFollow == null)
                torsoFollow = GetComponentInParent<BlobAnimationView>();
            if (torsoFollow == null)
                torsoFollow = transform.root.GetComponentInChildren<BlobAnimationView>();
        }

        protected override void QUpdate(QuantumGame game)
        {
            WeaponView weaponView = weaponInstantiator != null ? weaponInstantiator.CurrentWeaponView : null;
            if (cameraTransform == null || weaponView == null) return;

            var frame = game.Frames.Predicted;
            if (frame.Has<Aim>(_entityRef) == false) return;

            float dt = Time.deltaTime;

            Vector3 worldDir = ResolveAimWorldDirection(frame, frame.Get<Aim>(_entityRef));
            Vector2 screenDir = ProjectToScreen(worldDir);

            float smoothT = 1f - Mathf.Exp(-rotationSmoothing * dt);

            if (screenDir.sqrMagnitude > 0.0001f)
            {
                float targetAngle = Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg + angleOffset;
                currentAngle = Mathf.LerpAngle(currentAngle, targetAngle, smoothT);
                currentAimDir = screenDir.normalized;
            }

            // Flip in lockstep with the character's own facing rather than deriving it
            // independently from aim direction - otherwise the gun could mirror one way while
            // the body still faces the other (e.g. aiming behind you).
            isFlipped = torsoFollow != null
                ? torsoFollow.FacingSign < 0f
                : flipWhenAimingLeft && screenDir.x < 0f;

            // Body Follow: lean lags on its own slow time constant (a visible delay for the
            // body's overall acceleration tilt); rock/bob pass through unlagged since they
            // already oscillate every stride - artificial smoothing there just kills the motion.
            float followRotation = 0f;
            float followRight = 0f;
            float followUp = 0f;
            if (enableBodyFollow && torsoFollow != null)
            {
                float leanT = 1f - Mathf.Exp(-followLag * dt);
                laggedLeanDegrees = Mathf.LerpAngle(laggedLeanDegrees, torsoFollow.CurrentLeanDegrees, leanT);
                float rockDegrees = torsoFollow.CurrentRockDegrees;
                float bobOffset = torsoFollow.CurrentBobOffset;

                followRotation = (laggedLeanDegrees * LeanRotationScale + rockDegrees * RockRotationScale) * followAmount;
                // Lean is a left/right-signed quantity (BlobAnimationView derives it from
                // Mathf.Sign(velocity.x)), so its positional swing belongs on the right axis, not up.
                followRight = laggedLeanDegrees * LeanPositionPerDegree * followAmount;
                followUp = bobOffset * BobPositionScale * followAmount;

                // Counteracts root's jump squash/stretch dragging the gun's rest position along
                // with it in world space (a taller root pushes a child further from the pivot).
                // Driven by the actual scale BlobAnimationView applied this frame rather than
                // reading root's transform ourselves - root is simultaneously rotated (billboard)
                // and non-uniformly scaled, and Transform.lossyScale can't reliably decompose
                // that combination.
                followUp += (torsoFollow.CurrentRootVerticalScale - 1f) * squashPositionCompensation;
            }
            else
            {
                laggedLeanDegrees = 0f;
            }

            // Speed Sway: a lightly-damped spring pulls the gun opposite the entity's current
            // velocity - trailing back while accelerating, then swinging past rest once velocity
            // drops (e.g. coming to a stop), and offsetting opposite vertical speed so a jump
            // doesn't look glued to the torso.
            if (enableSpeedSway)
            {
                Vector3 velocity = Vector3.zero;
                if (frame.Has<KCC>(_entityRef))
                    velocity = frame.Get<KCC>(_entityRef).Data.RealVelocity.ToUnityVector3();

                // Ground velocity (x/z) is projected through the full camera basis, not just
                // cameraTransform.right - on a pitched top-down camera, running toward/away from
                // the camera shows up mostly as screen-up motion, not screen-right, so dropping
                // the projected .y here would make forward/backward running produce almost no
                // sway. World-vertical (jump/fall) speed stacks onto that same up axis, unprojected.
                Vector2 horizontalScreenVel = ProjectToScreen(new Vector3(velocity.x, 0f, velocity.z));
                float maxSway = MaxSway * swayAmount;
                Vector2 swayTarget = new Vector2(
                    Mathf.Clamp(-horizontalScreenVel.x * HorizontalSwayPerSpeed * swayAmount, -maxSway, maxSway),
                    Mathf.Clamp((-horizontalScreenVel.y * HorizontalSwayPerSpeed - velocity.y * VerticalSwayPerSpeed) * swayAmount, -maxSway, maxSway));

                float springFrequency = Mathf.Lerp(SwaySpringFrequencyMin, SwaySpringFrequencyMax, swaySpringiness);
                float springDamping = Mathf.Lerp(SwaySpringDampingMax, SwaySpringDampingMin, swaySpringiness);
                // Vertical (jump/fall + forward run) settles faster than horizontal - otherwise
                // the sway from a jump lingers through the whole airtime instead of snapping back.
                Vector2 springFrequencyPerAxis = new Vector2(springFrequency, springFrequency * verticalSwaySpringMultiplier);
                IntegrateSway(swayTarget, springFrequencyPerAxis, springDamping, dt);
            }
            else
            {
                swayOffset = Vector2.zero;
                swayVelocity = Vector2.zero;
            }

            Quaternion facingCamera = Quaternion.LookRotation(cameraTransform.forward, Vector3.up);
            Vector2 extraOffset = new Vector2(swayOffset.x + followRight, swayOffset.y + followUp);

            // Ground-plane projection of worldDir, not screenDir - screenDir already went through
            // ProjectToScreen (camera right/up), which is the wrong basis for anything that needs
            // to stay level with the ground rather than the screen (see AimPose.FlatWorldDirection).
            Vector3 flatWorldDirection = new Vector3(worldDir.x, 0f, worldDir.z);
            if (flatWorldDirection.sqrMagnitude < 0.0001f)
                flatWorldDirection = Vector3.forward;

            var pose = new WeaponView.AimPose(
                currentAngle + followRotation,
                facingCamera,
                currentAimDir,
                isFlipped,
                extraOffset,
                cameraTransform.right,
                cameraTransform.up,
                flatWorldDirection.normalized);

            weaponView.ApplyAim(pose, smoothT);
        }

        // Damped harmonic oscillator pulling swayOffset toward a moving target, with overshoot/
        // bounce when damping < 1 - the same math BlobAnimationView uses for its landing spring,
        // just driven by an arbitrary target instead of a fixed 0, and with an independent
        // frequency per axis so vertical (jump) can settle faster than horizontal (run).
        private void IntegrateSway(Vector2 target, Vector2 frequency, float damping, float dt)
        {
            Vector2 omega = frequency * Mathf.PI * 2f;
            Vector2 displacement = swayOffset - target;
            Vector2 force = new Vector2(
                -omega.x * omega.x * displacement.x - 2f * damping * omega.x * swayVelocity.x,
                -omega.y * omega.y * displacement.y - 2f * damping * omega.y * swayVelocity.y);
            swayVelocity += force * dt;
            swayOffset += swayVelocity * dt;
        }

        private Vector2 ProjectToScreen(Vector3 worldDir)
        {
            return new Vector2(Vector3.Dot(worldDir, cameraTransform.right), Vector3.Dot(worldDir, cameraTransform.up));
        }

        // Aim.Angle is always flat (AimSystem deliberately ignores Y so flying/elevated enemies
        // don't skew which one counts as closest - see its own comment), so on its own the gun
        // would never visually tip up/down even when the target actually sits higher or lower -
        // only its screen-space bearing would change. Aiming at Aim.Target's real position instead
        // (mirroring ProjectileAimUtility.ResolveAimDirection, which real shots already use) makes
        // the visual tilt match where the shot actually goes. Falls back to the flat direction
        // while nothing is targeted (moving with no enemy in range).
        private Vector3 ResolveAimWorldDirection(Frame frame, Aim aim)
        {
            float angleRad = aim.Angle.AsFloat * Mathf.Deg2Rad;
            Vector3 flatDir = new Vector3(Mathf.Sin(angleRad), 0f, Mathf.Cos(angleRad));

            if (aim.Target == EntityRef.None || frame.Has<Transform3D>(aim.Target) == false)
                return flatDir;

            Vector3 selfPosition = frame.Get<Transform3D>(_entityRef).Position.ToUnityVector3();
            Vector3 targetPosition = frame.Get<Transform3D>(aim.Target).Position.ToUnityVector3();
            Vector3 delta = targetPosition - selfPosition;

            return delta.sqrMagnitude > 0.0001f ? delta : flatDir;
        }
    }
}

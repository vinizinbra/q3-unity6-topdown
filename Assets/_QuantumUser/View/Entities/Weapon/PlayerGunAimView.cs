using Photon.Deterministic;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Computes the gun's aim rotation and secondary-motion offsets from the entity's Aim.Angle
    // (see AimSystem), which is already resolved once in simulation and holds its last direction
    // while stationary - so this doesn't need its own speed threshold or a MonoBehaviour movement
    // reference the way a client-side equivalent would. While a real target is locked, the on-
    // screen rotation is resolved by WorldToScreenPoint-ing the weapon's own actual (elevated,
    // sway/follow-offset) position and the target's position and taking the delta between them
    // (see ResolveScreenAimDirection) - the real "what does this look like on screen" answer under
    // a perspective camera, correct regardless of camera pitch or how close/how far below the gun's
    // own pivot the target is. With no target, it falls back to reconstructing a flat world-space
    // direction from Aim.Angle and projecting it through the camera's basis vectors (right/up) -
    // the same trick BlobAnimationView uses for tilt, fine for a directionless facing but not for a
    // real point-to-point aim (see ProjectToScreen's own comment for why).
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
        // Resolved from cameraTransform in Awake (falls back to Camera.main) - the actual Camera
        // component, needed to WorldToScreenPoint the real weapon/target world positions.
        private Camera aimCamera;
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

        [Header("Jump Flip (follows BlobAnimationView.CurrentFlipDegrees)")]
        [SerializeField, Tooltip("Spin the weapon along with the body's flip even while aimed at a real target.")]
        private bool rotateWeaponWithTarget = false;
        [SerializeField, Tooltip("Spin the weapon along with the body's flip while it has no target to aim at. If both this and rotateWeaponWithTarget are off, the weapon never reacts to a flip at all.")]
        private bool rotateWeaponWithoutTarget = true;
        [SerializeField, Tooltip("How quickly the weapon's own rotation catches up to the body's flip (exponential lerp rate) - lower = more lag/inertia, so the gun trails the tumble instead of matching it frame-for-frame. Uses LerpAngle, so a reasonable value also eases smoothly through the flip's own 360°->0° reset instead of snapping - too low a value (falling more than ~180° behind) can make it briefly spin the wrong way to catch up.")]
        private float flipLagSpeed = 10f;

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
        // The weapon's own lagged copy of torsoFollow.CurrentFlipDegrees - see flipLagSpeed.
        private float laggedFlipDegrees;
        private Vector2 swayOffset;
        private Vector2 swayVelocity;

        public override void Awake()
        {
            base.Awake();

            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;

            // Real Camera component (not just its transform) - needed to WorldToScreenPoint the
            // actual weapon/target positions below instead of approximating with a direction-vector
            // projection through the camera's basis vectors.
            aimCamera = cameraTransform != null ? cameraTransform.GetComponentInParent<Camera>() : null;
            if (aimCamera == null)
                aimCamera = Camera.main;

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

            Aim aim = frame.Get<Aim>(_entityRef);
            Vector3 worldDir = ResolveAimWorldDirection(frame, aim);
            Vector2 screenDir = ResolveScreenAimDirection(frame, aim, weaponView, worldDir);

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

            // Jump Flip: which case(s) the weapon should spin along with the body for, per the two
            // flags - both off means it never reacts to a flip at all. Computed BEFORE Body Follow/
            // Speed Sway below (not just before pose construction) so weaponIsFlipping can gate
            // both of them - a flip is already a large, deliberate motion, and Body Follow's lean/
            // rock/bob or Speed Sway's velocity-reactive spring blending on top of it at the same
            // time made the two impossible to tell apart, especially while eyeballing
            // flipPivotOffset (a fast auto-hop is exactly when velocity/lean are also largest).
            bool hasTarget = aim.Target != EntityRef.None;
            bool shouldFollowFlip = (hasTarget && rotateWeaponWithTarget) || (hasTarget == false && rotateWeaponWithoutTarget);
            // Distinct from shouldFollowFlip - that's just "is the weapon configured to react in
            // this targeting state", true even at rest with no flip happening at all. This also
            // requires an actual flip in progress (CurrentFlipDegrees != 0, which BlobAnimationView
            // guarantees is exactly 0 whenever no flip is playing), so Body Follow/Speed Sway stay
            // fully live during ordinary movement and only cut out for the flip's own duration.
            bool weaponIsFlipping = shouldFollowFlip && torsoFollow != null && torsoFollow.CurrentFlipDegrees != 0f;

            float flipTarget = weaponIsFlipping ? torsoFollow.CurrentFlipDegrees : 0f;
            laggedFlipDegrees = Mathf.LerpAngle(laggedFlipDegrees, flipTarget, 1f - Mathf.Exp(-flipLagSpeed * dt));
            float flipRotation = laggedFlipDegrees;

            // Body Follow: lean lags on its own slow time constant (a visible delay for the
            // body's overall acceleration tilt); rock/bob pass through unlagged since they
            // already oscillate every stride - artificial smoothing there just kills the motion.
            float followRotation = 0f;
            float followRight = 0f;
            float followUp = 0f;
            if (enableBodyFollow && torsoFollow != null && weaponIsFlipping == false)
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
            if (enableSpeedSway && weaponIsFlipping == false)
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
                flipRotation,
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
        // frequency per axis so vertical (jump) can settle faster than horizontal (run). Bounded
        // overshoot (3x MaxSway) is the fail-safe against a low/variable-framerate spike reading
        // as the gun flying off the character - see DampedSpring's own comment for why the naive
        // per-frame integration can't be trusted to stay finite on its own.
        private void IntegrateSway(Vector2 target, Vector2 frequency, float damping, float dt)
        {
            DampedSpring.Integrate(ref swayOffset, ref swayVelocity, target, frequency, damping, dt, MaxSway * 3f);
        }

        private Vector2 ProjectToScreen(Vector3 worldDir)
        {
            return new Vector2(Vector3.Dot(worldDir, cameraTransform.right), Vector3.Dot(worldDir, cameraTransform.up));
        }

        // ProjectToScreen(worldDir) projects a DIRECTION through the camera's basis vectors, which
        // implicitly assumes an orthographic camera and ignores where the two endpoints actually are
        // - fine for the flat/no-target fallback below (there's no real target point to speak of),
        // but wrong for a locked target: the weapon's own visible pivot sits well above and offset
        // from the player's Transform3D.Position (root/feet) once sway/follow/hand-grip offsets are
        // applied (WeaponView.ApplyAim), and under a real PERSPECTIVE camera that elevation/offset
        // difference creates genuine screen-space parallax the direction-only approach can't see -
        // most visible at close range, where a target standing right next to the player can read as
        // the gun aiming "up" even though the world-space direction to it is basically flat/downward.
        // WorldToScreenPoint-ing the weapon's actual current position and the target's actual
        // position and taking the delta between them is the real "what does this look like on
        // screen" answer, correct regardless of camera pitch, weapon elevation or target distance.
        private Vector2 ResolveScreenAimDirection(Frame frame, Aim aim, WeaponView weaponView, Vector3 fallbackWorldDir)
        {
            if (aimCamera != null && aim.Target != EntityRef.None && frame.Has<Transform3D>(aim.Target) == true)
            {
                Vector3 targetPosition = frame.Get<Transform3D>(aim.Target).Position.ToUnityVector3();
                Vector3 gunScreen = aimCamera.WorldToScreenPoint(weaponView.transform.position);
                Vector3 targetScreen = aimCamera.WorldToScreenPoint(targetPosition);
                Vector2 screenDelta = new Vector2(targetScreen.x - gunScreen.x, targetScreen.y - gunScreen.y);

                if (screenDelta.sqrMagnitude > 0.0001f)
                    return screenDelta;
            }

            return ProjectToScreen(fallbackWorldDir);
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

using NaughtyAttributes;
using Photon.Deterministic;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Purely cosmetic squash-and-stretch skin over the KCC-driven rigid body. Reads velocity/
    // grounded state off the predicted frame and a PlayerJumped event for takeoff timing; never
    // writes back to simulation state. Sits alongside CharView on the character's view prefab.
    public class BlobAnimationView : CustomQuantumEntityViewComponent
    {
        [Header("Rig (assign once sprite art exists)")]
        [SerializeField] private Transform root;
        [SerializeField] private Transform head;
        [SerializeField] private Transform torso;
        [SerializeField] private Transform legLeft;
        [SerializeField] private Transform legRight;
        [SerializeField, Tooltip("Separate prop transform (e.g. a skateboard) that only ever tilts while jumping - untouched by idle/run.")]
        private Transform skateboard;

        // Read-only so outside code (PlayerPortraitUiWidget, pulling a UI icon straight off the
        // rig's own head sprite rather than a separately-authored portrait asset) can find the head
        // without being able to reassign the rig transform this component itself animates.
        public Transform Head => head;

        [Header("Facing")]
        [SerializeField] private bool billboardToCamera = true;
        [SerializeField, Tooltip("Fallback only, used when Aim is missing: minimum |velocity.x| before the facing flip commits. The normal path reads Aim.FacingSign, which AimSystem already computes with its own deadzone.")]
        private float facingDeadzone = 0.2f;
        [SerializeField, Tooltip("Constant Z nudge on root, purely visual - lines the sprite up as centered under an angled orthographic camera. Doesn't touch gameplay position (KCC/collision are untouched).")]
        private float cameraCenterZOffset = 0f;

        [Header("Idle")]
        [SerializeField] private float idleBreatheFrequency = 1.2f;
        [SerializeField] private float idleBreatheAmount = 0.05f;
        [SerializeField] private float idleBobAmount = 0.03f;
        [SerializeField] private float idleWobbleDegrees = 2f;
        [SerializeField] private float idleWobbleSpeed = 0.3f;

        [Header("Movement")]
        [SerializeField] private float runReferenceSpeed = 5f;
        [SerializeField, Tooltip("Above this horizontal speed, idle becomes run.")] private float moveSpeedEpsilon = 0.15f;

        [Header("Run")]
        [SerializeField, Tooltip("Run = stepping stride. Rollerblade = push-glide skating stride. Heavy = sharp-lift, hard-slam stomp. Reuses every stat below for any style - only the leg motion function differs.")]
        private RunStyle runStyle = RunStyle.Run;
        [SerializeField, Tooltip("Drives torso squash, bounce and step-rock.")] private float runStrideFrequency = 2.2f;
        [SerializeField] private float runSquashAmount = 0.18f;
        [SerializeField] private float runBounceAmount = 0.08f;
        [SerializeField, Tooltip("Lean angle while travel direction matches facing (normal forward run).")]
        private float runLeanDegreesForward = -8;
        [SerializeField, Tooltip("Lean angle while travel direction is opposed to facing (backpedaling/strafing away from aim).")]
        private float runLeanDegreesBackward = -2;
        [SerializeField, Tooltip("How fast the lean angle eases toward its target - smooths out the pop when moveAlignSign flips (forward run into a sudden backpedal, or vice versa).")]
        private float leanLerpSpeed = 10f;
        [SerializeField] private float runStepRockDegrees = 8f;
        [SerializeField, Tooltip("Drives leg lift and swing, independent of the squash/bounce stride frequency above.")] private float runLegStrideFrequency = 2.2f;
        [SerializeField] private float runLegLiftAmount = 0.12f;
        [SerializeField] private float runLegSwingDegrees = 25f;

        [Header("Jump (applied as a single squash/stretch on root, not on individual parts)")]
        [SerializeField] private float anticipationDuration = 0.08f;
        [SerializeField] private float anticipationSquash = 0.35f;
        [SerializeField] private float takeoffStretch = 0.35f;
        [SerializeField] private float airStretchPerSpeed = 0.05f;
        [SerializeField] private float maxAirStretch = 0.4f;
        [SerializeField, Tooltip("Legs shorten toward this fraction of their base length while airborne - knees pulled up. Assumes each leg's pivot is at the hip so shrinking retracts the foot upward.")]
        private float airLegTuckScale = 0.45f;
        [SerializeField] private float airLegTuckLerpSpeed = 12f;
        [SerializeField, Tooltip("Leg angle while airborne, split scissor-style: the left leg swings forward by this many degrees (matches runLegSwingDegrees' positive-forward convention) while the right leg swings back by the same amount. 0 = legs hang straight while airborne.")]
        private float airLegAngleDegrees = 20f;
        [SerializeField, Tooltip("Z angle the skateboard transform eases toward while ascending (verticalSpeed above +skateboardSpeedThreshold).")]
        private float skateboardAngleUpDegrees = 15f;
        [SerializeField, Tooltip("Z angle the skateboard transform eases toward near the apex, while |verticalSpeed| is below skateboardSpeedThreshold - the brief hang time between rising and falling.")]
        private float skateboardAngleApexDegrees = 0f;
        [SerializeField, Tooltip("Z angle the skateboard transform eases toward while falling (verticalSpeed below -skateboardSpeedThreshold).")]
        private float skateboardAngleDownDegrees = -15f;
        [SerializeField, Tooltip("Vertical speed magnitude that separates rising/apex/falling for the skateboard angle above. Below this on either side of 0 counts as the apex.")]
        private float skateboardSpeedThreshold = 2f;

        [Header("Landing")]
        [SerializeField] private float landingSquashPerSpeed = 0.06f;
        [SerializeField] private float maxLandingSquash = 0.6f;
        [SerializeField] private float landingSpringFrequency = 6f;
        [SerializeField] private float landingSpringDamping = 0.35f;

        [Header("General")]
        [SerializeField] private float squashLerpSpeed = 14f;
        [SerializeField] private float volumePreservation = 0.6f;
        [SerializeField, Tooltip("How much of idle/run's torso squash the head also gets.")] private float headSquashInfluence = 0.4f;
        [SerializeField, Range(0f, 1f), Tooltip("How much of root's jump squash/stretch (anticipation/takeoff/air/landing) the head also gets, after counter-scaling out the rest. 0 = head stays put no matter how hard root squashes; 1 = head follows root exactly like torso does.")]
        private float jumpHeadSquashInfluence = 0.25f;

        private enum RunStyle { Run, Rollerblade, Heavy }

        private enum State { Idle, Run, Anticipate, Air, Landing }
        private State _state = State.Idle;
        private float _stateTimer;

        private Vector3 _headBaseScale, _torsoBaseScale;
        private Vector3 _legLeftBaseScale, _legRightBaseScale;
        private Vector3 _legLeftBasePos, _legRightBasePos;
        private Quaternion _legLeftBaseRot, _legRightBaseRot;
        private float _legScaleT = 1f;
        private float _legAngleT;
        private Quaternion _skateboardBaseRot;
        private float _skateboardAngleT;
        private Vector3 _headBaseLocalPos, _torsoBaseLocalPos;
        private Vector3 _rootBaseLocalPos, _rootBaseScale;
        // Nothing else writes head rotation (unlike legs/torso), so this is only ever the punch's
        // own base to twist away from - see PunchHeadRotation/LateUpdate.
        private Quaternion _headBaseRot;

        // Torso/head squash for idle breathing and run's stride wave. Positive = compressed, negative = stretched.
        private float _squashT;

        // Jump squash/stretch - anticipation, takeoff, air and landing - applied to root as a whole.
        // Independent of _squashT above; the two never mix.
        private float _jumpSquashT;
        private bool _springActive;
        private float _springVelocity;

        private float _stridePhase;
        private float _legPhase;
        private float _facingSign = 1f;
        private float _wobbleSeed;
        private float _leanT;

        // Shoot punch state - five independent PrimeTween punches, all kicked externally by
        // WeaponView (see the public Punch* methods below) using that weapon's own
        // WeaponAnimationParams tuning, since the right feel differs per weapon. Each decays back
        // to 0 (rest) on its own; all five are applied additively in LateUpdate, strictly after
        // this frame's locomotion pose - see LateUpdate's own comment for why.
        private Vector3 _headPunchOffset;
        private float _bodyPunchRotation;
        private float _headPunchRotation;
        private Vector3 _bodyPunchScale;
        private Vector3 _headPunchScale;

        private bool _wasGrounded = true;

        // Set by ResetToInitialPoseAndPause, consumed on the next QUpdate - i.e. the first frame
        // after resuming from the pause it triggers, so whatever pose was hand-tweaked in the
        // Scene view while paused becomes the new baseline instead of Awake's original one.
        private bool _recaptureBaselineOnResume;

        // Exposed so other view components (e.g. PlayerGunAimView) can sample the torso's
        // current sway/bob and lag toward it on their own time constant, without duplicating
        // this rig's gait math. Lean and rock are split rather than pre-summed because they
        // move on very different timescales - lean drifts slowly with acceleration, rock
        // oscillates every stride - and a follower typically wants to lag each differently.
        // FacingSign mirrors root's own scale.x sign (+1 = right, -1 = left), so other view
        // components can flip in lockstep with the body instead of deriving facing on their own.
        public float FacingSign => _facingSign;
        // Lets other view components (e.g. JuggernautView, while charge is active) scale up just
        // the forward lean without touching backward/air lean or reimplementing the lean easing -
        // _leanT already lerps toward whatever leanTarget this multiplier produces, so flipping it
        // back to 1 reverts through the same easing instead of needing its own transition.
        public float RunLeanForwardMultiplier { get; set; } = 1f;
        public float CurrentLeanDegrees { get; private set; }
        public float CurrentRockDegrees { get; private set; }
        public float CurrentBobOffset { get; private set; }
        // root's actual current vertical scale multiplier from jump squash/stretch (1 = rest).
        // Exposed as a plain value rather than expecting followers to read root.lossyScale
        // themselves - root is both rotated (billboard) and non-uniformly scaled at the same
        // time, and lossyScale can't reliably decompose that combination (shearing).
        public float CurrentRootVerticalScale { get; private set; } = 1f;

        public override void Awake()
        {
            base.Awake();
            CacheBaseline();
            _wobbleSeed = Random.value * 1000f;
            QuantumEvent.Subscribe<EventPlayerJumped>(this, OnPlayerJumped);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);

            // The Punch* methods' tweens (Tween.PunchCustom(this, ...)) can still be decaying when
            // this view is destroyed - without this, PrimeTween logs a stack-trace-capturing error
            // per orphaned tween every time that happens.
            Tween.StopAll(this);
        }

        // Editor workflow: snaps the rig back to its current baseline, pauses so the pose can be
        // hand-tweaked in the Scene view without the animation fighting the edit, and marks the
        // baseline for recapture - see _recaptureBaselineOnResume - so the tweaked pose becomes
        // the new rest pose on resume instead of being overwritten by the old one.
        [Button("Reset To Initial Pose & Pause")]
        private void ResetToInitialPoseAndPause()
        {
            ApplyBaselineTransforms();
            _recaptureBaselineOnResume = true;
            Debug.Break();
        }

        private void ApplyBaselineTransforms()
        {
            if (root != null) { root.localPosition = _rootBaseLocalPos; root.localScale = _rootBaseScale; }
            if (head != null) { head.localPosition = _headBaseLocalPos; head.localScale = _headBaseScale; head.localRotation = _headBaseRot; }
            if (torso != null) { torso.localPosition = _torsoBaseLocalPos; torso.localScale = _torsoBaseScale; torso.localRotation = Quaternion.identity; }
            if (legLeft != null) { legLeft.localPosition = _legLeftBasePos; legLeft.localRotation = _legLeftBaseRot; legLeft.localScale = _legLeftBaseScale; }
            if (legRight != null) { legRight.localPosition = _legRightBasePos; legRight.localRotation = _legRightBaseRot; legRight.localScale = _legRightBaseScale; }
            if (skateboard != null) { skateboard.localRotation = _skateboardBaseRot; }
        }

        private void CacheBaseline()
        {
            if (root != null) { _rootBaseLocalPos = root.localPosition; _rootBaseScale = root.localScale; }
            if (head != null) { _headBaseScale = head.localScale; _headBaseLocalPos = head.localPosition; _headBaseRot = head.localRotation; }
            if (torso != null) { _torsoBaseScale = torso.localScale; _torsoBaseLocalPos = torso.localPosition; }
            if (legLeft != null) { _legLeftBasePos = legLeft.localPosition; _legLeftBaseRot = legLeft.localRotation; _legLeftBaseScale = legLeft.localScale; }
            if (legRight != null) { _legRightBasePos = legRight.localPosition; _legRightBaseRot = legRight.localRotation; _legRightBaseScale = legRight.localScale; }
            if (skateboard != null) { _skateboardBaseRot = skateboard.localRotation; }
        }

        private void OnPlayerJumped(EventPlayerJumped e)
        {
            if (e.Entity != _entityRef) return;

            _state = State.Anticipate;
            _stateTimer = 0f;
            _springActive = false;
            _jumpSquashT = anticipationSquash; // instant, held - no easing into it
        }

        // Five independent punches, each called by WeaponView.Shoot() with that weapon's own
        // WeaponAnimationParams tuning - kept as plain per-effect methods rather than one bundled
        // call so a weapon can fire only the ones it wants (e.g. skip the rotation kicks by
        // passing zero strength) without BlobAnimationView needing to know about
        // CharacterPunchSettings at all. All independent of _state/ApplyPose - shooting mid-run or
        // mid-air still kicks - and applied in LateUpdate, not written directly here, since
        // ApplyPose (running inside QUpdate/Update) is the sole writer of these transforms every
        // frame and would just stomp a direct write next frame.
        public void PunchHeadOffset(Vector3 strength, float duration, float frequency)
        {
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(strength, duration, frequency),
                (view, val) => view._headPunchOffset = val);
        }

        public void PunchBodyRotation(float degrees, float duration, float frequency)
        {
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(degrees, 0f, 0f), duration, frequency),
                (view, val) => view._bodyPunchRotation = val.x);
        }

        public void PunchHeadRotation(float degrees, float duration, float frequency)
        {
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(degrees, 0f, 0f), duration, frequency),
                (view, val) => view._headPunchRotation = val.x);
        }

        public void PunchBodyScale(Vector3 strength, float duration, float frequency)
        {
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(strength, duration, frequency),
                (view, val) => view._bodyPunchScale = val);
        }

        public void PunchHeadScale(Vector3 strength, float duration, float frequency)
        {
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(strength, duration, frequency),
                (view, val) => view._headPunchScale = val);
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (root == null && head == null && torso == null && legLeft == null && legRight == null)
                return;

            if (_recaptureBaselineOnResume)
            {
                CacheBaseline();
                _recaptureBaselineOnResume = false;
            }

            var frame = game.Frames.Predicted;
            if (frame.Has<KCC>(_entityRef) == false)
                return;

            var kcc = frame.Get<KCC>(_entityRef);
            Vector3 velocity = kcc.Data.RealVelocity.ToUnityVector3();
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            float verticalSpeed = velocity.y;
            bool isGrounded = kcc.Data.IsGrounded;
            bool justLanded = isGrounded && _wasGrounded == false;
            bool justLeftGround = isGrounded == false && _wasGrounded;

            float dt = Time.deltaTime;

            UpdateFacing(frame, velocity);

            // Facing now follows aim rather than velocity, so travel direction and facing can
            // disagree (e.g. strafing/backpedaling while aiming the other way). moveXSign is the
            // character's actual left/right travel; moveAlignSign is +1 when that travel matches
            // facing (normal forward stride) and -1 when it's opposed (backpedal) - used below to
            // run the stride/leg cycle in reverse and lean into the true direction of travel
            // instead of the faced one, so backpedaling doesn't read as moonwalking.
            float moveXSign = Mathf.Abs(velocity.x) > moveSpeedEpsilon ? Mathf.Sign(velocity.x) : _facingSign;
            float moveAlignSign = moveXSign * _facingSign;

            if (justLanded)
            {
                float impactSpeed = Mathf.Abs(Mathf.Min(0f, verticalSpeed));
                _jumpSquashT = Mathf.Clamp(impactSpeed * landingSquashPerSpeed, 0f, maxLandingSquash);
                _springVelocity = 0f;
                _springActive = true;
                _state = DetermineGroundedState(horizontalSpeed);
            }
            else if (_state == State.Anticipate)
            {
                _stateTimer += dt;
                if (_stateTimer >= anticipationDuration || justLeftGround)
                {
                    _jumpSquashT = -takeoffStretch; // instant pop, no easing
                    _state = State.Air;
                    _springActive = false;
                }
            }
            else if (isGrounded == false)
            {
                _state = State.Air;
                _springActive = false;
                float targetStretch = -Mathf.Clamp(Mathf.Abs(verticalSpeed) * airStretchPerSpeed, 0f, maxAirStretch);
                _jumpSquashT = Mathf.Lerp(_jumpSquashT, targetStretch, 1f - Mathf.Exp(-squashLerpSpeed * dt));
            }
            else
            {
                _state = DetermineGroundedState(horizontalSpeed);
            }

            if (_springActive)
            {
                IntegrateLandingSpring(dt);
            }

            float leanTarget = 0f;
            float rockTarget = 0f;
            float bobTarget = 0f;
            float upperBodyBobTarget = 0f;

            switch (_state)
            {
                case State.Idle:
                {
                    float breathe = Mathf.Sin(Time.time * idleBreatheFrequency * Mathf.PI * 2f) * idleBreatheAmount;
                    // Root's landing squash/bounce is a scale on the parent of torso/head, so it
                    // compounds with whatever torso/head squash idle/run add on top of it. While
                    // that spring is still settling, hold torso/head neutral and let landing read
                    // cleanly on its own instead of the two stacking into one messy shape.
                    _squashT = Mathf.Lerp(_squashT, _springActive ? 0f : breathe, 1f - Mathf.Exp(-squashLerpSpeed * dt));
                    // Upper-body only: root (and the legs hanging off it) stays put so feet don't
                    // sink into the ground on the downward half of the breathing cycle.
                    upperBodyBobTarget = Mathf.Sin(Time.time * idleBreatheFrequency * Mathf.PI * 2f) * idleBobAmount;
                    float wobble = (Mathf.PerlinNoise(_wobbleSeed, Time.time * idleWobbleSpeed) - 0.5f) * 2f;
                    rockTarget = wobble * idleWobbleDegrees;
                    RelaxLegs(dt);
                    ApplyLegAngle(0f, dt);
                    ApplySkateboardAngle(0f, dt);
                    break;
                }
                case State.Run:
                {
                    float bodyHz = runStrideFrequency * Mathf.Max(horizontalSpeed / runReferenceSpeed, 0.2f);
                    _stridePhase += bodyHz * dt * moveAlignSign;
                    _stridePhase -= Mathf.Floor(_stridePhase);

                    float legHz = runLegStrideFrequency * Mathf.Max(horizontalSpeed / runReferenceSpeed, 0.2f);
                    _legPhase += legHz * dt * moveAlignSign;
                    _legPhase -= Mathf.Floor(_legPhase);

                    float runSquash = Mathf.Cos(_stridePhase * 4f * Mathf.PI) * runSquashAmount;
                    // See the Idle case above - suppressed while the landing spring is still active.
                    _squashT = Mathf.Lerp(_squashT, _springActive ? 0f : runSquash, 1f - Mathf.Exp(-squashLerpSpeed * dt * 2f));

                    // Lean follows real travel direction (moveXSign), not facing - momentum leans
                    // toward where the body is actually going, matching old (pre-aim-facing)
                    // behavior whenever travel and facing agree, and correctly reversing when they don't.
                    // Magnitude picks forward vs backward degrees based on moveAlignSign (whether
                    // that travel matches or opposes facing), sign comes from moveXSign.
                    float runLeanDegrees = moveAlignSign >= 0f ? runLeanDegreesForward * RunLeanForwardMultiplier : runLeanDegreesBackward;
                    leanTarget = Mathf.Clamp01(horizontalSpeed / runReferenceSpeed) * runLeanDegrees * moveXSign;

                    if (runStyle == RunStyle.Rollerblade)
                    {
                        // One carve per full glide cycle (not two, like a run's left-right step)
                        // and wider - sells weight transfer between legs instead of a jog wobble.
                        // Bounce is mostly flattened out - blades glide low and smooth, no bouncing.
                        bobTarget = Mathf.Abs(Mathf.Sin(_stridePhase * 2f * Mathf.PI)) * runBounceAmount * 0.25f;
                        rockTarget = Mathf.Sin(_stridePhase * 2f * Mathf.PI) * runStepRockDegrees * 1.6f;
                    }
                    else
                    {
                        bobTarget = Mathf.Abs(Mathf.Sin(_stridePhase * 4f * Mathf.PI)) * runBounceAmount;
                        rockTarget = Mathf.Sin(_stridePhase * 4f * Mathf.PI) * runStepRockDegrees;
                    }

                    // Legs are position/rotation only, not scale, so they don't compound with
                    // root's landing spring the way torso/head squash does - no need to wait for
                    // it to settle before running starts moving the legs.
                    switch (runStyle)
                    {
                        case RunStyle.Rollerblade:
                            AnimateLegsSkating(_legPhase, runLegLiftAmount, runLegSwingDegrees);
                            break;
                        case RunStyle.Heavy:
                            AnimateLegsHeavy(_legPhase, runLegLiftAmount, runLegSwingDegrees);
                            break;
                        default:
                            AnimateLegs(_legPhase, runLegLiftAmount, runLegSwingDegrees);
                            break;
                    }
                    ApplySkateboardAngle(0f, dt);
                    break;
                }
                case State.Anticipate:
                    // Held pose - _jumpSquashT already set instantly in OnPlayerJumped, nothing to ease.
                    // Torso/head squash relaxes to neutral so root's jump squash is the only deformation.
                    _squashT = Mathf.Lerp(_squashT, 0f, 1f - Mathf.Exp(-squashLerpSpeed * dt));
                    RelaxLegs(dt);
                    ApplyLegAngle(0f, dt);
                    ApplySkateboardAngle(0f, dt);
                    break;
                case State.Air:
                    _squashT = Mathf.Lerp(_squashT, 0f, 1f - Mathf.Exp(-squashLerpSpeed * dt));
                    float airLeanDegrees = (moveAlignSign >= 0f ? runLeanDegreesForward : runLeanDegreesBackward) * 0.5f;
                    leanTarget = Mathf.Clamp(horizontalSpeed / runReferenceSpeed, 0f, 1f) * airLeanDegrees * moveXSign;
                    RelaxLegs(dt);
                    ApplyLegAngle(airLegAngleDegrees, dt);
                    float skateboardTarget = verticalSpeed > skateboardSpeedThreshold ? skateboardAngleUpDegrees
                        : verticalSpeed < -skateboardSpeedThreshold ? skateboardAngleDownDegrees
                        : skateboardAngleApexDegrees;
                    ApplySkateboardAngle(skateboardTarget, dt);
                    break;
            }

            // Knees pulled up while airborne - legs shorten toward the tuck fraction, then back
            // to full length as soon as grounded (Idle/Walk/Run/Anticipate all target 1).
            float legScaleTarget = _state == State.Air ? airLegTuckScale : 1f;
            _legScaleT = Mathf.Lerp(_legScaleT, legScaleTarget, 1f - Mathf.Exp(-airLegTuckLerpSpeed * dt));
            ApplyLegScale(_legScaleT);

            _leanT = Mathf.Lerp(_leanT, leanTarget, 1f - Mathf.Exp(-leanLerpSpeed * dt));
            ApplyPose(_leanT, rockTarget, bobTarget, upperBodyBobTarget);

            _wasGrounded = isGrounded;
        }

        // All five shoot punches applied as their own pass, strictly after QUpdate's locomotion
        // pose (Unity guarantees LateUpdate runs after Update on the same object, and
        // CustomQuantumEntityViewComponent's own Update - which drives QUpdate - never touches
        // LateUpdate, so this can't collide with it). Kept independent of _state/ApplyPose
        // entirely so Idle/Run/Air's own per-frame pose writes can never bury the punch, no matter
        // how those evolve later - it always lands on top of whatever pose was just set this frame.
        private void LateUpdate()
        {
            if (head != null)
            {
                head.localPosition += _headPunchOffset;

                // Multiplicative on top of ApplyPose's own head scale, same shape as root below.
                var headScale = head.localScale;
                headScale.x *= 1f + _headPunchScale.x;
                headScale.y *= 1f + _headPunchScale.y;
                headScale.z *= 1f + _headPunchScale.z;
                head.localScale = headScale;

                head.localRotation = _headBaseRot * Quaternion.Euler(0f, 0f, _headPunchRotation);
            }

            if (root != null)
            {
                var rootScale = root.localScale;
                rootScale.x *= 1f + _bodyPunchScale.x;
                rootScale.y *= 1f + _bodyPunchScale.y;
                rootScale.z *= 1f + _bodyPunchScale.z;
                root.localScale = rootScale;

                // Right-multiplying twists around root's own current Z axis on top of whatever
                // rotation QUpdate just set (world rotation in the billboard case, local
                // otherwise - this reads correctly either way since it's relative to root's own
                // orientation).
                if (_bodyPunchRotation != 0f)
                    root.rotation *= Quaternion.Euler(0f, 0f, _bodyPunchRotation);
            }
        }

        private State DetermineGroundedState(float horizontalSpeed)
        {
            return horizontalSpeed > moveSpeedEpsilon ? State.Run : State.Idle;
        }

        private void IntegrateLandingSpring(float dt)
        {
            // Damped harmonic oscillator pulling _jumpSquashT back to 0, with overshoot/bounce.
            // Bounded overshoot (2x maxLandingSquash) keeps a bad-frame spike from reading as the
            // rig flying apart - see DampedSpring's own comment for why the naive form can't be
            // trusted to stay finite on its own at low/variable framerate.
            DampedSpring.Integrate(ref _jumpSquashT, ref _springVelocity, 0f, landingSpringFrequency, landingSpringDamping, dt, maxLandingSquash * 2f);

            if (Mathf.Abs(_jumpSquashT) < 0.01f && Mathf.Abs(_springVelocity) < 0.05f)
            {
                _jumpSquashT = 0f;
                _springActive = false;
            }
        }

        private void AnimateLegs(float phase, float liftAmount, float swingDegrees)
        {
            if (legLeft != null)
            {
                float legPhase = phase * 2f * Mathf.PI;
                float lift = Mathf.Max(0f, Mathf.Sin(legPhase)) * liftAmount;
                legLeft.localPosition = _legLeftBasePos + new Vector3(0f, lift, 0f);
                // Swings forward (+) while lifted/airborne, backward (-) while grounded/pushing off -
                // matches the lift wave above: forward at contact (phase 0.5), back at liftoff (phase 0).
                float swing = -Mathf.Cos(legPhase) * swingDegrees;
                legLeft.localRotation = _legLeftBaseRot * Quaternion.Euler(0f, 0f, swing);
            }

            if (legRight != null)
            {
                float legPhase = (phase + 0.5f) * 2f * Mathf.PI;
                float lift = Mathf.Max(0f, Mathf.Sin(legPhase)) * liftAmount;
                legRight.localPosition = _legRightBasePos + new Vector3(0f, lift, 0f);
                float swing = -Mathf.Cos(legPhase) * swingDegrees;
                legRight.localRotation = _legRightBaseRot * Quaternion.Euler(0f, 0f, swing);
            }
        }

        // Push-glide skating stride: one leg extends out to its own side and trails while the
        // other tucks back under the body, alternating - a rollerblade push never crosses a leg
        // past center. Reuses the same liftAmount/swingDegrees stats as a lateral push distance
        // and blade angle, so Run and Rollerblade share identical tuning.
        private void AnimateLegsSkating(float phase, float liftAmount, float swingDegrees)
        {
            AnimateSkatingLeg(legLeft, _legLeftBasePos, _legLeftBaseRot, phase, -1f, liftAmount, swingDegrees);
            AnimateSkatingLeg(legRight, _legRightBasePos, _legRightBaseRot, phase + 0.5f, 1f, liftAmount, swingDegrees);
        }

        private static void AnimateSkatingLeg(Transform leg, Vector3 basePos, Quaternion baseRot, float phase, float side, float liftAmount, float swingDegrees)
        {
            if (leg == null) return;

            float legPhase = phase * 2f * Mathf.PI;
            // 0 = tucked under the body, 1 = fully pushed out. (1-cos)/2 eases to a stop at both
            // ends - unlike a rectified sine, it has no velocity kink at zero, so the leg glides
            // to full extension and back instead of visibly snapping through center.
            float glide = (1f - Mathf.Cos(legPhase)) * 0.5f;
            leg.localPosition = basePos + new Vector3(side * glide * liftAmount * 1.5f, glide * liftAmount * 0.25f, 0f);
            leg.localRotation = baseRot * Quaternion.Euler(0f, 0f, side * glide * swingDegrees);
        }

        // Stomping gait: squaring the lift curve biases it toward a fast rise and a hard drop
        // instead of run's smooth sinusoidal step, and the swing is dialed back since a heavy
        // stomp plants straight down rather than swinging fore-aft. Same liftAmount/swingDegrees
        // stats as AnimateLegs, just a heavier-feeling curve through them.
        private void AnimateLegsHeavy(float phase, float liftAmount, float swingDegrees)
        {
            if (legLeft != null)
            {
                float legPhase = phase * 2f * Mathf.PI;
                float raw = Mathf.Max(0f, Mathf.Sin(legPhase));
                float lift = raw * raw * liftAmount;
                legLeft.localPosition = _legLeftBasePos + new Vector3(0f, lift, 0f);
                float swing = -Mathf.Cos(legPhase) * swingDegrees * 0.5f;
                legLeft.localRotation = _legLeftBaseRot * Quaternion.Euler(0f, 0f, swing);
            }

            if (legRight != null)
            {
                float legPhase = (phase + 0.5f) * 2f * Mathf.PI;
                float raw = Mathf.Max(0f, Mathf.Sin(legPhase));
                float lift = raw * raw * liftAmount;
                legRight.localPosition = _legRightBasePos + new Vector3(0f, lift, 0f);
                float swing = -Mathf.Cos(legPhase) * swingDegrees * 0.5f;
                legRight.localRotation = _legRightBaseRot * Quaternion.Euler(0f, 0f, swing);
            }
        }

        // Position only - rotation is handled separately by ApplyLegAngle so the jump angle
        // (scissor split while airborne) can ease independently of how fast legs relax back
        // toward their resting position.
        private void RelaxLegs(float dt)
        {
            float t = 1f - Mathf.Exp(-squashLerpSpeed * dt);
            if (legLeft != null)
                legLeft.localPosition = Vector3.Lerp(legLeft.localPosition, _legLeftBasePos, t);
            if (legRight != null)
                legRight.localPosition = Vector3.Lerp(legRight.localPosition, _legRightBasePos, t);
        }

        // Scissor split while airborne: left leg swings forward by angleDegrees, right leg swings
        // back by the same amount (mirrors runLegSwingDegrees' positive-forward convention).
        // Called from Idle/Anticipate/Air only - Run/Rollerblade/Heavy set leg rotation directly
        // each frame via their own AnimateLegs* function, so this must stay out of their way.
        private void ApplyLegAngle(float targetDegrees, float dt)
        {
            _legAngleT = Mathf.Lerp(_legAngleT, targetDegrees, 1f - Mathf.Exp(-airLegTuckLerpSpeed * dt));
            if (legLeft != null)
                legLeft.localRotation = _legLeftBaseRot * Quaternion.Euler(0f, 0f, _legAngleT);
            if (legRight != null)
                legRight.localRotation = _legRightBaseRot * Quaternion.Euler(0f, 0f, -_legAngleT);
        }

        // Skateboard is a separate prop transform, not part of the leg rig - it only tilts while
        // airborne (targetDegrees = skateboardAngleDegrees from the Air case) and eases back to
        // its own base rotation everywhere else (Idle/Run/Anticipate pass 0).
        private void ApplySkateboardAngle(float targetDegrees, float dt)
        {
            _skateboardAngleT = Mathf.Lerp(_skateboardAngleT, targetDegrees, 1f - Mathf.Exp(-airLegTuckLerpSpeed * dt));
            if (skateboard != null)
                skateboard.localRotation = _skateboardBaseRot * Quaternion.Euler(0f, 0f, _skateboardAngleT);
        }

        private void ApplyLegScale(float verticalMult)
        {
            if (legLeft != null)
                legLeft.localScale = new Vector3(_legLeftBaseScale.x, _legLeftBaseScale.y * verticalMult, _legLeftBaseScale.z);
            if (legRight != null)
                legRight.localScale = new Vector3(_legRightBaseScale.x, _legRightBaseScale.y * verticalMult, _legRightBaseScale.z);
        }

        private void UpdateFacing(Frame frame, Vector3 velocity)
        {
            // Reads Aim.FacingSign (AimSystem) rather than re-deriving its own hysteresis from
            // Angle/velocity here - AimSystem already applies the same deadzone this used to
            // apply locally, and simulation code (StatUtility.GetWeaponHoldOffset) reads that
            // same field, so body/weapon sprite flip can never disagree with the muzzle's mirror.
            if (frame.Has<Aim>(_entityRef))
            {
                _facingSign = frame.Get<Aim>(_entityRef).FacingSign.AsFloat;
            }
            else if (Mathf.Abs(velocity.x) > facingDeadzone)
            {
                _facingSign = Mathf.Sign(velocity.x);
            }
        }

        private void ApplyPose(float leanDegrees, float rockDegrees, float bobOffset, float upperBodyBobOffset)
        {
            float verticalMult = Mathf.Clamp(1f - _squashT * volumePreservation, 0.15f, 3f);
            float horizontalMult = Mathf.Clamp(1f / verticalMult, 0.3f, 3f);

            if (torso != null)
            {
                torso.localScale = Vector3.Scale(_torsoBaseScale, new Vector3(horizontalMult, verticalMult, horizontalMult));
                var torsoPos = _torsoBaseLocalPos;
                torsoPos.y += upperBodyBobOffset;
                torso.localPosition = torsoPos;
            }

            // Jump squash/stretch lives on root (see below) so it reaches every child, head
            // included, purely through parent-child scale inheritance. Counter-scale the head
            // here to claw back most of that - a landing splat that also squishes the head into
            // mush reads as broken rather than expressive, so the head only takes a fraction of
            // what root does (jumpHeadSquashInfluence) while torso still gets it in full.
            float rootVerticalMult = Mathf.Clamp(1f - _jumpSquashT * volumePreservation, 0.15f, 3f);
            float rootHorizontalMult = Mathf.Clamp(1f / rootVerticalMult, 0.3f, 3f);

            if (head != null)
            {
                float headVertical = Mathf.Lerp(1f, verticalMult, headSquashInfluence);
                float headHorizontal = Mathf.Lerp(1f, horizontalMult, headSquashInfluence);
                float headJumpVertical = Mathf.Lerp(1f, rootVerticalMult, jumpHeadSquashInfluence) / rootVerticalMult;
                float headJumpHorizontal = Mathf.Lerp(1f, rootHorizontalMult, jumpHeadSquashInfluence) / rootHorizontalMult;
                head.localScale = Vector3.Scale(_headBaseScale, new Vector3(headHorizontal * headJumpHorizontal, headVertical * headJumpVertical, headHorizontal * headJumpHorizontal));
                var headPos = _headBaseLocalPos;
                headPos.y += upperBodyBobOffset;
                head.localPosition = headPos;
                // Shoot punch is layered on top in LateUpdate, strictly after this pose - see
                // LateUpdate's own comment for why.
            }

            if (root != null)
            {
                var localPos = _rootBaseLocalPos;
                localPos.y += bobOffset;
                localPos.z += cameraCenterZOffset;
                root.localPosition = localPos;

                // Jump squash/stretch - the whole rig deforms together as one blob, instead of
                // torso/head/legs each animating separately.
                var scale = _rootBaseScale;
                scale.x *= _facingSign * rootHorizontalMult;
                scale.y *= rootVerticalMult;
                scale.z *= rootHorizontalMult;
                root.localScale = scale;
                CurrentRootVerticalScale = rootVerticalMult;

                float totalTilt = leanDegrees + rockDegrees;
                CurrentLeanDegrees = leanDegrees;
                CurrentRockDegrees = rockDegrees;
                CurrentBobOffset = bobOffset + upperBodyBobOffset;

                if (billboardToCamera && Camera.main != null)
                {
                    var camForward = Camera.main.transform.forward;
                    root.rotation = Quaternion.LookRotation(camForward, Vector3.up) * Quaternion.Euler(0f, 0f, totalTilt);
                }
                else
                {
                    root.localRotation = Quaternion.Euler(0f, 0f, totalTilt);
                }
            }
        }
    }
}

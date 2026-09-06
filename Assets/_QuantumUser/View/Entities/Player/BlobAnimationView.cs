using NaughtyAttributes;
using Photon.Deterministic;
using PrimeTween;
using QuantumUser.View.Managers;
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

        // The rig's own top transform - everything this component poses hangs off it. Read-only,
        // same reasoning as Head above. Exposed for CharacterPreviewWidget, which shows a hero in
        // the menu by keeping ONLY this branch of the instantiated prefab and discarding the rest
        // (weapon, shadow, colliders, skill FX), so a preview frames the character itself rather
        // than the full gameplay entity built around it.
        public Transform Root => root;

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

        [Header("Jump Flip (auto-hop DOWN off a ledge only - see EventPlayerAutoJumpedDown; a mantle-up or a manual/button jump never triggers this)")]
        [SerializeField, Tooltip("Unchecked = auto-hop-down plays exactly like any other jump (anticipation squash only, no tumble) - the same fallback a mantle/manual jump already gets.")]
        private bool useFlip = true;
        [SerializeField, Tooltip("Seconds for one full 360° rotation.")]
        private float jumpFlipDuration = 0.45f;
        [SerializeField, Tooltip("Front flip while travelling the same way you're facing (forward hop off a ledge), back flip while backpedaling off one facing the other way. Checked swaps the two.")]
        private bool invertFlipDirection = false;
        [SerializeField, Tooltip("How fast the LEGS catch up to root's own flip rotation (exponential lerp rate) - high = legs lead the rotation almost rigidly, low = legs trail behind.")]
        private float legFlipLagSpeed = 26f;
        [SerializeField, Tooltip("Same as legFlipLagSpeed but for the torso - tuned lower so it trails the legs.")]
        private float torsoFlipLagSpeed = 16f;
        [SerializeField, Tooltip("Same as legFlipLagSpeed but for the head - tuned lowest so it whips behind last, like a follow-through crack, and keeps settling for a moment after root's own rotation has already finished.")]
        private float headFlipLagSpeed = 9f;
        [SerializeField, Tooltip("How far above root's own local origin (root sits at the feet/ground contact point) the flip pivots around, so the character tumbles in place around roughly its own center of mass instead of swinging around its feet. Tune against the rig's real height - a good starting guess is the torso's own local Y offset.")]
        private float flipPivotHeight = 0.6f;
        [SerializeField, Range(0.1f, 1f), Tooltip("Root squashes down to this fraction of its normal vertical scale at the flip's midpoints (90°/270°, twice per revolution) and back to full size at 0°/180° - fakes the foreshortening a real tumble would have as it turns edge-on to camera, which a flat Z-axis spin otherwise doesn't produce on its own. 1 = no squash, a purely rigid spin.")]
        private float flipMidRotationSquash = 0.55f;
        [SerializeField, Tooltip("Seconds to finish the CURRENT rotation in when a dash interrupts an in-progress flip, instead of running out the full jumpFlipDuration. Always keeps spinning forward to completion (never unwinds backward) - only the remaining speed changes. Landing and the flip's own natural completion are unaffected (see CancelFlip).")]
        private float dashFlipSpeedUpDuration = 0.2f;

        [Header("Landing")]
        [SerializeField] private float landingSquashPerSpeed = 0.06f;
        [SerializeField] private float maxLandingSquash = 0.6f;
        [SerializeField] private float landingSpringFrequency = 6f;
        [SerializeField] private float landingSpringDamping = 0.35f;

        [Header("Downed/KO (see docs/revive.md) - a plain active-object swap, NOT a procedural pose. Alive shows bodyRoot, Downed shows downedRoot, KO shows koRoot. Each root is a separately-authored GameObject with its own sprite art.")]
        [SerializeField, Tooltip("The normal, alive rig - shown only while Alive. MUST be a CHILD of the object this component sits on (not this object itself, or an ancestor), or disabling it would stop QUpdate from firing and it could never re-enable. Typically the same GameObject as root.")]
        private GameObject bodyRoot;
        [SerializeField, Tooltip("Downed-pose visual - shown only while Downed. Same child-of-this-component requirement as bodyRoot.")]
        private GameObject downedRoot;
        [SerializeField, Tooltip("KO-pose visual - shown only while KO. Same child-of-this-component requirement as bodyRoot.")]
        private GameObject koRoot;
        [SerializeField, Tooltip("The weapon hands (WeaponHandGripView's rig) - shown only while Alive, hidden alongside bodyRoot on Downed/KO. Separate field because the hands track the weapon grips, so they aren't parented under bodyRoot and wouldn't be hidden by it. Point this at their common parent GameObject.")]
        private GameObject handsRoot;
        [SerializeField, Tooltip("Punch-scale strength/duration/frequency played the instant EventPlayerRevived fires for this player - reuses the same additive PunchBodyScale mechanism WeaponView's shoot punches use (see OnPlayerRevived). Also snaps the rig back to its authored rest scale first: Animate() skips the whole squash/stretch pass entirely while Downed/KO (see its own early-return above), so whatever deformation was mid-pose the instant this player went down is otherwise still sitting on the rig the moment bodyRoot reactivates, reading as a random leftover scale rather than a clean revive.")]
        private Vector3 revivePunchScaleStrength = new Vector3(0.35f, 0.35f, 0f);
        [SerializeField] private float revivePunchScaleDuration = 0.4f;
        [SerializeField] private float revivePunchScaleFrequency = 10f;

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

        // Jump Flip - a single 360° tumble, independent of _state/_jumpSquashT (same "orthogonal
        // effect layered on top of the normal pose" idiom the shoot punches below already use), so it
        // can play over an auto-hop-down's ordinary anticipation/air squash without touching either.
        // _flipT runs 0->1 over jumpFlipDuration and drives root's own rotation directly; cancelled
        // (not eased) back to 0 on landing since a partial flip finishing on the ground reads as
        // broken.
        private bool _flipActive;
        // True only during a dash-interrupt speed-up (see the isDashing branch in Animate) - _flipT
        // is still counting UP toward 1 (finishing the current rotation forward, never reversing),
        // just over dashFlipSpeedUpDuration instead of the normal jumpFlipDuration.
        private bool _flipSpeedingUp;
        private float _flipT;
        private float _flipDegrees;
        // Captured once at trigger time (see OnPlayerAutoJumpedDown) - NOT recomputed from live
        // _facingSign every frame, so turning to aim the other way mid-air can't reverse an
        // already-spinning flip. See that field's own comment for why _facingSign is folded in at all.
        private float _flipSign = 1f;

        // Torso/head/legs each track their own smoothed COPY of _flipT (0-1 progress, not raw
        // degrees - avoids any 360/0 wrap discontinuity when the flip resets) chasing it at their
        // own rate (see legFlipLagSpeed/torsoFlipLagSpeed/headFlipLagSpeed). Root's rotation always
        // carries the full flip immediately (it's the parent every other part hangs off), so the gap
        // between a part's own smoothed progress and root's is applied as a small counter-rotation on
        // that part - giving the classic "core leads, extremities trail" follow-through instead of
        // the whole rig spinning as one rigid block. Deliberately NOT reset to 0 when the flip
        // completes naturally (only on landing-cancel) - letting them keep chasing _flipT back down
        // to 0 on their own is what produces the brief settle/whip-crack after root's own spin is
        // already done.
        private float _legFlipProgress, _torsoFlipProgress, _headFlipProgress;
        private float _legFlipLagDegrees, _torsoFlipLagDegrees, _headFlipLagDegrees;

        private float _stridePhase;
        private float _legPhase;
        private float _facingSign = 1f;
        // Cached copy of Animate()'s own local moveAlignSign (+1 = travelling the same way you're
        // facing, -1 = backpedaling) - read once at flip-trigger time (OnPlayerAutoJumpedDown) to
        // decide front vs back, since the event handler itself has no velocity to derive it from.
        private float _moveAlignSign = 1f;
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

        // Lobby character preview (CharacterPreviewWidget): this rig is an instance of the hero
        // prefab standing in a MenuScene with no QuantumRunner at all, so QUpdate never fires -
        // the widget drives Animate directly through TickPreview instead. The flag exists purely
        // to mute the two sound paths, which key off _entityRef (EntityRef.None here) and would
        // ask AudioManager for a voice for a character that isn't in any match.
        private bool _previewMode;
        // Billboarding normally faces Camera.main. A preview rig is rendered by its own offscreen
        // camera on its own layer, and Camera.main in a menu is the menu's camera pointing
        // somewhere else entirely - which would turn the sprite edge-on. Set, this wins over
        // Camera.main; unset (every in-game rig), nothing changes.
        private Camera _previewCamera;

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
        // Root's current Jump Flip rotation in degrees (0 whenever no flip is playing) - lets a
        // follower (PlayerGunAimView, while untargeted) spin along with the body instead of holding
        // its last aim direction through a tumble it has no target to justify pointing away from.
        public float CurrentFlipDegrees => _flipDegrees;

        [Header("Sound")]
        [SerializeField, SoundDataPicker, Tooltip("Played on EventPlayerJumped - the same event that starts the anticipation squash. Note jumping here is the AUTO-jump ledge assist (AutoJumpSystem), not a button the player pressed, so this is informational rather than input confirmation: keep it subtle. Leave empty to skip.")]
        private SoundData jumpSound;

        [SerializeField, Tooltip("Shared player FX config - LandBurst is played at the exact same justLanded moment/impactSpeed as landSound below (see PlayLandBurst), gated by PlayerFxConfig.LandMinImpactSpeed. Leave empty to skip.")]
        private PlayerFxConfig fxConfig;

        [SerializeField, SoundDataPicker, Tooltip("Played the frame the character regains ground, alongside the landing squash. Volume is scaled by impact speed (see landSoundMinImpactSpeed / landSoundFullImpactSpeed) so a small hop off a ledge doesn't land as hard as a long fall. Leave empty to skip.")]
        private SoundData landSound;

        [SerializeField, Tooltip("Downward speed at or below which a landing is silent - stops the constant micro-landings of walking over uneven geometry from firing a step-thud every few frames.")]
        private float landSoundMinImpactSpeed = 2f;

        [SerializeField, Tooltip("Downward speed at which the landing sound reaches full volume. Between this and the minimum it scales linearly.")]
        private float landSoundFullImpactSpeed = 12f;

        [SerializeField, SoundDataPicker, Tooltip("Footstep, played every footstepDistance world units actually travelled while grounded. Deliberately distance-driven rather than timed, so it self-syncs to real speed (Haste, slows, backpedalling) without reading the stride animation's internals. See the class comment on why this is the first thing to cut if the mix gets crowded. Leave empty to skip.")]
        private SoundData footstepSound;

        [SerializeField, Tooltip("World units of grounded travel between footsteps. Larger = fewer steps. Tune against the run cycle so steps land on the stride rather than drifting against it.")]
        private float footstepDistance = 2.2f;

        // Distance-accumulator for footsteps, and the last position it measured from.
        private float _footstepAccumulator;
        private Vector3 _lastFootstepPosition;
        private bool _hasFootstepPosition;

        public override void Awake()
        {
            base.Awake();
            CacheBaseline();
            _wobbleSeed = Random.value * 1000f;
            QuantumEvent.Subscribe<EventPlayerJumped>(this, OnPlayerJumped);
            QuantumEvent.Subscribe<EventPlayerAutoJumpedDown>(this, OnPlayerAutoJumpedDown);
            QuantumEvent.Subscribe<EventPlayerRevived>(this, OnPlayerRevived);
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

        // Debug preview for the Downed/KO object swap, without a running simulation. QUpdate reasserts
        // the swap off the real PlayerLifeState every frame once one is driving it, so this is purely
        // an Editor/Play-Mode convenience for checking the authored downedRoot/koRoot visuals.
        [Button]
        private void PreviewAlive() => ApplyLifeStateVisuals(PlayerLifeStateKind.Alive);

        [Button]
        private void PreviewDowned() => ApplyLifeStateVisuals(PlayerLifeStateKind.Downed);

        [Button]
        private void PreviewKO() => ApplyLifeStateVisuals(PlayerLifeStateKind.KO);

        // Shows flipPivotHeight in the Scene view so it can be eyeballed against the rig without a
        // live flip running - drawn off root's CURRENT transform, so it stays accurate at rest,
        // mid-pose, or (Play Mode) mid-flip. Selected-only so it doesn't clutter the scene for every
        // other character on screen at once.
        private void OnDrawGizmosSelected()
        {
            if (root == null) return;

            Vector3 pivotWorld = root.TransformPoint(Vector3.up * flipPivotHeight);
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(pivotWorld, 0.06f);
            Gizmos.DrawLine(root.position, pivotWorld);
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
            BeginJumpAnticipation();
        }

        // Auto-hop DOWN off a ledge only (see EventPlayerAutoJumpedDown) - same anticipation squash
        // every jump gets, plus the one-shot tumble a mantle/manual jump never plays.
        private void OnPlayerAutoJumpedDown(EventPlayerAutoJumpedDown e)
        {
            if (e.Entity != _entityRef) return;
            BeginJumpAnticipation();

            if (useFlip == false) return; // falls back to a plain jump, same as a mantle/manual jump

            CancelFlip(); // clean slate in case of an overlapping re-trigger
            _flipActive = true;

            // Front flip while travelling the same way you're facing (moving right while aiming
            // right, or left while aiming left - moveAlignSign >= 0), back flip while backpedaling
            // off the ledge (moving right while aiming left, or vice versa) - invertFlipDirection
            // swaps the two. _moveAlignSign is a cached copy of Animate()'s own local value (this
            // handler has no velocity of its own to derive it from) - see that field's own comment.
            bool isBackflip = (_moveAlignSign < 0f) != invertFlipDirection;

            // Captured once here, not re-derived from live facing every frame - otherwise turning to
            // aim the other way mid-air (a very normal thing to do while airborne) would reverse an
            // already-spinning flip partway through (start a frontflip, end a backflip).
            //
            // The literal +1/-1 mapping here is empirical, not derived - given this rig's actual
            // axis/camera conventions, a NEGATIVE angle is what reads on screen as tumbling forward.
            _flipSign = (isBackflip ? 1f : -1f) * _facingSign;
        }

        // Fires for teammate-hold, self-revive, and the auto-revive-on-secure sweep alike (see
        // docs/revive.md) - all funnel through the same PlayerLifeStateUtility.Revive, one event.
        // Animate() skips its entire deformation pass while not Alive (see its own early-return),
        // so whatever squash/stretch/flip state was mid-pose the instant this player went
        // Downed/KO is still sitting here untouched - zero it and snap back to rest before
        // punching, or the punch would ring out from whatever random leftover scale was frozen in
        // rather than from a clean rest pose.
        private void OnPlayerRevived(EventPlayerRevived e)
        {
            if (e.Target != _entityRef) return;

            _squashT = 0f;
            _jumpSquashT = 0f;
            _springVelocity = 0f;
            _springActive = false;
            CancelFlip();
            ApplyBaselineTransforms();

            PunchBodyScale(revivePunchScaleStrength, revivePunchScaleDuration, revivePunchScaleFrequency);
        }

        // Shared HARD reset for every place a flip actually ENDS - landing, its own natural
        // completion (including a dash's sped-up finish, which also lands here), or a fresh
        // re-trigger clearing stale state first. Always resets everything together (root's own
        // _flipT alongside every part's lag progress) so no part is ever left mid-catch-up after
        // root's rotation has already stopped - a lagging part still easing toward a target that
        // already snapped to 0 reads as the character rotating slightly back toward rest right
        // after the flip finished.
        private void CancelFlip()
        {
            _flipActive = false;
            _flipSpeedingUp = false;
            _flipT = 0f;
            _legFlipProgress = 0f;
            _torsoFlipProgress = 0f;
            _headFlipProgress = 0f;
        }

        private void BeginJumpAnticipation()
        {
            if (jumpSound != null)
                EntitySound.PlayAttached(jumpSound, transform, _entityRef);

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

        // Drives the rig for one frame with no simulation behind it: standing still, on the
        // ground, alive - i.e. the idle breathe/bob/wobble cycle and nothing else. Called every
        // frame by CharacterPreviewWidget, which owns the instantiated prefab and the offscreen
        // camera rendering it. Deliberately a push from outside rather than this component
        // growing its own Update(): CustomQuantumEntityViewComponent already declares a private
        // Update() driving QUpdate, and a second one here would hide it and break every live rig.
        public void TickPreview(float dt)
        {
            _previewMode = true;
            Animate(Vector3.zero, true, PlayerLifeStateKind.Alive, dt);
        }

        // See _previewCamera. Pass null to fall back to Camera.main.
        public void SetPreviewCamera(Camera camera)
        {
            _previewCamera = camera;
        }

        protected override void QUpdate(QuantumGame game)
        {
            var frame = game.Frames.Predicted;
            if (frame.Has<KCC>(_entityRef) == false)
                return;

            var kcc = frame.Get<KCC>(_entityRef);

            // Downed/KO (see docs/revive.md) - a plain active-object swap off PlayerLifeState.State,
            // no procedural pose. Alive shows bodyRoot, Downed shows downedRoot, KO shows koRoot.
            PlayerLifeStateKind lifeState = frame.Has<PlayerLifeState>(_entityRef) == true
                ? frame.Get<PlayerLifeState>(_entityRef).State
                : PlayerLifeStateKind.Alive;

            // Fall-respawn delay pending (see PlayerFallSystem/LevelConfig.FallRespawnDelay) - the
            // character has vanished off the map and is about to be teleported back. Polled every
            // tick, same "self-healing, no event needed" idiom AccessoryView already uses.
            bool isFallPending = FallStateUtility.IsFallPending(frame, _entityRef);

            Vector3 velocity = kcc.Data.RealVelocity.ToUnityVector3();
            UpdateFacing(frame, velocity);

            // Same poll DashFxView already uses - dashing outright cancels an in-progress Jump Flip
            // (see Animate's own cancel block), since a dash's own burst of speed/i-frames reads as
            // a deliberate interrupt, not something that should keep tumbling underneath it.
            bool isDashing = frame.Has<CharacterSkills>(_entityRef) == true
                && frame.Get<CharacterSkills>(_entityRef).DashSkill.State == SkillState.Active;

            Animate(velocity, kcc.Data.IsGrounded, lifeState, Time.deltaTime, isDashing, isFallPending);
        }

        // Drives one frame of the rig off plain values instead of a Frame, so the exact same pose
        // math serves both the live entity (QUpdate above) and the simulation-free lobby character
        // preview (TickPreview above, see CharacterPreviewWidget) - rather than the preview growing
        // its own second copy of the idle cycle to drift out of sync with this one. Everything
        // Quantum-specific stays in QUpdate: the KCC/PlayerLifeState/CharacterSkills reads, and the
        // Aim read that UpdateFacing does. Nothing in here touches _entityRef except the two sound
        // paths, and those no-op under _previewMode.
        private void Animate(Vector3 velocity, bool isGrounded, PlayerLifeStateKind lifeState, float dt, bool isDashing = false, bool isFallPending = false)
        {
            if (root == null && head == null && torso == null && legLeft == null && legRight == null)
                return;

            if (_recaptureBaselineOnResume)
            {
                CacheBaseline();
                _recaptureBaselineOnResume = false;
            }

            ApplyLifeStateVisuals(lifeState, isFallPending);

            // While not Alive - or a fall-respawn is pending - the normal velocity-driven locomotion
            // below is skipped entirely: an incapacitated player can't move anyway
            // (PlayerLifeStateUtility.IsIncapacitated), and a player currently off the map has
            // nothing visible to animate.
            if (lifeState != PlayerLifeStateKind.Alive || isFallPending == true)
            {
                _springActive = false; // cancel any in-flight landing spring so it doesn't resume on revive
                _wasGrounded = isGrounded;
                return;
            }

            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            float verticalSpeed = velocity.y;
            bool justLanded = isGrounded && _wasGrounded == false;
            bool justLeftGround = isGrounded == false && _wasGrounded;

            // Landing cancels the flip outright, not eased - a flip caught mid-rotation on the
            // ground reads as broken rather than interrupted, and landing is already its own
            // distinct squash beat that shouldn't have a tumble running underneath it.
            if (justLanded && _flipActive)
            {
                CancelFlip();
            }
            // Dashing out of a flip instead speeds up to FINISH the rotation quickly
            // (dashFlipSpeedUpDuration) rather than either an instant snap or unwinding backward -
            // it should always keep spinning the same way it was already going, just faster.
            else if (isDashing && _flipActive)
            {
                _flipActive = false;
                _flipSpeedingUp = true;
            }

            // Facing now follows aim rather than velocity, so travel direction and facing can
            // disagree (e.g. strafing/backpedaling while aiming the other way). moveXSign is the
            // character's actual left/right travel; moveAlignSign is +1 when that travel matches
            // facing (normal forward stride) and -1 when it's opposed (backpedal) - used below to
            // run the stride/leg cycle in reverse and lean into the true direction of travel
            // instead of the faced one, so backpedaling doesn't read as moonwalking.
            float moveXSign = Mathf.Abs(velocity.x) > moveSpeedEpsilon ? Mathf.Sign(velocity.x) : _facingSign;
            float moveAlignSign = moveXSign * _facingSign;
            _moveAlignSign = moveAlignSign; // cached for OnPlayerAutoJumpedDown - see that field's own comment

            if (justLanded)
            {
                float impactSpeed = Mathf.Abs(Mathf.Min(0f, verticalSpeed));
                PlayLandSound(impactSpeed);
                PlayLandBurst(impactSpeed);
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

            UpdateFootsteps(isGrounded, horizontalSpeed);

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

            if (_flipActive)
            {
                _flipT += dt / Mathf.Max(0.01f, jumpFlipDuration);
                if (_flipT >= 1f)
                {
                    // Hard reset, same as CancelFlip - a part still lagging behind when root snaps
                    // back to 0 would otherwise keep visibly catching up for a moment afterward,
                    // reading as the character rotating slightly back toward its rest pose right
                    // after the flip already finished.
                    CancelFlip();
                }
            }
            else if (_flipSpeedingUp)
            {
                // Dash interrupt (see the justLanded/isDashing block above) - keeps counting _flipT
                // UP toward 1 (finishing the CURRENT rotation forward) rather than back toward 0,
                // which would visibly spin the character backward to unwind it - a flip must always
                // keep turning the direction it was already turning, only the remaining speed
                // changes. A fixed-rate MoveTowards rather than an exponential lerp so it actually
                // finishes in dashFlipSpeedUpDuration regardless of how far into the flip it was
                // interrupted, instead of asymptotically trailing off forever.
                _flipT = Mathf.MoveTowards(_flipT, 1f, dt / Mathf.Max(0.01f, dashFlipSpeedUpDuration));
                if (_flipT >= 1f)
                {
                    CancelFlip();
                }
            }

            // Legs/torso/head each ease toward root's own _flipT at their own rate (see the fields'
            // own comment) - this naturally rides along with the dash speed-up above too, adding
            // their own extra lag on top of root's own faster finish rather than needing special-casing.
            _legFlipProgress = Mathf.Lerp(_legFlipProgress, _flipT, 1f - Mathf.Exp(-legFlipLagSpeed * dt));
            _torsoFlipProgress = Mathf.Lerp(_torsoFlipProgress, _flipT, 1f - Mathf.Exp(-torsoFlipLagSpeed * dt));
            _headFlipProgress = Mathf.Lerp(_headFlipProgress, _flipT, 1f - Mathf.Exp(-headFlipLagSpeed * dt));

            // _flipSign was captured once at trigger time (see OnPlayerAutoJumpedDown) rather than
            // read live here - it already folds in the facing this jump started with, and reusing
            // that same fixed sign for the whole flip (instead of re-deriving it from _facingSign
            // every frame) is what keeps the rotation going the same way even if the player turns to
            // aim the other way mid-air.
            _flipDegrees = _flipT * 360f * _flipSign;
            _legFlipLagDegrees = _legFlipProgress * 360f * _flipSign - _flipDegrees;
            _torsoFlipLagDegrees = _torsoFlipProgress * 360f * _flipSign - _flipDegrees;
            _headFlipLagDegrees = _headFlipProgress * 360f * _flipSign - _flipDegrees;

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

                // _headFlipLagDegrees is the follow-through lag behind root's own flip - see that
                // field's own comment; 0 whenever no flip is in play, so this composes for free with
                // the shoot-punch rotation it already sits alongside here.
                head.localRotation = _headBaseRot * Quaternion.Euler(0f, 0f, _headPunchRotation + _headFlipLagDegrees);
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

        // Scaled by how hard the landing actually was, using the same impact speed the landing
        // squash is derived from - so the sound and the visual always agree about how big it was.
        // Below landSoundMinImpactSpeed it's skipped entirely: walking across uneven chunk geometry
        // produces a stream of tiny regroundings, and a thud on each one reads as a stutter.
        private void PlayLandSound(float impactSpeed)
        {
            if (_previewMode || landSound == null || impactSpeed < landSoundMinImpactSpeed)
                return;

            float range = Mathf.Max(0.01f, landSoundFullImpactSpeed - landSoundMinImpactSpeed);
            float volume = Mathf.Clamp01((impactSpeed - landSoundMinImpactSpeed) / range);

            EntitySound.PlayAttached(landSound, transform, _entityRef, volume);

            // A landing interrupts the stride, so the next footstep should be a full stride away
            // rather than firing immediately on top of the thud.
            _footstepAccumulator = 0f;
        }

        // Same justLanded call site as PlayLandSound above - reusing it here (instead of a
        // separately-polling GroundedFxView) means this can never drift out of sync with, or
        // simply go missing from, the landing moment landSound already fires on correctly.
        private void PlayLandBurst(float impactSpeed)
        {
            if (_previewMode || fxConfig == null || fxConfig.LandBurst.Prefab == null || EffectsManager.Instance == null)
                return;

            if (impactSpeed < fxConfig.LandMinImpactSpeed)
                return;

            Quaternion rotation = transform.rotation * Quaternion.Euler(fxConfig.LandBurst.RotationOffset);
            Vector3 scale = fxConfig.LandBurst.Prefab.transform.localScale * fxConfig.LandBurst.ScaleMultiplier;
            Vector3 position = ResolveGroundPosition() + fxConfig.LandBurst.ResolveWorldPositionOffset(transform);
            EffectsManager.Instance.PlayEffect(fxConfig.LandBurst.Prefab, position, rotation, scale);
        }

        // KCC.Position (mirrored onto Transform3D, which this GameObject's transform follows) is
        // the capsule's BASE, i.e. ground/feet level, not center - see
        // EnemyMovementUtility.ResolveEntityCenter's own +Height/2 to reach center from it. Reading
        // it straight from simulation state here (rather than trusting transform.position) keeps
        // this correct regardless of which GameObject in the hierarchy this component ends up on.
        private Vector3 ResolveGroundPosition()
        {
            if (_game == null)
                return transform.position;

            Frame f = _game.Frames.Verified;
            if (f == null || f.Has<Transform3D>(_entityRef) == false)
                return transform.position;

            return f.Get<Transform3D>(_entityRef).Position.ToUnityVector3();
        }

        // Distance-driven rather than timed: accumulate real horizontal travel and fire a step every
        // footstepDistance units. That self-scales with movement speed - Haste, slows and
        // backpedalling all change step cadence for free - without this view needing to know
        // anything about the run cycle's own phase.
        private void UpdateFootsteps(bool isGrounded, float horizontalSpeed)
        {
            if (_previewMode || footstepSound == null || footstepDistance <= 0f)
                return;

            Vector3 position = transform.position;

            if (isGrounded == false || horizontalSpeed <= moveSpeedEpsilon)
            {
                // Airborne or standing still: hold the accumulator and drop the reference point, so
                // the distance covered by a dash or a fall never counts toward a footstep.
                _hasFootstepPosition = false;
                return;
            }

            if (_hasFootstepPosition == false)
            {
                _lastFootstepPosition = position;
                _hasFootstepPosition = true;
                return;
            }

            Vector3 delta = position - _lastFootstepPosition;
            delta.y = 0f;
            _footstepAccumulator += delta.magnitude;
            _lastFootstepPosition = position;

            if (_footstepAccumulator < footstepDistance)
                return;

            // Reset rather than subtract: a single frame that covered several strides (a teleport,
            // a respawn) should produce ONE step, not a burst of them.
            _footstepAccumulator = 0f;
            EntitySound.PlayAttached(footstepSound, transform, _entityRef);
        }

        private State DetermineGroundedState(float horizontalSpeed)
        {
            return horizontalSpeed > moveSpeedEpsilon ? State.Run : State.Idle;
        }

        // Shows exactly one of the three authored roots for the current life state (see docs/revive.md).
        // Idempotent - SetActive only changes state when it differs - so calling this every frame is
        // free. Any root left unassigned is simply skipped.
        //
        // isFallPending (PlayerFallSystem's own FallRespawnTimer, see LevelConfig.FallRespawnDelay)
        // forces every root off regardless of life state - a fall isn't a life-state change, it's a
        // separate "vanished off the map, about to respawn" window layered on top of it.
        private void ApplyLifeStateVisuals(PlayerLifeStateKind state, bool isFallPending = false)
        {
            bool alive = state == PlayerLifeStateKind.Alive && isFallPending == false;
            SetActive(bodyRoot, alive);
            SetActive(handsRoot, alive);
            SetActive(downedRoot, isFallPending == false && state == PlayerLifeStateKind.Downed);
            SetActive(koRoot, isFallPending == false && state == PlayerLifeStateKind.KO);
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
                go.SetActive(active);
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
            // Follow-through lag, see _legFlipLagDegrees - 0 whenever no flip is in play.
            if (legLeft != null)
                legLeft.localRotation = _legLeftBaseRot * Quaternion.Euler(0f, 0f, _legAngleT + _legFlipLagDegrees);
            if (legRight != null)
                legRight.localRotation = _legRightBaseRot * Quaternion.Euler(0f, 0f, -_legAngleT + _legFlipLagDegrees);
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

        private void ApplyPose(float leanDegrees, float rockDegrees, float bobOffset, float upperBodyBobOffset, float depthOffset = 0f)
        {
            float verticalMult = Mathf.Clamp(1f - _squashT * volumePreservation, 0.15f, 3f);
            float horizontalMult = Mathf.Clamp(1f / verticalMult, 0.3f, 3f);

            if (torso != null)
            {
                torso.localScale = Vector3.Scale(_torsoBaseScale, new Vector3(horizontalMult, verticalMult, horizontalMult));
                var torsoPos = _torsoBaseLocalPos;
                torsoPos.y += upperBodyBobOffset;
                torso.localPosition = torsoPos;
                // Follow-through lag, see _torsoFlipLagDegrees - 0 whenever no flip is in play, so
                // this never fights any other torso rotation (torso has none of its own otherwise).
                torso.localRotation = Quaternion.Euler(0f, 0f, _torsoFlipLagDegrees);
            }

            // Jump squash/stretch lives on root (see below) so it reaches every child, head
            // included, purely through parent-child scale inheritance. Counter-scale the head
            // here to claw back most of that - a landing splat that also squishes the head into
            // mush reads as broken rather than expressive, so the head only takes a fraction of
            // what root does (jumpHeadSquashInfluence) while torso still gets it in full.
            float rootVerticalMult = Mathf.Clamp(1f - _jumpSquashT * volumePreservation, 0.15f, 3f);
            float rootHorizontalMult = Mathf.Clamp(1f / rootVerticalMult, 0.3f, 3f);

            // Jump Flip overrides speed-driven squash/stretch entirely while it plays (a
            // non-uniform stretch spinning through a full 360° would read as the rig warping
            // mid-tumble, not a clean flip) and replaces it with a squash driven purely by
            // rotation PHASE instead: a flat Z-axis spin never actually foreshortens on a
            // billboarded sprite the way a real tumble would, so this fakes that - squashing
            // toward flipMidRotationSquash at the two points (90°/270°) where a real body
            // rotating edge-on to the camera would look thinnest, and back to full size at
            // 0°/180° (facing the camera square-on) - twice per revolution, same as how a
            // spinning coin shows its thin edge twice per turn. Reads as far more
            // three-dimensional/believable than an undeformed spin. A pure no-op at rest
            // (_flipDegrees is 0, sin(0) is 0, so both multipliers land on exactly 1).
            float flipRad = _flipDegrees * Mathf.Deg2Rad;
            float flipVerticalMult = Mathf.Lerp(1f, flipMidRotationSquash, Mathf.Abs(Mathf.Sin(flipRad)));

            if (_flipActive || _flipSpeedingUp)
            {
                rootVerticalMult = flipVerticalMult;
                rootHorizontalMult = Mathf.Clamp(1f / flipVerticalMult, 0.3f, 3f);
            }

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
                localPos.z += depthOffset;
                root.localPosition = localPos;

                // Jump squash/stretch - the whole rig deforms together as one blob, instead of
                // torso/head/legs each animating separately.
                var scale = _rootBaseScale;
                scale.x *= _facingSign * rootHorizontalMult;
                scale.y *= rootVerticalMult;
                scale.z *= rootHorizontalMult;
                root.localScale = scale;
                CurrentRootVerticalScale = rootVerticalMult;

                // Jump Flip - a spin about the billboard's own local Z axis (the same axis
                // lean/rock/totalTilt already rotate on, i.e. the axis pointing straight at the
                // camera after LookRotation) so it's centered on the sprite's current facing rather
                // than tumbling it off-plane. 0 whenever no flip is active, so this is a pure no-op
                // for every jump that isn't an auto-hop off a ledge. Torso/head/legs each counter
                // this with their own small lag offset - see _torsoFlipLagDegrees etc.
                float totalTilt = leanDegrees + rockDegrees + _flipDegrees;
                CurrentLeanDegrees = leanDegrees;
                CurrentRockDegrees = rockDegrees;
                CurrentBobOffset = bobOffset + upperBodyBobOffset;

                // Root's own origin sits at the feet (ground contact point) - rotating root about
                // ITS origin would swing head/torso through a wide arc around the feet instead of
                // tumbling in place. pivotLocal picks a point roughly at the character's own center
                // instead; baseRotation (no flip) vs fullRotation (with flip) lets us solve for the
                // root position that keeps that pivot point fixed in world space across the flip,
                // exactly like rotating around an arbitrary pivot. This is a pure no-op for
                // lean/rock alone (fullRotation == baseRotation when _flipDegrees is 0), so idle/run
                // tilt is completely unaffected - only the flip itself gets re-centered.
                Vector3 pivotLocal = Vector3.up * flipPivotHeight;

                Camera billboardCamera = _previewCamera != null ? _previewCamera : Camera.main;
                if (billboardToCamera && billboardCamera != null)
                {
                    var camForward = billboardCamera.transform.forward;
                    Quaternion billboardBase = Quaternion.LookRotation(camForward, Vector3.up);
                    Quaternion baseRotation = billboardBase * Quaternion.Euler(0f, 0f, leanDegrees + rockDegrees);
                    Quaternion fullRotation = billboardBase * Quaternion.Euler(0f, 0f, totalTilt);

                    Vector3 worldRootPos = root.position; // reflects the localPosition set above
                    Vector3 pivotWorld = worldRootPos + baseRotation * pivotLocal;

                    root.rotation = fullRotation;
                    root.position = pivotWorld - fullRotation * pivotLocal;
                }
                else
                {
                    Quaternion baseRotation = Quaternion.Euler(0f, 0f, leanDegrees + rockDegrees);
                    Quaternion fullRotation = Quaternion.Euler(0f, 0f, totalTilt);

                    Vector3 pivotAtRest = localPos + baseRotation * pivotLocal;

                    root.localRotation = fullRotation;
                    root.localPosition = pivotAtRest - fullRotation * pivotLocal;
                }
            }
        }
    }
}

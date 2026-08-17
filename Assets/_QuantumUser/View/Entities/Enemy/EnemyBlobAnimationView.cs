using NaughtyAttributes;
using Photon.Deterministic;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Simplified sibling of BlobAnimationView for enemies - same squash/stretch blob feel, but
    // no legs (enemies don't have a leg rig) and no jump/landing. Idle/Run are purely reactive to
    // PhysicsBody3D.Velocity every frame; Die is a one-shot trigger fired automatically off
    // Enemy.Phase reaching Dead. Attack-phase animation (windup/strike/etc.) is NOT owned here
    // anymore - EnemyAttackVisualsView reads Enemy.Phase/EnemyActionData and calls PlayAttackStep()
    // below with whatever AttackVisualStep the active action configured for that phase, so the
    // same tell plays identically for every enemy sharing that EnemyActionData instead of being a
    // fixed per-prefab choice. This component only knows *how* to render a step (the squash/lean/
    // rock/bob math), never *which* one or *when*. head/torso are optional - with no split rig,
    // root itself is the whole visible body and carries the squash directly.
    public class EnemyBlobAnimationView : CustomQuantumEntityViewComponent
    {
        // This component lives on the generic enemy prototype (shared across enemy types), not on
        // EnemyDataAsset.ViewPrefab - the rig only exists once EnemyView.SpawnSprite instantiates
        // that prefab at runtime, so it can't be an Inspector-wired SerializeField like a normal
        // sibling reference. See SetRig. (root is the whole body if head/torso are left empty)
        private EnemyViewRig rig;

        // Head/torso are optional - not every enemy rig splits the body into separate parts; when
        // left unset on the rig, root carries the squash directly (rootCarriesSquash below). Arm
        // is likewise optional - only enemies whose attack needs arm-only motion
        // (AttackAnimationType.ArmSwingBack/ArmSnap below) or continuous aim tracking (see
        // EnemyArmAimView) use it. All must be a child of root (or another transform under it) so
        // they inherit the facing-flip scale together.
        private Transform root => rig != null ? rig.EnemyRoot : null;
        private Transform head => rig != null ? rig.Head : null;
        private Transform torso => rig != null ? rig.Torso : null;
        private Transform arm => rig != null ? rig.Arm : null;

        [Header("Facing")]
        [SerializeField] private bool billboardToCamera = true;
        [SerializeField] private float facingDeadzone = 0.2f;

        [Header("Idle")]
        [SerializeField] private float idleBreatheFrequency = 1.2f;
        [SerializeField] private float idleBreatheAmount = 0.05f;
        [SerializeField] private float idleWobbleDegrees = 2f;
        [SerializeField] private float idleWobbleSpeed = 0.3f;

        [Header("Movement")]
        [SerializeField] private float runReferenceSpeed = 4f;
        [SerializeField, Tooltip("Above this horizontal speed, idle becomes run.")] private float moveSpeedEpsilon = 0.15f;

        [Header("Run")]
        [SerializeField] private float runStrideFrequency = 2.2f;
        [SerializeField] private float runSquashAmount = 0.18f;
        [SerializeField] private float runBounceAmount = 0.08f;
        [SerializeField] private float runLeanDegrees = 12f;
        [SerializeField] private float runStepRockDegrees = 8f;

        [Header("Die (hop up, then swings over and lands flat - final pose holds)")]
        [SerializeField] private float dieDuration = 0.6f;
        [SerializeField] private float dieJumpHeight = 0.35f;
        [SerializeField, Tooltip("How far the body rolls over as it falls. Root pivots from its base (ground level), so keep this at/below 90 - past 90 the body rotates past flat and swings back down through the ground.")] private float dieToppleDegrees = 90f;
        [SerializeField] private float dieSquash = 0.4f;
        [SerializeField, Tooltip("Extra vertical (local Y) nudge applied to the final fallen pose - positive lifts the corpse off the ground, negative sinks it in, to compensate if the rig's pivot doesn't line up with the visual ground contact point.")] private float dieGroundOffsetY = 0f;
        [SerializeField, Tooltip("Extra depth (local Z) nudge applied to the final fallen pose - shifts the corpse forward/back, e.g. to keep it visually in front of/behind other sprites once it's lying flat.")] private float dieGroundOffsetZ = 0f;
        [SerializeField, Tooltip("Extra pause after the fall finishes before the shrink (below) begins, so the fallen corpse holds on screen for a beat first.")] private float dieShrinkDelay = 0.5f;
        [SerializeField, Tooltip("Once the fall above finishes (plus the delay above), the corpse shrinks to nothing over this many seconds. Keep dieDuration + dieShrinkDelay + this under EnemyDataAsset.DeathLingerTime or the entity/view gets torn down mid-shrink.")] private float dieShrinkDuration = 0.4f;

        [Header("Burrow (shrinks/sinks away on dive, holds hidden while Burrowed, grows back on resurface)")]
        [SerializeField] private float burrowSinkDuration = 0.35f;
        [SerializeField] private float burrowRiseDuration = 0.35f;
        [SerializeField, Tooltip("Squash pop played at the midpoint of the sink/rise transition.")] private float burrowSquash = 0.5f;
        [SerializeField, Tooltip("How far the body visually sinks into the ground as it shrinks away. Local Z (depth), not Y - see jumpCrouchSinkAmount's own tooltip below for why.")] private float burrowSinkAmount = 0.3f;

        [Header("Jump (cliff-climb/gap-jump traversal hop - EnemyMovementUtility.BeginTraversalJump - small crouch then a pop)")]
        [SerializeField, Tooltip("Squash/sink strength of the crouch played during Enemy.TraversalJumpAnticipationTimer - the real windup EnemyMovementUtility.QueueTraversalJump opens before the hop launches (EnemyMovementUtility.TraversalJumpAnticipationTime), not a guessed fraction of the hop itself.")]
        private float jumpCrouchSquash = 0.3f;
        [SerializeField, Tooltip("Local Z (depth), not Y - root's local Y is real world-up (the actual hop arc TickTraversalJump bakes into Transform3D.Position), so sinking the anticipation crouch on Y would visually push the sprite below the real ground plane it's standing on. Z has no such floor to clip through.")]
        private float jumpCrouchSinkAmount = 0.1f;
        [SerializeField, Tooltip("Stretch pop played the instant the anticipation ends and the hop actually launches, decaying back to neutral by the time it lands (Enemy.TraversalJumpTimer/TraversalJumpDuration) - the vertical arc itself is already baked into Transform3D.Position by TickTraversalJump, so this is purely the squash/stretch tell layered on top.")]
        private float jumpPopStretch = 0.35f;

        [Header("Center Pivot (AttackVisualStep.CenterPivot)")]
        [SerializeField, Tooltip("Default local-space height, in root's own unscaled units, from root's pivot (its base/ground-contact point - see dieToppleDegrees' tooltip) up to the point a CenterPivot-enabled step rotates around, used whenever that step's own PivotHeightOverride is left at 0. 0 here too = auto-detect from EnemyViewRig.ReferenceSprite's bounds at spawn, so it self-corrects per rig without hand-tuning; set explicitly only if a rig's sprite bounds don't line up with its actual visual center.")]
        private float defaultCenterPivotHeight = 0f;

        [Header("General")]
        [SerializeField] private float squashLerpSpeed = 14f;
        [SerializeField] private float volumePreservation = 0.6f;
        [SerializeField, Tooltip("How much of the body's squash the head also gets.")] private float headSquashInfluence = 0.4f;

        [Header("Debug")]
        [SerializeField, Tooltip("Assign any AttackVisualStep here (e.g. copy one out of an EnemyActionData asset) to preview it with the button below, without a running simulation.")]
        private AttackVisualStep debugTestStep;

        private enum State { Idle, Run, AttackStep, Die, Burrow, Jump }
        private State _state = State.Idle;
        private float _stateTimer;
        private float _horizontalSpeed;
        private EnemyActionPhase? _lastEnemyPhase;
        private bool _lastBurrowed;
        private bool _lastJumping;

        // True while Enemy.TraversalJumpAnticipationTimer is still counting down (crouch), false
        // once the real hop has actually launched (TraversalJumpDuration - pop/flight). Sampled once
        // per QUpdate and read by UpdatePose's Jump case alongside _jumpT below.
        private bool _jumpAnticipating;

        // 0-1 progress through whichever of the two phases above is currently active - the
        // anticipation window (TraversalJumpAnticipationTimer counting down from
        // EnemyMovementUtility.TraversalJumpAnticipationTime) or the hop itself (TraversalJumpTimer/
        // TraversalJumpDuration) - driven by the simulation's own timers rather than a separately-
        // ticking local one, so the crouch/pop animation always matches the real hop exactly,
        // regardless of its actual duration (climb vs. gap, slow vs. fast enemy).
        private float _jumpT;

        // The step currently driving State.AttackStep's pose - set by PlayAttackStep, read by
        // UpdatePose. Owned externally (EnemyAttackVisualsView / the debug button below).
        private AttackVisualStep _currentStep;

        private Vector3 _headBaseScale, _torsoBaseScale;
        private Vector3 _headBaseLocalPos, _torsoBaseLocalPos;
        private Vector3 _rootBaseLocalPos, _rootBaseScale;
        private Quaternion _armBaseLocalRot = Quaternion.identity;
        private Vector3 _armBaseScale = Vector3.one;
        private Vector3 _armBaseLocalPos;

        // Only used when defaultCenterPivotHeight is left at 0 - see CacheBaseline.
        private float _autoCenterPivotHeight;

        // Whatever ReferenceSprite's SpriteRenderer actually held at spawn (before any attack
        // swap) - cached once in CacheBaseline so ResetBodySprite always has the
        // real rest sprite to restore, regardless of what a previous pooled use of this same
        // ViewPrefabPool instance left the renderer showing.
        private Sprite _defaultBodySprite;

        // Counts down whatever AttackVisualStep.Duration was passed to the most recent
        // ApplyStepSprite call - once it reaches 0, QUpdate reverts to _defaultBodySprite, same
        // "this step's own Duration, not the whole attack" scoping the transform-animation
        // channel already gets from _stateTimer vs. _currentStep.Duration. <= 0 = no swap pending.
        private float _bodySpriteTimeRemaining;

        // Positive = compressed, negative = stretched. Single value - unlike the player's blob,
        // there's no separate jump-squash to keep independent from the idle/run breathing squash.
        private float _squashT;

        private float _stridePhase;
        private float _facingSign = 1f;
        private float _wobbleSeed;
        private float _dieToppleSign = 1f;

        // 0 = full size, 1 = fully shrunk - only advances once the fall (dieDuration) itself has
        // finished, so the shrink never overlaps/fights the topple.
        private float _dieShrinkT;

        // 0 = fully visible, 1 = fully hidden - driven toward 1 while sinking (TriggerBurrowDown)
        // and back toward 0 while rising (TriggerBurrowUp), unlike _dieShrinkT which only ever
        // goes one way. Sits pinned at 1 in between (while Burrowed stays true and the enemy is
        // actually traveling underground) since nothing drives it further until the falling edge.
        private float _burrowT;
        private bool _burrowSinking;

        // Optional sibling on the same generic entity GameObject (not the rig) - see SetShadow.
        // Null for any enemy view with no HasShadow component.
        private HasShadow _shadow;
        private float _shadowBaseScale;

        public override void Awake()
        {
            base.Awake();
            _wobbleSeed = Random.value * 1000f;
        }

        // Called by EnemyView.SpawnSprite right after it instantiates EnemyDataAsset.ViewPrefab -
        // root/head/torso/arm are all null until this runs (this component's own Awake fires long
        // before that, while the prototype is still authored/static), so CacheBaseline has to run
        // here instead of Awake or it would cache nothing.
        public void SetRig(EnemyViewRig rig)
        {
            this.rig = rig;
            CacheBaseline();
        }

        // Called by EnemyView.ConnectRig, AFTER EnemyView.SpawnSprite has already called
        // shadow.SetBaseScale(radius * 2f) - so shadow.BaseScale here is already the entity's real
        // radius-based footprint, not whatever flat value HasShadow was authored with. Cached once
        // rather than read live every frame since nothing else ever changes it after spawn.
        public void SetShadow(HasShadow shadow)
        {
            _shadow = shadow;
            _shadowBaseScale = shadow != null ? shadow.BaseScale : 0f;
        }

        private void CacheBaseline()
        {
            if (root != null) { _rootBaseLocalPos = root.localPosition; _rootBaseScale = root.localScale; }
            if (head != null) { _headBaseScale = head.localScale; _headBaseLocalPos = head.localPosition; }
            if (torso != null) { _torsoBaseScale = torso.localScale; _torsoBaseLocalPos = torso.localPosition; }
            if (arm != null) { _armBaseLocalRot = arm.localRotation; _armBaseScale = arm.localScale; _armBaseLocalPos = arm.localPosition; }

            // rig.ReferenceSprite.bounds is world-space at this point (fit scale/position already
            // applied by EnemyView.SpawnSprite before SetRig runs) - InverseTransformPoint converts
            // that back into root's own unscaled local frame, the same units defaultCenterPivotHeight/
            // PivotHeightOverride are authored in, so a 0 (unset) field transparently falls back to
            // this per-rig estimate.
            if (root != null && rig != null && rig.ReferenceSprite != null)
                _autoCenterPivotHeight = root.InverseTransformPoint(rig.ReferenceSprite.bounds.center).y;

            _defaultBodySprite = rig != null && rig.ReferenceSprite != null ? rig.ReferenceSprite.sprite : null;
        }

        // FacingSign mirrors root's own scale.x sign (+1 = right, -1 = left) - exposed so
        // EnemyArmAimView can flip its aim angle in lockstep with the body instead of deriving
        // facing on its own (same reasoning as BlobAnimationView.FacingSign for PlayerGunAimView).
        public float FacingSign => _facingSign;

        // Called by EnemyAttackVisualsView once per attack phase that has a configured step - the
        // step's own AnimationType/Duration/style fields drive UpdatePose's State.AttackStep case
        // below, same math this component always used, just sourced from the passed-in step
        // instead of a fixed per-prefab Inspector choice. A step with AnimationType == None is a
        // no-op (that phase has no body animation configured, e.g. a phase used only for a particle).
        public void PlayAttackStep(AttackVisualStep step)
        {
            if (step == null || step.AnimationType == AttackAnimationType.None)
                return;

            _currentStep = step;
            _state = State.AttackStep;
            _stateTimer = 0f;

            if (step.AnimationType == AttackAnimationType.Lunge)
                _squashT = -step.Lunge.Stretch; // instant pop, eased back to neutral in UpdatePose

            UpdatePose(0f); // apply the t=0 pose immediately - don't wait for the next tick to render it
        }

        // Called by EnemyAttackVisualsView.PlayPhase for whichever AttackVisualStep just played
        // (Anticipation/Begin/OnGoing/End) - a step with no BodySprite configured is a no-op, so
        // the currently-showing sprite (whatever an earlier step in this same attack set, or
        // still the rest sprite) is left untouched. Mirrors PlayAttackStep's own "some steps opt
        // in, some don't" shape, just for the body's sprite instead of its transform animation.
        // Only shows for this step's own duration - QUpdate counts _bodySpriteTimeRemaining down
        // and reverts to the rest sprite once it runs out, rather than leaving the swap in place
        // until some later step (or the whole attack ending) touches it.
        public void ApplyStepSprite(Sprite sprite, float duration)
        {
            if (rig == null || rig.ReferenceSprite == null || sprite == null)
                return;

            rig.ReferenceSprite.sprite = sprite;
            _bodySpriteTimeRemaining = duration;
        }

        // Called by EnemyAttackVisualsView on the attackNoLongerActive edge (Enemy.Phase leaving
        // the Preparation/Telegraph/Active window) and from DeInitialize - unconditionally
        // restores whatever sprite this rig actually spawned with, regardless of which step (if
        // any) last swapped it via ApplyStepSprite, and cancels any pending duration countdown so
        // QUpdate doesn't try to revert an already-reverted sprite next frame.
        public void ResetBodySprite()
        {
            _bodySpriteTimeRemaining = 0f;

            if (rig == null || rig.ReferenceSprite == null)
                return;

            rig.ReferenceSprite.sprite = _defaultBodySprite;
        }

        [Button]
        public void TriggerDie()
        {
            _state = State.Die;
            _stateTimer = 0f;
            _dieShrinkT = 0f;
            _dieToppleSign = Random.value < 0.5f ? -1f : 1f; // vary which way the corpse falls
            UpdatePose(0f);
        }

        [Button, Tooltip("Preview debugTestStep above without a running simulation.")]
        private void PlayDebugTestStep()
        {
            PlayAttackStep(debugTestStep);
        }

        [Button]
        public void TriggerJump()
        {
            _state = State.Jump;
            UpdatePose(0f);
        }

        [Button]
        public void TriggerBurrowDown()
        {
            _state = State.Burrow;
            _burrowSinking = true;
            UpdatePose(0f);
        }

        [Button]
        public void TriggerBurrowUp()
        {
            _state = State.Burrow;
            _burrowSinking = false;
            UpdatePose(0f);
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (root == null && head == null && torso == null)
                return;

            var frame = game.Frames.Predicted;
            bool hasEnemy = frame.Has<Enemy>(_entityRef);
            Enemy enemy = hasEnemy == true ? frame.Get<Enemy>(_entityRef) : default;
            EnemyActionPhase? enemyPhase = hasEnemy == true ? enemy.Phase : null;

            if (enemyPhase.HasValue == true)
            {
                if (enemyPhase == EnemyActionPhase.Dead && _lastEnemyPhase != EnemyActionPhase.Dead)
                    TriggerDie();

                _lastEnemyPhase = enemyPhase;
            }

            // Burrowed is a plain tag (see BurrowDeliveryData/Burrowed.qtn), not part of
            // EnemyActionPhase - watched the same edge-triggered way as Dead above, just off its
            // own bool instead of a phase value.
            bool isBurrowed = frame.Has<Burrowed>(_entityRef);

            if (isBurrowed != _lastBurrowed)
            {
                if (isBurrowed == true)
                    TriggerBurrowDown();
                else
                    TriggerBurrowUp();

                _lastBurrowed = isBurrowed;
            }

            // Two back-to-back windows, both watched the same edge-triggered way as Burrowed above:
            // TraversalJumpAnticipationTimer counting down (EnemyMovementUtility.QueueTraversalJump's
            // brief crouch windup) then TraversalJumpDuration active (the real kinematic hop -
            // BeginTraversalJump/TickTraversalJump). <= 0 on both means nothing going on.
            bool isAnticipatingJump = hasEnemy == true && enemy.TraversalJumpAnticipationTimer > FP._0;
            bool isJumpInFlight = hasEnemy == true && enemy.TraversalJumpDuration > FP._0;
            bool isJumping = isAnticipatingJump == true || isJumpInFlight == true;

            if (isJumping != _lastJumping)
            {
                if (isJumping == true)
                    TriggerJump();

                _lastJumping = isJumping;
            }

            // _jumpT rides whichever phase's own simulation timer is currently active rather than a
            // separately-ticking local one, so the crouch/pop always matches the real windup/hop
            // exactly, whether it's a short climb or a long, speed-scaled gap jump.
            _jumpAnticipating = isAnticipatingJump;
            _jumpT = isAnticipatingJump == true
                ? Mathf.Clamp01(1f - (enemy.TraversalJumpAnticipationTimer / EnemyMovementUtility.TraversalJumpAnticipationTime).AsFloat)
                : isJumpInFlight == true
                    ? Mathf.Clamp01((enemy.TraversalJumpTimer / enemy.TraversalJumpDuration).AsFloat)
                    : 0f;

            Vector3 velocity = frame.Has<PhysicsBody3D>(_entityRef) == true
                ? frame.Get<PhysicsBody3D>(_entityRef).Velocity.ToUnityVector3()
                : Vector3.zero;
            _horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            float dt = Time.deltaTime;

            // Ice+RiftMark's Deep Freeze reaction stretches the enemy's own Preparation/Telegraph windup
            // (StatusEffectUtility.GetAnticipationMultiplier - see EnemySystem.UpdatePreparation,
            // which scales the simulation's own StateTimer the same way). Only while actually
            // playing the windup step - Active/Recovery attack steps (Begin/OnGoing/End) are
            // untouched, same scoping the simulation side already uses. Scaling dt itself here (not
            // just the _stateTimer increment below) also slows every dt-driven lerp inside
            // UpdatePose's AttackStep case, so the whole windup step evolves consistently, not just
            // its Duration ramp.
            if (_state == State.AttackStep
                && (enemyPhase == EnemyActionPhase.Preparation || enemyPhase == EnemyActionPhase.Telegraph))
            {
                dt *= StatusEffectUtility.GetAnticipationMultiplier(frame, _entityRef).AsFloat;
            }

            // Recovery is the attack's downtime - the enemy just stands there resting, so
            // re-aiming toward a target that keeps moving during this window would spin the sprite
            // right after the strike lands, reading as another attack instead of a rest beat.
            // EnemySystem already never touches Aim.Angle during Recovery, but this makes the
            // "no facing changes during downtime" rule explicit at the view layer too. Active
            // (e.g. a dash) is excluded from this freeze - the enemy is genuinely moving toward a
            // real point there, so facing should keep following it.
            if (enemyPhase != EnemyActionPhase.Recovery)
                UpdateFacing(frame, velocity);

            _stateTimer += dt;

            // Independent of _state/_stateTimer above - a step can carry a BodySprite with
            // AnimationType.None (sprite-only, no transform tell), which never touches _state at
            // all (see PlayAttackStep's own early-out), so this needs its own timer rather than
            // riding triggerStillPlaying below.
            if (_bodySpriteTimeRemaining > 0f)
            {
                _bodySpriteTimeRemaining -= dt;

                if (_bodySpriteTimeRemaining <= 0f)
                    ResetBodySprite();
            }

            bool triggerStillPlaying = (_state == State.AttackStep && _currentStep != null && _stateTimer < _currentStep.Duration)
                || _state == State.Die // holds its final fallen pose once triggered - never reverts on its own
                || (_state == State.Burrow && (isBurrowed == true || _burrowT > 0f)) // holds hidden while still Burrowed, then plays out the rise
                || (_state == State.Jump && isJumping == true); // reverts to grounded the instant the real hop lands

            if (triggerStillPlaying == false)
                _state = DetermineGroundedState(_horizontalSpeed);

            UpdatePose(dt);
        }

        private void UpdatePose(float dt)
        {
            float leanTarget = 0f;
            float rockTarget = 0f;
            float bobTarget = 0f;
            float depthTarget = 0f;
            float armRotationTarget = 0f;
            float armScaleTarget = 0f;
            float punchScaleTarget = 0f;
            bool centerPivot = false;
            float pivotHeightOverride = 0f;

            switch (_state)
            {
                case State.Idle:
                {
                    float breathe = Mathf.Sin(Time.time * idleBreatheFrequency * Mathf.PI * 2f) * idleBreatheAmount;
                    _squashT = Mathf.Lerp(_squashT, breathe, 1f - Mathf.Exp(-squashLerpSpeed * dt));
                    float wobble = (Mathf.PerlinNoise(_wobbleSeed, Time.time * idleWobbleSpeed) - 0.5f) * 2f;
                    rockTarget = wobble * idleWobbleDegrees;
                    break;
                }
                case State.Run:
                {
                    float bodyHz = runStrideFrequency * Mathf.Max(_horizontalSpeed / runReferenceSpeed, 0.2f);
                    _stridePhase += bodyHz * dt;
                    _stridePhase -= Mathf.Floor(_stridePhase);

                    float runSquash = Mathf.Cos(_stridePhase * 4f * Mathf.PI) * runSquashAmount;
                    _squashT = Mathf.Lerp(_squashT, runSquash, 1f - Mathf.Exp(-squashLerpSpeed * dt * 2f));

                    bobTarget = Mathf.Abs(Mathf.Sin(_stridePhase * 4f * Mathf.PI)) * runBounceAmount;
                    leanTarget = Mathf.Clamp01(_horizontalSpeed / runReferenceSpeed) * runLeanDegrees * _facingSign;
                    rockTarget = Mathf.Sin(_stridePhase * 4f * Mathf.PI) * runStepRockDegrees;
                    break;
                }
                case State.AttackStep:
                {
                    AttackVisualStep step = _currentStep;
                    if (step == null)
                        break;

                    float duration = step.Duration;
                    float t = duration > 0f ? Mathf.Clamp01(_stateTimer / duration) : 1f;
                    float decay = 1f - t; // ease-out toward neutral; styles that decay from an instant peak use this

                    // Author-controlled per step (AttackVisualStepDrawer only shows these for the
                    // rotation-producing types below) rather than hardcoded per AnimationType - a
                    // whip-crack Snap might want the base pivot's arc just as much as a rocking
                    // Shake, while a big Spin almost always wants to stay centered. Read once here
                    // regardless of which case below actually sets rockTarget, since exactly one of
                    // them can run per step.
                    centerPivot = step.CenterPivot;
                    pivotHeightOverride = step.PivotHeightOverride;

                    switch (step.AnimationType)
                    {
                        case AttackAnimationType.Shake:
                            // Constant coil, rapid rotational jitter for the whole step - aggression/rage tell.
                            // Driven by _stateTimer (time since this step began), not the absolute Time.time -
                            // so the jitter frequency itself stretches along with an anticipation-slowed
                            // windup instead of staying locked to real time while everything else slows down.
                            rockTarget = Mathf.Sin(_stateTimer * step.Shake.Frequency * Mathf.PI * 2f) * step.Shake.RockDegrees;
                            break;

                        case AttackAnimationType.SwingBack:
                        {
                            // Held coil, body eases into a pull-back away from its facing direction as the step builds.
                            float swingT = Mathf.Sin(t * Mathf.PI * 0.5f); // ease-out toward the peak
                            rockTarget = step.SwingBack.Degrees * swingT * _facingSign;
                            break;
                        }

                        case AttackAnimationType.Pulse:
                        {
                            // Rhythmic squash pulsing that grows in amplitude - charging-up tell.
                            // Same _stateTimer reasoning as Shake above - the pulse rate itself needs to
                            // stretch under Freeze, not just the amplitude ramp.
                            float pulseWave = Mathf.Sin(_stateTimer * step.Pulse.Frequency * Mathf.PI * 2f);
                            float pulseAmplitude = Mathf.Lerp(0f, step.Pulse.MaxSquash, t);
                            _squashT = pulseWave * pulseAmplitude;
                            break;
                        }

                        case AttackAnimationType.Crouch:
                        {
                            // Progressively sinks and compresses, coiled tight right before release - pounce/leap tell.
                            // Sink rides depthTarget (local Z), not bobTarget (local Y) - see jumpCrouchSinkAmount's
                            // own tooltip above for why Y would visually push the body below the real ground plane.
                            float crouchT = Mathf.Sin(t * Mathf.PI * 0.5f);
                            _squashT = Mathf.Lerp(0f, step.Crouch.Squash, crouchT);
                            depthTarget = -step.Crouch.SinkAmount * crouchT;
                            break;
                        }

                        case AttackAnimationType.Inflate:
                        {
                            // Progressively swells outward (negative squash = stretch/grow), as if powering up or inhaling.
                            float inflateT = Mathf.Sin(t * Mathf.PI * 0.5f);
                            _squashT = -Mathf.Lerp(0f, step.Inflate.Amount, inflateT);
                            break;
                        }

                        case AttackAnimationType.Lunge:
                            // Instant stretch pop (set on trigger) that decays back to neutral -
                            // simple committed strike, no windup needed to read it.
                            _squashT = Mathf.Lerp(_squashT, 0f, 1f - Mathf.Exp(-squashLerpSpeed * dt));
                            break;

                        case AttackAnimationType.Slam:
                        {
                            // Starts compressed and sunk from the impact, springs back up to neutral.
                            // Sink rides depthTarget (local Z), not bobTarget (local Y) - same reasoning as Crouch above.
                            float slamT = decay * decay;
                            _squashT = Mathf.Lerp(_squashT, step.Slam.Squash * slamT, 1f - Mathf.Exp(-squashLerpSpeed * dt * 2f));
                            depthTarget = -step.Slam.SinkAmount * slamT;
                            break;
                        }

                        case AttackAnimationType.Snap:
                        {
                            // Whip-crack rotation toward the facing direction that snaps back to neutral fast.
                            float snapT = decay * decay * decay;
                            rockTarget = step.Snap.Degrees * snapT * _facingSign;
                            _squashT = Mathf.Lerp(_squashT, 0f, 1f - Mathf.Exp(-squashLerpSpeed * dt));
                            break;
                        }

                        case AttackAnimationType.Chomp:
                            // Rapid double-bite squash pulse that dies out over the step - bite/peck tell.
                            _squashT = Mathf.Sin(t * step.Chomp.Pulses * Mathf.PI * 2f) * step.Chomp.Squash * decay;
                            break;

                        case AttackAnimationType.Spin:
                        {
                            // Full rotational spin through the strike, easing out right at the end.
                            float spinT = 1f - decay * decay;
                            rockTarget = step.Spin.Degrees * spinT * _facingSign;
                            _squashT = Mathf.Lerp(_squashT, 0f, 1f - Mathf.Exp(-squashLerpSpeed * dt));
                            break;
                        }

                        case AttackAnimationType.ArmSwingBack:
                        {
                            // Arm-only counterpart of SwingBack above - held coil, arm eases into a
                            // pull-back away from facing as the step builds. Pairs with ArmSnap on a
                            // later step for a windup/strike split, the same composition the body
                            // already uses across separate AttackVisualStep slots.
                            //
                            // No _facingSign here, unlike every body-level case above - arm is a
                            // child of root, and root's own facing-flip scale (scale.x *=
                            // _facingSign below in ApplyPose) already mirrors a plain local
                            // rotation into "away from facing" on both sides automatically (the
                            // same reason head/torso need zero facing-aware math to flip). Baking
                            // _facingSign in here too would double-mirror and cancel the flip out.
                            float swingT = Mathf.Sin(t * Mathf.PI * 0.5f);
                            float armSwingBackDegrees = step.ArmSwingBack.Degrees * swingT;
                            armRotationTarget = armSwingBackDegrees;

                            // Rides the exact same swingT envelope as the rotation above, so a
                            // wind-up reads as one coiling motion (rotate+grow together) rather than
                            // two tells racing at different rates. 0 (default) = no scale at all,
                            // identical to this type's original rotation-only behavior.
                            armScaleTarget = step.ArmSwingBack.ArmScale * swingT;

                            // BodyFollow lets a fraction of the same coil bleed into the body's own
                            // rock so the torso doesn't stay dead-still while only the arm moves -
                            // rockTarget drives root's OWN rotation directly (not a child), so this
                            // DOES need _facingSign, same as every body-level case above.
                            rockTarget = armSwingBackDegrees * step.ArmSwingBack.BodyFollow * _facingSign;
                            break;
                        }

                        case AttackAnimationType.ArmSnap:
                        {
                            // Arm-only counterpart of Snap above - whip-crack rotation on the arm
                            // toward facing that snaps back to neutral fast. See ArmSwingBack above
                            // for why this omits _facingSign on the arm's own rotation.
                            float snapT = decay * decay * decay;
                            float armSnapDegrees = step.ArmSnap.Degrees * snapT;
                            armRotationTarget = armSnapDegrees;

                            // Same snapT envelope as the rotation above - see ArmSwingBack's own
                            // ArmScale comment. 0 (default) = no scale, original behavior.
                            armScaleTarget = step.ArmSnap.ArmScale * snapT;

                            rockTarget = armSnapDegrees * step.ArmSnap.BodyFollow * _facingSign;
                            break;
                        }

                        case AttackAnimationType.ArmPunch:
                        {
                            // Standalone punch (no paired windup step needed) combining ArmSnap's own
                            // whip-crack rotation envelope, PunchScale's own ring formula retargeted
                            // to the arm alone, and a short extra jitter that only shows up right at
                            // the moment of impact - see ArmPunchParams' own class comment.
                            float punchT = decay * decay * decay;
                            float armPunchDegrees = step.ArmPunch.Degrees * punchT;

                            // impactWindow decays much faster than punchT (decay^6 vs decay^3) so the
                            // extra jitter only rattles through the first sliver of the step instead
                            // of lingering through the whole whip-crack - a brief "impact" accent, not
                            // a second sustained shake layered under the punch.
                            float impactWindow = decay * decay * decay * decay * decay * decay;
                            float impactShake = Mathf.Sin(_stateTimer * step.ArmPunch.ImpactShakeFrequency * Mathf.PI * 2f) * step.ArmPunch.ImpactShakeDegrees * impactWindow;
                            armRotationTarget = armPunchDegrees + impactShake;

                            armScaleTarget = Mathf.Sin(t * step.ArmPunch.ScaleFrequency * Mathf.PI * 2f) * decay * step.ArmPunch.ScaleStrength;

                            rockTarget = armPunchDegrees * step.ArmPunch.BodyFollow * _facingSign;
                            break;
                        }

                        case AttackAnimationType.PunchScale:
                            // Elastic uniform scale punch - decaying ring from the moment the step
                            // starts, applied on top of (not through) _squashT/volumePreservation so
                            // it grows/shrinks the whole body together instead of squashing one axis
                            // against another - see ApplyPose's punchScaleMult.
                            punchScaleTarget = Mathf.Sin(t * step.PunchScale.Frequency * Mathf.PI * 2f) * decay * step.PunchScale.Strength;
                            break;
                    }
                    break;
                }

                case State.Die:
                {
                    float t = dieDuration > 0f ? Mathf.Clamp01(_stateTimer / dieDuration) : 1f;

                    // Rolls over toward its final fallen angle throughout the whole hop, so it's
                    // already toppled by the time it touches back down instead of landing upright
                    // and then falling over separately.
                    float toppleT = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic
                    rockTarget = dieToppleDegrees * toppleT * _dieToppleSign;

                    // Symmetric hop back to rest, plus tunable Y/Z nudges (ramped in with the
                    // topple) to compensate for however the fallen pose actually sits relative to
                    // the ground once rotated.
                    bobTarget = Mathf.Sin(t * Mathf.PI) * dieJumpHeight + dieGroundOffsetY * toppleT;
                    depthTarget = dieGroundOffsetZ * toppleT;

                    float squashTarget = Mathf.Lerp(0f, dieSquash, toppleT);
                    _squashT = Mathf.Lerp(_squashT, squashTarget, 1f - Mathf.Exp(-squashLerpSpeed * dt));

                    if (_stateTimer >= dieDuration + dieShrinkDelay && dieShrinkDuration > 0f)
                        _dieShrinkT = Mathf.Clamp01(_dieShrinkT + dt / dieShrinkDuration);

                    break;
                }

                case State.Jump:
                {
                    // Small crouch (compress + sink) through the real anticipation window
                    // (Enemy.TraversalJumpAnticipationTimer, opened by
                    // EnemyMovementUtility.QueueTraversalJump), then a stretch pop the instant the
                    // hop actually launches that decays back to neutral by landing - same crouch/
                    // decay shapes AttackAnimationType.Crouch/Slam already use for a windup/impact
                    // tell, just driven by the hop's own two real timers (_jumpAnticipating/_jumpT)
                    // instead of an attack step's. The vertical arc itself is already baked into
                    // Transform3D.Position by TickTraversalJump - this is purely the squash character
                    // animation layered on top, not a substitute for it.
                    if (_jumpAnticipating == true)
                    {
                        float crouchT = Mathf.Sin(_jumpT * Mathf.PI * 0.5f);
                        _squashT = Mathf.Lerp(_squashT, jumpCrouchSquash * crouchT, 1f - Mathf.Exp(-squashLerpSpeed * dt));
                        depthTarget = -jumpCrouchSinkAmount * crouchT;
                    }
                    else
                    {
                        float decay = 1f - _jumpT;
                        _squashT = Mathf.Lerp(_squashT, -jumpPopStretch * decay * decay, 1f - Mathf.Exp(-squashLerpSpeed * dt * 2f));
                    }
                    break;
                }

                case State.Burrow:
                {
                    // Driven toward 1 (hidden) while sinking, back toward 0 (visible) while rising -
                    // pinned at whichever end it reaches until the next trigger flips direction, see
                    // TriggerBurrowDown/TriggerBurrowUp.
                    float duration = _burrowSinking ? burrowSinkDuration : burrowRiseDuration;
                    float rate = duration > 0f ? dt / duration : 1f;
                    _burrowT = Mathf.MoveTowards(_burrowT, _burrowSinking ? 1f : 0f, rate);

                    // Peaks at the midpoint of the transition regardless of direction, so the same
                    // curve reads as a "pop" both diving down and popping back up. Sink rides
                    // depthTarget (local Z), not bobTarget (local Y) - see jumpCrouchSinkAmount's
                    // own tooltip above for why.
                    _squashT = burrowSquash * Mathf.Sin(_burrowT * Mathf.PI);
                    depthTarget = -burrowSinkAmount * _burrowT;
                    break;
                }
            }

            ApplyPose(leanTarget, rockTarget, bobTarget, depthTarget, armRotationTarget, armScaleTarget, punchScaleTarget, centerPivot, pivotHeightOverride);
        }

        private State DetermineGroundedState(float horizontalSpeed)
        {
            return horizontalSpeed > moveSpeedEpsilon ? State.Run : State.Idle;
        }

        private void UpdateFacing(Frame frame, Vector3 velocity)
        {
            // Prefer the resolved Aim angle (kept up to date by EnemyMovementUtility.FaceTarget
            // while Chasing/Anticipating) over raw velocity, so facing follows the actual target
            // instead of just which way the enemy was last walking - otherwise the sprite freezes
            // on stale movement facing while stationary (Anticipating), which is exactly when the
            // attack/projectile fires, and can visibly mismatch the real attack direction.
            if (frame.Has<Aim>(_entityRef) == true)
            {
                float angleRad = frame.Get<Aim>(_entityRef).Angle.AsFloat * Mathf.Deg2Rad;
                float dirX = Mathf.Sin(angleRad);

                if (Mathf.Abs(dirX) > facingDeadzone)
                    _facingSign = Mathf.Sign(dirX);
            }
            else if (Mathf.Abs(velocity.x) > facingDeadzone)
            {
                _facingSign = Mathf.Sign(velocity.x);
            }
        }

        private void ApplyPose(float leanDegrees, float rockDegrees, float bobOffset, float depthOffset = 0f, float armRotationDegrees = 0f, float armScaleOffset = 0f, float punchScaleOffset = 0f, bool centerPivot = false, float pivotHeightOverride = 0f)
        {
            float verticalMult = Mathf.Clamp(1f - _squashT * volumePreservation, 0.15f, 3f);
            float horizontalMult = Mathf.Clamp(1f / verticalMult, 0.3f, 3f);

            // Independent shrink sources, multiplied together rather than picking one - Die's is
            // one-way (_dieShrinkT never comes back down), Burrow's is reversible (_burrowT), and
            // in practice only one is ever non-zero at a time for a given enemy.
            float shrinkMult = (1f - Easing.Evaluate(_dieShrinkT, Ease.InBack)) * (1f - Easing.Evaluate(_burrowT, Ease.InOutSine));

            // Ground shadow blob (GroundBlobManager) sizes itself purely off HasShadow.BaseScale -
            // it never reads the sprite's own live scale - so without this it would sit at full
            // size while the sprite above shrinks to nothing during a Die/Burrow animation. Clamped
            // to 0 (not left raw) since Ease.InBack can briefly overshoot past 1/below 0 for its
            // anticipation dip, and a negative localScale on the shadow's own quad would flip it
            // rather than just shrinking it - a glitch the sprite's own back-ease overshoot doesn't
            // have this same problem with, since a brief inverted flash there just reads as part of
            // the squash.
            if (_shadow != null)
                _shadow.SetBaseScale(_shadowBaseScale * Mathf.Max(0f, shrinkMult));

            // AttackAnimationType.PunchScale's own channel - applied uniformly across all axes, on
            // top of (not blended with) the squash/stretch mults above, so it reads as the whole
            // body growing/shrinking rather than one axis fighting another.
            float punchScaleMult = 1f + punchScaleOffset;

            // No torso to carry the squash - root is the whole visible body, so it takes the
            // squash directly instead of just facing/lean/bob.
            bool rootCarriesSquash = torso == null;

            // Z scale is left at each transform's own authored base below (never multiplied by
            // horizontalMult/shrinkMult/punchScaleMult) - this is a 2D sprite game, a flat quad has
            // no visible depth extent, so scaling Z has no visible effect regardless of what would
            // have driven it.

            if (torso != null)
            {
                Vector3 torsoScale = Vector3.Scale(_torsoBaseScale, new Vector3(horizontalMult, verticalMult, horizontalMult)) * shrinkMult * punchScaleMult;
                torsoScale.z = _torsoBaseScale.z;
                torso.localScale = torsoScale;
                torso.localPosition = _torsoBaseLocalPos;
            }

            if (head != null)
            {
                float headVertical = Mathf.Lerp(1f, verticalMult, headSquashInfluence);
                float headHorizontal = Mathf.Lerp(1f, horizontalMult, headSquashInfluence);
                Vector3 headScale = Vector3.Scale(_headBaseScale, new Vector3(headHorizontal, headVertical, headHorizontal)) * shrinkMult * punchScaleMult;
                headScale.z = _headBaseScale.z;
                head.localScale = headScale;
                head.localPosition = _headBaseLocalPos;
            }

            if (root != null)
            {
                var localPos = _rootBaseLocalPos;
                localPos.y += bobOffset;
                localPos.z += depthOffset;

                float rootVerticalMult = rootCarriesSquash == true ? verticalMult : 1f;
                float rootHorizontalMult = rootCarriesSquash == true ? horizontalMult : 1f;

                var scale = _rootBaseScale;
                scale.x *= rootHorizontalMult * _facingSign * shrinkMult * punchScaleMult;
                scale.y *= rootVerticalMult * shrinkMult * punchScaleMult;
                root.localScale = scale;

                float totalTilt = leanDegrees + rockDegrees;
                var tiltRotation = Quaternion.Euler(0f, 0f, totalTilt);

                // AttackVisualStep.CenterPivot (only settable on the rotation-producing step types -
                // see AttackVisualStepDrawer) would otherwise rock around root's own base/ground
                // pivot like every other tilt source here - fine for those by default (idle
                // wobble/run rock/die topple are meant to rock from the ground, and most rock/lean
                // tells read fine that way too), but a step can opt into compensating position by
                // the pivot-to-center offset rotated by this frame's tilt instead - the same trick
                // as rotating/scaling around an arbitrary pivot: keeps the point pivotHeight up from
                // root's origin visually fixed while root itself still rotates (and its base swings)
                // around it. A big Spin almost always wants this; a whip-crack Snap usually doesn't.
                if (centerPivot)
                {
                    float pivotHeight = pivotHeightOverride != 0f ? pivotHeightOverride
                        : defaultCenterPivotHeight != 0f ? defaultCenterPivotHeight
                        : _autoCenterPivotHeight;
                    Vector3 pivotOffset = Vector3.Scale(scale, new Vector3(0f, pivotHeight, 0f));
                    localPos += pivotOffset - tiltRotation * pivotOffset;
                }

                root.localPosition = localPos;

                if (billboardToCamera && Camera.main != null)
                {
                    var camForward = Camera.main.transform.forward;
                    root.rotation = Quaternion.LookRotation(camForward, Vector3.up) * tiltRotation;
                }
                else
                {
                    root.localRotation = tiltRotation;
                }
            }

            // Local rotation relative to the arm's own rest pose (_armBaseLocalRot), applied on
            // top of root's tilt/facing-flip above since arm is a child of root - no camera-facing
            // math needed here, root already handles that for the whole subtree.
            if (arm != null)
            {
                arm.localRotation = _armBaseLocalRot * Quaternion.Euler(0f, 0f, armRotationDegrees);

                // X/Y only, on top of the arm's OWN authored base scale (not root's punchScaleMult) -
                // arm is a child of root, so it already inherits root's own scale/facing-flip
                // through the transform hierarchy; this only adds the arm-specific channel
                // ArmSwingBack/ArmSnap/ArmPunch drive on top of that. Z is left at its authored base,
                // never multiplied - this is a 2D sprite game, a flat quad's Z scale is never
                // actually visible regardless of what drives it.
                Vector2 armScaleMult = (Vector2)_armBaseScale * (1f + armScaleOffset);
                arm.localScale = new Vector3(armScaleMult.x, armScaleMult.y, _armBaseScale.z);

                // Reset to the arm's own rest local position every frame - no channel currently
                // offsets it (a Z-depth punch offset isn't visible on a flat 2D sprite, so it was
                // removed rather than kept as a dead field/channel), but this still guards against
                // ViewPrefabPool reuse leaving a residual position from a previous enemy's rig, same
                // reasoning as GunBaseLocalPosition's own comment.
                arm.localPosition = _armBaseLocalPos;
            }
        }
    }
}

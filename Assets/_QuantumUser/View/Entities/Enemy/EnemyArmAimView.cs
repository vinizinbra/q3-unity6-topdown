using NaughtyAttributes;
using Photon.Deterministic;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Continuous gun-aim tracking for shooter enemies - unlike AttackVisualStep's ArmSwingBack/
    // ArmSnap (one-shot, phase-triggered tells driven by EnemyAttackVisualsView through
    // EnemyBlobAnimationView.PlayAttackStep, which animate rig.Arm), this keeps rig.Gun pointed at
    // Enemy.Target every frame (not Aim.Target - see QUpdate's comment on why). Targets Gun
    // specifically, NOT Arm, so a rig that also wants a phase-triggered arm swing (ArmSwingBack/
    // ArmSnap) doesn't have this component's continuous tracking fight EnemyBlobAnimationView over
    // the same transform every frame - see EnemyViewRig.Gun's own comment. A rig with no separate
    // gun sprite can just point Gun at the same transform as Arm (e.g. ScavengerHunt-Ranged); a rig
    // with no continuous aim needs at all (Gun left unassigned) makes this component a no-op via the
    // null check in QUpdate.
    //
    // The AIM ANGLE is computed by projecting the true world aim direction onto the live camera's
    // own right/up basis (QUpdate), not by InverseTransformDirection-ing it through gunParent -
    // gunParent (EnemyBlobAnimationView's root, or Arm if Gun is nested under it) is reshaped every
    // frame by squash/stretch/lean/rock/bob, so a projection relative to it would drift with
    // whatever body animation happens to be playing instead of staying anchored to the screen. The
    // RESULT is still written as a plain gun.localRotation, though (not a world rotation) - see
    // QUpdate's own comment on why the mirror-flip reflection math only holds for a local rotation
    // and doesn't generalize cleanly to a world rotation composed with an extra camera-facing term.
    public class EnemyArmAimView : CustomQuantumEntityViewComponent
    {
        // This component lives on the generic enemy prototype (shared across enemy types), not on
        // EnemyDataAsset.ViewPrefab - the rig only exists once EnemyView.SpawnSprite instantiates
        // that prefab at runtime, so it can't be an Inspector-wired SerializeField. See SetRig.
        private EnemyViewRig rig;

        [SerializeField, Tooltip("Falls back to Camera.main if left empty. The aim rotation is built from this camera's live right/up/forward basis (see QUpdate) rather than the gun's parent transform, so it can't be skewed by whatever squash/stretch/lean/mirror the body rig is currently animating.")]
        private Transform cameraTransform;

        public override void Awake()
        {
            base.Awake();

            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        [Header("References")]
        [SerializeField, Tooltip("Optional - sibling EnemyBlobAnimationView, read only for its FacingSign to pick a default aim direction while there's no target (see ResolveDefaultAimDirection). Leave empty to fall back to Aim.Angle's own flat direction instead.")]
        private EnemyBlobAnimationView blobAnimationView;

        private Transform gun => rig != null ? rig.Gun : null;

        [Header("Aim")]
        [SerializeField, Tooltip("How quickly the gun turns to face a new direction. Higher = snappier.")]
        private float rotationSmoothing = 20f;

        // Recoil-kick tuning (degrees/distance/duration/frequency/asymmetry) is resolved off rig,
        // NOT Inspector-wired SerializeFields here - same reasoning as Muzzle/PreShootMuzzle/
        // ArmAngleOffset above: this component is one shared instance living on the generic enemy
        // prototype, so it can't hold a hardcoded "how hard does THIS enemy's weapon kick" tuning
        // the way a per-ViewPrefab EnemyViewRig field can. See EnemyViewRig's own comment on why
        // this (not AttackVisualStep's ArmSwingBack/ArmSnap) is the correct shoot-tell mechanism
        // for an enemy with continuous aim tracking.

        // Resolved off rig.Muzzle/rig.PreShootMuzzle rather than Inspector-wired SerializeFields on
        // this component - this component is a single shared instance living on the generic enemy
        // prototype (see class comment/SetRig), so it can't hold a hardcoded reference to any one
        // enemy type's own muzzle particles the way per-ViewPrefab fields could; every enemy type's
        // own EnemyViewRig supplies its own instead. Cached in SetRig rather than read live off rig
        // every Fire()/PlayPreShoot() call, same reasoning as _gunBaseLocalPosition.
        private ParticleSystem _muzzleParticle;
        private ParticleSystem _preShootMuzzleParticle;

        [Header("Debug")]
        [SerializeField, Tooltip("Local angle (degrees) to preview with the button below, without a running simulation.")]
        private float debugTestAngle;

        private float _currentAngle;
        private float _recoilCurrent;
        private Vector2 _recoilPositionOffset;
        private Vector3 _gunBaseLocalPosition;

        // Called by EnemyView.SpawnSprite right after it instantiates EnemyDataAsset.ViewPrefab -
        // gun is null until this runs (this component's own Awake fires long before that), so the
        // base-position cache has to happen here instead of Awake or it would cache nothing.
        // Reads rig.GunBaseLocalPosition (cached once in EnemyViewRig.Awake), NOT gun.localPosition
        // live - the rig GameObject is pooled (ViewPrefabPool) and reused across many different
        // enemies over a run without ever resetting its transforms, so a live read here could bake
        // in whatever offset a previous enemy's rig was left at (e.g. died mid recoil-kick) as this
        // enemy's permanent rest position instead of the gun's true authored rest pose.
        public void SetRig(EnemyViewRig rig)
        {
            this.rig = rig;

            if (gun != null)
                _gunBaseLocalPosition = rig.GunBaseLocalPosition;

            _muzzleParticle = rig != null ? rig.Muzzle : null;
            _preShootMuzzleParticle = rig != null ? rig.PreShootMuzzle : null;
        }

        // Fire()'s recoil kicks (Tween.PunchCustom(this, ...)) are frequently still decaying when
        // the enemy dies mid-shot - without this, PrimeTween logs a stack-trace-capturing error
        // per orphaned tween every time that happens (see Constants.onCompleteCallbackIgnored).
        public override void OnDestroy()
        {
            base.OnDestroy();
            Tween.StopAll(this);
        }

        [Button, Tooltip("Preview debugTestAngle above without a running simulation.")]
        private void PreviewDebugAngle()
        {
            if (gun == null)
                return;

            gun.localRotation = Quaternion.Euler(0f, 0f, debugTestAngle);
        }

        // Called by EnemyAttackVisualsView the same tick a shot's projectile spawns - two
        // independent PunchCustom kicks (rotation, position) plus a muzzle flash, same pattern as
        // WeaponView.Shoot(). Each shot starts a fresh punch rather than stacking onto whatever a
        // still-decaying previous shot left behind (PrimeTween has no built-in additive-stacking
        // mode outside an experimental flag this project doesn't enable) - keep recoilDuration at
        // or below the enemy's fire interval if rapid fire shouldn't visibly reset mid-decay. No-op
        // (not a crash) if previewed with no rig assigned, same as the other debug buttons here.
        [Button, Tooltip("Preview Fire() without a running simulation.")]
        public void Fire()
        {
            if (rig == null)
                return;

            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(rig.RecoilKickDegrees, 0f, 0f), rig.RecoilDuration, rig.RecoilFrequency, asymmetryFactor: rig.RecoilAsymmetry),
                (view, val) => view._recoilCurrent = val.x);

            // Backward from the gun's current true (facing-independent) aim direction - stored in
            // true/unmirrored terms; QUpdate applies the real parent-mirror sign fresh every frame
            // when writing it to gun.localPosition, rather than baking a sign in here that could
            // go stale if facing flips again before this shot's kick finishes decaying.
            float aimRad = _currentAngle * Mathf.Deg2Rad;
            Vector2 aimDir = new Vector2(Mathf.Cos(aimRad), Mathf.Sin(aimRad));
            Vector2 kick = -aimDir * rig.RecoilKickDistance;
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(kick.x, kick.y, 0f), rig.RecoilDuration, rig.RecoilFrequency, asymmetryFactor: rig.RecoilAsymmetry),
                (view, val) => view._recoilPositionOffset = new Vector2(val.x, val.y));

            if (_muzzleParticle != null)
                _muzzleParticle.Play(true);
        }

        // Called by EnemyAttackVisualsView at the start of the windup (AttackPhase.Anticipation) -
        // a charge-up/telegraph tell that reads as "about to shoot," distinct from Fire()'s muzzle
        // flash which reads as "just shot." No recoil kick here - only the shot itself should
        // punch the gun.
        [Button, Tooltip("Preview PlayPreShoot() without a running simulation.")]
        public void PlayPreShoot()
        {
            if (_preShootMuzzleParticle != null)
                _preShootMuzzleParticle.Play(true);
        }

        // Called by EnemyAttackVisualsView the instant windup ends, whether that's because the shot
        // actually fired or the attack got interrupted (see exitedAnticipating's own comment) -
        // StopEmitting rather than an instant clear, so any particles already emitted still finish
        // their own lifetime instead of vanishing mid-flight.
        [Button, Tooltip("Preview StopPreShoot() without a running simulation.")]
        public void StopPreShoot()
        {
            if (_preShootMuzzleParticle != null)
                _preShootMuzzleParticle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        protected override void QUpdate(QuantumGame game)
        {
            // gun == null covers both "this rig has no Gun assigned" (a melee rig relying on
            // ArmSwingBack/ArmSnap instead, e.g. ScavengerHunt-Slammer - see EnemyViewRig.Gun's own
            // comment) and "no rig set yet" (rig == null, since the gun property itself null-checks
            // rig) in one check.
            if (gun == null || cameraTransform == null)
                return;

            Frame frame = game.Frames.Predicted;

            if (frame.Has<Aim>(_entityRef) == false || frame.Has<Enemy>(_entityRef) == false)
                return;

            // The entity the AI is actually chasing/attacking lives on Enemy.Target, not
            // Aim.Target - AimSystem (the only writer of Aim.Target) requires a KCC component,
            // which enemies don't have (they move via PhysicsBody3D), so Aim.Target sits at its
            // prototype default (EntityRef.None) forever for every enemy. Aim.Angle is still
            // useful below (ResolveDefaultAimDirection's last-resort fallback), just not Target.
            Enemy enemy = frame.Get<Enemy>(_entityRef);
            Vector3 worldDir = ResolveAimWorldDirection(frame, enemy.Target, frame.Get<Aim>(_entityRef));

            // Flattened to the ground plane (Y=0) before projecting into screen space - worldDir
            // can tilt significantly toward/away from a target at a different elevation (see
            // ResolveAimWorldDirection's own comment), and that tilt read through the camera's
            // pitched "up" axis was rotating the flat 2D gun sprite far enough to visually dip
            // into the ground when aiming at a target well below/in front - same class of problem
            // WeaponView.AimPose.FlatWorldDirection exists to avoid, just applied
            // to the sprite rotation itself here instead of a 3D beam. The horizontal bearing (the
            // actual direction to the target) is unaffected - only the elevation tilt is dropped.
            worldDir.y = 0f;

            // Project onto the CAMERA's own live basis (not the gun's parent transform) - same
            // approach PlayerGunAimView.ProjectToScreen already uses for the player's gun. Using
            // gunParent.InverseTransformDirection here instead would re-introduce the tilt bug:
            // gunParent is reshaped every frame by squash/stretch/lean/rock/bob, so a projection
            // relative to it drifts with whatever animation happens to be playing instead of
            // staying anchored to the screen.
            Vector2 screenDir = new Vector2(Vector3.Dot(worldDir, cameraTransform.right), Vector3.Dot(worldDir, cameraTransform.up));

            float dt = Time.deltaTime;

            // Holds the last tracked angle through Recovery ("downtime," the post-attack cooldown -
            // see EnemySystem.CancelWindup/CancelActive/UpdateActive, all of which pair
            // Phase = Recovery with StateTimer = action.DownTime) instead of continuing to chase a
            // moving target - the enemy can't act again until this ends, so still swinging the
            // weapon to follow the target reads as tracking an attack that isn't actually coming.
            bool isRecovering = enemy.Phase == EnemyActionPhase.Recovery;

            // _currentAngle tracks the TRUE bearing only - rig.ArmAngleOffset is deliberately NOT
            // added here (unlike before). It's applied after the mirror reflection below instead,
            // since it corrects for how the art itself is drawn (a fixed property of the sprite,
            // unaffected by which way the character happens to be facing) rather than being part
            // of the true aim direction the reflection math needs to mirror-correct.
            if (isRecovering == false && screenDir.sqrMagnitude > 0.0001f)
            {
                float targetAngle = Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg;
                float smoothT = 1f - Mathf.Exp(-rotationSmoothing * dt);
                _currentAngle = Mathf.LerpAngle(_currentAngle, targetAngle, smoothT);
            }

            // _recoilCurrent/_recoilPositionOffset are no longer hand-integrated springs - Fire()
            // drives both directly via Tween.PunchCustom, which runs on PrimeTween's own update
            // loop; this just reads whatever value that tween currently has each frame.

            Transform gunParent = gun.parent != null ? gun.parent : gun;
            bool flipped = gunParent.lossyScale.x < 0f;

            // Renders the true/unmirrored angle correctly regardless of facing by reflecting it
            // across 180 degrees when gunParent is mirrored, rather than touching gun's own scale -
            // a scale-based "fix" can't work for a rotated child (mirroring and rotation don't
            // commute - it just moves which axis flips, it can't remove the flip for every angle at
            // once). Concretely: rendering a plain LOCAL rotation φ through a parent mirrored on X
            // negates the X component of the resulting direction but leaves Y alone (cos φ -> -cos
            // φ, sin φ unchanged), so cos(180-θ) = -cos θ and sin(180-θ) = sin θ exactly cancels
            // that out, making the on-screen result match the true direction θ on both sides. This
            // only holds for a plain LOCAL rotation (gun.localRotation below) - it does NOT
            // generalize cleanly to a world-space rotation composed with an extra facing-camera
            // term, so don't "simplify" this back to gun.rotation without re-deriving the math for
            // that exact composition first.
            float trueAngle = _currentAngle + _recoilCurrent;
            float renderedAngle = flipped ? 180f - trueAngle : trueAngle;

            // rig.ArmAngleOffset is added AFTER the reflection above, not folded into trueAngle -
            // it corrects for the art's own rest-drawn orientation (e.g. -90 if drawn pointing up),
            // which stays the SAME fixed correction regardless of facing. Folding it into trueAngle
            // instead would put it on the wrong side of the "180 - x" reflection, silently negating
            // its effect whenever the character is flipped - the offset would visibly invert
            // instead of applying consistently on both sides.
            gun.localRotation = Quaternion.Euler(0f, 0f, renderedAngle + rig.ArmAngleOffset);

            // gun.localPosition lives in gunParent's frame and is a plain additive offset (not a
            // rotated one), so unlike the rotation above, a straight axis-sign flip DOES correctly
            // cancel the mirror here - only the X axis flips (mirroring is left/right only), Y
            // never does.
            float parentSign = flipped ? -1f : 1f;
            Vector3 gunLocalPos = _gunBaseLocalPosition;
            gunLocalPos.x += _recoilPositionOffset.x * parentSign;
            gunLocalPos.y += _recoilPositionOffset.y;
            gun.localPosition = gunLocalPos;
        }

        // Same trick as PlayerGunAimView.ResolveAimWorldDirection - Aim.Angle is always flat, so
        // this computes the real 3D delta toward the target when one exists (elevation included),
        // falling back to ResolveDefaultAimDirection while nothing is targeted. QUpdate flattens
        // the elevation back out before actually using this for rotation (see its own comment) -
        // this method still returns the true 3D delta rather than an already-flattened one, since
        // ResolveDefaultAimDirection's own fallback is naturally flat anyway and there's no other
        // caller that would need the un-flattened version, but keeping the flatten call-site-local
        // makes it obvious this method's actual output only ever feeds the ground-plane bearing.
        // Uses the same EnemyMovementUtility.TryGetTargetPosition helper EnemyAttackVisualsView
        // already relies on for telegraph anchoring, rather than hand-rolling the same existence/
        // Transform3D check again here.
        private Vector3 ResolveAimWorldDirection(Frame frame, EntityRef target, Aim aim)
        {
            if (EnemyMovementUtility.TryGetTargetPosition(frame, target, out FPVector3 targetPosition) == false)
                return ResolveDefaultAimDirection(aim);

            Vector3 selfPosition = frame.Get<Transform3D>(_entityRef).Position.ToUnityVector3();
            Vector3 delta = targetPosition.ToUnityVector3() - selfPosition;

            return delta.sqrMagnitude > 0.0001f ? delta : ResolveDefaultAimDirection(aim);
        }

        // With nothing to aim at, point along the body's own current facing (screen right/left) -
        // Aim.Angle isn't guaranteed to still track facing once there's no target left to re-aim
        // at (EnemySystem only maintains it while actively chasing/attacking), so this reads
        // EnemyBlobAnimationView.FacingSign instead, the same value the body sprite itself flips
        // by, and only falls back to Aim.Angle's flat direction if no blobAnimationView is wired.
        private Vector3 ResolveDefaultAimDirection(Aim aim)
        {
            if (blobAnimationView != null)
                return new Vector3(blobAnimationView.FacingSign, 0f, 0f);

            float angleRad = aim.Angle.AsFloat * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angleRad), 0f, Mathf.Cos(angleRad));
        }
    }
}

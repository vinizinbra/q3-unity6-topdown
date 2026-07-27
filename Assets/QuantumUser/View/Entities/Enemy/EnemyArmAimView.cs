using NaughtyAttributes;
using Photon.Deterministic;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Continuous arm-aim tracking for shooter enemies - unlike AttackVisualStep's ArmSwingBack/
    // ArmSnap (one-shot, phase-triggered tells driven by EnemyAttackVisualsView through
    // EnemyBlobAnimationView.PlayAttackStep), this just keeps the arm pointed at Enemy.Target
    // every frame (not Aim.Target - see QUpdate's comment on why). Use one or the other per enemy
    // rig depending on whether its attack reads as a body/arm swing or a continuous point-and-
    // shoot - wiring both onto the same arm transform would fight over its rotation, AND they need
    // opposite facing-flip handling so they're incompatible on the same transform regardless.
    //
    // ArmSwingBack/ArmSnap are a symmetric LOCAL gesture (pull back "away from facing") - letting
    // root's own facing-flip scale mirror the rotation for free is exactly correct there: the same
    // local motion should visually mirror on both sides. Aiming is the opposite case - it targets
    // an ABSOLUTE world direction, which must render identically regardless of facing, so the
    // mirror has to be undone rather than embraced. A pure scale on arm CANNOT do that for a
    // rotated child (mirroring and rotation don't commute - a scale-based "fix" just moves which
    // axis flips, it can't remove the flip for every angle at once); the correct fix for mirroring
    // a rotation is reflecting the angle itself (`flipped ? 180 - angle : angle`, in
    // QUpdate/ApplyRenderedRotation below), which composes correctly through arm's whole child
    // hierarchy (muzzle particle included) with no scale trickery needed anywhere.
    public class EnemyArmAimView : CustomQuantumEntityViewComponent
    {
        // sin(45deg) - FollowCamera's fixed tilt (Assets/QuantumUser/View/Camera/FollowCamera.cs).
        // Characters in this project never billboard to camera (flat sprites, identity rotation
        // beyond an in-plane Z tilt), so a world-space aim direction has to be projected into the
        // arm's own flat local plane rather than camera-relative screen space - world-Z (depth)
        // reads as vertical motion on a flat sprite, foreshortened by this constant, the same
        // conversion MechanicalLegRig.cs uses for IK targets. Retune if FollowCamera's tilt ever
        // changes (it should be sin(tiltDegrees)).
        private const float CameraTiltSin = 0.7071f;

        // This component lives on the generic enemy prototype (shared across enemy types), not on
        // EnemyDataAsset.ViewPrefab - the rig only exists once EnemyView.SpawnSprite instantiates
        // that prefab at runtime, so it can't be an Inspector-wired SerializeField. See SetRig.
        // Arm's inherited facing-flip scale is compensated by reflecting the rendered angle (see
        // QUpdate), not by touching this transform's own scale, so the aim always renders as the
        // true world direction.
        private EnemyViewRig rig;

        [Header("References")]
        [SerializeField, Tooltip("Optional - sibling EnemyBlobAnimationView, read only for its FacingSign to pick a default aim direction while there's no target (see ResolveDefaultAimDirection). Leave empty to fall back to Aim.Angle's own flat direction instead.")]
        private EnemyBlobAnimationView blobAnimationView;

        // Must be a child of rig.EnemyRoot (or another transform under it) so its inherited
        // facing-flip scale matches what QUpdate's angle-reflection math assumes.
        private Transform arm => rig != null ? rig.Arm : null;

        [Header("Aim")]
        [SerializeField, Tooltip("Degrees added so the arm's own rest orientation lines up with angle 0 (arm's parent +X). -90 if the arm art is drawn pointing up.")]
        private float angleOffset;
        [SerializeField, Tooltip("How quickly the arm turns to face a new direction. Higher = snappier.")]
        private float rotationSmoothing = 20f;

        [Header("Shoot Recoil")]
        [SerializeField, Tooltip("Degrees the arm kicks on each shot, additive on top of the aim angle above (owned here, not AttackVisualStep's ArmSnap - that writes the same arm.localRotation every frame and would fight this for control of it). No facing sign applied - it's added to the true/unmirrored angle before the facing reflection in QUpdate, same as the aim angle itself.")]
        private float recoilKickDegrees = 8f;
        [SerializeField, Tooltip("Local-plane distance the arm snaps back on each shot, opposite its current aim direction - a position kick alongside the rotation kick above, same idea as WeaponView's recoilKickDistance.")]
        private float recoilKickDistance = 0.08f;
        [SerializeField, Tooltip("How long each shot's kick takes to fully settle back to rest. Each shot starts its own independent punch (see Fire()) rather than accumulating with any still-decaying kick from a previous shot, so keep this at or below the enemy's fire interval if rapid fire shouldn't visibly reset mid-decay.")]
        private float recoilDuration = 0.15f;
        [SerializeField, Tooltip("Oscillations per second as the kick settles.")]
        private float recoilFrequency = 16f;
        [SerializeField, Range(0f, 1f), Tooltip("0 = full recoil (kicks back, swings past rest, settles), 1 = no recoil (eases straight back to rest with no overshoot). Shared by the rotation and position kicks.")]
        private float recoilAsymmetry = 0.3f;
        [SerializeField, Tooltip("Particle system parented at the muzzle (a child of arm, so it tracks aim + recoil), restarted on every shot. No MuzzleMirrorFix needed - the angle reflection in QUpdate keeps arm's own scale untouched (identity/rest), so a plain child with no rotation of its own composes through correctly and never sees any mirroring.")]
        private ParticleSystem muzzleParticle;

        [Header("Debug")]
        [SerializeField, Tooltip("Local angle (degrees) to preview with the button below, without a running simulation.")]
        private float debugTestAngle;

        private float _currentAngle;
        private float _recoilCurrent;
        private Vector2 _recoilPositionOffset;
        private Vector3 _armBaseLocalPosition;

        // Called by EnemyView.SpawnSprite right after it instantiates EnemyDataAsset.ViewPrefab -
        // arm is null until this runs (this component's own Awake fires long before that), so the
        // base-position cache has to happen here instead of Awake or it would cache nothing.
        public void SetRig(EnemyViewRig rig)
        {
            this.rig = rig;

            if (arm != null)
                _armBaseLocalPosition = arm.localPosition;
        }

        [Button, Tooltip("Preview debugTestAngle above without a running simulation.")]
        private void PreviewDebugAngle()
        {
            if (arm == null)
                return;

            arm.localRotation = Quaternion.Euler(0f, 0f, debugTestAngle);
        }

        // Called by EnemyAttackVisualsView the same tick a shot's projectile spawns - two
        // independent PunchCustom kicks (rotation, position) plus a muzzle flash, same pattern as
        // WeaponView.Shoot(). Each shot starts a fresh punch rather than stacking onto whatever a
        // still-decaying previous shot left behind (PrimeTween has no built-in additive-stacking
        // mode outside an experimental flag this project doesn't enable) - keep recoilDuration at
        // or below the enemy's fire interval if rapid fire shouldn't visibly reset mid-decay.
        [Button, Tooltip("Preview Fire() without a running simulation.")]
        public void Fire()
        {
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(recoilKickDegrees, 0f, 0f), recoilDuration, recoilFrequency, asymmetryFactor: recoilAsymmetry),
                (view, val) => view._recoilCurrent = val.x);

            // Backward from the arm's current true (facing-independent) aim direction - stored in
            // true/unmirrored terms; QUpdate applies the real parent-mirror sign fresh every frame
            // when writing it to arm.localPosition, rather than baking a sign in here that could
            // go stale if facing flips again before this shot's kick finishes decaying.
            float aimRad = _currentAngle * Mathf.Deg2Rad;
            Vector2 aimDir = new Vector2(Mathf.Cos(aimRad), Mathf.Sin(aimRad));
            Vector2 kick = -aimDir * recoilKickDistance;
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(kick.x, kick.y, 0f), recoilDuration, recoilFrequency, asymmetryFactor: recoilAsymmetry),
                (view, val) => view._recoilPositionOffset = new Vector2(val.x, val.y));

            if (muzzleParticle != null)
                muzzleParticle.Play(true);
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (arm == null)
                return;

            Frame frame = game.Frames.Predicted;

            if (frame.Has<Aim>(_entityRef) == false || frame.Has<Enemy>(_entityRef) == false)
                return;

            // The entity the AI is actually chasing/attacking lives on Enemy.Target, not
            // Aim.Target - AimSystem (the only writer of Aim.Target) requires a KCC component,
            // which enemies don't have (they move via PhysicsBody3D), so Aim.Target sits at its
            // prototype default (EntityRef.None) forever for every enemy. Aim.Angle is still
            // useful below (ResolveDefaultAimDirection's last-resort fallback), just not Target.
            EntityRef target = frame.Get<Enemy>(_entityRef).Target;
            Vector3 worldDir = ResolveAimWorldDirection(frame, target, frame.Get<Aim>(_entityRef));

            // Project into the parent's own local plane (not camera space - see CameraTiltSin
            // above). InverseTransformDirection is rotation-only, so this angle computation is
            // unaffected by the parent's facing-flip scale either way - the resulting angle is
            // always the true, facing-independent direction; ApplyRenderedRotation below is what
            // turns that into the correct on-screen rotation for whichever way root is mirrored.
            Transform armParent = arm.parent != null ? arm.parent : arm;
            Vector3 localDir = armParent.InverseTransformDirection(worldDir);
            Vector2 planeDir = new Vector2(localDir.x, localDir.y + localDir.z * CameraTiltSin);

            float dt = Time.deltaTime;

            if (planeDir.sqrMagnitude > 0.0001f)
            {
                float targetAngle = Mathf.Atan2(planeDir.y, planeDir.x) * Mathf.Rad2Deg + angleOffset;
                float smoothT = 1f - Mathf.Exp(-rotationSmoothing * dt);
                _currentAngle = Mathf.LerpAngle(_currentAngle, targetAngle, smoothT);
            }

            // _recoilCurrent/_recoilPositionOffset are no longer hand-integrated springs - Fire()
            // drives both directly via Tween.PunchCustom, which runs on PrimeTween's own update
            // loop; this just reads whatever value that tween currently has each frame.

            bool flipped = armParent.lossyScale.x < 0f;
            ApplyRenderedRotation(_currentAngle + _recoilCurrent, flipped);

            // arm.localPosition lives in armParent's frame and is a plain additive offset (not a
            // rotated one), so unlike the rotation above, a straight axis-sign flip DOES correctly
            // cancel the mirror here - only the X axis flips (mirroring is left/right only), Y
            // never does.
            float parentSign = flipped ? -1f : 1f;
            Vector3 armLocalPos = _armBaseLocalPosition;
            armLocalPos.x += _recoilPositionOffset.x * parentSign;
            armLocalPos.y += _recoilPositionOffset.y;
            arm.localPosition = armLocalPos;
        }

        // Renders a true/unmirrored angle correctly regardless of facing by reflecting it across
        // 180 degrees when armParent is mirrored, rather than touching arm's own scale (see the
        // class comment for why a scale-based "fix" can't work for a rotated child - it was the
        // actual cause of the arm pointing the wrong way, specifically inverted, when flipped).
        // Concretely: rendering a plain rotation φ through a parent mirrored on X negates the X
        // component of the resulting direction but leaves Y alone (cos φ -> -cos φ, sin φ
        // unchanged), so cos(180-θ) = -cos θ and sin(180-θ) = sin θ exactly cancels that out,
        // making the on-screen result match the true direction θ on both sides.
        private void ApplyRenderedRotation(float trueAngle, bool flipped)
        {
            float renderedAngle = flipped ? 180f - trueAngle : trueAngle;
            arm.localRotation = Quaternion.Euler(0f, 0f, renderedAngle);
        }

        // Same trick as PlayerGunAimView.ResolveAimWorldDirection - Aim.Angle is always flat, so
        // this tilts toward the target's real elevation when one exists, falling back to
        // ResolveDefaultAimDirection while nothing is targeted. Uses the same
        // EnemyMovementUtility.TryGetTargetPosition helper EnemyAttackVisualsView already relies
        // on for telegraph anchoring, rather than hand-rolling the same existence/Transform3D
        // check again here.
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

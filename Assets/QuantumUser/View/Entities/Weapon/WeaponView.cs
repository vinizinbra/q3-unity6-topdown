using NaughtyAttributes;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Root component of a weapon's visual prefab (one per WeaponDataAsset - pistol, shotgun,
    // etc). PlayerGunAimView computes the generic aim/sway/follow pose every frame (rotation,
    // aim direction, flip, camera basis) and hands it to ApplyAim() below rather than touching
    // the transform itself - this is the one place that owns everything specific to this
    // weapon: the cardinal position offsets, hand grips, and the shoot recoil "animation"
    // (previously a separate WeaponRecoilView - folded in here since it's just as weapon-specific
    // as the offsets, and needs its own EntityRef to know which OnPlayerFired events are its own).
    public class WeaponView : CustomQuantumEntityViewComponent
    {
        // Everything PlayerGunAimView computes each frame that isn't specific to this weapon's
        // own art/tuning: rotation, aim direction (also used as the recoil kick direction below),
        // flip, sway/follow already summed into one offset, and the camera basis needed to
        // project that offset into world space. Recoil itself is added internally.
        public readonly struct AimPose
        {
            public readonly float RotationDegrees;
            public readonly Quaternion FacingCamera;
            public readonly Vector2 AimDirection;
            public readonly bool Flipped;
            public readonly Vector2 ExtraOffset;
            public readonly Vector3 CameraRight;
            public readonly Vector3 CameraUp;

            public AimPose(float rotationDegrees, Quaternion facingCamera, Vector2 aimDirection, bool flipped, Vector2 extraOffset, Vector3 cameraRight, Vector3 cameraUp)
            {
                RotationDegrees = rotationDegrees;
                FacingCamera = facingCamera;
                AimDirection = aimDirection;
                Flipped = flipped;
                ExtraOffset = extraOffset;
                CameraRight = cameraRight;
                CameraUp = cameraUp;
            }
        }

        [Header("Position Offset")]
        [SerializeField, Tooltip("Screen-space offset (right, up) blended in while aiming directly right. Mirrored automatically when flipped/aiming left.")]
        private Vector2 rightOffset;
        [SerializeField, Tooltip("Screen-space offset (right, up) blended in while aiming directly up.")]
        private Vector2 upOffset;
        [SerializeField, Tooltip("Screen-space offset (right, up) blended in while aiming directly down.")]
        private Vector2 downOffset;

        [Header("Hand Grips")]
        [SerializeField, Tooltip("Local position (relative to this weapon) where the right hand blob should sit while held. Z defaults to -0.01 so the hand renders in front of the gun sprite instead of behind it - this object is billboarded to face the camera, so local Z tracks camera depth.")]
        private Vector3 rightHandGrip = new Vector3(0f, 0f, -0.01f);
        [SerializeField, Tooltip("Local position (relative to this weapon) where the left hand blob (off-hand support) should sit while held. Same Z convention as rightHandGrip.")]
        private Vector3 leftHandGrip = new Vector3(0f, 0f, -0.01f);

        [Header("Shoot Recoil")]
        [SerializeField, Tooltip("Screen-space distance the gun snaps back on each shot, opposite the currently-held aim direction.")]
        private float recoilKickDistance = 0.12f;
        [SerializeField, Tooltip("Degrees the muzzle kicks up on each shot (auto-mirrored when the gun is flipped, so it always reads as 'up' on screen).")]
        private float recoilRotationKick = 6f;
        [SerializeField, Tooltip("How long each shot's kick takes to fully settle back to rest. Each shot starts its own independent punch (see Shoot()) rather than accumulating with any still-decaying kick from a previous shot, so keep this at or below the weapon's fire interval if rapid fire shouldn't visibly reset mid-decay.")]
        private float recoilDuration = 0.15f;
        [SerializeField, Tooltip("Oscillations per second as the kick settles.")]
        private float recoilFrequency = 16f;
        [SerializeField, Range(0f, 1f), Tooltip("0 = full recoil (kicks back, swings past rest, settles), 1 = no recoil (eases straight back to rest with no overshoot). Shared by the position, rotation, and knockback kicks below.")]
        private float recoilAsymmetry = 0.3f;
        [SerializeField, Tooltip("Particle system parented at the muzzle, restarted on every shot (e.g. an Epic Toon FX Muzzleflash prefab).")]
        private ParticleSystem muzzleParticle;

        [Header("Shoot Knockback")]
        [SerializeField, Tooltip("Distance the gun punches back along the camera's forward axis (away from the viewer) on each shot - a depth kick distinct from the screen-space position/rotation kick above.")]
        private float knockbackDistance = 0.1f;
        [SerializeField, Range(0f, 1f), Tooltip("Fraction the gun squashes down in scale at the peak of the knockback punch.")]
        private float knockbackScalePunch = 0.1f;

        // Resolved through this weapon's own transform every call - TransformPoint applies its
        // current rotation (billboard + aim) and scale (the Y-axis flip in ApplyAim), so the
        // grip tracks correctly however the weapon is currently facing without WeaponHandGripView
        // needing to know anything about that.
        public Vector3 RightHandGripPosition => transform.TransformPoint(rightHandGrip);
        public Vector3 LeftHandGripPosition => transform.TransformPoint(leftHandGrip);

        private Vector3 baseScale = Vector3.one;
        private Vector3 restLocalPosition;
        private Vector2 currentOffset;

        private Vector2 lastAimDir = Vector2.right;
        private bool lastFlipped;
        private Vector2 recoilOffset;
        private float recoilRotationCurrent;
        private float knockbackPunch;

        public override void Awake()
        {
            base.Awake();
            CacheRestPose();
            QuantumEvent.Subscribe<EventPlayerFired>(this, OnPlayerFired);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        private void CacheRestPose()
        {
            baseScale = transform.localScale;
            restLocalPosition = transform.localPosition;
        }

        private void OnPlayerFired(EventPlayerFired e)
        {
            if (e.Entity != _entityRef) return;
            Shoot();
        }

        // Three independent PunchCustom kicks (position, rotation, knockback), each punching its
        // own field from 0 back to 0 - PunchCustom's callback always hands back a Vector3
        // regardless of the target value's real shape, so rotation/knockback (plain floats) only
        // ever read .x, and position reads .x/.y. Each shot starts a fresh punch rather than
        // stacking onto whatever a still-decaying previous shot left behind (PrimeTween has no
        // built-in additive-stacking mode outside an experimental flag this project doesn't
        // enable) - same non-stacking behavior BlobAnimationView's own shoot-punch already uses
        // for the head kick, so a fast weapon should keep recoilDuration at or below its fire
        // interval to avoid overlapping kicks visibly resetting each other.
        public void Shoot()
        {
            Vector2 kickDir = -lastAimDir * recoilKickDistance;
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(kickDir.x, kickDir.y, 0f), recoilDuration, recoilFrequency, asymmetryFactor: recoilAsymmetry),
                (view, val) => view.recoilOffset = new Vector2(val.x, val.y));

            float rotationKick = recoilRotationKick * (lastFlipped ? -1f : 1f);
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(rotationKick, 0f, 0f), recoilDuration, recoilFrequency, asymmetryFactor: recoilAsymmetry),
                (view, val) => view.recoilRotationCurrent = val.x);

            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(1f, 0f, 0f), recoilDuration, recoilFrequency, asymmetryFactor: recoilAsymmetry),
                (view, val) => view.knockbackPunch = val.x);

            PlayMuzzleParticle();
        }

        [Button]
        private void PlayMuzzleParticle()
        {
            if (muzzleParticle == null) return;
            muzzleParticle.Play(true);
        }

        public void ApplyAim(in AimPose pose, float smoothT)
        {
            lastAimDir = pose.AimDirection;
            lastFlipped = pose.Flipped;

            float rightWeight = Mathf.Abs(pose.AimDirection.x);
            float upWeight = Mathf.Max(pose.AimDirection.y, 0f);
            float downWeight = Mathf.Max(-pose.AimDirection.y, 0f);

            // Blend the three cardinal offsets by how closely the aim direction matches each one.
            // Right uses abs(x) rather than signed x, since flipping already mirrors left/right -
            // this way rightOffset covers both without a 4th field.
            Vector2 targetOffset = rightOffset * rightWeight + upOffset * upWeight + downOffset * downWeight;
            if (pose.Flipped) targetOffset.x = -targetOffset.x;
            currentOffset = Vector2.Lerp(currentOffset, targetOffset, smoothT);

            transform.rotation = pose.FacingCamera * Quaternion.Euler(0f, 0f, pose.RotationDegrees + recoilRotationCurrent);

            Vector3 scale = baseScale * (1f - knockbackPunch * knockbackScalePunch);
            scale.y *= pose.Flipped ? -1f : 1f;
            transform.localScale = scale;

            Vector2 totalOffset = currentOffset + pose.ExtraOffset + recoilOffset;
            Vector3 worldOffset = pose.CameraRight * totalOffset.x + pose.CameraUp * totalOffset.y - transform.forward * (knockbackPunch * knockbackDistance);

            // World-space add rather than converting into the parent's local space: the parent rig
            // is billboard-rotated AND non-uniformly scaled every frame by squash/stretch/bob, which
            // shears its transform matrix. InverseTransformVector-ing a pure screen-space (camera
            // right/up) offset through that shear was leaking it into the weapon's local Z (and X),
            // so the gun visibly drifted in depth while running. Resolving the rest anchor to world
            // space and adding the offset there keeps it exactly in the plane it was computed in,
            // regardless of how sheared the parent currently is.
            Vector3 restWorldPosition = transform.parent != null ? transform.parent.TransformPoint(restLocalPosition) : restLocalPosition;
            transform.position = restWorldPosition + worldOffset;

            // Re-pin local Z to the rest pose after the world-space add above - that add keeps
            // X/Y correct even while the parent rig is sheared by billboard rotation + non-uniform
            // squash/stretch scale, but the same shear can leak into local Z. Overwriting it here
            // (rather than clamping in world space) keeps the weapon's depth exactly where it
            // started relative to its parent, regardless of how the parent is currently oriented.
            Vector3 localPosition = transform.localPosition;
            localPosition.z = restLocalPosition.z;
            transform.localPosition = localPosition;
        }

        // Nothing left to do per-frame here - recoilOffset/recoilRotationCurrent/knockbackPunch
        // used to be hand-integrated damped-harmonic-oscillator springs updated here every frame;
        // Shoot() now drives all three directly via Tween.PunchCustom, which runs on PrimeTween's
        // own update loop. Required override (CustomQuantumEntityViewComponent.QUpdate is
        // abstract), otherwise unused by this component.
        protected override void QUpdate(QuantumGame game)
        {
        }
    }
}

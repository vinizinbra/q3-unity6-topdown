using System.Collections.Generic;
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

            // Ground-plane (XZ), camera-independent bearing - unlike AimDirection/CameraRight/
            // CameraUp (which all live in the camera's billboard-facing plane, correct for
            // positioning a 2D sprite), this is what a 3D effect that must stay level with the
            // ground - see WeaponView.OrientBeamParticle - should build its rotation from. The two
            // planes only coincide when the camera looks straight down; on any tilted top-down
            // camera a billboard-plane rotation visibly dips into/out of the floor.
            public readonly Vector3 FlatWorldDirection;

            public AimPose(float rotationDegrees, Quaternion facingCamera, Vector2 aimDirection, bool flipped, Vector2 extraOffset, Vector3 cameraRight, Vector3 cameraUp, Vector3 flatWorldDirection)
            {
                RotationDegrees = rotationDegrees;
                FacingCamera = facingCamera;
                AimDirection = aimDirection;
                Flipped = flipped;
                ExtraOffset = extraOffset;
                CameraRight = cameraRight;
                CameraUp = cameraUp;
                FlatWorldDirection = flatWorldDirection;
            }
        }

        // All position/hand-grip/recoil/knockback tuning lives on this one field so it can be
        // right-click Copy'd and Paste'd onto another weapon's WeaponView in the Inspector
        // instead of copying the whole component.
        [SerializeField]
        private WeaponAnimationParams anim = new WeaponAnimationParams();

        [Header("References")]
        [SerializeField, Tooltip("Falls back to a BlobAnimationView anywhere under the rig root if left empty - same resolution PlayerGunAimView.torsoFollow uses. Shoot() kicks anim's Character Shoot Punch settings into this every shot.")]
        private BlobAnimationView character;

        [Header("Muzzle Flash")]
        [SerializeField, Tooltip("Particle system parented at the muzzle, restarted on every shot (e.g. an Epic Toon FX Muzzleflash prefab).")]
        private ParticleSystem muzzleParticle;

        [Header("Hitscan Tracer")]
        [SerializeField, Tooltip("WeaponTracerView prefab - draws the line and plays its own begin/end particles (see WeaponTracerView). Pooled rather than instantiated fresh per shot (see tracerPool), so a rapid-fire weapon's overlapping fades read as one near-continuous beam instead of discrete flashes. Leave empty for no tracer. Ignored for Projectile weapons - they never fire EventHitscanFired.")]
        private WeaponTracerView tracerPrefab;

        // Reused across shots instead of Instantiate/Destroy per hitscan pellet - see
        // GetPooledTracer. Grows to roughly this weapon's peak concurrent pellet count (shotgun
        // PelletCount, or however many rapid shots overlap within one tracer's fade duration) and
        // stays there.
        private readonly List<WeaponTracerView> tracerPool = new List<WeaponTracerView>();

        [Header("Continuous Fire Particle")]
        [SerializeField, Tooltip("Particle system played continuously while this weapon keeps firing (e.g. a looping laser beam stream) - Play()'d on the first shot of a burst, Stop()'d once no new shot arrives within Beam Stop Grace. A start/stop toggle, not per-shot restarted like muzzleParticle. Leave empty to skip.")]
        private ParticleSystem beamParticle;
        [SerializeField, Tooltip("Seconds since the last shot before beamParticle is stopped. Keep at or above the weapon's fire interval (1/FireRate) so back-to-back shots don't visibly stop and restart it between hits.")]
        private float beamStopGrace = 0.15f;

        private float timeSinceLastShotForBeam;
        private bool beamFiring;

        // Resolved through this weapon's own transform every call - TransformPoint applies its
        // current rotation (billboard + aim) and scale (the Y-axis flip in ApplyAim), so the
        // grip tracks correctly however the weapon is currently facing without WeaponHandGripView
        // needing to know anything about that.
        public Vector3 RightHandGripPosition => transform.TransformPoint(anim.rightHandGrip);
        public Vector3 LeftHandGripPosition => transform.TransformPoint(anim.leftHandGrip);

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

            if (character == null)
                character = GetComponentInParent<BlobAnimationView>();
            if (character == null)
                character = transform.root.GetComponentInChildren<BlobAnimationView>();

            QuantumEvent.Subscribe<EventPlayerFired>(this, OnPlayerFired);
            QuantumEvent.Subscribe<EventHitscanFired>(this, OnHitscanFired);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);

            // Shoot()'s recoil kicks (Tween.PunchCustom(this, ...)) are frequently still decaying
            // when the owner dies mid-shot - without this, PrimeTween logs a stack-trace-capturing
            // error per orphaned tween every time that happens.
            Tween.StopAll(this);

            for (int i = 0; i < tracerPool.Count; i++)
            {
                if (tracerPool[i] != null)
                    Destroy(tracerPool[i].gameObject);
            }
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
            NotifyBeamFired();
        }

        // Starts beamParticle once, on the first shot of a burst - Update() below is what stops it
        // again once shots stop arriving, not this. Safe to call every shot regardless of fire type
        // (hitscan or projectile); a no-op while beamParticle is already playing.
        private void NotifyBeamFired()
        {
            if (beamParticle == null) return;

            timeSinceLastShotForBeam = 0f;

            if (beamFiring == true) return;

            beamFiring = true;
            beamParticle.Play(true);
        }

        private void Update()
        {
            if (beamFiring == false) return;

            timeSinceLastShotForBeam += Time.deltaTime;

            if (timeSinceLastShotForBeam < beamStopGrace) return;

            beamFiring = false;
            beamParticle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // One event per hitscan pellet (see WeaponSystem.FireHitscan) - a Projectile weapon never
        // fires this, so tracerPrefab simply goes unused on one of those.
        private void OnHitscanFired(EventHitscanFired e)
        {
            if (e.Owner != _entityRef) return;
            if (tracerPrefab == null) return;

            Vector3 origin = e.Origin.ToUnityVector3();
            Vector3 endPoint = e.EndPoint.ToUnityVector3();

            GetPooledTracer(origin).Play(origin, endPoint, e.DidHit);
        }

        // Reuses the first idle instance (its previous fade already finished - see
        // WeaponTracerView.IsPlaying) rather than instantiating fresh every shot. A high-fire-rate
        // weapon will keep finding every instance still mid-fade and grow the pool by one instead -
        // that's expected and what makes overlapping pellets/rapid shots read as one continuous
        // beam rather than a single flickering line.
        private WeaponTracerView GetPooledTracer(Vector3 origin)
        {
            for (int i = 0; i < tracerPool.Count; i++)
            {
                if (tracerPool[i].IsPlaying == false)
                    return tracerPool[i];
            }

            WeaponTracerView tracer = Instantiate(tracerPrefab, origin, Quaternion.identity);
            tracerPool.Add(tracer);
            return tracer;
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
        [Button("Test Shoot")]
        public void Shoot()
        {
            Vector2 kickDir = -lastAimDir * anim.recoilKickDistance;
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(kickDir.x, kickDir.y, 0f), anim.recoilDuration, anim.recoilFrequency, asymmetryFactor: anim.recoilAsymmetry),
                (view, val) => view.recoilOffset = new Vector2(val.x, val.y));

            float rotationKick = anim.recoilRotationKick * (lastFlipped ? -1f : 1f);
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(rotationKick, 0f, 0f), anim.recoilDuration, anim.recoilFrequency, asymmetryFactor: anim.recoilAsymmetry),
                (view, val) => view.recoilRotationCurrent = val.x);

            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(1f, 0f, 0f), anim.recoilDuration, anim.recoilFrequency, asymmetryFactor: anim.recoilAsymmetry),
                (view, val) => view.knockbackPunch = val.x);

            PunchCharacter();
            PlayMuzzleParticle();
        }

        // Kicks this weapon's own Character Shoot Punch tuning into the shooter's BlobAnimationView
        // every shot - lives here (not on BlobAnimationView itself) since the right feel is
        // per-weapon (a shotgun should knock the body around more than a pistol), and this is
        // already the one place that owns everything weapon-specific about the recoil "animation".
        // Rotation kicks flip by lastFlipped, same convention Shoot()'s own rotationKick above uses,
        // so they always read as recoiling away from the muzzle regardless of facing.
        private void PunchCharacter()
        {
            if (character == null) return;

            float flip = lastFlipped ? -1f : 1f;

            character.PunchHeadOffset(anim.shakePositionHead.Strength, anim.shakePositionHead.Duration, anim.shakePositionHead.Frequency);
            character.PunchBodyRotation(anim.shakeRotationBody.Strength.x * flip, anim.shakeRotationBody.Duration, anim.shakeRotationBody.Frequency);
            character.PunchHeadRotation(anim.shakeRotationHead.Strength.x * flip, anim.shakeRotationHead.Duration, anim.shakeRotationHead.Frequency);
            character.PunchBodyScale(anim.shakeScaleBody.Strength, anim.shakeScaleBody.Duration, anim.shakeScaleBody.Frequency);
            character.PunchHeadScale(anim.shakeScaleHead.Strength, anim.shakeScaleHead.Duration, anim.shakeScaleHead.Frequency);
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
            Vector2 targetOffset = anim.rightOffset * rightWeight + anim.upOffset * upWeight + anim.downOffset * downWeight;
            if (pose.Flipped) targetOffset.x = -targetOffset.x;
            currentOffset = Vector2.Lerp(currentOffset, targetOffset, smoothT);

            transform.rotation = pose.FacingCamera * Quaternion.Euler(0f, 0f, pose.RotationDegrees + recoilRotationCurrent);

            Vector3 scale = baseScale * (1f - knockbackPunch * anim.knockbackScalePunch);
            scale.y *= pose.Flipped ? -1f : 1f;
            transform.localScale = scale;

            Vector2 totalOffset = currentOffset + pose.ExtraOffset + recoilOffset;
            Vector3 worldOffset = pose.CameraRight * totalOffset.x + pose.CameraUp * totalOffset.y - transform.forward * (knockbackPunch * anim.knockbackDistance);

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

            OrientBeamParticle(pose);
        }

        // beamParticle can't just inherit this weapon's own transform.rotation - that's a billboard
        // (pose.FacingCamera) composed with a screen-plane Z spin, so its local +Z (the default
        // Cone shape's emission axis) points toward/away from the camera, not across the screen at
        // whatever's being aimed at. Same class of bug the position offset above already has a
        // comment about (parent rig shears under billboard + non-uniform scale) - solved the same
        // way, by computing the aim direction explicitly instead of trusting anything inherited
        // through the hierarchy. Built from FlatWorldDirection/Vector3.up (ground-plane, camera-
        // independent), not CameraRight/CameraUp - those live in the camera's billboard-facing
        // plane, which only coincides with the ground when the camera looks straight down. Using
        // them here would keep the beam visually level with the SCREEN, not the ground, and it'd
        // tip into/out of the floor on any tilted top-down camera.
        private void OrientBeamParticle(in AimPose pose)
        {
            if (beamParticle == null) return;
            if (pose.FlatWorldDirection.sqrMagnitude < 0.0001f) return;

            beamParticle.transform.rotation = Quaternion.LookRotation(pose.FlatWorldDirection, Vector3.up);
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

using NaughtyAttributes;
using Photon.Deterministic;
using PrimeTween;
using UnityEngine.Serialization;
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
    //
    // Deliberately owns NOTHING about how a shot itself is drawn. Tracer lines and the looping
    // continuous-fire beam both used to live here; they are now one of the interchangeable
    // HitscanViewBase styles (LineRenderer/Particle/Continuous), added to this same prefab and
    // driven off the simulation's own per-segment EventHitscanFired rather than off "the player
    // pulled the trigger". That split is what let the continuous beam follow a Ricochet bounce and
    // stop guessing at aim direction - see HitscanViewBase.
    public class WeaponView : CustomQuantumEntityViewComponent
    {
        // Everything PlayerGunAimView computes each frame that isn't specific to this weapon's
        // own art/tuning: rotation, aim direction (also used as the recoil kick direction below),
        // flip, sway/follow already summed into one offset, and the camera basis needed to
        // project that offset into world space. Recoil itself is added internally.
        public readonly struct AimPose
        {
            public readonly float RotationDegrees;
            // The Jump Flip's OWN contribution to rotation, kept separate from RotationDegrees
            // (which is everything else - base aim, Body Follow, recoil) rather than pre-summed
            // into it, so ApplyAim can pivot-correct ONLY this part (see flipPivotOffset) - the
            // normal aim rotation must keep pivoting around the grip exactly as it always has,
            // only the flip needs re-centering. 0 whenever no flip is playing.
            public readonly float FlipDegrees;
            public readonly Quaternion FacingCamera;
            public readonly Vector2 AimDirection;
            public readonly bool Flipped;
            public readonly Vector2 ExtraOffset;
            public readonly Vector3 CameraRight;
            public readonly Vector3 CameraUp;

            // Ground-plane (XZ), camera-independent bearing - unlike AimDirection/CameraRight/
            // CameraUp (which all live in the camera's billboard-facing plane, correct for
            // positioning a 2D sprite), this is what a 3D effect that must stay level with the
            // ground should build its rotation from. Currently unused - the looping beam particle
            // that needed it lived here until the HitscanViewBase styles took over, and those work
            // in world space off the simulation's own hit positions instead. Kept because it is a
            // property of the pose, not of that one effect, and the next ground-aligned effect will
            // want it (see EnemyArmAimView for the same problem solved separately). The two
            // planes only coincide when the camera looks straight down; on any tilted top-down
            // camera a billboard-plane rotation visibly dips into/out of the floor.
            public readonly Vector3 FlatWorldDirection;

            public AimPose(float rotationDegrees, float flipDegrees, Quaternion facingCamera, Vector2 aimDirection, bool flipped, Vector2 extraOffset, Vector3 cameraRight, Vector3 cameraUp, Vector3 flatWorldDirection)
            {
                RotationDegrees = rotationDegrees;
                FlipDegrees = flipDegrees;
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

        [Header("Jump Flip")]
        [SerializeField, Tooltip("EYE-TUNE THIS PER WEAPON. This transform's own origin sits at the grip (see rightHandGrip/leftHandGrip below), not the sprite's visual center - rotating around the grip is fine for normal small aim adjustments, but a full 360° Jump Flip rotation around an off-center grip swings the visible sprite through a wide arc instead of spinning in place. This is that pivot point instead, in this weapon's own LOCAL space (same space rightHandGrip/leftHandGrip are authored in) - only the flip's own rotation gets re-centered around it, normal aim/recoil/sway keep pivoting around the grip exactly as before. Leave at (0,0,0) to fall back to the old grip-pivot behavior. Play Mode + Test Shoot/an actual flip is the only way to eyeball a good value.")]
        private Vector3 flipPivotOffset;

        [Header("Muzzle Flash")]
        [SerializeField, Tooltip("Particle system parented at the muzzle, restarted on every shot (e.g. an Epic Toon FX Muzzleflash prefab).")]
        private ParticleSystem muzzleParticle;

        // Where a projectile's visual should actually leave from - read LIVE by ProjectileView at
        // spawn instead of the simulation's own Projectile.SpawnPosition, which is only an
        // approximation ("caster position + a small forward nudge + hand height", see
        // StatUtility.GetWeaponHoldOffset/ProjectileSpawner.ResolveSpawnOrigin) never anchored to
        // this weapon's actual authored barrel length. Falls back to this weapon's own root when no
        // muzzleParticle is assigned, same "leave empty to draw from an approximation" convention
        // HitscanViewBase.muzzle already uses for hitscan shots.
        public Transform MuzzleTransform => muzzleParticle != null ? muzzleParticle.transform : transform;

        // Resolved through this weapon's own transform every call - TransformPoint applies its
        // current rotation (billboard + aim + the facingFlip 180° turn in ApplyAim) and scale
        // (always positive/uniform now), so the grip tracks correctly however the weapon is
        // currently facing without WeaponHandGripView needing to know anything about that.
        public Vector3 RightHandGripPosition => transform.TransformPoint(anim.rightHandGrip);
        public Vector3 LeftHandGripPosition => transform.TransformPoint(anim.leftHandGrip);

        // The rest of the hand pose WeaponHandGripView applies alongside the position above -
        // authored per weapon with the grips themselves (a heavy weapon wants a different hand
        // angle/size than a pistol). Rotation is an absolute local-rotation override, scale a
        // multiplier over whatever the hand rig itself authors, so both default to "unchanged".
        // No flip mirroring needed here anymore - WeaponHandGripView composes this on top of
        // weaponView.transform.rotation (hand.rotation = weaponRotation * Quaternion.Euler(this)),
        // which already carries ApplyAim's facingFlip 180° turn, same as any other child would.
        // A manual Z-negation here used to be required because the old flip was a bare
        // localScale.y sign that ISN'T inherited through that multiply - now that the flip is a
        // real rotation, doing it again here would double-mirror the grip angle.
        public Vector3 RightHandGripRotation => anim.rightHandGripRotation;
        public Vector3 LeftHandGripRotation => anim.leftHandGripRotation;
        public Vector3 RightHandGripScale => anim.rightHandGripScale;
        public Vector3 LeftHandGripScale => anim.leftHandGripScale;

        // Whether the gun is currently mirrored - PlayerGunAimView derives this from
        // BlobAnimationView.FacingSign, so it's the character's own facing, not an independently
        // computed one. Exposed for WeaponHandGripView, whose hands hang off the rig's
        // WeaponLocator rather than the body root and so don't inherit the body's own flip, and
        // whose own hand.localScale.y mirror (a separate, still-scale-based flip of the hand mesh
        // itself, untouched by this weapon's own switch to a rotation-based flip) still needs it.
        public bool Flipped => lastFlipped;

        private Vector3 baseScale = Vector3.one;
        private Vector3 restLocalPosition;
        private Vector2 currentOffset;

        private Vector2 lastAimDir = Vector2.right;
        private bool lastFlipped;
        private Vector2 recoilOffset;
        private float recoilRotationCurrent;
        private float knockbackPunch;

        [Header("Sound")]
        [SerializeField, SoundDataPicker, Tooltip("Played once per shot, on the same EventPlayerFired that drives the recoil kick. Author its pitch/volume variance and cooldown on the SoundData itself - a fast weapon fires many times a second, so the Group budget (Weapons) is what stops a sustained burst turning to mush. Leave empty for a silent weapon.")]
        private SoundData fireSound;

        [SerializeField, SoundDataPicker, Tooltip("Played the moment a reload BEGINS - the magazine-out/rack sound. Detected from Weapon.ReloadTimer going positive rather than an event, since the simulation only raises an event when a reload COMPLETES (WeaponSystem.StartReload is silent). Leave empty to skip.")]
        private SoundData reloadStartSound;

        [FormerlySerializedAs("reloadSound")]
        [SerializeField, SoundDataPicker, Tooltip("Played when a reload COMPLETES and the weapon is ready again (EventWeaponReloaded) - the magazine-in/slide-forward sound. This is the one the player actually listens for, so keep it distinct from the start. Leave empty to skip.")]
        private SoundData reloadReadySound;

        public override void Awake()
        {
            base.Awake();
            CacheRestPose();

            if (character == null)
                character = GetComponentInParent<BlobAnimationView>();
            if (character == null)
                character = transform.root.GetComponentInChildren<BlobAnimationView>();

            QuantumEvent.Subscribe<EventPlayerFired>(this, OnPlayerFired);
            QuantumEvent.Subscribe<EventWeaponReloaded>(this, OnWeaponReloaded);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);

            // Shoot()'s recoil kicks (Tween.PunchCustom(this, ...)) are frequently still decaying
            // when the owner dies mid-shot - without this, PrimeTween logs a stack-trace-capturing
            // error per orphaned tween every time that happens.
            Tween.StopAll(this);
        }

        // Rising-edge state for PollReloadStart.
        private bool _wasReloading;

        private void CacheRestPose()
        {
            baseScale = transform.localScale;
            restLocalPosition = transform.localPosition;
        }

        private void OnPlayerFired(EventPlayerFired e)
        {
            if (e.Entity != _entityRef) return;

            // Attached rather than fired-and-forgotten at a point: a weapon moves with the player
            // every frame, and this transform is the one thing guaranteed to still be where the gun
            // is. Only its position is read, so the billboard rotation/flip/shear ApplyAim bakes
            // into it (see this class's own comments) doesn't affect the sound.
            if (fireSound != null)
                EntitySound.PlayAttached(fireSound, transform, _entityRef);

            Shoot();
        }

        private void OnWeaponReloaded(EventWeaponReloaded e)
        {
            if (e.Entity != _entityRef) return;

            if (reloadReadySound != null)
                EntitySound.PlayAttached(reloadReadySound, transform, _entityRef);
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
            // First, before any of the recoil/knockback tweens below start - UnparentAndPlay
            // snapshots the muzzle at this weapon's CURRENT pose, and none of Shoot()'s own
            // animation (recoilOffset, recoilRotationCurrent, knockbackPunch) has been kicked off
            // yet at this point in the method, so there's nothing for it to catch mid-punch.
            UnparentAndPlay();

            Vector2 kickDir = -lastAimDir * anim.recoilKickDistance;
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(kickDir.x, kickDir.y, 0f), anim.recoilDuration, anim.recoilFrequency, asymmetryFactor: anim.recoilAsymmetry),
                (view, val) => view.recoilOffset = new Vector2(val.x, val.y));

            float rotationKick = anim.recoilRotationKick * (lastFlipped ? -1f : 1f);
            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(rotationKick, 0f, 0f), anim.recoilDuration, anim.recoilFrequency, asymmetryFactor: anim.recoilAsymmetry),
                (view, val) => view.recoilRotationCurrent = val.x);

            Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(new Vector3(1f, 0f, 0f), anim.recoilDuration, anim.recoilFrequency, asymmetryFactor: anim.recoilAsymmetry),
                (view, val) => view.knockbackPunch = val.x);

            PunchCharacter();
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

        // Shows flipPivotOffset in the Scene view so it can be eyeballed against the sprite without
        // needing a live flip running - drawn off the weapon's CURRENT transform, so it stays
        // accurate whether checked at rest, mid-aim, or (Play Mode) mid-flip. Selected-only so it
        // doesn't clutter the scene for every other weapon instance at once.
        private void OnDrawGizmosSelected()
        {
            Vector3 pivotWorld = transform.TransformPoint(flipPivotOffset);
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(pivotWorld, 0.04f);
            Gizmos.DrawLine(transform.position, pivotWorld);
        }

        // Cached the first time UnparentAndPlay runs - the muzzle's authored local position/
        // rotation relative to this weapon's own root (transform), i.e. exactly where the prefab
        // placed it. Scale is deliberately NOT cached/reapplied here - the parent.parent =
        // .../true reparents below already preserve world scale on their own each time, and
        // forcing localScale back to this original (relative-to-transform) value after the SECOND
        // reparent (now relative to transform.parent, a different scale context) fought that and
        // produced the wrong size.
        private bool muzzleRestPoseCached;
        private Vector3 muzzleRestLocalPosition;
        private Quaternion muzzleRestLocalRotation;

        // Re-syncs the muzzle to this weapon's CURRENT pose, then hands it up to transform.parent
        // (the character rig's weapon socket) before playing - so it's immune to everything
        // Shoot() does to THIS weapon's own transform afterward (recoil kick, knockback
        // pushback/scale), without needing to track a separately-computed "clean" pose by hand.
        //
        // Step by step: reparenting to `transform` first (preserving world pose, same as the
        // default Transform.parent setter always does) puts the muzzle back under a well-defined
        // parent regardless of where the previous call last left it; writing the cached rest
        // local pose on top then resets it to its exact authored offset from THIS weapon, which is
        // currently sitting at its own clean aim pose (Shoot() calls this before starting any
        // recoil/knockback tween - see its own comment). Reparenting up to transform.parent
        // (again preserving world pose) bakes that exact world placement into a new local offset
        // relative to the socket instead - which the recoil/knockback about to run on `transform`
        // never touches, so the muzzle stays put through the whole shot. Play() doesn't emit
        // synchronously (Unity processes it on its own particle update pass later this frame), but
        // nothing moves this transform again until the NEXT shot re-syncs it, so there's no race
        // like there would be if this reset itself back afterward.
        private void UnparentAndPlay()
        {
            if (muzzleParticle == null) return;

            Transform muzzle = muzzleParticle.transform;

            if (muzzleRestPoseCached == false)
            {
                muzzleRestLocalPosition = muzzle.localPosition;
                muzzleRestLocalRotation = muzzle.localRotation;
                muzzleRestPoseCached = true;
            }

            muzzle.parent = transform;
            muzzle.localPosition = muzzleRestLocalPosition;
            muzzle.localRotation = muzzleRestLocalRotation;

            muzzle.parent = transform.parent;

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

            // Facing left/right (pose.Flipped) used to mirror via a negative localScale.y instead
            // of this - swapped for a real 180° turn around this weapon's own local X axis (the
            // barrel/forward direction) so scale always stays positive/uniform. A negative-scale
            // mirror inverts the matrix determinant, which every child (muzzle flash, hitscan
            // tracers, anything spawned off this transform) also inherits and has to actively
            // compensate for; a rotation just physically turns the weapon around like the real
            // object it represents, so nothing downstream needs special-casing for it.
            //
            // MUST be X, not Y - RotationDegrees already fully encodes where to point (it's an
            // atan2 of the actual on-screen aim direction, correct for every angle including
            // pointing left, computed independently of pose.Flipped), same the way it always did.
            // Composing a Y-turn (which flips local X, the forward axis, along with everything
            // else) here adds a uniform extra 180° on top of that already-correct angle - looked
            // fully inverted (left read as right, up as down, down as up) for every direction, not
            // just left/right, because the "forward" reference itself flipped. Rotating around X
            // instead leaves forward (local +X) untouched by construction - only the sprite's
            // vertical extent (Y) and depth (Z) flip, exactly matching the old negative-Y-scale
            // mirror's actual effect (that trick only ever reflected off-axis geometry too; the
            // aim angle it multiplied in was already unmirrored, same as here).
            //
            // Composed as the rightmost (innermost) factor below - applied in this weapon's own
            // rest-local space before the billboard/aim rotation is layered on, same convention
            // the old scale flip used (scale is applied before rotation in Unity's local-to-world
            // matrix too).
            Quaternion facingFlip = pose.Flipped ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity;

            // baseRotation is everything EXCEPT the Jump Flip (aim/follow/recoil/facing) - the
            // normal rotation this weapon has always used, pivoting around the grip (transform's
            // own origin). fullRotation adds the Jump Flip's own contribution on top. Kept
            // separate so the position pivot-correction below only has to re-center THAT flip -
            // see flipPivotOffset. Not to be confused with facingFlip above (pose.Flipped, which
            // side the weapon faces) - pose.FlipDegrees here is the unrelated 360° spin animation.
            Quaternion baseRotation = pose.FacingCamera * Quaternion.Euler(0f, 0f, pose.RotationDegrees + recoilRotationCurrent) * facingFlip;
            Quaternion fullRotation = pose.FacingCamera * Quaternion.Euler(0f, 0f, pose.RotationDegrees + recoilRotationCurrent + pose.FlipDegrees) * facingFlip;

            Vector3 scale = baseScale * (1f - knockbackPunch * anim.knockbackScalePunch);
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
            Vector3 basePosition = restWorldPosition + worldOffset;

            // Re-center the FLIP only: solve for the position that keeps flipPivotOffset fixed in
            // world space across the difference between baseRotation and fullRotation, exactly like
            // BlobAnimationView's own root pivot-correction. A pure no-op when FlipDegrees is 0
            // (fullRotation == baseRotation), so every existing weapon is bit-for-bit unaffected
            // until flipPivotOffset is actually tuned away from (0,0,0) AND a flip is playing.
            Vector3 pivotWorld = basePosition + baseRotation * flipPivotOffset;

            transform.rotation = fullRotation;
            transform.position = pivotWorld - fullRotation * flipPivotOffset;

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
            PollReloadStart(game);
        }

        // Reload START has no event of its own - WeaponSystem.StartReload just sets ReloadTimer, and
        // the only event it ever raises is WeaponReloaded on COMPLETION. Rather than add a .qtn
        // event (and a codegen dependency) for a purely cosmetic cue, watch the timer go positive:
        // it's set once when the reload begins and counts down to 0, so a rising edge is exactly the
        // start. Same read ContinuousHitscanView.IsReloading already does for the beam.
        private void PollReloadStart(QuantumGame game)
        {
            Frame frame = game != null ? game.Frames.Predicted : null;

            bool reloading = frame != null
                && frame.TryGet<Weapon>(_entityRef, out var weapon)
                && weapon.ReloadTimer > FP._0;

            if (reloading && _wasReloading == false && reloadStartSound != null)
                EntitySound.PlayAttached(reloadStartSound, transform, _entityRef);

            _wasReloading = reloading;
        }
    }
}

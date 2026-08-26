using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // The WORLD COLLECTIBLE half of the Recoverable Accessory Guard's presentation (see
    // docs/accessory-guard.md) - lives on the ONE shared, hero-agnostic DroppedAccessory prototype
    // and does two jobs, both purely cosmetic:
    //
    //   1. paints it with whichever hero's accessory actually dropped, resolved through
    //      DroppedAccessory.Owner -> CharacterStats.CharacterData -> Accessory.CollectibleSprite;
    //   2. spins it while it's still in the air, settling to a flat Y = 0 as it lands.
    //
    // This is why the simulation needs only one prototype instead of one per hero (see
    // AccessoryGuardUtility.SpawnCollectible): every gameplay behaviour of the pickup - the pop arc,
    // landing, owner-only collection, despawn - is identical for all heroes, so only the sprite
    // varies, and a sprite is presentation. Nothing here is hero-branched, and nothing here writes
    // back to simulation state.
    public class DroppedAccessoryView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Renderer painted with the owning hero's CharacterData.Accessory.CollectibleSprite. Left unassigned, the first SpriteRenderer found in this prefab's children is used.")]
        private SpriteRenderer spriteRenderer;

        [SerializeField, Tooltip("Kept visible when the owner's hero data has no CollectibleSprite authored - the prototype's own placeholder sprite is left untouched rather than blanking the pickup out, so an unauthored hero still drops something the player can see and walk to.")]
        private bool keepPlaceholderWhenUnauthored = true;

        // Which axis the spin turns around, applied in the BILLBOARD's own space when billboarding
        // (so Z is the screen plane, not world Z). Z is the default because a camera-facing sprite
        // rolling in its own plane stays fully visible the whole way round; Y yaws it instead, which
        // takes a flat sprite edge-on twice per turn and reads as flickering. Authored rather than
        // hardcoded because which one suits a given piece of art is a look decision, not a code one.
        public enum SpinAxis { X, Y, Z }

        [Header("Spin")]
        [SerializeField, Tooltip("Transform spun while airborne. Left unassigned, the sprite renderer's own transform is used. Its PIVOT is what the spin turns around, so centre the sprite's pivot (or park this transform at the sprite's centre) or it will swing rather than spin in place.")]
        private Transform spinTransform;

        [SerializeField, Tooltip("Keep the accessory facing the camera while it spins. This component applies the camera-facing rotation ITSELF, in LateUpdate, with the spin composed on top - so a separate Billboard component on the same transform is redundant and is disabled automatically (two writers to one rotation is a coin flip on script order). Uncheck for a fixed-angle prop that keeps its authored rotation instead.")]
        private bool billboardToCamera = true;

        [SerializeField, Tooltip("Axis the spin turns around, and the one that lands back at 0. While billboarding this is in the billboard's own space, so Z is the screen plane. Without billboarding it is the local axis, and the other two keep whatever the prefab authored.")]
        private SpinAxis spinAxis = SpinAxis.Z;

        [SerializeField, Tooltip("Spin rate around the chosen axis while the accessory is still in the air. Negative spins the other way.")]
        private float spinSpeedDegreesPerSecond = 540f;

        [SerializeField, Tooltip("How long the spin takes to ease down to 0 once the pop arc lands. It always finishes by continuing FORWARD to 0 rather than winding back, so it never visibly reverses. 0 snaps to 0 the instant it lands.")]
        private float landingSettleDuration = 0.25f;

        private bool _resolved;

        // The prototype's own authored sprite scale, captured before anything multiplies it - so a
        // per-hero CollectibleScale always composes with the prefab rather than compounding off a
        // previous hero's correction on a re-resolve.
        private Vector3 _authoredSpriteScale = Vector3.one;

        // The authored local rotation, captured once. Only the spin axis is ever overwritten, so a
        // prototype tilted to face an angled camera (AccessoryOrb's sprite child sits at X = 45)
        // keeps that tilt through the whole spin and landing.
        private Vector3 _authoredEuler;

        // The spin angle is accumulated HERE rather than read back off localEulerAngles each frame.
        // That read-back is the bug this replaces: Unity derives those Euler angles from the
        // underlying quaternion, and for a transform with a non-zero X (or Z) tilt the decomposition
        // returns a different-but-equivalent triple as the spin passes 90 degrees - so the value read
        // back is not the value written, and the accumulation stalls or jitters instead of turning.
        // Owning the angle outright makes it correct for any authored tilt.
        private float _spinAngle;

        private bool _wasAirborne;
        private bool _settling;
        private float _settleFromAngle;
        private float _settleSweep;
        private float _settleElapsed;

        // Cached the same lazy way Billboard does it - Camera.main is a tagged lookup, not free.
        private Camera _camera;

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            _resolved = false;
            _wasAirborne = false;
            _settling = false;

            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>(includeInactive: true);

            if (spriteRenderer != null)
                _authoredSpriteScale = spriteRenderer.transform.localScale;

            if (spinTransform == null && spriteRenderer != null)
                spinTransform = spriteRenderer.transform;

            if (spinTransform != null)
            {
                _authoredEuler = spinTransform.localEulerAngles;
                _spinAngle = 0f;

                // Exactly one thing may own this rotation. Billboard writes an absolute world
                // rotation every LateUpdate, so leaving it enabled alongside our own write makes the
                // result depend on script execution order - the spin would work or not work
                // arbitrarily. We reproduce its behaviour verbatim (same LookRotation) and compose
                // the spin on top, so disabling it loses nothing and removes the race.
                Billboard billboard = spinTransform.GetComponent<Billboard>();

                if (billboard != null && billboardToCamera == true)
                {
                    billboard.enabled = false;
                }
                else if (billboard != null)
                {
                    LogHelper.Warn("Accessory", $"{name}'s spin transform ({spinTransform.name}) has a Billboard and " +
                        "billboardToCamera is off - Billboard hard-sets rotation every LateUpdate, so the spin will " +
                        "never be visible. Either tick billboardToCamera or remove the Billboard.", this);
                }
            }

            TryResolveSprite(game);
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (_resolved == false)
                TryResolveSprite(game);

            UpdateSpin(game);
        }

        // PopVelocity is present for exactly as long as the accessory is genuinely in the air -
        // OrbSpawnUtility.SpawnWithPop adds it, PopMotionSystem removes it the instant the arc lands
        // (see PopVelocity.qtn). Reading that directly means the visual can never disagree with the
        // simulation's own idea of "has it landed", which is the same signal AccessoryGuardSystem
        // uses to flip Airborne -> Dropped and make the pickup collectible.
        private void UpdateSpin(QuantumGame game)
        {
            if (spinTransform == null)
                return;

            Frame frame = game?.Frames.Predicted;

            if (frame == null)
                return;

            bool airborne = frame.Has<PopVelocity>(_entityRef);

            if (airborne == true)
            {
                _wasAirborne = true;
                _settling = false;

                _spinAngle = Mathf.Repeat(_spinAngle + spinSpeedDegreesPerSecond * Time.deltaTime, 360f);
                return;
            }

            // Just landed this frame - start easing whatever angle it happens to be at down to 0.
            if (_wasAirborne == true)
            {
                _wasAirborne = false;

                if (landingSettleDuration <= 0f || Mathf.Approximately(_spinAngle, 0f) == true)
                {
                    _spinAngle = 0f;
                    return;
                }

                _settling = true;
                _settleElapsed = 0f;
                _settleFromAngle = _spinAngle;

                // Distance to 0 travelling FORWARD in the spin's own direction, so the settle reads
                // as the spin winding down rather than snapping back the way it came.
                _settleSweep = spinSpeedDegreesPerSecond >= 0f
                    ? Mathf.Repeat(360f - _spinAngle, 360f)
                    : -Mathf.Repeat(_spinAngle, 360f);
            }

            if (_settling == false)
                return;

            _settleElapsed += Time.deltaTime;

            float t = Mathf.Clamp01(_settleElapsed / landingSettleDuration);
            float eased = 1f - (1f - t) * (1f - t); // ease-out quad - fast on touchdown, gently to rest

            _spinAngle = Mathf.Repeat(_settleFromAngle + _settleSweep * eased, 360f);

            if (t < 1f)
                return;

            _spinAngle = 0f;
            _settling = false;
        }

        // The rotation is written in LateUpdate, not in QUpdate above, so it always lands AFTER
        // anything else that touches rotation this frame - the same slot Billboard itself writes in.
        // QUpdate only advances the angle; this is the single place the transform is actually set.
        private void LateUpdate()
        {
            if (spinTransform == null)
                return;

            if (billboardToCamera == true)
            {
                // Reproduces Billboard verbatim (that component is disabled on this transform in
                // Initialize - see there for why exactly one writer matters), then composes the spin
                // on top IN THE BILLBOARD'S OWN SPACE. That is what lets the accessory keep facing
                // the camera the whole time it spins: with spinAxis Z it rolls in the screen plane
                // and never goes edge-on, which a world-space spin could not do.
                if (_camera == null)
                    _camera = Camera.main;

                if (_camera == null)
                    return;

                Quaternion facing = Quaternion.LookRotation(_camera.transform.forward, Vector3.up);
                spinTransform.rotation = facing * Quaternion.AngleAxis(_spinAngle, ResolveAxis());
                return;
            }

            // Fixed-angle prop: keep the authored local rotation and drive only the spin axis, so a
            // prototype tilted to face an angled camera keeps that tilt through the whole spin.
            Vector3 euler = _authoredEuler;

            switch (spinAxis)
            {
                case SpinAxis.X: euler.x = _spinAngle; break;
                case SpinAxis.Y: euler.y = _spinAngle; break;
                default: euler.z = _spinAngle; break;
            }

            spinTransform.localEulerAngles = euler;
        }

        private Vector3 ResolveAxis()
        {
            switch (spinAxis)
            {
                case SpinAxis.X: return Vector3.right;
                case SpinAxis.Y: return Vector3.up;
                default: return Vector3.forward;
            }
        }

        private unsafe void TryResolveSprite(QuantumGame game)
        {
            if (spriteRenderer == null)
            {
                LogHelper.Warn("Accessory", $"{name} has no SpriteRenderer - the dropped accessory will be invisible", this);
                _resolved = true;
                return;
            }

            Frame frame = game.Frames.Predicted;

            if (frame.Unsafe.TryGetPointer<DroppedAccessory>(_entityRef, out var dropped) == false)
                return;

            EntityRef owner = dropped->Owner;

            if (owner == EntityRef.None || frame.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return;

            if (stats->CharacterData.IsValid == false)
                return;

            CharacterData data = frame.FindAsset(stats->CharacterData);
            _resolved = true;

            if (data == null || data.Accessory.CollectibleSprite == null)
            {
                if (keepPlaceholderWhenUnauthored == false)
                    spriteRenderer.enabled = false;

                return;
            }

            spriteRenderer.sprite = data.Accessory.CollectibleSprite;

            // 0/unset reads as 1, the same "an unset multiplier defaults safely" convention
            // EnemyFactionSkin.ScaleMultiplier already uses - so a hero authored before this field
            // existed is unaffected.
            float scale = data.Accessory.CollectibleScale > 0f ? data.Accessory.CollectibleScale : 1f;
            spriteRenderer.transform.localScale = _authoredSpriteScale * scale;
        }
    }
}

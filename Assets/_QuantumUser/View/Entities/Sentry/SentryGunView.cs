using NaughtyAttributes;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Per-entity view for a SentryBarrel - rotates this gun's visual to face its own Aim.Angle
    // (SentryBarrelSystem resolves this independently per barrel, searching from that barrel's own
    // position - see SentryBarrelSystem's own comment - so different barrels can be facing different
    // enemies). Same camera-basis projection PlayerGunAimView/WeaponView use for the player's own
    // held weapon (reconstruct the flat world direction from the angle, project it onto the camera's
    // actual right/up so it reads correctly under this project's pitched camera, then derive a
    // screen-space angle) - trimmed down to just rotation+flip, since a stationary turret has no
    // body to follow and no movement to sway against.
    public class SentryGunView : CustomQuantumEntityViewComponent
    {
        // Mirrors the simulation's own SentryBarrel.SlotIndex (which of the 4 SentryWeaponUpgrade
        // slots armed this barrel) - read once in Initialize, since it's baked at spawn and never
        // changes. Exposed purely for identification/debugging today (e.g. telling barrels apart in
        // the Inspector or from other view scripts); nothing here currently branches on it.
        public byte SlotIndex { get; private set; }

        [SerializeField, Tooltip("Falls back to Camera.main if left empty.")]
        private Transform cameraTransform;

        [SerializeField, Tooltip("The sprite/rig transform actually rotated - falls back to this component's own transform if left empty.")]
        private Transform visualRoot;

        [SerializeField, Tooltip("Swapped to SentryBarrel.Source's own per-slot WeaponSprite (SentryWeaponSystemsSkillAction.View.cs) in Initialize, if that asset authored one - falls back to a GetComponentInChildren lookup if left empty. Leave the granting asset's WeaponSprite unset to keep whatever this already shows.")]
        private SpriteRenderer spriteRenderer;

        [SerializeField, Tooltip("Where the sentry's tentacle should grab this gun - the equivalent of a weapon's own hand-grip anchor (WeaponView.RightHandGripPosition). MUST be a child of visualRoot so it inherits the aim rotation, the left-facing flip, the idle float and the shoot punch; anything outside visualRoot is a fixed point the gun visibly swings away from. Falls back to visualRoot itself if left empty, which still tracks correctly - just at the gun's own pivot rather than a hand-authored spot on it.")]
        private Transform gripAnchor;

        [SerializeField, Tooltip("Depth nudge applied to the grip point so the tentacle tip draws IN FRONT of the gun sprite instead of z-fighting with it or disappearing behind it. visualRoot is billboarded to face the camera, so its local +Z runs away from the viewer - a NEGATIVE value pulls the hand toward the camera. Purely presentational; it never moves the gun itself.")]
        private float gripZOffset = -0.02f;

        // The transform SentryView.OnSentryBarrelSpawned actually pins the tentacle to - a runtime
        // child of the authored anchor, built in Awake, never the authored transform itself. Keeping
        // it separate is what lets gripZOffset exist at all: the fallback anchor is visualRoot, and
        // nudging THAT would move the whole gun sprite rather than just the hand.
        private Transform gripPoint;

        [SerializeField, Tooltip("Degrees added so the sprite's own rest orientation lines up with angle 0 (screen-right). -90 if the gun art is drawn pointing up.")]
        private float angleOffset = -90f;

        [SerializeField, Tooltip("How quickly the gun turns to face a new direction. Higher = snappier.")]
        private float rotationSmoothing = 20f;

        [SerializeField, Tooltip("Mirror on the local Y axis when facing left instead of continuing to rotate upside-down - same convention WeaponView.ApplyAim uses for the player's own gun. A barrel has no body/FacingSign of its own to sync with, so this is derived straight from its own aim direction.")]
        private bool flipWhenAimingLeft = true;

        [Header("Shoot Punch")]
        [SerializeField, Tooltip("Particle system parented at the muzzle, restarted on every shot (e.g. an Epic Toon FX Muzzleflash prefab) - same convention as WeaponView.muzzleParticle for the player's own gun.")]
        private ParticleSystem muzzleParticle;
        [SerializeField, Tooltip("Punch-scale strength on X/Y, as a FRACTION of the gun's authored scale (0.2 = 20% bigger at the peak). Composed into visualRoot's own per-frame scale write rather than tweened onto a transform directly - see Shoot(). Z is never punched - a Vector2 so that can't drift from the Inspector.")]
        private Vector2 punchStrength = new Vector2(0.2f, 0.2f);
        [SerializeField, Tooltip("How long the punch takes to settle back to rest.")]
        private float punchDuration = 0.25f;
        [SerializeField, Tooltip("PrimeTween's own shake frequency (oscillations per second) - higher reads as a snappier, more jittery punch.")]
        private float punchFrequency = 10f;

        [SerializeField, Tooltip("Per-axis shake distance on every shot, in visualRoot's own LOCAL space - the same frame of reference the idle float below uses (its parent is the barrel entity, which carries the chassis rotation), NOT screen space and NOT relative to where the gun is currently aiming. Leaving an axis at 0 locks it, which is the whole point: (0, 0.05, 0) is a pure vertical kick, (0.05, 0, 0) a pure sideways one. Z reads as depth toward/away from the camera on a billboarded sprite, so it is usually left at 0. All axes 0 skips the shake entirely.")]
        private Vector3 shakePositionStrength = new Vector3(0f, 0.05f, 0f);
        [SerializeField, Tooltip("How long the position shake takes to settle back to rest. Keep it at or below the barrel's fire interval - a faster barrel restarts the shake before it has finished, and a restart re-anchors from rest rather than stacking.")]
        private float shakePositionDuration = 0.2f;
        [SerializeField, Tooltip("Oscillations per second for the position shake. Higher reads as a sharper rattle; low values read as a single shove.")]
        private float shakePositionFrequency = 14f;

        [Header("Idle Float")]
        [SerializeField, Tooltip("How far the gun bobs left/right from rest while idle.")]
        private float idleFloatAmplitudeX = 0.03f;
        [SerializeField, Tooltip("How far the gun bobs up/down from rest while idle.")]
        private float idleFloatAmplitudeY = 0.05f;
        [SerializeField, Tooltip("Base bob cycles per second - randomized per-instance by +/- idleFloatFrequencyJitter (phase AND frequency, not just phase) so multiple barrels on one sentry never bob in lockstep or drift back into sync with each other.")]
        private float idleFloatFrequency = 1f;
        [SerializeField, Range(0f, 1f), Tooltip("Random +/- fraction of idleFloatFrequency rolled once per instance in Awake - e.g. 0.3 means this barrel's actual X/Y frequencies each land anywhere from 70% to 130% of the base, independently.")]
        private float idleFloatFrequencyJitter = 0.3f;

        private Vector3 restLocalPosition;
        private float idlePhaseX;
        private float idlePhaseY;
        private float idleFrequencyX;
        private float idleFrequencyY;

        private float currentAngle;
        private bool isFlipped;
        private Vector3 baseScale = Vector3.one;

        // The live shoot-punch, as a fraction over rest (0 = at rest). Owned as a FIELD rather than
        // tweened straight onto a transform because QUpdate below rewrites visualRoot.localScale
        // every single frame for the left-facing flip - and visualRoot is the very same GameObject
        // the SpriteRenderer sits on, so a Tween.PunchScale on it was simply overwritten before it
        // could ever be seen. Same "one writer per transform, compose everything into it" shape
        // WeaponView.Shoot already uses for the player's own gun recoil.
        private Vector3 punchScale;

        // The live shot shake, in the same local space restLocalPosition/the idle float already work
        // in - composed into the ONE localPosition write in QUpdate for exactly the same reason
        // punchScale is: that write happens every frame, so a tween owning the transform directly
        // would be overwritten before it rendered.
        private Vector3 punchPosition;

        public override void Awake()
        {
            base.Awake();

            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;

            if (visualRoot == null)
            {
                // Falling back to this component's own root transform is almost always wrong here:
                // that's the exact transform QuantumEntityView writes the entity's simulated
                // Transform3D.Position onto every frame. QUpdate below then unconditionally
                // overwrites localPosition on visualRoot for rotation/idle-float/punch-scale, which -
                // if visualRoot IS the root - stomps that sync right back to a fixed spawn-time
                // value every frame, visually freezing this barrel in place regardless of how
                // correctly the simulation actually moves it (e.g. following a chassis that's
                // settling to the ground). Assign a separate child transform in the Inspector.
                LogHelper.Warn("Sentry", $"{name} has no visualRoot assigned - falling back to its own root transform, which will fight QuantumEntityView's own position sync. Assign a child transform instead.");
                visualRoot = transform;
            }

            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            baseScale = visualRoot.localScale;
            restLocalPosition = visualRoot.localPosition;

            CreateGripPoint();

            // Randomized once per instance (not just phase - frequency too) so 4 barrels on one
            // sentry don't bob in visible lockstep, and don't gradually drift back into sync with
            // each other over time the way a phase-only offset eventually would.
            idlePhaseX = Random.Range(0f, Mathf.PI * 2f);
            idlePhaseY = Random.Range(0f, Mathf.PI * 2f);
            idleFrequencyX = idleFloatFrequency * Random.Range(1f - idleFloatFrequencyJitter, 1f + idleFloatFrequencyJitter);
            idleFrequencyY = idleFloatFrequency * Random.Range(1f - idleFloatFrequencyJitter, 1f + idleFloatFrequencyJitter);

            QuantumEvent.Subscribe<EventPlayerFired>(this, OnPlayerFired);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        // Read by SentryView.OnSentryBarrelSpawned to pin the same-indexed tentacle leg onto this gun.
        //
        // Deliberately NOT this component's own root transform: that is the one QuantumEntityView
        // writes the barrel entity's raw simulated Transform3D.Position onto every frame, and nothing
        // else about the gun's presentation lives there. Rotation, flip, idle float and shoot punch
        // are all applied to visualRoot (a child) in QUpdate, so a tentacle pinned to the root grabs a
        // point the gun continuously moves away from - it reads as the tentacle pointing NEAR the gun
        // rather than holding it. Same reason WeaponHandGripView resolves the player's hand grip
        // through the weapon's own live transform instead of the character's.
        public Transform GripAnchor => gripPoint != null ? gripPoint : (gripAnchor != null ? gripAnchor : visualRoot);

        // Parented under the authored anchor (or visualRoot when none is authored) with identity local
        // rotation/scale, so it inherits every bit of the gun's motion and adds only the depth nudge.
        // worldPositionStays: false on SetParent - this is being placed BY its local offset, not
        // preserving some world pose it never had.
        private void CreateGripPoint()
        {
            Transform anchor = gripAnchor != null ? gripAnchor : visualRoot;

            if (anchor == null)
                return;

            gripPoint = new GameObject("GripPoint").transform;
            gripPoint.SetParent(anchor, false);
            gripPoint.localPosition = new Vector3(0f, 0f, gripZOffset);
        }

        // WeaponSystem fires this generically for any Weapon-carrying entity, not just real
        // players - same event a barrel's own shots trigger as a real player's would.
        private void OnPlayerFired(EventPlayerFired e)
        {
            if (e.Entity != _entityRef) return;
            Shoot();
        }

        // Stops any punch already mid-flight before starting a new one - rapid fire otherwise stacks
        // overlapping PunchScale tweens on the same transform, each capturing whatever (already
        // displaced) scale the previous one left it at as its own new baseline, compounding instead
        // of settling. Idle float phases reset to 0 (not re-randomized) so the bob is at zero
        // displacement - sin(0) = 0 - the instant a shot fires, then eases back into its cycle on
        // its own; without this the punch could land while the idle bob is mid-swing, reading as a
        // jump/pop instead of a clean recoil.
        [Button("Test Shoot")]
        public void Shoot()
        {
            idlePhaseX = 0f;
            idlePhaseY = 0f;

            // Stopped rather than stacked: a fast barrel fires again well inside punchDuration, and
            // overlapping punches on one value compound instead of settling. Targets `this` (not a
            // transform) since both punches drive fields - see punchScale/punchPosition.
            Tween.StopAll(this);

            // Each punch runs 0 -> 0, so restarting one always re-anchors it at rest. A strength of
            // zero is skipped and its field cleared BY HAND instead: StopAll leaves a half-decayed
            // value exactly where it stopped, which would otherwise stick the gun off-centre for
            // good the moment an axis is zeroed in the Inspector.
            Vector3 scaleStrength = new Vector3(punchStrength.x, punchStrength.y, 0f);

            if (scaleStrength.sqrMagnitude > 0f)
            {
                Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(scaleStrength, punchDuration, punchFrequency),
                    (view, val) => view.punchScale = val);
            }
            else
            {
                punchScale = Vector3.zero;
            }

            if (shakePositionStrength.sqrMagnitude > 0f)
            {
                Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(shakePositionStrength, shakePositionDuration, shakePositionFrequency),
                    (view, val) => view.punchPosition = val);
            }
            else
            {
                punchPosition = Vector3.zero;
            }

            PlayMuzzleParticle();
        }

        [Button]
        private void PlayMuzzleParticle()
        {
            if (muzzleParticle == null) return;
            muzzleParticle.Play(true);
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            if (game.Frames.Verified.TryGet<SentryBarrel>(_entityRef, out var barrel) == false)
                return;

            SlotIndex = barrel.SlotIndex;

            if (barrel.Source.IsValid == false || spriteRenderer == null)
                return;

            // One ranked asset now arms all three Ascension slots, so the sprite is resolved per
            // SLOT rather than per asset - see SentryWeaponSystemsSkillAction.View.cs. Slot 0 (the
            // baseline Cannon) has no granting asset at all, which the Source.IsValid check above
            // already filtered out.
            SentryWeaponSystemsSkillAction source = game.Frames.Verified.FindAsset(barrel.Source);
            Sprite weaponSprite = source.GetWeaponSprite(barrel.SlotIndex);

            if (weaponSprite != null)
            {
                spriteRenderer.sprite = weaponSprite;
            }
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (cameraTransform == null) return;

            Frame frame = game.Frames.Predicted;
            if (frame.Has<Aim>(_entityRef) == false) return;

            float dt = Time.deltaTime;
            float angleRad = frame.Get<Aim>(_entityRef).Angle.AsFloat * Mathf.Deg2Rad;
            Vector3 worldDir = new Vector3(Mathf.Sin(angleRad), 0f, Mathf.Cos(angleRad));
            Vector2 screenDir = ProjectToScreen(worldDir);

            float smoothT = 1f - Mathf.Exp(-rotationSmoothing * dt);

            if (screenDir.sqrMagnitude > 0.0001f)
            {
                float targetAngle = Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg + angleOffset;
                currentAngle = Mathf.LerpAngle(currentAngle, targetAngle, smoothT);
                isFlipped = flipWhenAimingLeft && screenDir.x < 0f;
            }

            Quaternion facingCamera = Quaternion.LookRotation(cameraTransform.forward, Vector3.up);
            visualRoot.rotation = facingCamera * Quaternion.Euler(0f, 0f, currentAngle);

            // Punch first, flip second - punchScale is a fraction over the AUTHORED scale, so it has
            // to multiply baseScale before the mirror negates Y, or a punch would read as shrinking
            // the gun whenever it happens to be facing left.
            Vector3 scale = Vector3.Scale(baseScale, Vector3.one + punchScale);
            scale.y *= isFlipped ? -1f : 1f;
            visualRoot.localScale = scale;

            visualRoot.localPosition = restLocalPosition + IntegrateIdleFloat(dt) + punchPosition;
        }

        // Independent X/Y sine waves, each with their own randomized (per-instance) phase and
        // frequency - a shared single sine would move every barrel in perfect lockstep, and a
        // phase-only offset would still eventually drift back into visible sync since they'd all
        // share the same period. Local space (not screen/camera space, unlike rotation above) - a
        // stable up/down/left/right bob relative to visualRoot's own parent, unaffected by
        // whichever way this specific gun currently happens to be aiming.
        private Vector3 IntegrateIdleFloat(float dt)
        {
            idlePhaseX += dt * idleFrequencyX * Mathf.PI * 2f;
            idlePhaseY += dt * idleFrequencyY * Mathf.PI * 2f;

            float bobX = Mathf.Sin(idlePhaseX) * idleFloatAmplitudeX;
            float bobY = Mathf.Sin(idlePhaseY) * idleFloatAmplitudeY;

            return new Vector3(bobX, bobY, 0f);
        }

        private Vector2 ProjectToScreen(Vector3 worldDir)
        {
            return new Vector2(Vector3.Dot(worldDir, cameraTransform.right), Vector3.Dot(worldDir, cameraTransform.up));
        }
    }
}

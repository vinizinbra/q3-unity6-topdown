using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // View for a Traversal Challenge's return pad (TraversalReturnTeleport, see
    // docs/traversal-challenge.md):
    //  - pops the pad in when it spawns (right after the reward is collected);
    //  - detaches its ExitMarker child and parks it on TraversalReturnTeleport.Destination. The exit
    //    is hidden at rest: it grows in while someone charges, bursts on arrival, then shrinks away;
    //  - while someone stands on the pad (largest TraversalReturnTeleport.Charge / ChargeTime), the
    //    charge emitters on BOTH ends start from nothing and grow (rate, speed, size) until the
    //    teleport fires, and the charge ring closes in on the pad;
    //  - a rising hum (chargeSound, pitch follows the charge) plays while anyone charges;
    //  - on EventTraversalReturnTeleported for THIS pad: departFx (beam + flash + shockwave + burst)
    //    on the pad and arrivalFx on the exit marker. The pad also arrives with departFx when it
    //    first spawns (right after the reward is collected).
    // Charge feedback is a pure read of the sim's charge each frame (rollback/late-join safe).
    public class TraversalReturnTeleportView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Scaled from 0 to its authored scale when the pad spawns.")]
        private Transform visualRoot;
        [SerializeField] private float popDuration = 0.5f;
        [SerializeField] private Ease popEase = Ease.OutBack;

        [SerializeField, Tooltip("Optional - spun around its local up axis every frame, faster while charging.")]
        private Transform spinner;
        [SerializeField] private float spinDegreesPerSecond = 60f;
        [SerializeField] private float chargedSpinMultiplier = 5f;

        [Header("Charging")]
        [SerializeField, Tooltip("Emitters on the pad AND on the exit marker that are silent at rest and ramp up with the charge.")]
        private ParticleSystem[] chargeEmitters;
        [SerializeField, Tooltip("Emission rate (per emitter) at full charge.")]
        private float chargeMaxRate = 60f;
        [SerializeField] private float chargeMaxSpeedMultiplier = 2.5f;
        [SerializeField] private float chargeMaxSizeMultiplier = 1.8f;
        [SerializeField, Tooltip("Optional - shown only while charging; scales from chargeRingStartScale down to chargeRingEndScale as the charge fills (closing in).")]
        private Transform chargeRing;
        [SerializeField] private float chargeRingStartScale = 1.6f;
        [SerializeField] private float chargeRingEndScale = 0.35f;
        [SerializeField, Tooltip("Optional - the pad's glow layers, scaled up with the charge so the whole portal brightens.")]
        private Transform chargeGlow;
        [SerializeField] private float chargeGlowMaxScale = 1.5f;

        [Header("Exit marker / teleport")]
        [SerializeField, Tooltip("Child detached at Initialize and placed on the pad's Destination - the arrival point's own glow + particles. Destroyed with the pad.")]
        private Transform exitMarker;
        [SerializeField, Tooltip("The exit marker's visible part - scaled 0 at rest, grows in while charging, shrinks away after an arrival.")]
        private Transform exitVisual;
        [SerializeField] private float exitGrowSpeed = 5f;
        [SerializeField, Tooltip("Seconds the exit stays (shrinking) after someone arrives.")]
        private float exitLingerTime = 0.6f;
        [SerializeField, Tooltip("Optional - played (with children) on the pad every time someone departs, and once when the pad first appears.")]
        private ParticleSystem departFx;
        [SerializeField, Tooltip("Optional - played (with children) on the exit marker every time someone arrives.")]
        private ParticleSystem arrivalFx;
        [SerializeField, SoundDataPicker] private SoundData teleportSound;
        [SerializeField, SoundDataPicker, Tooltip("Optional - hum started when someone begins charging; its pitch rises with the charge, faded out if they step off, cut on teleport.")]
        private SoundData chargeSound;
        [SerializeField] private Vector2 chargePitchRange = new Vector2(0.7f, 1.5f);

        private Vector3 _visualRestScale = Vector3.one;
        private Vector3 _exitRestScale = Vector3.one;
        private Vector3 _glowRestScale = Vector3.one;
        private float _exitScale;
        private float _exitLinger;
        private SoundHandle _chargeHandle = SoundHandle.None;
        private float[] _baseSizeMultipliers;
        private Tween _popTween;

        public override void Awake()
        {
            if (visualRoot != null)
                _visualRestScale = visualRoot.localScale;
            if (exitVisual != null)
                _exitRestScale = exitVisual.localScale;
            if (chargeGlow != null)
                _glowRestScale = chargeGlow.localScale;

            if (chargeEmitters != null)
            {
                _baseSizeMultipliers = new float[chargeEmitters.Length];

                for (int i = 0; i < chargeEmitters.Length; i++)
                {
                    if (chargeEmitters[i] == null)
                        continue;

                    _baseSizeMultipliers[i] = chargeEmitters[i].main.startSizeMultiplier;
                }
            }

            base.Awake();
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            QuantumEvent.UnsubscribeListener(this);
            QuantumEvent.Subscribe<EventTraversalReturnTeleported>(this, OnTeleported);

            if (exitMarker != null)
                exitMarker.SetParent(null, worldPositionStays: true);

            _exitScale = 0f;
            _exitLinger = 0f;
            ApplyExitScale();
            ApplyCharge(0f);

            if (visualRoot != null)
                _popTween = Tween.Scale(visualRoot, Vector3.zero, _visualRestScale, popDuration, popEase);

            PlayFx(departFx);
        }

        public override void DeInitialize(QuantumGame game)
        {
            QuantumEvent.UnsubscribeListener(this);
            _popTween.Stop();
            _chargeHandle.Stop();

            if (exitMarker != null)
                Destroy(exitMarker.gameObject);

            base.DeInitialize(game);
        }

        public override void OnDestroy()
        {
            QuantumEvent.UnsubscribeListener(this);

            if (exitMarker != null)
                Destroy(exitMarker.gameObject);

            base.OnDestroy();
        }

        protected override unsafe void QUpdate(QuantumGame game)
        {
            Frame f = game.Frames.Predicted;
            float charge = 0f;

            if (f.Unsafe.TryGetPointer<TraversalReturnTeleport>(_entityRef, out var pad) == true)
            {
                // Destination is written right after f.Create (SpawnReturnTeleport) - read it live
                // rather than once, so the marker is right even if the view spawned first.
                if (exitMarker != null)
                    exitMarker.position = pad->Destination.ToUnityVector3();

                float chargeTime = pad->ChargeTime.AsFloat;
                var charges = pad->Charge;

                for (int i = 0; i < charges.Length && chargeTime > 0f; i++)
                    charge = Mathf.Max(charge, Mathf.Clamp01(charges[i].AsFloat / chargeTime));
            }

            ApplyCharge(charge);
            UpdateExit(charge);
            UpdateChargeSound(charge);

            if (spinner != null)
                spinner.Rotate(Vector3.up, spinDegreesPerSecond * Mathf.Lerp(1f, chargedSpinMultiplier, charge) * Time.deltaTime, Space.Self);
        }

        // Starts from nothing (rate 0) and grows - eased in, so the last moments before the warp
        // read as the most intense.
        private void ApplyCharge(float charge)
        {
            float eased = charge * charge;

            if (chargeEmitters != null)
            {
                for (int i = 0; i < chargeEmitters.Length; i++)
                {
                    ParticleSystem emitter = chargeEmitters[i];

                    if (emitter == null)
                        continue;

                    var emission = emitter.emission;
                    emission.rateOverTimeMultiplier = charge > 0f ? Mathf.Lerp(chargeMaxRate * 0.15f, chargeMaxRate, eased) : 0f;

                    // These emitters move via Velocity over Lifetime (startSpeed is 0), so the speed
                    // ramp goes through its speed modifier, not startSpeedMultiplier.
                    var velocity = emitter.velocityOverLifetime;
                    velocity.speedModifierMultiplier = Mathf.Lerp(1f, chargeMaxSpeedMultiplier, eased);

                    // Relative to the authored size - on a TwoConstants curve the multiplier IS the
                    // max constant, so writing an absolute value would replace the size outright.
                    var main = emitter.main;
                    main.startSizeMultiplier = _baseSizeMultipliers[i] * Mathf.Lerp(1f, chargeMaxSizeMultiplier, eased);

                    if (emitter.isPlaying == false)
                        emitter.Play(true);
                }
            }

            if (chargeRing != null)
            {
                bool charging = charge > 0f;

                if (chargeRing.gameObject.activeSelf != charging)
                    chargeRing.gameObject.SetActive(charging);

                if (charging)
                    chargeRing.localScale = Vector3.one * Mathf.Lerp(chargeRingStartScale, chargeRingEndScale, charge);
            }

            if (chargeGlow != null)
                chargeGlow.localScale = _glowRestScale * Mathf.Lerp(1f, chargeGlowMaxScale, eased);
        }

        // Hidden at rest; grows in while anyone charges; after an arrival it stays at full size for a
        // beat and then shrinks away (_exitLinger counts down).
        private void UpdateExit(float charge)
        {
            float target = charge > 0f ? 1f : 0f;

            if (_exitLinger > 0f)
            {
                _exitLinger -= Time.deltaTime;
                target = Mathf.Max(target, Mathf.Clamp01(_exitLinger / exitLingerTime));
            }

            _exitScale = Mathf.MoveTowards(_exitScale, target, exitGrowSpeed * Time.deltaTime);
            ApplyExitScale();
        }

        private void ApplyExitScale()
        {
            if (exitVisual == null)
                return;

            bool shown = _exitScale > 0.001f;

            if (exitVisual.gameObject.activeSelf != shown)
                exitVisual.gameObject.SetActive(shown);

            if (shown)
                exitVisual.localScale = _exitRestScale * _exitScale;
        }

        private void UpdateChargeSound(float charge)
        {
            if (chargeSound == null)
                return;

            if (charge <= 0f)
            {
                if (_chargeHandle.IsValid)
                {
                    _chargeHandle.Stop();
                    _chargeHandle = SoundHandle.None;
                }

                return;
            }

            if (_chargeHandle.IsValid == false || _chargeHandle.IsPlaying == false)
                _chargeHandle = AudioManager.PlayAt(chargeSound, transform.position);

            _chargeHandle.SetPitch(Mathf.Lerp(chargePitchRange.x, chargePitchRange.y, charge));
        }

        private void OnTeleported(EventTraversalReturnTeleported e)
        {
            if (e.Teleport != _entityRef)
                return;

            _exitScale = 1f;
            _exitLinger = exitLingerTime;
            ApplyExitScale();

            _chargeHandle.Stop(0.1f);
            _chargeHandle = SoundHandle.None;

            PlayFx(departFx);
            PlayFx(arrivalFx);

            if (teleportSound != null)
                AudioManager.Play(teleportSound);
        }

        private static void PlayFx(ParticleSystem fx)
        {
            if (fx == null)
                return;

            fx.Clear(true);
            fx.Play(true);
        }
    }
}

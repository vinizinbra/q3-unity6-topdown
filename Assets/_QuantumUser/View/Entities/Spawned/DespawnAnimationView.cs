using NaughtyAttributes;
using Photon.Deterministic;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Generic despawn telegraph for any entity carrying the generic DestroyAfterTime component
    // (Vortex, Sentry, ...): shakes briefly to warn it's about to vanish, then scales down to zero
    // right before DestroyAfterTimeSystem actually destroys it. Driven directly off the verified-
    // via-Predicted DestroyAfterTime.RemainingTime each frame rather than a local countdown, so the
    // animation stays in sync across rollback with no state of its own to desync.
    public class DespawnAnimationView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Transform that shakes/scales. Defaults to this component's own transform if left unassigned.")]
        private Transform visual;

        [Header("Timing (counts down from RemainingTime)")]
        [SerializeField, Tooltip("Window right before destruction where the scale-down plays.")]
        private float scaleDownDuration = 0.5f;
        [SerializeField, Tooltip("Shapes the scale-down falloff - InQuad/InCubic accelerate into the vanish, OutBack overshoots past 0 first, etc.")]
        private Ease scaleDownEase = Ease.InQuad;
        [SerializeField, Tooltip("Window before the scale-down window where the anticipation shake plays.")]
        private float anticipationDuration = 0.3f;

        [Header("Anticipation Shake")]
        [SerializeField] private float shakeAmount = 0.08f;
        [SerializeField] private float shakeFrequency = 30f;
        [SerializeField, Tooltip("Extra scale wobble layered on top of the positional shake, as a fraction of base scale. 0 leaves scale untouched (off by default).")]
        private float shakeScaleAmount = 0f;

        [Header("Idle (minimal blob-scale breathing; positional bob off by default - skip it on Vortex, its own shader already wobbles the mesh)")]
        [SerializeField] private float idleScalePulseAmount = 0.03f;
        [SerializeField] private float idleScalePulseFrequency = 1.5f;
        [SerializeField] private float idleBobAmount = 0f;
        [SerializeField] private float idleBobFrequency = 1f;

        private Vector3 _baseLocalPosition;
        private Vector3 _baseLocalScale;
        private float _shakeSeedX, _shakeSeedY, _shakeSeedZ, _shakeSeedScale;
        private float? _previewRemaining;

        public override void Awake()
        {
            base.Awake();

            if (visual == null)
                visual = transform;

            _baseLocalPosition = visual.localPosition;
            _baseLocalScale = visual.localScale;
            _shakeSeedX = Random.value * 1000f;
            _shakeSeedY = Random.value * 1000f;
            _shakeSeedZ = Random.value * 1000f;
            _shakeSeedScale = Random.value * 1000f;
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            visual.localPosition = _baseLocalPosition;
            visual.localScale = _baseLocalScale;
        }

        [Button("Preview Despawn Sequence")]
        private void PreviewDespawnSequence()
        {
            _previewRemaining = anticipationDuration + scaleDownDuration;
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (_previewRemaining.HasValue)
            {
                _previewRemaining -= Time.deltaTime;
                if (_previewRemaining.Value <= 0f)
                {
                    _previewRemaining = null;
                    visual.localScale = _baseLocalScale;
                    visual.localPosition = _baseLocalPosition;
                    return;
                }

                Apply(_previewRemaining.Value);
                return;
            }

            var frame = game.Frames.Predicted;
            if (frame.TryGet<DestroyAfterTime>(_entityRef, out DestroyAfterTime lifetime) == false)
            {
                ApplyIdle();
                return;
            }

            Apply(lifetime.RemainingTime.AsFloat);
        }

        private void Apply(float remaining)
        {
            if (remaining <= scaleDownDuration)
            {
                float t = scaleDownDuration > 0f ? Mathf.Clamp01(remaining / scaleDownDuration) : 0f;
                float eased = Easing.Evaluate(t, scaleDownEase);
                visual.localScale = _baseLocalScale * eased;
                visual.localPosition = _baseLocalPosition;
            }
            else if (remaining <= scaleDownDuration + anticipationDuration)
            {
                ApplyShake();
            }
            else
            {
                ApplyIdle();
            }
        }

        private void ApplyShake()
        {
            float t = Time.time * shakeFrequency;
            visual.localPosition = _baseLocalPosition + new Vector3(
                (Mathf.PerlinNoise(_shakeSeedX, t) - 0.5f) * 2f,
                (Mathf.PerlinNoise(_shakeSeedY, t) - 0.5f) * 2f,
                (Mathf.PerlinNoise(_shakeSeedZ, t) - 0.5f) * 2f) * shakeAmount;

            float scaleWobble = 1f + (Mathf.PerlinNoise(_shakeSeedScale, t) - 0.5f) * 2f * shakeScaleAmount;
            visual.localScale = _baseLocalScale * scaleWobble;
        }

        private void ApplyIdle()
        {
            visual.localScale = _baseLocalScale * (1f + Mathf.Sin(Time.time * idleScalePulseFrequency * Mathf.PI * 2f) * idleScalePulseAmount);
            visual.localPosition = _baseLocalPosition + new Vector3(0f, Mathf.Sin(Time.time * idleBobFrequency * Mathf.PI * 2f) * idleBobAmount, 0f);
        }
    }
}

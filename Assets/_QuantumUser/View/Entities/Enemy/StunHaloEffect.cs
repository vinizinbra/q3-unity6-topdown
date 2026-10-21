using UnityEngine;

namespace Quantum
{
    // Stun's ambient "seeing stars" halo - lives on the root of StatusEffectsManager.stunParticlePrefab.
    // The root ParticleSystem never emits on its own (emission module ENABLED at rate 0 - a disabled
    // module makes isEmitting read false even while playing, which LateUpdate takes as "released" and
    // pops every star on the spot; Awake forces it on). This drives exactly starCount
    // particles by hand every frame (SetParticles) so each star can be placed on the orbit and popped
    // individually: star i stays up while the stun's remaining fraction is above i/starCount, so the
    // halo counts the stun down one star at a time. StatusEffectsManager feeds
    // StatusEffects.StunRemaining each frame (SetRemainingSeconds); the fraction is taken against the
    // longest value seen since this instance was enabled, so a re-stun that extends it re-grows the
    // missing stars.
    //
    // Everything is in the root's local space (simulation space Local, scaling mode Hierarchy), so
    // the tracker's per-entity transform/scale positions and sizes the whole halo.
    [RequireComponent(typeof(ParticleSystem))]
    public class StunHaloEffect : MonoBehaviour
    {
        [SerializeField, Range(1, 6)] private int starCount = 3;
        [SerializeField, Tooltip("Orbit radius, local units (prefab reference diameter 1).")]
        private float orbitRadius = 0.45f;
        [SerializeField, Tooltip("Front-to-back radius as a fraction of orbitRadius. The 45deg gameplay camera stretches a round flat ring vertically - the front star would dip into the body - so a squashed ellipse reads as a tight halo instead.")]
        private float orbitDepth = 0.35f;
        [SerializeField, Tooltip("Ring tilt toward the camera (degrees) - 0 = flat on the ground plane.")]
        private float orbitTilt = 0f;
        [SerializeField, Tooltip("Revolutions per second.")]
        private float orbitSpeed = 0.7f;
        [SerializeField] private float starSize = 0.32f;
        [SerializeField, Tooltip("Each star bobs up/down by this much, phase-offset per star.")]
        private float bobAmount = 0.05f;
        [SerializeField] private float spinSpeed = 180f;

        [Header("Bonk")]
        [SerializeField, Tooltip("Child star-burst (t=0 burst, no loop) played the moment a stun lands - by the root's own Play() on acquisition, and again from here whenever a re-stun pushes the remaining time back up. Rides alongside HitFeedback's Jolt punch-scale and EffectsManager's Jolt spark. Optional.")]
        private ParticleSystem bonkBurst;

        [Header("Pop")]
        [SerializeField, Tooltip("Played at a star's position when it pops (one burst, local space). Optional.")]
        private ParticleSystem popBurst;
        [SerializeField] private int popBurstCount = 6;
        [SerializeField, Tooltip("Seconds a star takes to swell then vanish when it pops.")]
        private float popDuration = 0.18f;
        [SerializeField] private float popSwell = 1.6f;
        [SerializeField, Tooltip("Seconds a (re)appearing star takes to grow in.")]
        private float growDuration = 0.15f;

        private ParticleSystem _system;
        private ParticleSystem.Particle[] _particles;
        private float[] _starScale;   // 0..1 grow-in, then held at 1
        private float[] _popTimer;    // > 0 while popping
        private bool[] _alive;
        private float _remaining = 1f;
        private float _peakSeconds;
        private float _lastSeconds;
        private bool _bonkPending;
        private float _angle;
        private Color32 _color;

        private void Awake()
        {
            _system = GetComponent<ParticleSystem>();
            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            _color = _system.main.startColor.color;
            _particles = new ParticleSystem.Particle[starCount];
            _starScale = new float[starCount];
            _popTimer = new float[starCount];
            _alive = new bool[starCount];
        }

        // Pooled instance - every reuse starts with a full, freshly grown halo.
        private void OnEnable()
        {
            _remaining = 1f;
            _peakSeconds = 0f;
            _lastSeconds = 0f;
            // The first bonk needs nothing from us - the tracker's Play() on this root also plays
            // the bonk child's t=0 burst.
            _bonkPending = false;
            _angle = Random.value * 360f;
            for (var i = 0; i < starCount; i++)
            {
                _alive[i] = true;
                _starScale[i] = 0f;
                _popTimer[i] = 0f;
            }
        }

        public void SetRemainingSeconds(float seconds)
        {
            // A re-stun refreshes StunRemaining upward - bonk again and restart the countdown from it.
            if (_lastSeconds > 0f && seconds > _lastSeconds + 0.05f)
            {
                _bonkPending = true;
                _peakSeconds = seconds;
            }

            _lastSeconds = seconds;
            _peakSeconds = Mathf.Max(_peakSeconds, seconds);
            _remaining = _peakSeconds > 0f ? Mathf.Clamp01(seconds / _peakSeconds) : 0f;
        }

        private void LateUpdate()
        {
            if (_system.isEmitting == false)
            {
                // Released by the tracker (status ended) - let the stars shrink out instead of
                // snapping off, then stop feeding particles so IsAlive() goes false and the pool
                // reclaims the instance.
                _remaining = 0f;
            }

            // Re-stun bonk. Stop-then-Play restarts the burst even if the previous one is still
            // playing; StopEmitting leaves its live particles to finish.
            if (_bonkPending == true && _system.isEmitting == true)
            {
                _bonkPending = false;
                if (bonkBurst != null)
                {
                    bonkBurst.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    bonkBurst.Play(true);
                }
            }

            float dt = Time.deltaTime;
            _angle += orbitSpeed * 360f * dt;
            Quaternion tilt = Quaternion.Euler(-orbitTilt, 0f, 0f);
            int count = 0;

            for (var i = 0; i < starCount; i++)
            {
                // Highest index pops first, so the halo empties in a steady direction.
                bool shouldBeAlive = _remaining > (float)i / starCount;

                if (_alive[i] == true && shouldBeAlive == false)
                {
                    _alive[i] = false;
                    _popTimer[i] = popDuration;
                    EmitPop(StarPosition(i, tilt));
                }
                else if (_alive[i] == false && shouldBeAlive == true && _popTimer[i] <= 0f)
                {
                    _alive[i] = true;
                    _starScale[i] = 0f;
                }

                float scale;
                if (_alive[i] == true)
                {
                    _starScale[i] = growDuration > 0f ? Mathf.MoveTowards(_starScale[i], 1f, dt / growDuration) : 1f;
                    scale = EaseOutBack(_starScale[i]);
                }
                else if (_popTimer[i] > 0f)
                {
                    _popTimer[i] -= dt;
                    float t = 1f - Mathf.Clamp01(_popTimer[i] / popDuration);
                    // Swell to popSwell over the first 40%, then collapse to nothing.
                    scale = t < 0.4f ? Mathf.Lerp(1f, popSwell, t / 0.4f) : Mathf.Lerp(popSwell, 0f, (t - 0.4f) / 0.6f);
                }
                else
                    continue;

                ref ParticleSystem.Particle p = ref _particles[count++];
                p.position = StarPosition(i, tilt);
                p.startSize = starSize * scale;
                p.rotation = _angle * spinSpeed / 360f + i * 40f;
                p.startColor = _color;
                p.startLifetime = p.remainingLifetime = 1f;
                p.velocity = Vector3.zero;
            }

            _system.SetParticles(_particles, count);
        }

        private Vector3 StarPosition(int i, Quaternion tilt)
        {
            float a = (_angle + i * 360f / starCount) * Mathf.Deg2Rad;
            float bob = Mathf.Sin(a * 2f) * bobAmount;
            return tilt * new Vector3(Mathf.Cos(a) * orbitRadius, bob, Mathf.Sin(a) * orbitRadius * orbitDepth);
        }

        private void EmitPop(Vector3 localPosition)
        {
            if (popBurst == null)
                return;

            var emit = new ParticleSystem.EmitParams { position = localPosition, applyShapeToPosition = true };
            popBurst.Emit(emit, popBurstCount);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}

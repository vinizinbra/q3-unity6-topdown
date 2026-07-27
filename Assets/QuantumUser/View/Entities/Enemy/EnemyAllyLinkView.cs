using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Draws a line from a support-type enemy (Shielder, FlyingShielder) to whichever ally its
    // current action is targeting - Enemy.Target only ever resolves to another Enemy for an
    // ally-targeting policy (HighestHealthAllyInRangeTargetingData/LowestHealthAllyTargetingData),
    // so this naturally never lights up for a normal player-targeting attacker. Hidden only for
    // Idle/Dead rather than gating on a specific delivery - covers both an instant-resolving hit
    // effect and a WaitForImpact projectile still in flight during Active, with no need to know
    // which one is in play. Chasing stays visible too (not just Preparation-through-Recovery) -
    // EnemySystem routes Recovery back through a single Chasing tick before re-Preparing its next
    // action on the same still-valid ally (see EnemySystem.UpdateRecovery's leash-range check), and
    // hiding on Chasing made the link blink off for that one tick every time the action looped.
    [RequireComponent(typeof(LineRenderer))]
    public class EnemyAllyLinkView : CustomQuantumEntityViewComponent
    {
        [Header("Line")]
        [SerializeField] private Color linkColor = new Color(0.4f, 1f, 0.4f, 0.7f);
        [SerializeField] private float width = 0.05f;

        [Header("Wobble")]
        [SerializeField, Tooltip("Points along the line, excluding the two anchored ends - higher = smoother wobble, more expensive.")]
        private int segments = 16;
        [SerializeField, Tooltip("Max perpendicular offset at the midpoint - tapers to zero at both ends so the line still meets the two particles exactly, same as a Zelda Magnesis tether rather than a straight beam.")]
        private float wobbleAmplitude = 0.3f;
        [SerializeField, Tooltip("How fast the wobble noise scrolls over time - higher = more frantic, lower = a slow lazy drift.")]
        private float wobbleSpeed = 2f;
        [SerializeField, Tooltip("How many wobble bumps fit along the line's length - higher = tighter wiggles.")]
        private float wobbleFrequency = 3f;

        [Header("Endpoint Particles")]
        [SerializeField, Tooltip("Looping prefab pulled from EffectsManager's pool for the caster's own collider center, scaled to match its collider radius. Must not auto-destroy/one-shot itself - lifetime is owned by this component via GetHeldInstance/ReleaseHeldInstance, not EffectsManager.PlayEffect's fire-and-forget shape.")]
        private ParticleSystem originParticlePrefab;
        [SerializeField, Tooltip("Same as originParticlePrefab, but repositioned to the ally target's collider center every frame instead.")]
        private ParticleSystem targetParticlePrefab;

        private LineRenderer _line;
        private ParticleSystem _originInstance;
        private ParticleSystem _targetInstance;
        private float _noiseSeed;
        private bool _active;

        public override void Awake()
        {
            base.Awake();
            _line = GetComponent<LineRenderer>();
            // Offsets each instance's noise sample so multiple simultaneous links don't wobble in
            // lockstep - purely cosmetic (View-side), so plain UnityEngine.Random is fine here.
            _noiseSeed = Random.value * 1000f;
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            _line.useWorldSpace = true;
            _line.startColor = linkColor;
            _line.endColor = linkColor;
            _line.startWidth = width;
            _line.endWidth = width;

            _active = false;
            _line.enabled = false;
        }

        protected override void QUpdate(QuantumGame game)
        {
            Frame frame = game.Frames.Predicted;

            if (TryResolveAllyTarget(frame, out EntityRef target) == false)
            {
                SetActive(false);
                return;
            }

            SetActive(true);

            Vector3 selfCenter = EnemyMovementUtility.ResolveEntityCenter(frame, _entityRef).ToUnityVector3();
            Vector3 targetCenter = EnemyMovementUtility.ResolveEntityCenter(frame, target).ToUnityVector3();

            UpdateWobblingLine(selfCenter, targetCenter);

            if (_originInstance == null || _targetInstance == null)
                return; // no EffectsManager (or a prefab wasn't assigned) - line still draws on its own

            float selfRadius = EnemyMovementUtility.ResolveEntityRadius(frame, _entityRef).AsFloat;
            float targetRadius = EnemyMovementUtility.ResolveEntityRadius(frame, target).AsFloat;

            _originInstance.transform.SetPositionAndRotation(selfCenter, Quaternion.identity);
            _targetInstance.transform.SetPositionAndRotation(targetCenter, Quaternion.identity);
            _originInstance.transform.localScale = Vector3.one * selfRadius;
            _targetInstance.transform.localScale = Vector3.one * targetRadius;
        }

        private void UpdateWobblingLine(Vector3 start, Vector3 end)
        {
            if (_line.positionCount != segments + 1)
                _line.positionCount = segments + 1;

            Vector3 direction = end - start;
            float length = direction.magnitude;
            Vector3 forward = length > 0.0001f ? direction / length : Vector3.forward;

            // Two perpendicular axes rather than one, so the wobble isn't confined to a single flat
            // plane (reads as a chaotic tether, not a 2D sine wave).
            Vector3 perpA = Vector3.Cross(forward, Vector3.up);
            if (perpA.sqrMagnitude < 0.0001f)
                perpA = Vector3.Cross(forward, Vector3.right);
            perpA.Normalize();
            Vector3 perpB = Vector3.Cross(forward, perpA);

            float time = Time.time * wobbleSpeed;

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 basePoint = Vector3.Lerp(start, end, t);

                // Zero at both ends (t=0/1), max at the midpoint - keeps the beam anchored exactly
                // on the two particles no matter how wide it wobbles in between.
                float falloff = Mathf.Sin(t * Mathf.PI);

                float noiseA = Mathf.PerlinNoise(_noiseSeed + t * wobbleFrequency, time) - 0.5f;
                float noiseB = Mathf.PerlinNoise(_noiseSeed + 100f + t * wobbleFrequency, time) - 0.5f;

                Vector3 offset = (perpA * noiseA + perpB * noiseB) * (wobbleAmplitude * falloff * 2f);
                _line.SetPosition(i, basePoint + offset);
            }
        }

        private void SetActive(bool active)
        {
            if (_active == active)
                return;

            _active = active;
            _line.enabled = active;

            if (active)
            {
                if (EffectsManager.Instance != null)
                {
                    _originInstance = EffectsManager.Instance.GetHeldInstance(originParticlePrefab);
                    _targetInstance = EffectsManager.Instance.GetHeldInstance(targetParticlePrefab);
                    _originInstance?.Play();
                    _targetInstance?.Play();
                }
            }
            else
            {
                if (EffectsManager.Instance != null)
                {
                    EffectsManager.Instance.ReleaseHeldInstance(originParticlePrefab, _originInstance);
                    EffectsManager.Instance.ReleaseHeldInstance(targetParticlePrefab, _targetInstance);
                }

                _originInstance = null;
                _targetInstance = null;
            }
        }

        private bool TryResolveAllyTarget(Frame frame, out EntityRef target)
        {
            target = default;

            if (frame == null || frame.Has<Enemy>(_entityRef) == false)
                return false;

            Enemy enemy = frame.Get<Enemy>(_entityRef);

            if (enemy.Phase is EnemyActionPhase.Idle or EnemyActionPhase.Dead)
                return false;

            if (enemy.Target == EntityRef.None || frame.Has<Enemy>(enemy.Target) == false)
                return false;

            if (frame.Has<Transform3D>(enemy.Target) == false)
                return false;

            target = enemy.Target;
            return true;
        }
    }
}

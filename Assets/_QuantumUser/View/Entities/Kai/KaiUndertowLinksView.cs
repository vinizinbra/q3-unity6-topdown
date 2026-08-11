using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Kai-anchored counterpart to a per-enemy link view - draws a tether between every enemy
    // currently pulled by Kai's own Undertow ascension and whichever other enemy it's being dragged
    // toward (UndertowPull.LinkTarget - see Heroes/Kai/Undertow.qtn). Lives on Kai's own prefab
    // instead of on every enemy prototype, using a small FIXED pool of pre-authored child
    // LineRenderers (assign them in the Inspector) rather than one component per enemy - Undertow can
    // affect several enemies at once (each hit lands its own independent pull), so this scans ALL live
    // UndertowPull instances every frame and hands out slots find-or-assign, same "cap concurrent
    // count, evict nothing" idiom StatusEffects.HasteRemaining's own fixed array already uses. A link
    // beyond the slot count simply isn't drawn (a visual cap only, not a gameplay one).
    //
    // UndertowPull carries no owner/source field - Undertow is the only thing that ever creates it, so
    // no ownership check is needed to know "these links are mine." In the rare case of two Kais
    // simultaneously in co-op, each Kai's own instance of this view redraws the same links
    // independently - a harmless cosmetic doubling, not a gameplay bug.
    public class KaiUndertowLinksView : CustomQuantumEntityViewComponent
    {
        [Header("Line Slots")]
        [SerializeField, Tooltip("Pre-authored child LineRenderers, one per simultaneous Undertow link this can show at once (e.g. 4). Leave an element unassigned to just lower the concurrent cap.")]
        private LineRenderer[] lines = new LineRenderer[4];

        [Header("Line Style")]
        [SerializeField] private Color linkColor = new Color(0.6f, 0.4f, 1f, 0.8f);
        [SerializeField] private float width = 0.04f;

        [Header("Wobble")]
        [SerializeField, Tooltip("Points along the line, excluding the two anchored ends.")]
        private int segments = 10;
        [SerializeField, Tooltip("Max perpendicular offset at the midpoint - tapers to zero at both ends. Kept small/fast given how short-lived each link is (~0.2s at rank 1).")]
        private float wobbleAmplitude = 0.15f;
        [SerializeField] private float wobbleSpeed = 6f;
        [SerializeField] private float wobbleFrequency = 4f;

        [Header("Endpoint Particles")]
        [SerializeField, Tooltip("Looping prefab pulled from EffectsManager's pool for the struck enemy's own collider center, one instance per active slot. Must not auto-destroy/one-shot itself - lifetime is owned by this component via GetHeldInstance/ReleaseHeldInstance.")]
        private ParticleSystem originParticlePrefab;
        [SerializeField, Tooltip("Same as originParticlePrefab, but repositioned to the pull target's collider center every frame instead.")]
        private ParticleSystem targetParticlePrefab;

        private EntityRef[] _slotSource;
        private ParticleSystem[] _slotOriginInstance;
        private ParticleSystem[] _slotTargetInstance;
        private float[] _noiseSeeds;

        public override void Awake()
        {
            base.Awake();

            int count = lines.Length;
            _slotSource = new EntityRef[count];
            _slotOriginInstance = new ParticleSystem[count];
            _slotTargetInstance = new ParticleSystem[count];
            _noiseSeeds = new float[count];

            for (int i = 0; i < count; i++)
            {
                _noiseSeeds[i] = Random.value * 1000f;

                if (lines[i] == null)
                    continue;

                lines[i].useWorldSpace = true;
                lines[i].startColor = linkColor;
                lines[i].endColor = linkColor;
                lines[i].startWidth = width;
                lines[i].endWidth = width;
                lines[i].enabled = false;
            }
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);

            for (int i = 0; i < lines.Length; i++)
            {
                FreeSlot(i);
            }
        }

        protected override void QUpdate(QuantumGame game)
        {
            Frame frame = game.Frames.Predicted;

            if (frame == null)
                return;

            // Free any slot whose pull has since ended (Remaining hit 0, or its LinkTarget died).
            for (int i = 0; i < lines.Length; i++)
            {
                if (_slotSource[i] == EntityRef.None || IsStillActive(frame, _slotSource[i], out _))
                    continue;

                FreeSlot(i);
            }

            var pulls = frame.Filter<UndertowPull, Transform3D>();

            while (pulls.Next(out EntityRef entity, out UndertowPull pull, out Transform3D transform))
            {
                if (pull.LinkTarget == EntityRef.None || frame.Exists(pull.LinkTarget) == false)
                    continue;

                if (frame.Has<Transform3D>(pull.LinkTarget) == false)
                    continue;

                int slot = FindOrAssignSlot(entity);

                if (slot < 0)
                    continue; // every slot already in use this frame - visual cap only

                Vector3 selfCenter = EnemyMovementUtility.ResolveEntityCenter(frame, entity).ToUnityVector3();
                Vector3 targetCenter = EnemyMovementUtility.ResolveEntityCenter(frame, pull.LinkTarget).ToUnityVector3();

                UpdateWobblingLine(slot, selfCenter, targetCenter);

                if (_slotOriginInstance[slot] != null)
                {
                    float selfRadius = EnemyMovementUtility.ResolveEntityRadius(frame, entity).AsFloat;
                    _slotOriginInstance[slot].transform.SetPositionAndRotation(selfCenter, Quaternion.identity);
                    _slotOriginInstance[slot].transform.localScale = Vector3.one * selfRadius;
                }

                if (_slotTargetInstance[slot] != null)
                {
                    float targetRadius = EnemyMovementUtility.ResolveEntityRadius(frame, pull.LinkTarget).AsFloat;
                    _slotTargetInstance[slot].transform.SetPositionAndRotation(targetCenter, Quaternion.identity);
                    _slotTargetInstance[slot].transform.localScale = Vector3.one * targetRadius;
                }
            }
        }

        private static bool IsStillActive(Frame frame, EntityRef source, out UndertowPull pull)
        {
            pull = default;

            if (frame.Has<UndertowPull>(source) == false)
                return false;

            pull = frame.Get<UndertowPull>(source);
            return pull.LinkTarget != EntityRef.None && frame.Exists(pull.LinkTarget) == true;
        }

        private int FindOrAssignSlot(EntityRef source)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (_slotSource[i] == source)
                    return i;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                if (_slotSource[i] != EntityRef.None || lines[i] == null)
                    continue;

                _slotSource[i] = source;
                lines[i].enabled = true;

                if (EffectsManager.Instance != null)
                {
                    _slotOriginInstance[i] = EffectsManager.Instance.GetHeldInstance(originParticlePrefab);
                    _slotTargetInstance[i] = EffectsManager.Instance.GetHeldInstance(targetParticlePrefab);
                    _slotOriginInstance[i]?.Play();
                    _slotTargetInstance[i]?.Play();
                }

                return i;
            }

            return -1;
        }

        private void FreeSlot(int slot)
        {
            _slotSource[slot] = EntityRef.None;

            if (lines[slot] != null)
            {
                lines[slot].enabled = false;
            }

            if (EffectsManager.Instance != null)
            {
                EffectsManager.Instance.ReleaseHeldInstance(originParticlePrefab, _slotOriginInstance[slot]);
                EffectsManager.Instance.ReleaseHeldInstance(targetParticlePrefab, _slotTargetInstance[slot]);
            }

            _slotOriginInstance[slot] = null;
            _slotTargetInstance[slot] = null;
        }

        private void UpdateWobblingLine(int slot, Vector3 start, Vector3 end)
        {
            LineRenderer line = lines[slot];

            if (line == null)
                return;

            if (line.positionCount != segments + 1)
                line.positionCount = segments + 1;

            Vector3 direction = end - start;
            float length = direction.magnitude;
            Vector3 forward = length > 0.0001f ? direction / length : Vector3.forward;

            Vector3 perpA = Vector3.Cross(forward, Vector3.up);
            if (perpA.sqrMagnitude < 0.0001f)
                perpA = Vector3.Cross(forward, Vector3.right);
            perpA.Normalize();
            Vector3 perpB = Vector3.Cross(forward, perpA);

            float time = Time.time * wobbleSpeed;
            float noiseSeed = _noiseSeeds[slot];

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 basePoint = Vector3.Lerp(start, end, t);
                float falloff = Mathf.Sin(t * Mathf.PI);

                float noiseA = Mathf.PerlinNoise(noiseSeed + t * wobbleFrequency, time) - 0.5f;
                float noiseB = Mathf.PerlinNoise(noiseSeed + 100f + t * wobbleFrequency, time) - 0.5f;

                Vector3 offset = (perpA * noiseA + perpB * noiseB) * (wobbleAmplitude * falloff * 2f);
                line.SetPosition(i, basePoint + offset);
            }
        }
    }
}

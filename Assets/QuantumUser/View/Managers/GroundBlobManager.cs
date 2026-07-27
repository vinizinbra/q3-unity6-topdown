namespace QuantumUser.View.Managers
{
    using System.Collections.Generic;
    using NaughtyAttributes;
    using Quantum;
    using UnityEngine;
    using UnityEngine.Pool;
    using UnityEngine.Serialization;

    // Pools ground-projected blob instances behind one shared GroundBlobConfig, so characters/
    // enemies (HasShadow, HasLight) don't each run their own raycast+follow MonoBehaviour - this
    // single LateUpdate loop does the raycast/placement/falloff for every active blob at once, one
    // Unity message dispatch instead of N.
    //
    // Shadows and lights are the same pooled GameObject/prefab, only tinted differently, so a
    // recolor happens immediately on Acquire - otherwise an instance previously used as a light
    // would keep that RGB (LateUpdate only ever touches alpha) when handed back out as a shadow.
    //
    // DefaultExecutionOrder pins Awake() (which sets Instance) ahead of every default-order script,
    // so scene-placed HasShadow/HasLight instances - whose OnEnable can otherwise race this Awake -
    // always see a non-null Instance.
    [DefaultExecutionOrder(-1000)]
    public class GroundBlobManager : MonoBehaviour
    {
        public static GroundBlobManager Instance;

        private static readonly Quaternion FlatRotation = Quaternion.Euler(90f, 0f, 0f);

        [Expandable] public GroundBlobConfig config;
        [FormerlySerializedAs("shadowPrefab")]
        [SerializeField, Tooltip("Must have a SpriteRenderer. Its rotation is forced flat every frame regardless of authored rotation.")]
        private GameObject blobPrefab;
        [SerializeField, Tooltip("Instances created up front so the first few blob-owners spawning don't pay an Instantiate cost. The pool still grows past this on demand.")]
        private int prewarmCount = 8;

        private ObjectPool<GameObject> pool;
        private readonly List<GroundBlobHandle> active = new List<GroundBlobHandle>();

        private void Awake()
        {
            Instance = this;

            pool = new ObjectPool<GameObject>(
                createFunc: () => Instantiate(blobPrefab, transform),
                actionOnGet: instance => instance.SetActive(true),
                actionOnRelease: instance => instance.SetActive(false),
                actionOnDestroy: instance => Destroy(instance));

            Prewarm(prewarmCount);
        }

        public GroundBlobHandle AcquireShadow(Transform target, float baseScale)
        {
            return Acquire(target, baseScale, config != null ? config.ShadowColor : Color.black, isLight: false);
        }

        public GroundBlobHandle AcquireLight(Transform target, float baseScale, Color color)
        {
            return Acquire(target, baseScale, color, isLight: true);
        }

        public void Release(GroundBlobHandle handle)
        {
            if (handle == null) return;

            active.Remove(handle);
            pool.Release(handle.GameObject);
        }

        private GroundBlobHandle Acquire(Transform target, float baseScale, Color tint, bool isLight)
        {
            if (blobPrefab == null) return null;

            var instance = pool.Get();
            var renderer = instance.GetComponent<SpriteRenderer>();

            Color color = renderer.color;
            color.r = tint.r;
            color.g = tint.g;
            color.b = tint.b;
            renderer.color = color;

            var handle = new GroundBlobHandle
            {
                GameObject = instance,
                Renderer = renderer,
                Target = target,
                BaseScale = baseScale,
                IsLight = isLight
            };
            active.Add(handle);
            return handle;
        }

        private void LateUpdate()
        {
            if (config == null) return;

            for (int i = 0; i < active.Count; i++)
                UpdateBlob(active[i]);
        }

        private void UpdateBlob(GroundBlobHandle blob)
        {
            Vector3 origin = blob.Target.position + Vector3.up * config.RaycastHeight;
            bool hasGround = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, config.RaycastHeight + config.MaxRaycastDistance, config.GroundLayer);

            blob.Renderer.enabled = hasGround;
            if (!hasGround) return; // e.g. falling past the edge of the level - no floor to project onto

            Vector3 offset = new Vector3(config.ShadowOffset.x, config.GroundOffset, config.ShadowOffset.y);
            blob.GameObject.transform.SetPositionAndRotation(hit.point + offset, FlatRotation);

            float height = Mathf.Max(0f, blob.Target.position.y - hit.point.y);
            float t = config.MaxHeightForFalloff > 0f ? Mathf.Clamp01(height / config.MaxHeightForFalloff) : 0f;
            float falloff = config.HeightFalloffCurve.Evaluate(t); // 1 = full size/alpha at ground, eases toward 0 with height

            float scale = blob.BaseScale * Mathf.Lerp(config.MinScaleMultiplier, 1f, falloff);
            blob.GameObject.transform.localScale = new Vector3(scale, scale, scale);

            float maxAlpha = blob.IsLight ? config.LightAlpha : config.GroundAlpha;
            Color color = blob.Renderer.color;
            color.a = maxAlpha * Mathf.Lerp(config.MinAlphaMultiplier, 1f, falloff);
            blob.Renderer.color = color;
        }

        private void Prewarm(int count)
        {
            if (blobPrefab == null) return;

            var buffer = new GameObject[count];
            for (int i = 0; i < count; i++)
                buffer[i] = pool.Get();
            for (int i = 0; i < count; i++)
                pool.Release(buffer[i]);
        }
    }

    public sealed class GroundBlobHandle
    {
        internal GameObject GameObject;
        internal SpriteRenderer Renderer;
        internal Transform Target;
        internal float BaseScale;
        internal bool IsLight;
    }
}

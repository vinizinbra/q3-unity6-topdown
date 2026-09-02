namespace QuantumUser.View.Managers
{
    using System.Collections.Generic;
    using NaughtyAttributes;
    using Quantum;
    using QuantumUser.View.Util;
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
        [SerializeField, Tooltip("Material used while a pooled blob is serving as a LIGHT (HasLight). Shadows keep whatever material blobPrefab itself carries - Custom/BlobShadowSpriteMultiply, a MULTIPLY blend (Blend DstColor Zero) that can only ever darken, which is why a light drawn with it read as a grey patch instead of as light. Left empty this falls back to a runtime Sprites/Default material, which is the intended default; assign one explicitly only to use an additive or custom light material instead.")]
        private Material lightMaterial;
        [SerializeField, Tooltip("Instances created up front so the first few blob-owners spawning don't pay an Instantiate cost. The pool still grows past this on demand.")]
        private int prewarmCount = 8;

        private ObjectPool<GameObject> pool;
        private readonly List<GroundBlobHandle> active = new List<GroundBlobHandle>();

        // The material blobPrefab itself was authored with, captured once at Awake. A pooled
        // instance handed out as a light has its material swapped, so it needs this back the next
        // time it comes out of the pool as a shadow - the same self-healing reason Acquire rewrites
        // the tint on every single call rather than once at creation.
        private Material shadowMaterial;

        // Only built when no lightMaterial is assigned. Sprites/Default is a built-in shader with no
        // material asset sitting in the project to drag into the Inspector, and it is already in
        // Graphics settings' Always Included Shaders, so Shader.Find resolves it in a build too.
        // DontSave since it exists purely for this session, and destroyed with the manager so
        // entering/exiting Play mode repeatedly can't leak a material per run.
        private Material runtimeLightMaterial;

        private void Awake()
        {
            Instance = this;

            CacheMaterials();

            pool = new ObjectPool<GameObject>(
                createFunc: () => Instantiate(blobPrefab, transform),
                actionOnGet: instance => instance.SetActive(true),
                actionOnRelease: instance => instance.SetActive(false),
                actionOnDestroy: instance => Destroy(instance));

            Prewarm(prewarmCount);
        }

        private void CacheMaterials()
        {
            if (blobPrefab != null)
            {
                var prefabRenderer = blobPrefab.GetComponent<SpriteRenderer>();

                if (prefabRenderer != null)
                    shadowMaterial = prefabRenderer.sharedMaterial;
            }

            if (lightMaterial != null)
                return;

            Shader spriteShader = Shader.Find("Sprites/Default");

            if (spriteShader == null)
            {
                LogHelper.Warn("GroundBlob", "Sprites/Default not found - lights fall back to the shadow's multiply material and will read as grey patches.", this);
                return;
            }

            runtimeLightMaterial = new Material(spriteShader)
            {
                name = "GroundBlobLight (runtime Sprites/Default)",
                hideFlags = HideFlags.DontSave
            };
        }

        private void OnDestroy()
        {
            if (runtimeLightMaterial != null)
                Destroy(runtimeLightMaterial);
        }

        private Material ResolveLightMaterial()
        {
            return lightMaterial != null ? lightMaterial : runtimeLightMaterial;
        }

        // offset is a per-owner world X/Z nudge stacked on top of config.ShadowOffset - e.g. to slide
        // a shadow under a character whose sprite pivot sits off to one side, when the global offset
        // can't correct just that one character. Defaults to zero, so existing callers are unchanged.
        // alphaMultiplier multiplies config.GroundAlpha for this shadow only, after height falloff -
        // 1 (default) reproduces the exact pre-existing behavior.
        public GroundBlobHandle AcquireShadow(Transform target, float baseScale, Vector2 offset = default, float alphaMultiplier = 1f)
        {
            return Acquire(target, baseScale, config != null ? config.ShadowColor : Color.black, isLight: false, offset, alphaMultiplier);
        }

        public GroundBlobHandle AcquireLight(Transform target, float baseScale, Color color)
        {
            return Acquire(target, baseScale, color, isLight: true, offset: default, alphaMultiplier: 1f);
        }

        public void Release(GroundBlobHandle handle)
        {
            if (handle == null) return;

            active.Remove(handle);
            pool.Release(handle.GameObject);
        }

        private GroundBlobHandle Acquire(Transform target, float baseScale, Color tint, bool isLight, Vector2 offset, float alphaMultiplier)
        {
            if (blobPrefab == null) return null;

            var instance = pool.Get();
            var renderer = instance.GetComponent<SpriteRenderer>();

            // Swapped on every Acquire for the same reason the tint below is: instances are pooled
            // and shared between the two kinds, so one last handed out as a light must get the
            // shadow material back when it next serves as a shadow. sharedMaterial, never .material -
            // the latter instances a private copy per renderer, which both leaks and breaks batching
            // across every blob on screen.
            Material material = isLight ? ResolveLightMaterial() : shadowMaterial;

            if (material != null && renderer.sharedMaterial != material)
                renderer.sharedMaterial = material;

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
                IsLight = isLight,
                Offset = offset,
                AlphaMultiplier = alphaMultiplier
            };
            active.Add(handle);
            return handle;
        }

        private void LateUpdate()
        {
            if (config == null) return;

            // Backward so a stale entry can be removed in place without disturbing the indices of
            // whatever's still ahead of it in the list.
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (!UpdateBlob(active[i]))
                {
                    // Target was destroyed without the owning HasShadow's OnDisable ever running to
                    // Release it (e.g. the owning GameObject was destroyed directly). Reclaim the
                    // pooled instance and drop the handle here instead of leaving a dead entry that
                    // would otherwise throw on Target.position every frame from now on and, left
                    // unguarded, permanently stop every blob queued after it from updating.
                    pool.Release(active[i].GameObject);
                    active.RemoveAt(i);
                }
            }
        }

        private bool UpdateBlob(GroundBlobHandle blob)
        {
            if (blob.Target == null) return false;

            Vector3 origin = blob.Target.position + Vector3.up * config.RaycastHeight;
            bool hasGround = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, config.RaycastHeight + config.MaxRaycastDistance, config.GroundLayer);

            blob.Renderer.enabled = hasGround;
            if (!hasGround) return true; // e.g. falling past the edge of the level - no floor to project onto

            Vector3 offset = new Vector3(config.ShadowOffset.x + blob.Offset.x, config.GroundOffset, config.ShadowOffset.y + blob.Offset.y);
            blob.GameObject.transform.SetPositionAndRotation(hit.point + offset, FlatRotation);

            float height = Mathf.Max(0f, blob.Target.position.y - hit.point.y);
            float t = config.MaxHeightForFalloff > 0f ? Mathf.Clamp01(height / config.MaxHeightForFalloff) : 0f;
            float falloff = config.HeightFalloffCurve.Evaluate(t); // 1 = full size/alpha at ground, eases toward 0 with height

            // BaseScale alone only tracks the target's Quantum-side footprint (radius) - it never
            // reflects any scale the target's own Transform ends up with (e.g. a future visual
            // size-variance/boss-scale feature), so lossyScale.x is folded in too (uniform-scale
            // assumption - same convention EnemyAllyLinkView/TelegraphGrow already use). Shadow-only
            // ShadowScaleMultiplier is a global balance knob on top of that, skipped for lights.
            float lossyScale = blob.Target.lossyScale.x;
            float shadowMultiplier = blob.IsLight ? 1f : config.ShadowScaleMultiplier;
            float scale = blob.BaseScale * lossyScale * shadowMultiplier * Mathf.Lerp(config.MinScaleMultiplier, 1f, falloff);
            blob.GameObject.transform.localScale = new Vector3(scale, scale, scale);

            float maxAlpha = blob.IsLight ? config.LightAlpha : config.GroundAlpha;
            Color color = blob.Renderer.color;
            color.a = maxAlpha * Mathf.Lerp(config.MinAlphaMultiplier, 1f, falloff) * blob.AlphaMultiplier;
            blob.Renderer.color = color;

            return true;
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
        internal Vector2 Offset;
        internal float AlphaMultiplier = 1f;
    }
}

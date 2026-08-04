using System;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.Pool;

// Pools rectangular ground-footprint shadows for static buildings/props behind one shared
// BuildingShadowConfig, mirroring GroundBlobManager/HasShadow but for box footprints instead of
// circular blobs. One-shot rather than per-frame: buildings don't move once placed, so the ground
// check + sizing runs once on Acquire instead of every LateUpdate.
//
// Shadows stay parented under THIS manager, exactly like GroundBlobManager - never under the owning
// building - so they never inherit that building's (possibly non-uniform) scale in the first place.
// That's what keeps the sizing trivial: SpriteRenderer.size is written in plain world units with no
// lossyScale cancellation needed, and FlatRotation is forced in world space to tilt the sprite flat
// onto the ground (like a GroundDecal), independent of whatever the building's transform is doing.
[DefaultExecutionOrder(-1000)]
public class BuildingShadowManager : MonoBehaviour
{
    public static BuildingShadowManager Instance;

    private static readonly Quaternion FlatRotation = Quaternion.Euler(90f, 0f, 0f);

    [Expandable] public BuildingShadowConfig config;
    [SerializeField, Tooltip("Must have a SpriteRenderer (drawMode = Sliced).")]
    private GameObject shadowPrefab;
    [SerializeField, Tooltip("Instances created up front so the first few shadow-owners spawning don't pay an Instantiate cost. The pool still grows past this on demand.")]
    private int prewarmCount = 8;

    private ObjectPool<GameObject> pool;

    private void Awake()
    {
        Instance = this;

        pool = new ObjectPool<GameObject>(
            createFunc: () => Instantiate(shadowPrefab, transform),
            actionOnGet: instance => instance.SetActive(true),
            actionOnRelease: instance => instance.SetActive(false),
            actionOnDestroy: instance => Destroy(instance));

        Prewarm(prewarmCount);
    }

    public BuildingShadowHandle Acquire(Renderer footprintRenderer)
    {
        if (shadowPrefab == null)
        {
            LogHelper.Warn("BuildingShadow", "shadowPrefab is not assigned.", this);
            return null;
        }

        if (config == null)
        {
            LogHelper.Warn("BuildingShadow", "config is not assigned.", this);
            return null;
        }

        if (footprintRenderer == null)
        {
            LogHelper.Warn("BuildingShadow", "Acquire called with a null footprintRenderer.", this);
            return null;
        }

        if (TryGetFlatGroundHeight(footprintRenderer, out float groundY) == false)
            return null; // reason already logged by TryGetFlatGroundHeight

        GameObject instance = pool.Get();
        Transform instanceTransform = instance.transform;

        Bounds bounds = footprintRenderer.bounds;
        instanceTransform.SetPositionAndRotation(
            new Vector3(bounds.center.x, groundY + config.GroundOffset, bounds.center.z),
            FlatRotation);
        instanceTransform.localScale = Vector3.one;

        SpriteRenderer renderer = instance.GetComponent<SpriteRenderer>();
        if (renderer == null)
            LogHelper.Warn("BuildingShadow", "shadowPrefab has no SpriteRenderer.", this);
        else
        {
            renderer.size = new Vector2(bounds.size.x + config.ShadowPadding, bounds.size.z + config.ShadowPadding);
            renderer.enabled = true;
        }

        LogHelper.Log("BuildingShadow", $"shadow acquired for {footprintRenderer.name} at {instanceTransform.position}, size {renderer?.size}.", footprintRenderer);
        return new BuildingShadowHandle { GameObject = instance };
    }

    public void Release(BuildingShadowHandle handle)
    {
        if (handle == null) return;

        pool.Release(handle.GameObject);
    }

    private bool TryGetFlatGroundHeight(Renderer footprintRenderer, out float groundY)
    {
        groundY = 0f;

        Bounds bounds = footprintRenderer.bounds;
        Vector3 center = bounds.center;
        float halfX = bounds.extents.x + config.EdgeMargin;
        float halfZ = bounds.extents.z + config.EdgeMargin;

        Span<Vector3> corners = stackalloc Vector3[4]
        {
            center + new Vector3(halfX, 0f, halfZ),
            center + new Vector3(halfX, 0f, -halfZ),
            center + new Vector3(-halfX, 0f, halfZ),
            center + new Vector3(-halfX, 0f, -halfZ),
        };

        float minY = float.MaxValue;
        float maxY = float.MinValue;

        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 origin = corners[i] + Vector3.up * config.RaycastHeight;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, config.RaycastHeight + config.MaxRaycastDistance, config.GroundLayer) == false)
            {
                LogHelper.Log("BuildingShadow", $"corner {i} raycast missed from {origin} (groundLayer={config.GroundLayer.value}, distance={config.RaycastHeight + config.MaxRaycastDistance}).", footprintRenderer);
                return false;
            }

            minY = Mathf.Min(minY, hit.point.y);
            maxY = Mathf.Max(maxY, hit.point.y);
        }

        if (maxY - minY > config.FlatnessTolerance)
        {
            LogHelper.Log("BuildingShadow", $"ground not flat for {footprintRenderer.name} - corner heights span {maxY - minY:F3} (tolerance {config.FlatnessTolerance}).", footprintRenderer);
            return false;
        }

        groundY = (minY + maxY) * 0.5f;
        return true;
    }

    private void Prewarm(int count)
    {
        if (shadowPrefab == null) return;

        var buffer = new GameObject[count];
        for (int i = 0; i < count; i++)
            buffer[i] = pool.Get();
        for (int i = 0; i < count; i++)
            pool.Release(buffer[i]);
    }
}

public sealed class BuildingShadowHandle
{
    internal GameObject GameObject;
}

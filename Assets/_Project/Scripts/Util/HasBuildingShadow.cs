using System.Collections;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Marker + request point for a pooled building shadow: acquires one from BuildingShadowManager on
// enable, releases it back on disable. OnEnable/OnDisable rather than Awake/OnDestroy so this stays
// correct if the owning prefab is itself pooled (SetActive reuse) instead of actually
// destroyed/reinstantiated.
//
// Acquire can fail for a reason that has nothing to do with this building: the level's ground
// geometry may not have finished spawning yet when this OnEnable fires, so the flatness raycasts
// find nothing under any corner. Retrying a few times a second apart gives level spawning a chance
// to catch up before giving up on the shadow entirely; a single successful attempt stops the retries.
//
// A shadow can also be BAKED instead ("Bake Shadow Into Prefab") - a real child GameObject saved
// into the prefab, positioned/sized by the exact same config math the pool uses but then free to be
// hand-tweaked, for the cases the automatic runtime path can't serve (a building on sloped or
// non-flat ground, an offset silhouette, a footprint that just reads wrong). A baked shadow fully
// replaces the pooled one - once `bakedShadow` is assigned, this never asks the manager for
// anything, so the two can't ever double up.
public class HasBuildingShadow : MonoBehaviour
{
    [SerializeField, Tooltip("Renderer whose world-space bounds define this object's footprint (X/Z) and true center - normally this GameObject's own Renderer (auto-filled by Reset).")]
    private Renderer footprintRenderer;

    [SerializeField, Tooltip("How many times to retry acquiring a shadow if the ground isn't ready yet (e.g. level still spawning) before giving up.")]
    private int maxAcquireAttempts = 5;
    [SerializeField, Tooltip("Delay between acquire retries, in seconds.")]
    private float acquireRetryInterval = 1f;

    [Header("Baked Shadow")]
    [SerializeField, Tooltip("A hand-tweakable shadow saved into this prefab. Assigned by \"Bake Shadow Into Prefab\"; while set, no pooled shadow is ever requested at runtime.")]
    private GameObject bakedShadow;

    [SerializeField, Tooltip("Optional. Only needed when baking with no BuildingShadowManager in the open scene (e.g. in Prefab Mode) - normally left empty, and the manager's own shadowPrefab is used.")]
    private GameObject bakeShadowPrefabOverride;

    private BuildingShadowHandle handle;
    private Coroutine acquireRoutine;

    private void Reset()
    {
        footprintRenderer = GetComponent<Renderer>();
    }

    private void OnEnable()
    {
        if (bakedShadow != null) return; // baked shadow replaces the pooled one entirely

        acquireRoutine = StartCoroutine(AcquireWithRetries());
    }

    private void OnDisable()
    {
        if (acquireRoutine != null)
        {
            StopCoroutine(acquireRoutine);
            acquireRoutine = null;
        }

        if (handle == null) return;

        if (BuildingShadowManager.Instance != null)
            BuildingShadowManager.Instance.Release(handle);
        handle = null;
    }

    [Button("Try Get Shadow")]
    private void TryAcquireShadowNow()
    {
        if (bakedShadow != null)
        {
            LogHelper.Warn("BuildingShadow", $"{name} already has a baked shadow - clear it first if you want a pooled one instead.", this);
            return;
        }

        if (BuildingShadowManager.Instance == null)
        {
            LogHelper.Warn("BuildingShadow", "no BuildingShadowManager.Instance in the scene (Play mode only).", this);
            return;
        }

        BuildingShadowHandle result = BuildingShadowManager.Instance.Acquire(footprintRenderer);
        if (result == null)
        {
            LogHelper.Log("BuildingShadow", $"acquire attempt failed for {name} - see the BuildingShadowManager log above for the reason.", this);
            return;
        }

        if (handle != null)
            BuildingShadowManager.Instance.Release(handle);

        handle = result;
        LogHelper.Log("BuildingShadow", $"shadow acquired for {name}.", this);
    }

#if UNITY_EDITOR
    // Editor-only. Instantiates a real, persistent copy of the shadow prefab as a child of this
    // building, placed/rotated/sized by the exact same BuildingShadowConfig math the runtime pool
    // uses, then leaves it there to be hand-tweaked. Parenting is what differs from the pooled path:
    // a pooled shadow lives under the manager specifically so it never inherits the building's
    // (possibly non-uniform) scale, whereas a baked one HAS to be a child to be saved into the
    // prefab - so localScale is set to cancel the parent's lossyScale back out to 1, keeping
    // SpriteRenderer.size in plain world units exactly as the manager assumes.
    [Button("Bake Shadow Into Prefab")]
    private void BakeShadowIntoPrefab()
    {
        if (footprintRenderer == null)
            footprintRenderer = GetComponent<Renderer>();

        // Building meshes very often sit on a child, not the root - Reset() only ever looks at the
        // root, so without this the button would silently no-op on exactly those prefabs.
        if (footprintRenderer == null)
            footprintRenderer = GetComponentInChildren<Renderer>();

        if (footprintRenderer == null)
        {
            LogHelper.Warn("BuildingShadow", $"{name} has no footprintRenderer assigned and no Renderer anywhere under it - nothing to size a shadow from.", this);
            return;
        }

        BuildingShadowConfig config = ResolveBakeConfig();
        if (config == null)
        {
            LogHelper.Warn("BuildingShadow", "no BuildingShadowConfig found - assign one on a BuildingShadowManager in the open scene, or create one (Shadows/Building Shadow Config).", this);
            return;
        }

        GameObject prefab = ResolveBakeShadowPrefab();
        if (prefab == null)
        {
            LogHelper.Warn("BuildingShadow", "no shadow prefab found - open a scene containing a BuildingShadowManager, or assign bakeShadowPrefabOverride.", this);
            return;
        }

        ClearBakedShadow();

        // Editor-mode colliders can hold stale transforms until a physics step runs, which would
        // make the corner raycasts miss ground that is genuinely there.
        Physics.SyncTransforms();

        Bounds bounds = footprintRenderer.bounds;

        // Falls back to the footprint's own underside when the ground check fails - in Prefab Mode
        // there IS no ground to hit, and on sloped ground the flatness check is exactly what sends a
        // building down the bake path in the first place. Either way the point of baking is that the
        // result gets hand-corrected, so a sane starting height beats refusing to bake.
        if (BuildingShadowManager.TryGetFlatGroundHeight(footprintRenderer, config, out float groundY) == false)
        {
            groundY = bounds.min.y;
            LogHelper.Log("BuildingShadow", $"no flat ground found under {name} - baking at the footprint's underside (y={groundY:F3}) instead; nudge it by hand.", this);
        }

        // InstantiatePrefab keeps the prefab link (so shadow-prefab edits still propagate into every
        // baked copy); it only works on a prefab ASSET, so a scene object falls back to a plain copy.
        GameObject instance = PrefabUtility.IsPartOfPrefabAsset(prefab)
            ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, transform)
            : Instantiate(prefab, transform);

        instance.name = "BuildingShadow (Baked)";
        Undo.RegisterCreatedObjectUndo(instance, "Bake Building Shadow");

        Transform instanceTransform = instance.transform;
        instanceTransform.SetPositionAndRotation(
            BuildingShadowManager.ResolveShadowPosition(bounds, groundY, config),
            BuildingShadowManager.ShadowRotation);

        // Cancel the parent chain's scale so the shadow ends up at world scale 1, like a pooled one.
        // MEASURED, not derived from transform.lossyScale directly: the shadow is rotated 90 degrees
        // against its parent, so the parent's scale axes don't line up with the child's own. Reading
        // back the child's resulting lossyScale and inverting that is what makes a non-uniformly
        // scaled building (very common on props) produce a correctly-proportioned shadow instead of a
        // stretched one.
        instanceTransform.localScale = Vector3.one;
        Vector3 lossy = instanceTransform.lossyScale;
        instanceTransform.localScale = new Vector3(
            Mathf.Approximately(lossy.x, 0f) ? 1f : 1f / lossy.x,
            Mathf.Approximately(lossy.y, 0f) ? 1f : 1f / lossy.y,
            Mathf.Approximately(lossy.z, 0f) ? 1f : 1f / lossy.z);

        var renderer = instance.GetComponent<SpriteRenderer>();
        if (renderer == null)
            LogHelper.Warn("BuildingShadow", "shadow prefab has no SpriteRenderer - size was not applied.", this);
        else
        {
            renderer.size = BuildingShadowManager.ResolveShadowSize(bounds, config);
            renderer.enabled = true;
        }

        Undo.RecordObject(this, "Bake Building Shadow");
        bakedShadow = instance;
        PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        EditorUtility.SetDirty(this);

        LogHelper.Log("BuildingShadow", $"baked shadow for {name} at {instanceTransform.position}, size {renderer?.size} - tweak it by hand, then Apply the prefab override if this is a scene instance.", this);
    }

    [Button("Clear Baked Shadow")]
    private void ClearBakedShadow()
    {
        if (bakedShadow == null) return;

        Undo.DestroyObjectImmediate(bakedShadow);

        Undo.RecordObject(this, "Clear Baked Shadow");
        bakedShadow = null;
        PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        EditorUtility.SetDirty(this);
    }

    private BuildingShadowConfig ResolveBakeConfig()
    {
        BuildingShadowManager manager = FindAnyObjectByType<BuildingShadowManager>(FindObjectsInactive.Include);
        if (manager != null && manager.Config != null)
            return manager.Config;

        // Prefab Mode has no manager to read from - fall back to the project's single config asset.
        string[] guids = AssetDatabase.FindAssets("t:BuildingShadowConfig");
        if (guids.Length == 0) return null;

        if (guids.Length > 1)
            LogHelper.Warn("BuildingShadow", $"{guids.Length} BuildingShadowConfig assets exist - baking with the first one found. Open a scene with a BuildingShadowManager to pick deliberately.", this);

        return AssetDatabase.LoadAssetAtPath<BuildingShadowConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    private GameObject ResolveBakeShadowPrefab()
    {
        if (bakeShadowPrefabOverride != null)
            return bakeShadowPrefabOverride;

        BuildingShadowManager manager = FindAnyObjectByType<BuildingShadowManager>(FindObjectsInactive.Include);
        return manager != null ? manager.ShadowPrefab : null;
    }
#endif

    private IEnumerator AcquireWithRetries()
    {
        for (int attempt = 0; attempt < maxAcquireAttempts; attempt++)
        {
            if (BuildingShadowManager.Instance != null)
            {
                handle = BuildingShadowManager.Instance.Acquire(footprintRenderer);
                if (handle != null)
                {
                    acquireRoutine = null;
                    yield break;
                }
            }

            yield return new WaitForSeconds(acquireRetryInterval);
        }

        acquireRoutine = null;
    }
}

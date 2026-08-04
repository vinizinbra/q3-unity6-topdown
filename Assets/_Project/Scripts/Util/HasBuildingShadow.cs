using System.Collections;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

// Marker + request point for a pooled building shadow: acquires one from BuildingShadowManager on
// enable, releases it back on disable. OnEnable/OnDisable rather than Awake/OnDestroy so this stays
// correct if the owning prefab is itself pooled (SetActive reuse) instead of actually
// destroyed/reinstantiated.
//
// Acquire can fail for a reason that has nothing to do with this building: the level's ground
// geometry may not have finished spawning yet when this OnEnable fires, so the flatness raycasts
// find nothing under any corner. Retrying a few times a second apart gives level spawning a chance
// to catch up before giving up on the shadow entirely; a single successful attempt stops the retries.
public class HasBuildingShadow : MonoBehaviour
{
    [SerializeField, Tooltip("Renderer whose world-space bounds define this object's footprint (X/Z) and true center - normally this GameObject's own Renderer (auto-filled by Reset).")]
    private Renderer footprintRenderer;

    [SerializeField, Tooltip("How many times to retry acquiring a shadow if the ground isn't ready yet (e.g. level still spawning) before giving up.")]
    private int maxAcquireAttempts = 5;
    [SerializeField, Tooltip("Delay between acquire retries, in seconds.")]
    private float acquireRetryInterval = 1f;

    private BuildingShadowHandle handle;
    private Coroutine acquireRoutine;

    private void Reset()
    {
        footprintRenderer = GetComponent<Renderer>();
    }

    private void OnEnable()
    {
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

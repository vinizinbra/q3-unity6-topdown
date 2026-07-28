using QuantumUser.View.Managers;
using UnityEngine;

namespace Quantum
{
    // Marker + request point for a pooled ground light: acquires one from GroundBlobManager on
    // enable, tinted to `color` instead of the shared blob pool's shadow tint, and releases it back
    // on disable. OnEnable/OnDisable rather than Awake/OnDestroy so this stays correct if the owning
    // prefab is itself pooled (SetActive reuse) instead of actually destroyed and reinstantiated.
    public class HasLight : MonoBehaviour
    {
        [SerializeField, Tooltip("RGB tint applied to the ground blob while this light is active. Alpha is ignored - GroundBlobManager drives alpha from GroundBlobConfig's Light Alpha and height falloff instead.")]
        private Color color = Color.white;
        [SerializeField, Tooltip("Light size at ground level, before GroundBlobManager's height falloff is applied.")]
        private float baseScale = 1f;

        private GroundBlobHandle handle;

        private void OnEnable()
        {
            if (GroundBlobManager.Instance != null)
                handle = GroundBlobManager.Instance.AcquireLight(transform, baseScale, color);
        }

        private void OnDisable()
        {
            if (handle == null) return;

            if (GroundBlobManager.Instance != null)
                GroundBlobManager.Instance.Release(handle);
            handle = null;
        }
    }
}

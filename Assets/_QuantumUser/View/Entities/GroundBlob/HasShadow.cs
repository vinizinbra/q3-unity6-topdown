using QuantumUser.View.Managers;
using UnityEngine;

namespace Quantum
{
    // Marker + request point for a pooled blob shadow: acquires one from GroundBlobManager on
    // enable, releases it back on disable. OnEnable/OnDisable rather than Awake/OnDestroy so this
    // stays correct if the owning prefab is itself pooled (SetActive reuse) instead of actually
    // destroyed and reinstantiated.
    public class HasShadow : MonoBehaviour
    {
        [SerializeField, Tooltip("Shadow size at ground level, before GroundBlobManager's height falloff is applied.")]
        private float baseScale = 1f;

        private GroundBlobHandle handle;

        private void OnEnable()
        {
            if (GroundBlobManager.Instance != null)
                handle = GroundBlobManager.Instance.AcquireShadow(transform, baseScale);
        }

        private void OnDisable()
        {
            if (handle == null) return;

            if (GroundBlobManager.Instance != null)
                GroundBlobManager.Instance.Release(handle);
            handle = null;
        }

        // Called by EnemyView.SpawnSprite once the entity's actual radius is known - OnEnable
        // already ran by then (with baseScale still at whatever this GameObject was authored
        // with), so the live handle's BaseScale is updated directly here rather than
        // Release+Reacquire, which would flicker the shadow back through the pool for a frame.
        public void SetBaseScale(float scale)
        {
            baseScale = scale;

            if (handle != null)
                handle.BaseScale = scale;
        }
    }
}

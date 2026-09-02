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

        [SerializeField, Tooltip("Per-character world X/Z nudge for this shadow, stacked on top of GroundBlobConfig's global ShadowOffset. Use it to slide the blob under a sprite whose feet/pivot sit off-center, when only this one character needs correcting. Leave at zero for centered sprites.")]
        private Vector2 shadowOffset = Vector2.zero;

        [SerializeField, Tooltip("Multiplies GroundBlobConfig's GroundAlpha for this shadow only, after height falloff. 1 = default (no change). Use it to fade a specific character's shadow lighter/darker than the shared baseline, e.g. a translucent/ghosted enemy.")]
        private float shadowAlphaMultiplier = 1f;

        private GroundBlobHandle handle;

        // Read by EnemyBlobAnimationView.ApplyPose to rescale the shadow by the same shrinkMult
        // (Die's _dieShrinkT / Burrow's _burrowT) already applied to the sprite itself - the
        // ground blob (GroundBlobManager.UpdateBlob) sizes purely off BaseScale * height falloff,
        // never off the target transform's own live scale, so without this the shadow would sit at
        // full size while the sprite shrinks to nothing during a death/burrow animation.
        public float BaseScale => baseScale;

        private void OnEnable()
        {
            // Defends against a leaked handle if a prior OnEnable ever fired without a matching
            // OnDisable in between (e.g. re-enable ordering edge case) - without this the old handle
            // would stay orphaned in GroundBlobManager.active forever, pointed at a Target that may
            // later be destroyed out from under it.
            if (handle != null && GroundBlobManager.Instance != null)
            {
                GroundBlobManager.Instance.Release(handle);
                handle = null;
            }

            if (GroundBlobManager.Instance != null)
                handle = GroundBlobManager.Instance.AcquireShadow(transform, baseScale, shadowOffset, shadowAlphaMultiplier);
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

        // Mirrors SetBaseScale - lets a caller (e.g. a status effect view) drive the alpha
        // multiplier directly on the live handle instead of Release+Reacquire, which would flicker
        // the shadow back through the pool for a frame.
        public void SetAlphaMultiplier(float multiplier)
        {
            shadowAlphaMultiplier = multiplier;

            if (handle != null)
                handle.AlphaMultiplier = multiplier;
        }

        // Catches a manual edit of baseScale/shadowAlphaMultiplier in the Inspector while playing -
        // the Inspector writes straight to the serialized field, bypassing SetBaseScale/
        // SetAlphaMultiplier, so without this the live handle (already snapshotted into
        // GroundBlobManager's pool) would keep whatever value it had at OnEnable/last Set call
        // until the shadow was released and reacquired.
        private void OnValidate()
        {
            if (handle != null)
            {
                handle.BaseScale = baseScale;
                handle.Offset = shadowOffset;
                handle.AlphaMultiplier = shadowAlphaMultiplier;
            }
        }
    }
}

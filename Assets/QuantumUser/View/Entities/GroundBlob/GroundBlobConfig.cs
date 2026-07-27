using UnityEngine;

namespace Quantum
{
    [CreateAssetMenu(fileName = "GroundBlobConfig", menuName = "Quantum/View/Ground Blob Config")]
    public class GroundBlobConfig : ScriptableObject
    {
        [Header("Raycast")]
        [SerializeField, Tooltip("Start the downward raycast this far above the target, in case the target's own collider overlaps the ground.")]
        private float raycastHeight = 2f;
        [SerializeField] private float maxRaycastDistance = 20f;
        [SerializeField] private UnityEngine.LayerMask groundLayer;
        [SerializeField, Tooltip("Small lift above the ground hit point to avoid z-fighting with the floor.")]
        private float groundOffset = 0.02f;
        [SerializeField, Tooltip("Horizontal nudge (world X/Z) applied to every blob's ground position, e.g. to align under a sprite's feet when the pivot isn't centered.")]
        private Vector2 shadowOffset = Vector2.zero;

        [Header("Tint")]
        [SerializeField, Tooltip("RGB applied to a blob acquired as a shadow (via HasShadow). A light (via HasLight) overrides this with its own color instead - since blobs are pooled and shared between the two, this is also what a reused instance gets reset to when it goes from being a light back to being a shadow.")]
        private Color shadowColor = Color.black;

        [Header("Height Falloff")]
        [SerializeField, Tooltip("Height above ground at which the blob reaches its minimum size/alpha.")]
        private float maxHeightForFalloff = 5f;
        [SerializeField, Tooltip("1 at ground level easing to 0 at maxHeightForFalloff - reshape to taste.")]
        private AnimationCurve heightFalloffCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [SerializeField, Range(0f, 1f)] private float minScaleMultiplier = 0.4f;
        [SerializeField, Range(0f, 1f), Tooltip("Max alpha for a blob acquired as a shadow, at ground level.")]
        private float groundAlpha = 0.5f;
        [SerializeField, Range(0f, 1f), Tooltip("Max alpha for a blob acquired as a light, at ground level. Separate from groundAlpha since lights usually want to read as more solid/opaque than shadows.")]
        private float lightAlpha = 0.6f;
        [SerializeField, Range(0f, 1f)] private float minAlphaMultiplier = 0.15f;

        public float RaycastHeight => raycastHeight;
        public float MaxRaycastDistance => maxRaycastDistance;
        public UnityEngine.LayerMask GroundLayer => groundLayer;
        public float GroundOffset => groundOffset;
        public Vector2 ShadowOffset => shadowOffset;
        public Color ShadowColor => shadowColor;
        public float MaxHeightForFalloff => maxHeightForFalloff;
        public AnimationCurve HeightFalloffCurve => heightFalloffCurve;
        public float MinScaleMultiplier => minScaleMultiplier;
        public float GroundAlpha => groundAlpha;
        public float LightAlpha => lightAlpha;
        public float MinAlphaMultiplier => minAlphaMultiplier;
    }
}

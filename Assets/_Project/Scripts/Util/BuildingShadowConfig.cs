using UnityEngine;

[CreateAssetMenu(fileName = "BuildingShadowConfig", menuName = "Shadows/Building Shadow Config")]
public class BuildingShadowConfig : ScriptableObject
{
    [Header("Ground Check")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField, Tooltip("Start each corner raycast this far above the footprint, in case the object's own collider overlaps the ground.")]
    private float raycastHeight = 2f;
    [SerializeField] private float maxRaycastDistance = 20f;
    [SerializeField, Tooltip("How far past each footprint edge the corner raycasts are cast, so ground that stops just short of an edge fails the check instead of reading as flat.")]
    private float edgeMargin = 0.2f;
    [SerializeField, Tooltip("Max allowed height difference between the highest and lowest corner hit before the ground counts as not flat.")]
    private float flatnessTolerance = 0.05f;

    [Header("Shadow Sizing")]
    [SerializeField, Tooltip("Added to the world-space footprint on both axes so the shadow reads larger than the object's own silhouette.")]
    private float shadowPadding = 1.5f;
    [SerializeField, Tooltip("Small lift above the ground hit point to avoid z-fighting with the floor.")]
    private float groundOffset = 0.02f;

    public LayerMask GroundLayer => groundLayer;
    public float RaycastHeight => raycastHeight;
    public float MaxRaycastDistance => maxRaycastDistance;
    public float EdgeMargin => edgeMargin;
    public float FlatnessTolerance => flatnessTolerance;
    public float ShadowPadding => shadowPadding;
    public float GroundOffset => groundOffset;
}

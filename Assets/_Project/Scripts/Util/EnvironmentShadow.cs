using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

// A dead-simple, self-contained ground shadow for a static environment prop - the hand-placed
// counterpart to HasBuildingShadow/BuildingShadowManager's pooled, ground-raycast-driven path.
//
// Two deliberate differences from that system, both of which are why this exists:
//   - There is NO ground raycast and no flatness check. The shadow sits at a forced WORLD Y
//     (`worldY`), authored by hand. That makes it work in Prefab Mode, on sloped/uneven ground, and
//     before any level geometry has spawned - all the cases the raycast path refuses to serve.
//   - It re-solves itself the moment the parent's SCALE (or footprint) changes, so resizing the prop
//     in the Scene view resizes its shadow live, with no bake/re-bake step.
//
// This component lives ON the shadow GameObject itself (a child of the prop, normally an instance of
// BuildingShadowPrefab), not on the prop. It writes three things: world position (parent footprint
// centre in XZ, `worldY` in Y), world rotation (flat on the ground), and SpriteRenderer.size.
//
// Because the shadow IS a child, it inherits the prop's (often non-uniform) scale - so localScale is
// set to cancel the parent chain's lossyScale back out to 1. That's what keeps `size` in plain world
// units: MEASURED by writing scale 1 and reading the resulting lossyScale back, rather than derived
// from the parent's, because the shadow is rotated 90 degrees against its parent so their scale axes
// don't line up. Same reasoning HasBuildingShadow's bake path documents.
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class EnvironmentShadow : MonoBehaviour
{
    [SerializeField, Tooltip("Renderer whose world-space bounds define the footprint this shadow covers. Normally the parent prop's own Renderer (auto-filled by Reset).")]
    private Renderer footprintRenderer;

    [Header("Placement")]
    [SerializeField, Tooltip("Forced WORLD Y for the shadow. Nothing is raycast - this is the height, verbatim.")]
    private float worldY;
    [SerializeField, Tooltip("World-space XZ nudge off the footprint centre, for props whose silhouette is offset from their bounds.")]
    private Vector2 offset = Vector2.zero;
    [SerializeField, Tooltip("Rotate the shadow to the parent's Y rotation and size it off the footprint's LOCAL bounds. Leave off for an axis-aligned shadow sized off the world AABB (which grows when the prop is rotated).")]
    private bool matchParentYaw;

    [Header("Sizing")]
    [SerializeField, Tooltip("Multiplies the footprint size on both axes. 1 = exactly the prop's footprint.")]
    private float sizeScale = 1f;
    [SerializeField, Tooltip("Added to the footprint size on both axes AFTER sizeScale, in world units, so the shadow reads a little larger than the silhouette.")]
    private float padding = 0f;

    [Header("Updating")]
    [SerializeField, Tooltip("Keep re-solving in Play mode. Off by default - environment props don't move once placed, so the solve runs once on enable. Turn on for a prop that is animated or scaled at runtime.")]
    private bool trackAtRuntime;

    private SpriteRenderer shadowRenderer;

    // What the last applied solve was computed from - so the per-frame editor pass is a handful of
    // comparisons for a prop nobody is currently touching.
    private Vector3 lastParentScale;
    private Bounds lastBounds;
    private float lastWorldY;
    private bool hasApplied;

    private void Reset()
    {
        footprintRenderer = ResolveParentRenderer();
        worldY = transform.position.y;
    }

    private void OnEnable()
    {
        shadowRenderer = GetComponent<SpriteRenderer>();
        hasApplied = false;
        WarnIfSizeIsIgnored();
        Apply();
    }

    private void OnValidate()
    {
        shadowRenderer = GetComponent<SpriteRenderer>();
        hasApplied = false; // any inspector edit re-solves, even if the parent never moved
    }

    private void LateUpdate()
    {
        if (Application.isPlaying && trackAtRuntime == false && hasApplied) return;

        Apply();
    }

    [Button("Refresh Shadow")]
    private void Refresh()
    {
        if (footprintRenderer == null)
            footprintRenderer = ResolveParentRenderer();

        shadowRenderer = GetComponent<SpriteRenderer>();
        hasApplied = false;
        WarnIfSizeIsIgnored();
        Apply();
    }

    // SpriteRenderer.size is only honoured in Sliced/Tiled draw mode - in Simple mode the write is
    // silently ignored and the shadow just stays whatever size the sprite happens to be, which reads
    // as "this component does nothing".
    private void WarnIfSizeIsIgnored()
    {
        if (shadowRenderer != null && shadowRenderer.drawMode == SpriteDrawMode.Simple)
            LogHelper.Warn("EnvironmentShadow", $"{name}'s SpriteRenderer is in Simple draw mode - set it to Sliced so the shadow can be resized.", this);
    }

    private void Apply()
    {
        if (footprintRenderer == null || shadowRenderer == null) return;

        Transform parent = transform.parent;
        Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
        Bounds bounds = footprintRenderer.bounds;

        // Nothing the solve depends on has moved.
        if (hasApplied
            && parentScale == lastParentScale
            && bounds.center == lastBounds.center
            && bounds.size == lastBounds.size
            && Mathf.Approximately(worldY, lastWorldY))
            return;

        float yaw = matchParentYaw && parent != null ? parent.eulerAngles.y : 0f;
        Quaternion rotation = Quaternion.Euler(90f, yaw, 0f);

        transform.SetPositionAndRotation(
            new Vector3(bounds.center.x + offset.x, worldY, bounds.center.z + offset.y),
            rotation);

        // Cancel the parent chain's scale so the shadow ends up at world scale 1 and `size` below
        // stays in plain world units. Measured, not derived - see the class comment.
        transform.localScale = Vector3.one;
        Vector3 lossy = transform.lossyScale;
        transform.localScale = new Vector3(
            Mathf.Approximately(lossy.x, 0f) ? 1f : 1f / lossy.x,
            Mathf.Approximately(lossy.y, 0f) ? 1f : 1f / lossy.y,
            Mathf.Approximately(lossy.z, 0f) ? 1f : 1f / lossy.z);

        shadowRenderer.size = ResolveSize(bounds, parentScale);

        lastParentScale = parentScale;
        lastBounds = bounds;
        lastWorldY = worldY;
        hasApplied = true;
    }

    private Vector2 ResolveSize(Bounds worldBounds, Vector3 parentScale)
    {
        // matchParentYaw sizes off the mesh's own local bounds scaled by the parent, so a prop rotated
        // 45 degrees keeps its true footprint instead of the (larger) axis-aligned box around it.
        Vector2 footprint = matchParentYaw
            ? new Vector2(
                footprintRenderer.localBounds.size.x * Mathf.Abs(parentScale.x),
                footprintRenderer.localBounds.size.z * Mathf.Abs(parentScale.z))
            : new Vector2(worldBounds.size.x, worldBounds.size.z);

        return new Vector2(
            Mathf.Max(0f, footprint.x * sizeScale + padding),
            Mathf.Max(0f, footprint.y * sizeScale + padding));
    }

    private Renderer ResolveParentRenderer()
    {
        Transform parent = transform.parent;
        if (parent == null)
        {
            LogHelper.Warn("EnvironmentShadow", $"{name} has no parent - put this on a shadow child of the prop it belongs to.", this);
            return null;
        }

        // Anything under the shadow itself is part of the shadow, not the footprint.
        foreach (Renderer candidate in parent.GetComponentsInChildren<Renderer>(true))
        {
            if (candidate.transform.IsChildOf(transform)) continue;
            return candidate;
        }

        return null;
    }
}

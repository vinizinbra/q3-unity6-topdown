using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

/// <summary>
/// Ground-projected drop shadow: raycasts straight down from the target each frame,
/// pins a flat sprite to the hit point, and shrinks/fades it based on how high the
/// target currently is above the ground - the classic top-down "how high am I jumping"
/// readability cue. Lies flat on the XZ plane; never billboards to the camera (a shadow
/// facing the camera would look like a floating disc instead of a mark on the floor).
/// </summary>
public class PlayerShadow : MonoBehaviour
{
    [Header("References")]
    [SerializeField, Tooltip("Falls back to the nearest QuantumEntityView up the hierarchy if left empty (this component is expected to live under a character's view prefab).")]
    private Transform target;
    [SerializeField, Tooltip("Defaults to a SpriteRenderer on this object if left empty.")]
    private SpriteRenderer shadowRenderer;
    [SerializeField] private UnityEngine.LayerMask groundLayer;

    [Header("Raycast")]
    [SerializeField, Tooltip("Start the downward raycast this far above the target, in case the target's own collider overlaps the ground.")]
    private float raycastHeight = 2f;
    [SerializeField] private float maxRaycastDistance = 20f;
    [SerializeField, Tooltip("Small lift above the ground hit point to avoid z-fighting with the floor.")]
    private float groundOffset = 0.02f;

    [Header("Height Falloff")]
    [SerializeField, Tooltip("Height above ground at which the shadow reaches its minimum size/alpha.")]
    private float maxHeightForFalloff = 5f;
    [SerializeField, Tooltip("1 at ground level easing to 0 at maxHeightForFalloff - reshape to taste.")]
    private AnimationCurve heightFalloffCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [SerializeField] private float groundScale = 1f;
    [SerializeField, Range(0f, 1f)] private float minScaleMultiplier = 0.4f;
    [SerializeField, Range(0f, 1f)] private float groundAlpha = 0.5f;
    [SerializeField, Range(0f, 1f)] private float minAlphaMultiplier = 0.15f;

    private void Awake()
    {
        if (target == null)
        {
            var entityView = GetComponentInParent<QuantumEntityView>();
            if (entityView != null) target = entityView.transform;
        }
        if (target == null)
            LogHelper.Warn("PlayerShadow", $"'{name}' has no target and found no QuantumEntityView in its parents.", this);

        if (shadowRenderer == null) shadowRenderer = GetComponent<SpriteRenderer>();
        if (shadowRenderer == null)
            LogHelper.Warn("PlayerShadow", $"'{name}' has no SpriteRenderer assigned or attached.", this);

        // Lie flat on the ground plane, facing up. If it renders upside-down/mirrored
        // for your sprite's default orientation, flip this to (-90, 0, 0).
        transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
    }

    private void LateUpdate()
    {
        if (target == null || shadowRenderer == null) return;

        Vector3 origin = target.position + Vector3.up * raycastHeight;
        bool hasGround = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastHeight + maxRaycastDistance, groundLayer);

        // An overhang directly above the target (e.g. a roof/upper walkway on groundLayer)
        // can be the first thing hit, landing above the target's own position - reject it,
        // since a valid floor to project the shadow onto is always at or below the target.
        if (hasGround && hit.point.y > target.position.y) hasGround = false;

        shadowRenderer.enabled = hasGround;
        if (!hasGround) return; // e.g. falling past the edge of the level, or only an overhang above - no floor to project onto

        // Re-flatten every frame: we're parented under the character, so its rotation
        // (e.g. turning to face aim direction) would otherwise tilt our world rotation too.
        transform.SetPositionAndRotation(hit.point + Vector3.up * groundOffset, Quaternion.Euler(90f, 0f, 0f));

        float height = Mathf.Max(0f, target.position.y - hit.point.y);
        float t = maxHeightForFalloff > 0f ? Mathf.Clamp01(height / maxHeightForFalloff) : 0f;
        float falloff = heightFalloffCurve.Evaluate(t); // 1 = full size/alpha at ground, eases toward 0 with height

        float scale = groundScale * Mathf.Lerp(minScaleMultiplier, 1f, falloff);
        transform.localScale = new Vector3(scale, scale, scale);

        Color color = shadowRenderer.color;
        color.a = groundAlpha * Mathf.Lerp(minAlphaMultiplier, 1f, falloff);
        shadowRenderer.color = color;
    }
}

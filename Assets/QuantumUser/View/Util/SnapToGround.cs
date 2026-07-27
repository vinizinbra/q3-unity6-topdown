using UnityEngine;

namespace QuantumUser.View.Util
{
    // Snaps this transform to the floor point directly below a target once, via a downward
    // raycast - shared ground-projection logic for any effect that must spawn at floor level
    // (e.g. RunDustFxView) instead of at a character rig's own pivot. One-shot rather than
    // continuous: parent particle systems (or other ground effects) under this transform to have
    // them sit at floor level at spawn time, then move with the parent normally afterward.
    public class SnapToGround : MonoBehaviour
    {
        [SerializeField, Tooltip("Falls back to this transform's parent if left empty.")]
        private Transform target;
        [SerializeField] private UnityEngine.LayerMask groundLayer;

        [Header("Snap Timing")]
        [SerializeField] private bool snapOnAwake = true;
        [SerializeField, Tooltip("Re-snap whenever this object is (re)enabled - useful for pooled effects that get reused at a new position each time they're activated.")]
        private bool snapOnEnable = true;

        [Header("Raycast")]
        [SerializeField, Tooltip("Start the downward raycast this far above the target, in case the target's own collider overlaps the ground.")]
        private float raycastHeight = 2f;
        [SerializeField] private float maxRaycastDistance = 20f;
        [SerializeField, Tooltip("Small lift above the ground hit point to avoid z-fighting with the floor.")]
        private float groundOffset = 0.02f;

        public bool HasGround { get; private set; }

        private void Awake()
        {
            if (target == null)
                target = transform.parent;

            if (snapOnAwake == true)
                Snap();
        }

        private void OnEnable()
        {
            if (snapOnEnable == true)
                Snap();
        }

        private void Snap()
        {
            if (target == null)
                return;

            Vector3 origin = target.position + Vector3.up * raycastHeight;
            HasGround = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastHeight + maxRaycastDistance, groundLayer);
            if (HasGround == false)
                return;

            transform.position = hit.point + Vector3.up * groundOffset;
        }
    }
}

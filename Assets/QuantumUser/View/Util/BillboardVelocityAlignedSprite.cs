using UnityEngine;

namespace QuantumUser.View.Util
{
    // Always faces the camera (billboard) and rolls to point along its own recent movement
    // direction - derives velocity from the transform's own frame-to-frame world position
    // instead of reading a specific Quantum component (Projectile.Velocity, KCC, a tween, ...),
    // so it works on any moving view without coupling to one. Same screen-projection technique
    // ProjectileView/PlayerGunAimView use for their own rotation.
    public class BillboardVelocityAlignedSprite : MonoBehaviour
    {
        [SerializeField, Tooltip("Falls back to Camera.main if left empty.")]
        private Transform cameraTransform;
        [SerializeField, Tooltip("Degrees added on top of the computed angle. 0 if the art is drawn facing right (the default rest orientation); -90 if drawn facing up.")]
        private float angleOffset;
        [SerializeField, Tooltip("Below this speed (world units/sec) the roll angle is held instead of snapping toward zero-velocity noise - the sprite still keeps billboarding to the camera.")]
        private float minSpeed = 0.01f;

        private Vector3 _previousPosition;
        private bool _hasPreviousPosition;
        private float _rollAngle;

        private void Awake()
        {
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        private void LateUpdate()
        {
            if (cameraTransform == null)
                return;

            if (_hasPreviousPosition == false)
            {
                _previousPosition = transform.position;
                _hasPreviousPosition = true;
                return;
            }

            Vector3 velocity = (transform.position - _previousPosition) / Time.deltaTime;
            _previousPosition = transform.position;

            UpdateRollAngle(velocity);
            Billboard();
        }

        private void UpdateRollAngle(Vector3 velocity)
        {
            if (velocity.magnitude < minSpeed)
                return;

            Vector2 screenDir = new Vector2(Vector3.Dot(velocity, cameraTransform.right), Vector3.Dot(velocity, cameraTransform.up));
            if (screenDir.sqrMagnitude < 0.0001f)
                return;

            _rollAngle = Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg + angleOffset;
        }

        private void Billboard()
        {
            transform.rotation = Quaternion.LookRotation(cameraTransform.forward, Vector3.up) * Quaternion.Euler(0f, 0f, _rollAngle);
        }
    }
}

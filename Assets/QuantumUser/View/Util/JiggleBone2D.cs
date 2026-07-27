using NaughtyAttributes;
using UnityEngine;

namespace QuantumUser.View.Util
{
    // Generic secondary-motion bone: drop on any child transform (ear, ponytail tip, tail
    // segment, dangling strap...) and it automatically lags/overshoots behind whatever
    // transform it's set to follow, via a damped spring - same integration BlobAnimationView
    // uses for its landing spring. Pure Unity Transform math, no Quantum frame reads, so it
    // runs in LateUpdate after BlobAnimationView (or anything else) has already posed the rig
    // for this frame. Chain several along a tail (each one's follow = the previous segment,
    // which is the default) to get a cascading whip automatically, without hand-authoring
    // per-segment logic the way BlobAnimationView hand-tunes torso/head/legs.
    public class JiggleBone2D : MonoBehaviour
    {
        [SerializeField, Tooltip("Transform whose motion drives the jiggle. Defaults to this bone's parent.")]
        private Transform follow;

        [Header("Response")]
        [SerializeField, Tooltip("How far the bone displaces per unit of the follow's local velocity (m/s).")]
        private float positionLagAmount = 0.15f;
        [SerializeField, Tooltip("Degrees of Z rotation kick per unit of the follow's local lateral velocity (m/s).")]
        private float rotationLagDegreesPerSpeed = 6f;
        [SerializeField] private float maxOffsetDistance = 0.3f;
        [SerializeField] private float maxRotationDegrees = 30f;

        [Header("Spring")]
        [SerializeField] private float springFrequency = 8f;
        [SerializeField, Range(0f, 1f)] private float springDamping = 0.4f;

        [Header("Locks (freeze that axis at its base pose)")]
        [SerializeField] private bool lockPositionX;
        [SerializeField] private bool lockPositionY;
        [SerializeField] private bool lockRotation;

        private Vector3 _baseLocalPos;
        private Quaternion _baseLocalRot;
        private Vector3 _prevFollowPos;
        private bool _initialized;

        private Vector2 _posOffset, _posVelocity;
        private float _rotOffset, _rotVelocity;

        private void Awake()
        {
            if (follow == null)
                follow = transform.parent;

            _baseLocalPos = transform.localPosition;
            _baseLocalRot = transform.localRotation;
        }

        private void OnEnable()
        {
            // Forces a resync on the first LateUpdate after (re)enabling instead of computing
            // velocity against a stale _prevFollowPos - avoids a one-frame spring spike, e.g. on
            // a pooled enemy respawning somewhere else.
            _initialized = false;
            _posOffset = _posVelocity = Vector2.zero;
            _rotOffset = _rotVelocity = 0f;
        }

        private void LateUpdate()
        {
            if (follow == null)
                return;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            if (_initialized == false)
            {
                _prevFollowPos = follow.position;
                _initialized = true;
                return;
            }

            Vector3 worldVelocity = (follow.position - _prevFollowPos) / dt;
            // Velocity read in the follow's own local space so the lag direction stays stable
            // relative to the rig regardless of facing flips or billboard rotation.
            Vector3 localVelocity = follow.InverseTransformDirection(worldVelocity);

            // Bone lags opposite the direction it's being dragged, like a weight trailing behind.
            Vector2 targetOffset = Vector2.ClampMagnitude(new Vector2(-localVelocity.x, -localVelocity.y) * positionLagAmount, maxOffsetDistance);
            float targetRot = Mathf.Clamp(-localVelocity.x * rotationLagDegreesPerSpeed, -maxRotationDegrees, maxRotationDegrees);

            IntegrateSpring(dt, targetOffset, targetRot);
            ApplyLocks();

            transform.localPosition = _baseLocalPos + new Vector3(_posOffset.x, _posOffset.y, 0f);
            transform.localRotation = _baseLocalRot * Quaternion.Euler(0f, 0f, _rotOffset);

            _prevFollowPos = follow.position;
        }

        private void IntegrateSpring(float dt, Vector2 targetOffset, float targetRot)
        {
            float omega = springFrequency * Mathf.PI * 2f;

            Vector2 posForce = -omega * omega * (_posOffset - targetOffset) - 2f * springDamping * omega * _posVelocity;
            _posVelocity += posForce * dt;
            _posOffset += _posVelocity * dt;

            float rotForce = -omega * omega * (_rotOffset - targetRot) - 2f * springDamping * omega * _rotVelocity;
            _rotVelocity += rotForce * dt;
            _rotOffset += _rotVelocity * dt;
        }

        // Snaps a locked axis straight back to rest rather than feeding it a zeroed target -
        // a zeroed target would still let the spring overshoot through the base pose before
        // settling, which reads as a stray twitch on an axis that's supposed to be inert.
        private void ApplyLocks()
        {
            if (lockPositionX)
                _posOffset.x = _posVelocity.x = 0f;
            if (lockPositionY)
                _posOffset.y = _posVelocity.y = 0f;
            if (lockRotation)
                _rotOffset = _rotVelocity = 0f;
        }

        // Kicks the spring without needing to actually move the follow transform - lets you
        // preview the jiggle response from the Inspector while the rig sits still in the Editor.
        [Button]
        private void TestKick()
        {
            _posVelocity += new Vector2(1f, 1f);
            _rotVelocity += maxRotationDegrees * 4f;
        }
    }
}

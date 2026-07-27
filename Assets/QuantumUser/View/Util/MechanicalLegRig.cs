using NaughtyAttributes;
using UnityEngine;

namespace QuantumUser.View.Util
{
    /// <summary>
    /// One procedural arachnid leg, drawn entirely with a LineRenderer - no per-segment sprites,
    /// so there's nothing to hand-pose or hand-pivot. The leg reaches for a foot target via 2D
    /// FABRIK, plants that foot on real ground (3D raycast - level colliders are 3D even though
    /// the leg itself renders flat), and steps whenever the target drifts too far from the
    /// currently planted position - the classic "procedural spider leg" technique: a parented
    /// shoulder anchor and a world-space foot point that only gets rearranged (stepped) once the
    /// two drift too far apart, everything else is a rigid IK reach between them. stepEaseCurve
    /// is where the "juice" lives - give it an overshoot key to make each re-plant whip fast and
    /// settle instead of gliding smoothly.
    /// The LineRenderer is forced to world space + TransformZ alignment at Awake so it lies flat
    /// in the character's plane instead of billboarding to the camera - this project's characters
    /// never billboard (see Brutus.prefab), and a billboarding leg on a non-billboarding body
    /// would read as visually inconsistent.
    /// Fully self-contained: drop it on a single leg and it walks on its own; coordinate several
    /// with MechanicalWalkerBiped (or your own gait coordinator) for an alternating gait.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class MechanicalLegRig : MonoBehaviour
    {
        [Header("Chain")]
        [SerializeField, Tooltip("Defaults to the LineRenderer on this object.")]
        private LineRenderer lineRenderer;
        [SerializeField, Tooltip("How many straight segments make up the leg - more segments bend more smoothly for the same total length.")]
        private int segmentCount = 10;
        [SerializeField, Tooltip("Total leg length from shoulder to foot tip, divided evenly across segmentCount.")]
        private float totalLength = 2f;
        [SerializeField, Tooltip("Local origin the chain solves from. Defaults to this transform.")]
        private Transform limbRoot;

        [Header("Foot Target")]
        [SerializeField] private UnityEngine.LayerMask groundLayer;
        [SerializeField, Tooltip("Neutral foot position relative to limbRoot, in limbRoot's local space (X = sideways, Z = forward/back stance).")]
        private Vector3 restStanceLocalOffset = new Vector3(0.35f, 0f, 0f);
        [SerializeField, Tooltip("How far ahead of the rest stance (in seconds of current root velocity) the foot anticipates while moving.")]
        private float strideAnticipation = 0.15f;
        [SerializeField, Tooltip("Horizontal distance the target must drift from the planted foot before a new step triggers.")]
        private float stepDistanceThreshold = 0.5f;
        [SerializeField] private float stepDuration = 0.18f;
        [SerializeField] private float stepHeight = 0.25f;
        [SerializeField] private AnimationCurve stepArcCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 0f);
        [SerializeField, Tooltip("Eases the step's 0-1 progress before lerping start->target. Default overshoots past 1 then settles back, for a mechanical whip-crack snap instead of a smooth glide.")]
        private AnimationCurve stepEaseCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.6f, 1.15f), new Keyframe(1f, 1f));

        [Header("Ground Raycast")]
        [SerializeField, Tooltip("Start the downward raycast this far above the desired foot position.")]
        private float raycastHeight = 3f;
        [SerializeField] private float maxRaycastDistance = 10f;

        [Header("Screen Projection")]
        [SerializeField, Tooltip("How much a world-Z (forward/back) offset contributes to the rig's local Y - matches the gameplay camera's tilt (sin of its elevation angle; 0.7071 for the default 45 deg FollowCamera). Retune if the camera angle changes.")]
        private float depthForeshortening = 0.7071f;

        private Vector2[] _joints;
        private float[] _segmentLengths;

        private Vector3 _footWorldPosition;
        private Vector3 _stepStartWorldPosition;
        private Vector3 _stepTargetWorldPosition;
        private float _stepTimer;
        private bool _isStepping;

        private Vector3 _prevRootPosition;
        private Vector3 _rootVelocity;

        public bool IsStepping => _isStepping;
        public bool ExternalStepLock { get; set; }

        private void Awake()
        {
            if (limbRoot == null)
                limbRoot = transform;
            if (lineRenderer == null)
                lineRenderer = GetComponent<LineRenderer>();

            _joints = new Vector2[segmentCount + 1];
            _segmentLengths = new float[segmentCount];
            float length = totalLength / segmentCount;
            for (int i = 0; i < segmentCount; i++)
                _segmentLengths[i] = length;

            lineRenderer.useWorldSpace = true;
            lineRenderer.alignment = LineAlignment.TransformZ;
            lineRenderer.positionCount = segmentCount + 1;

            _prevRootPosition = limbRoot.position;
            _footWorldPosition = GroundedRestPosition();
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            _rootVelocity = (limbRoot.position - _prevRootPosition) / dt;
            _prevRootPosition = limbRoot.position;

            UpdateFootTarget(dt);
            SolveAndApply();
        }

        private void UpdateFootTarget(float dt)
        {
            if (_isStepping)
            {
                _stepTimer += dt / stepDuration;
                if (_stepTimer >= 1f)
                {
                    _stepTimer = 1f;
                    _isStepping = false;
                    _footWorldPosition = _stepTargetWorldPosition;
                }
                else
                {
                    Vector3 pose = Vector3.Lerp(_stepStartWorldPosition, _stepTargetWorldPosition, stepEaseCurve.Evaluate(_stepTimer));
                    pose.y += stepHeight * stepArcCurve.Evaluate(_stepTimer);
                    _footWorldPosition = pose;
                }
                return;
            }

            Vector3 desired = GroundedRestPosition();
            Vector2 currentGroundPos = new Vector2(_footWorldPosition.x, _footWorldPosition.z);
            Vector2 desiredGroundPos = new Vector2(desired.x, desired.z);

            if (ExternalStepLock == false && Vector2.Distance(currentGroundPos, desiredGroundPos) > stepDistanceThreshold)
                BeginStep(desired);
        }

        private void BeginStep(Vector3 target)
        {
            _isStepping = true;
            _stepTimer = 0f;
            _stepStartWorldPosition = _footWorldPosition;
            _stepTargetWorldPosition = target;
        }

        private Vector3 GroundedRestPosition()
        {
            Vector3 stance = limbRoot.TransformPoint(restStanceLocalOffset) + _rootVelocity * strideAnticipation;

            Vector3 origin = stance + Vector3.up * raycastHeight;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastHeight + maxRaycastDistance, groundLayer))
                stance.y = hit.point.y;

            return stance;
        }

        private void SolveAndApply()
        {
            Vector3 localOffset = limbRoot.InverseTransformPoint(_footWorldPosition);
            Vector2 target = new Vector2(localOffset.x, localOffset.y + localOffset.z * depthForeshortening);

            IkChain2D.Solve(_joints, _segmentLengths, Vector2.zero, target);

            for (int i = 0; i < _joints.Length; i++)
            {
                Vector3 worldPoint = limbRoot.TransformPoint(new Vector3(_joints[i].x, _joints[i].y, 0f));
                lineRenderer.SetPosition(i, worldPoint);
            }
        }

        // Forces a step regardless of the distance threshold - lets you preview the step arc
        // from the Inspector without having to actually move the character far enough to trigger one.
        [Button]
        private void TestStepNow()
        {
            if (Application.isPlaying)
                BeginStep(GroundedRestPosition());
        }

        private void OnDrawGizmosSelected()
        {
            if (limbRoot == null)
                return;

            Vector3 desired = Application.isPlaying ? _footWorldPosition : GroundedRestPosition();

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(limbRoot.position, desired);

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(desired, 0.05f);
        }
    }
}

using System;
using QuantumUser.View.Managers;
using UnityEngine;

/// <summary>
/// View-only procedural tentacle walker for a top-down 2D character.
///
/// The character is moved externally, such as by a Quantum EntityView.
/// This script only observes the rendered transform and animates the legs.
///
/// Assumptions:
/// - The character never rotates.
/// - The character may flip horizontally.
/// - Tentacles are purely visual.
/// - Feet remain planted in world space between steps.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProceduralTentacleWalker2D : MonoBehaviour
{
    [Serializable]
    private sealed class Tentacle
    {
        [Header("References")]

        [Tooltip("Point where the tentacle connects to the body.")]
        public Transform hip;

        [Tooltip(
            "Desired resting position of the tentacle endpoint. " +
            "Ignored while pinnedTarget is set."
        )]
        public Transform homeTarget;

        [Tooltip(
            "If set, this tentacle's tip locks onto this transform every " +
            "frame instead of stepping toward homeTarget - no gait, no " +
            "ground snapping. Use this to grip a specific object."
        )]
        public Transform pinnedTarget;

        [Tooltip("LineRenderer used to draw the tentacle.")]
        public LineRenderer lineRenderer;

        [Tooltip(
            "Optional object placed at the tentacle's animated endpoint. " +
            "Its position is updated every frame to follow the foot."
        )]
        public Transform endAnchor;

        [Header("Gait")]

        [Tooltip("Tentacles with the same group number step together.")]
        [Min(0)]
        public int gaitGroup;

        [Tooltip(
            "Stable local direction in which this tentacle curves. " +
            "Examples: (0, 1) for upward, (0, -1) for downward."
        )]
        public Vector2 localBendDirection = Vector2.up;

        [Tooltip("Multiplier applied to the global bend amount.")]
        [Min(0f)]
        public float bendMultiplier = 1f;

        [Tooltip(
            "Mirror this tentacle's bend direction when facing left. " +
            "Leave on for normal walking tentacles. Turn off for a " +
            "pinnedTarget tentacle whose target does not flip with the " +
            "character, so its bend does not curve toward the wrong side."
        )]
        public bool mirrorBendWithFacing = true;

        [Tooltip("Multiplier applied to the global step distance.")]
        [Min(0.01f)]
        public float stepDistanceMultiplier = 1f;

        [NonSerialized]
        public Vector3 footPosition;

        [NonSerialized]
        public Vector3 stepStart;

        [NonSerialized]
        public Vector3 stepTarget;

        [NonSerialized]
        public float stepProgress;

        [NonSerialized]
        public bool isStepping;

        [NonSerialized]
        public Vector3[] curvePoints;
    }

    [Header("References")]

    [Tooltip("The transform moved by the game simulation.")]
    [SerializeField]
    private Transform movementRoot;

    [Tooltip(
        "The visual transform that flips horizontally. " +
        "Hip and home targets should normally be children of this object."
    )]
    [SerializeField]
    private Transform visualRoot;

    [Header("Tentacles")]

    [SerializeField]
    private Tentacle[] tentacles = Array.Empty<Tentacle>();

    [Tooltip("Number of LineRenderer vertices used per tentacle.")]
    [SerializeField, Range(3, 20)]
    private int curveResolution = 8;

    [Header("Stepping")]

    [Tooltip("Distance from the home target required before stepping.")]
    [SerializeField, Min(0.01f)]
    private float stepDistance = 0.45f;

    [Tooltip("Duration of one step in seconds.")]
    [SerializeField, Min(0.01f)]
    private float stepDuration = 0.16f;

    [Tooltip(
        "Visual sideways arc applied while the foot moves. " +
        "Keep this relatively small for top-down movement."
    )]
    [SerializeField, Min(0f)]
    private float stepArc = 0.1f;

    [Tooltip(
        "Eases the step's 0-1 progress before lerping stepStart -> " +
        "stepTarget. Defaults to a smoothstep-equivalent curve; add an " +
        "overshoot key for a snappier plant."
    )]
    [SerializeField]
    private AnimationCurve stepEaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip(
        "Shapes the sideways arc amount (multiplied by stepArc) over the " +
        "step's 0-1 progress. Defaults to a smooth, rounded hump peaking " +
        "mid-step (flat tangents at every key, so it never overshoots)."
    )]
    [SerializeField]
    private AnimationCurve stepArcCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(0.5f, 1f, 0f, 0f),
        new Keyframe(1f, 0f, 0f, 0f)
    );

    [Tooltip(
        "Fraction of stepDuration that must elapse before the next gait " +
        "group is allowed to start. 1 = fully sequential, groups never " +
        "overlap. Lower values let the next group start mid-step, e.g. " +
        "0.5 starts it halfway through the current step."
    )]
    [SerializeField, Range(0f, 1f)]
    private float gaitOverlap = 1f;

    [Tooltip("Particle prefab played through EffectsManager each time a foot finishes a step.")]
    [SerializeField]
    private ParticleSystem landingEffect;

    [Header("Tentacle Shape")]

    [Tooltip("Base amount of curvature.")]
    [SerializeField, Min(0f)]
    private float bendAmount = 0.3f;

    [Tooltip("Additional curvature based on tentacle length.")]
    [SerializeField, Min(0f)]
    private float stretchBend = 0.1f;

    [Tooltip(
        "How rounded the curve is. 0 pulls both bezier handles out to " +
        "the hip and foot for a wide, gently domed shape. 1 crosses them " +
        "past the midpoint for a tighter S-hook. 0.5 is a single even bulge."
    )]
    [SerializeField, Range(0f, 1f)]
    private float curveSmoothness = 0.5f;

    [Header("Ground Raycast")]

    [Tooltip(
        "Level colliders the foot targets snap onto. " +
        "Without this, feet ignore platform edges and can hang over empty space."
    )]
    [SerializeField]
    private LayerMask groundLayer;

    [Tooltip("Start the downward raycast this far above the desired foot position.")]
    [SerializeField, Min(0f)]
    private float raycastHeight = 3f;

    [Tooltip("Maximum downward distance checked for ground below the raycast start.")]
    [SerializeField, Min(0f)]
    private float maxRaycastDistance = 10f;

    [Header("Movement Prediction")]

    [Tooltip(
        "Places the foot slightly ahead of the moving character. " +
        "Set to zero while initially testing."
    )]
    [SerializeField, Min(0f)]
    private float predictionTime = 0.03f;

    [Tooltip("Maximum distance added by movement prediction.")]
    [SerializeField, Min(0f)]
    private float maximumPredictionDistance = 0.15f;

    [Tooltip("Movement below this speed is treated as stationary.")]
    [SerializeField, Min(0f)]
    private float minimumMovementSpeed = 0.02f;

    [Header("Safety")]

    [Tooltip(
        "If the movement root travels farther than this in one frame, " +
        "the feet reset immediately."
    )]
    [SerializeField, Min(0.01f)]
    private float teleportDistance = 2f;

    [Tooltip("Use unscaled time for the visual animation.")]
    [SerializeField]
    private bool useUnscaledTime;

    private Vector3 _previousRootPosition;
    private Vector3 _observedVelocity;

    private int _lastGaitGroup = -1;
    private bool _initialized;

    private void Reset()
    {
        movementRoot = transform;
    }

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();
        ResetFeet();
    }

    private void OnValidate()
    {
        curveResolution = Mathf.Max(3, curveResolution);
        stepDistance = Mathf.Max(0.01f, stepDistance);
        stepDuration = Mathf.Max(0.01f, stepDuration);
        teleportDistance = Mathf.Max(0.01f, teleportDistance);

        if (movementRoot == null)
            movementRoot = transform;

        ConfigureLineRenderers();
    }

    private void LateUpdate()
    {
        if (!_initialized)
            Initialize();

        if (tentacles == null || tentacles.Length == 0)
            return;

        float deltaTime = useUnscaledTime
            ? Time.unscaledDeltaTime
            : Time.deltaTime;

        UpdateObservedMovement(deltaTime);
        UpdateCurrentSteps(deltaTime);
        UpdatePinnedTentacles();

        if (CanStartNextGaitGroup())
            TryStartNextGaitGroup();

        DrawTentacles();
    }

    /// <summary>
    /// Sets (or clears, if target is null) the pinnedTarget of the tentacle at the given index -
    /// for a caller that needs to grip a target resolved at runtime (e.g. a dynamically spawned
    /// object) rather than one wired up in the Inspector. Tentacle itself is a private nested
    /// class, so this is the only way to reach pinnedTarget from outside this component. No-ops
    /// silently if index is out of range.
    /// </summary>
    public void SetPinnedTarget(int index, Transform target)
    {
        if (tentacles == null || index < 0 || index >= tentacles.Length)
            return;

        tentacles[index].pinnedTarget = target;
    }

    /// <summary>
    /// Immediately places every foot at its current home target.
    /// This can also be called manually after teleporting the character.
    /// </summary>
    public void ResetFeet()
    {
        if (!_initialized)
            Initialize();

        if (movementRoot == null)
            return;

        _previousRootPosition = movementRoot.position;
        _observedVelocity = Vector3.zero;

        for (int i = 0; i < tentacles.Length; i++)
        {
            Tentacle tentacle = tentacles[i];

            if (!IsValid(tentacle))
                continue;

            Vector3 target = RestPosition(tentacle);

            tentacle.footPosition = target;
            tentacle.stepStart = target;
            tentacle.stepTarget = target;
            tentacle.stepProgress = 1f;
            tentacle.isStepping = false;
        }

        DrawTentacles();
    }

    private void Initialize()
    {
        if (movementRoot == null)
            movementRoot = transform;

        ConfigureLineRenderers();

        _previousRootPosition = movementRoot.position;
        _observedVelocity = Vector3.zero;

        if (tentacles != null)
        {
            for (int i = 0; i < tentacles.Length; i++)
            {
                Tentacle tentacle = tentacles[i];

                if (!IsValid(tentacle))
                    continue;

                Vector3 target = RestPosition(tentacle);

                tentacle.footPosition = target;
                tentacle.stepStart = target;
                tentacle.stepTarget = target;
                tentacle.stepProgress = 1f;
                tentacle.isStepping = false;
            }
        }

        _initialized = true;
    }

    private void ConfigureLineRenderers()
    {
        if (tentacles == null)
            return;

        int resolution = Mathf.Max(3, curveResolution);

        for (int i = 0; i < tentacles.Length; i++)
        {
            Tentacle tentacle = tentacles[i];

            if (tentacle == null || tentacle.lineRenderer == null)
                continue;

            tentacle.lineRenderer.useWorldSpace = true;
            tentacle.lineRenderer.positionCount = resolution;

            if (tentacle.curvePoints == null ||
                tentacle.curvePoints.Length != resolution)
            {
                tentacle.curvePoints = new Vector3[resolution];
            }
        }
    }

    private void UpdateObservedMovement(float deltaTime)
    {
        if (movementRoot == null)
            return;

        Vector3 currentPosition = movementRoot.position;
        Vector3 movement = currentPosition - _previousRootPosition;

        movement.y = 0f;

        if (movement.magnitude >= teleportDistance)
        {
            ResetFeet();
            return;
        }

        if (deltaTime > Mathf.Epsilon)
        {
            _observedVelocity = movement / deltaTime;
            _observedVelocity.y = 0f;

            float minimumSpeedSquared =
                minimumMovementSpeed * minimumMovementSpeed;

            if (_observedVelocity.sqrMagnitude < minimumSpeedSquared)
                _observedVelocity = Vector3.zero;
        }
        else
        {
            _observedVelocity = Vector3.zero;
        }

        _previousRootPosition = currentPosition;
    }

    private void UpdateCurrentSteps(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        for (int i = 0; i < tentacles.Length; i++)
        {
            Tentacle tentacle = tentacles[i];

            if (!IsValid(tentacle) || !tentacle.isStepping)
                continue;

            tentacle.stepProgress += deltaTime / stepDuration;

            float t = Mathf.Clamp01(tentacle.stepProgress);
            float easedT = stepEaseCurve.Evaluate(t);

            Vector3 position = Vector3.Lerp(
                tentacle.stepStart,
                tentacle.stepTarget,
                easedT
            );

            Vector3 arcDirection = GetWorldBendDirection(tentacle);

            float arcAmount =
                stepArcCurve.Evaluate(t) *
                stepArc;

            position += arcDirection * arcAmount;
            position.y = Mathf.Lerp(
                tentacle.stepStart.y,
                tentacle.stepTarget.y,
                easedT
            );

            tentacle.footPosition = position;

            if (t >= 1f)
            {
                tentacle.footPosition = tentacle.stepTarget;
                tentacle.stepProgress = 1f;
                tentacle.isStepping = false;

                PlayLandingEffect(
                    tentacle.footPosition,
                    tentacle.stepTarget - tentacle.stepStart
                );
            }
        }
    }

    private void UpdatePinnedTentacles()
    {
        for (int i = 0; i < tentacles.Length; i++)
        {
            Tentacle tentacle = tentacles[i];

            if (!IsValid(tentacle) || !IsPinned(tentacle))
                continue;

            Vector3 target = tentacle.pinnedTarget.position;

            tentacle.footPosition = target;
            tentacle.stepStart = target;
            tentacle.stepTarget = target;
            tentacle.stepProgress = 1f;
            tentacle.isStepping = false;
        }
    }

    private void PlayLandingEffect(Vector3 position, Vector3 direction)
    {
        if (landingEffect == null || EffectsManager.Instance == null)
            return;

        direction.y = 0f;

        Quaternion rotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction)
            : Quaternion.identity;

        EffectsManager.Instance.PlayEffect(
            landingEffect,
            position,
            rotation
        );
    }

    private bool CanStartNextGaitGroup()
    {
        for (int i = 0; i < tentacles.Length; i++)
        {
            Tentacle tentacle = tentacles[i];

            if (tentacle != null &&
                tentacle.isStepping &&
                tentacle.stepProgress < gaitOverlap)
            {
                return false;
            }
        }

        return true;
    }

    private void TryStartNextGaitGroup()
    {
        int selectedGroup = FindGaitGroupThatNeedsStep();

        if (selectedGroup < 0)
            return;

        Vector3 predictionOffset = CalculatePredictionOffset();
        bool startedAny = false;

        for (int i = 0; i < tentacles.Length; i++)
        {
            Tentacle tentacle = tentacles[i];

            if (!IsValid(tentacle) || tentacle.isStepping || IsPinned(tentacle))
                continue;

            if (tentacle.gaitGroup != selectedGroup)
                continue;

            Vector3 desiredTarget =
                tentacle.homeTarget.position +
                predictionOffset;

            desiredTarget.y = FindGroundHeight(
                desiredTarget,
                tentacle.homeTarget.position.y
            );

            float threshold =
                stepDistance *
                tentacle.stepDistanceMultiplier;

            float currentDistance = GroundDistance(
                tentacle.footPosition,
                desiredTarget
            );

            // Members of the same gait group move together, but a leg
            // already close to its target does not need to make a tiny step.
            if (currentDistance < threshold * 0.25f)
                continue;

            BeginStep(tentacle, desiredTarget);
            startedAny = true;
        }

        if (startedAny)
            _lastGaitGroup = selectedGroup;
    }

    private int FindGaitGroupThatNeedsStep()
    {
        Vector3 predictionOffset = CalculatePredictionOffset();

        int selectedGroup = -1;
        float selectedScore = 0f;

        for (int i = 0; i < tentacles.Length; i++)
        {
            Tentacle tentacle = tentacles[i];

            if (!IsValid(tentacle) || tentacle.isStepping || IsPinned(tentacle))
                continue;

            Vector3 desiredTarget =
                tentacle.homeTarget.position +
                predictionOffset;

            float threshold =
                stepDistance *
                tentacle.stepDistanceMultiplier;

            float distance = GroundDistance(
                tentacle.footPosition,
                desiredTarget
            );

            float excessDistance = distance - threshold;

            if (excessDistance <= 0f)
                continue;

            float score = excessDistance;

            // Prefer alternating groups when their scores are almost equal.
            if (tentacle.gaitGroup != _lastGaitGroup)
                score += 0.001f;

            if (selectedGroup < 0 || score > selectedScore)
            {
                selectedGroup = tentacle.gaitGroup;
                selectedScore = score;
            }
        }

        return selectedGroup;
    }

    private void BeginStep(
        Tentacle tentacle,
        Vector3 target)
    {
        tentacle.stepStart = tentacle.footPosition;
        tentacle.stepTarget = target;
        tentacle.stepProgress = 0f;
        tentacle.isStepping = true;
    }

    private float FindGroundHeight(Vector3 groundPosition, float fallbackHeight)
    {
        Vector3 origin = groundPosition + Vector3.up * raycastHeight;

        if (Physics.Raycast(
            origin,
            Vector3.down,
            out RaycastHit hit,
            raycastHeight + maxRaycastDistance,
            groundLayer))
        {
            return hit.point.y;
        }

        return fallbackHeight;
    }

    private Vector3 CalculatePredictionOffset()
    {
        Vector3 offset =
            _observedVelocity *
            predictionTime;

        offset.y = 0f;

        float maximumDistance =
            Mathf.Max(0f, maximumPredictionDistance);

        if (maximumDistance > 0f &&
            offset.sqrMagnitude >
            maximumDistance * maximumDistance)
        {
            offset =
                offset.normalized *
                maximumDistance;
        }

        return offset;
    }

    private void DrawTentacles()
    {
        if (tentacles == null)
            return;

        for (int i = 0; i < tentacles.Length; i++)
        {
            Tentacle tentacle = tentacles[i];

            if (!IsValid(tentacle))
                continue;

            DrawTentacle(tentacle);
        }
    }

    private void DrawTentacle(Tentacle tentacle)
    {
        LineRenderer line = tentacle.lineRenderer;

        if (line.positionCount != curveResolution)
            line.positionCount = curveResolution;

        if (tentacle.curvePoints == null ||
            tentacle.curvePoints.Length != curveResolution)
        {
            tentacle.curvePoints =
                new Vector3[curveResolution];
        }

        Vector3 start = tentacle.hip.position;
        Vector3 end = tentacle.footPosition;

        float length = GroundDistance(start, end);

        Vector3 bendDirection =
            GetWorldBendDirection(tentacle);

        float totalBend =
            (bendAmount + length * stretchBend) *
            tentacle.bendMultiplier;

        Vector3 bendOffset = bendDirection * totalBend;

        Vector3 control1 =
            Vector3.Lerp(start, end, curveSmoothness) +
            bendOffset;

        Vector3 control2 =
            Vector3.Lerp(start, end, 1f - curveSmoothness) +
            bendOffset;

        for (int i = 0; i < curveResolution; i++)
        {
            float t =
                i / (float)(curveResolution - 1);

            tentacle.curvePoints[i] =
                EvaluateCubicBezier(
                    start,
                    control1,
                    control2,
                    end,
                    t
                );
        }

        line.SetPositions(tentacle.curvePoints);

        if (tentacle.endAnchor != null)
            tentacle.endAnchor.position = end;
    }

    private Vector3 GetWorldBendDirection(
        Tentacle tentacle)
    {
        Vector3 direction = new Vector3(
            tentacle.localBendDirection.x,
            0f,
            tentacle.localBendDirection.y
        );

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.forward;

        direction.Normalize();

        if (tentacle.mirrorBendWithFacing && IsFacingLeft())
            direction.x *= -1f;

        return direction;
    }

    private bool IsFacingLeft()
    {
        if (visualRoot == null)
            return false;

        return visualRoot.localScale.x < 0f;
    }

    private static Vector3 EvaluateCubicBezier(
        Vector3 start,
        Vector3 control1,
        Vector3 control2,
        Vector3 end,
        float t)
    {
        float inverseT = 1f - t;

        return
            inverseT * inverseT * inverseT * start +
            3f * inverseT * inverseT * t * control1 +
            3f * inverseT * t * t * control2 +
            t * t * t * end;
    }


    private static float GroundDistance(Vector3 a, Vector3 b)
    {
        float deltaX = a.x - b.x;
        float deltaZ = a.z - b.z;

        return Mathf.Sqrt(
            deltaX * deltaX +
            deltaZ * deltaZ
        );
    }

    private static bool IsValid(Tentacle tentacle)
    {
        return tentacle != null &&
               tentacle.hip != null &&
               tentacle.lineRenderer != null &&
               (tentacle.homeTarget != null || tentacle.pinnedTarget != null);
    }

    private static bool IsPinned(Tentacle tentacle)
    {
        return tentacle.pinnedTarget != null;
    }

    private static Vector3 RestPosition(Tentacle tentacle)
    {
        return IsPinned(tentacle)
            ? tentacle.pinnedTarget.position
            : tentacle.homeTarget.position;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (tentacles == null)
            return;

        for (int i = 0; i < tentacles.Length; i++)
        {
            Tentacle tentacle = tentacles[i];

            if (tentacle == null)
                continue;

            if (tentacle.hip != null)
            {
                Gizmos.DrawWireSphere(
                    tentacle.hip.position,
                    0.04f
                );
            }

            if (tentacle.homeTarget != null)
            {
                Gizmos.DrawWireSphere(
                    tentacle.homeTarget.position,
                    0.05f
                );

                if (tentacle.hip != null)
                {
                    Gizmos.DrawLine(
                        tentacle.hip.position,
                        tentacle.homeTarget.position
                    );
                }
            }

            if (tentacle.pinnedTarget != null)
            {
                Gizmos.color = Color.magenta;

                Gizmos.DrawWireSphere(
                    tentacle.pinnedTarget.position,
                    0.06f
                );

                if (tentacle.hip != null)
                {
                    Gizmos.DrawLine(
                        tentacle.hip.position,
                        tentacle.pinnedTarget.position
                    );
                }

                Gizmos.color = Color.white;
            }
        }
    }
#endif
}
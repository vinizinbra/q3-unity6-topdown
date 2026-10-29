using UnityEngine;

// View-only animation for the Carnival map-rim rides (docs/tileset-builder.md, Carnival): the ride prefabs are a static
// base + one moving part, spawned by TilesetPlatformBuilder's water scatter like any other prop. Nothing here touches the
// simulation - pure cosmetics, skipped while the moving part is off screen.
//   Spin: the part turns about an axis through its pivot (Ferris wheel rotor, carousel / swing-ride canopy). Optional
//         "hangers" (Ferris wheel gondolas) are hung on a circle of the part and kept upright every frame.
//   Drop: the part slides along the root's up axis - slow rise, hold, fast fall, small bounce (drop tower car).
//   Path: car models ride a polyline (root-local pathPoints) back and forth like a shuttle coaster, eased at the ends,
//         facing along the track and rolling through loops (up = forward x tangent).
public class CarnivalRideAnimator : MonoBehaviour
{
    public enum Motion
    {
        Spin,
        Drop,
        Path
    }

    [SerializeField, Tooltip("The moving piece (a child of this root).")]
    private Transform part;

    [SerializeField]
    private Motion motion = Motion.Spin;

    [Header("Spin")]
    [SerializeField, Tooltip("Degrees per second (negative = the other way).")]
    private float degreesPerSecond = 20f;

    [SerializeField, Tooltip("Rotation axis in the part's MESH space. Zero = the mesh's thinnest bounds axis (a wheel spins about its depth).")]
    private Vector3 meshAxis;

    [SerializeField, Tooltip("Models hung on the part's rim and kept upright (Ferris wheel gondolas), cycled through.")]
    private GameObject[] hangerModels;

    [SerializeField, Min(0)]
    private int hangerCount = 8;

    [SerializeField, Tooltip("Hang point radius in the part's mesh space.")]
    private float hangerRadius = 3f;

    [SerializeField, Tooltip("Off: hangers stay upright (Ferris wheel gondolas). On: they ride the part, facing along the circle (carousel horses).")]
    private bool hangersRideThePart;

    [SerializeField, Tooltip("Hanger up/down bob (metres) - carousel horses; 0 = none.")]
    private float hangerBob;

    [SerializeField]
    private float hangerBobSpeed = 2.2f;

    [Header("Path")]
    [SerializeField, Tooltip("Track centreline in this root's local space.")]
    private Vector3[] pathPoints = new Vector3[0];

    [SerializeField, Tooltip("Car models (cycled), +X forward, pivot on the rail centre.")]
    private GameObject[] carModels;

    [SerializeField, Min(0)]
    private int carCount = 3;

    [SerializeField]
    private float carGap = 0.72f;

    [SerializeField, Tooltip("Average speed along the track (m/s).")]
    private float pathSpeed = 2.2f;

    [Header("Drop")]
    [SerializeField]
    private float lowOffset = -4.3f;

    [SerializeField]
    private float highOffset = 2.8f;

    [SerializeField]
    private float riseTime = 5f;

    [SerializeField]
    private float holdTime = 1.2f;

    [SerializeField]
    private float fallTime = 0.55f;

    [SerializeField]
    private float restTime = 2.2f;

    private Renderer partRenderer;
    private Quaternion baseRotation;
    private Vector3 basePosition;
    private Vector3 axisInParent;
    private Transform[] hangers;
    private Vector3[] anchors;
    private Transform[] cars;
    private float[] cumulative;
    private float phase;

    private void Start()
    {
        if (part == null)
        {
            enabled = false;
            return;
        }

        partRenderer = part.GetComponentInChildren<Renderer>();
        baseRotation = part.localRotation;
        basePosition = part.localPosition;

        // different rides of the same kind never move in lockstep
        var p = transform.position;
        phase = Mathf.Repeat(p.x * 0.731f + p.z * 1.379f, 60f);

        var meshFilter = part.GetComponentInChildren<MeshFilter>();
        var axis = meshAxis;
        if (axis == Vector3.zero)
            axis = ThinnestAxis(meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.bounds.size : Vector3.one);
        axisInParent = (part.localRotation * axis).normalized;

        if (motion == Motion.Spin && hangerModels != null && hangerModels.Length > 0 && hangerCount > 0)
            CreateHangers(axis);
        if (motion == Motion.Path)
            CreateCars();
    }

    private void Update()
    {
        if (partRenderer != null && !partRenderer.isVisible)
            return;

        var t = Time.time + phase;
        if (motion == Motion.Spin)
        {
            part.localRotation = Quaternion.AngleAxis(t * degreesPerSecond, axisInParent) * baseRotation;
            if (hangers != null)
            {
                for (var i = 0; i < hangers.Length; i++)
                {
                    if (hangersRideThePart)                          // children of the part: only the bob is animated
                        hangers[i].localPosition = anchors[i] + Vector3.up * (hangerBob * Mathf.Sin(t * hangerBobSpeed + i * 1.7f));
                    else
                        hangers[i].position = part.TransformPoint(anchors[i]);   // rotation stays the root's: upright
                }
            }
        }
        else if (motion == Motion.Path)
        {
            UpdateCars(t);
        }
        else
        {
            part.localPosition = basePosition + Vector3.up * DropOffset(t);
        }
    }

    // slow eased rise -> hold at the top -> fast (accelerating) fall -> decaying bounce at the bottom
    private float DropOffset(float t)
    {
        var cycle = riseTime + holdTime + fallTime + restTime;
        var u = Mathf.Repeat(t, cycle);
        if (u < riseTime)
            return Mathf.Lerp(lowOffset, highOffset, Mathf.SmoothStep(0f, 1f, u / riseTime));
        u -= riseTime;
        if (u < holdTime)
            return highOffset;
        u -= holdTime;
        if (u < fallTime)
        {
            var f = u / fallTime;
            return Mathf.Lerp(highOffset, lowOffset, f * f);
        }
        u -= fallTime;
        return lowOffset + Mathf.Abs(Mathf.Sin(u * 7f)) * 0.35f * Mathf.Exp(-u * 2.5f);
    }

    private void CreateCars()
    {
        if (carModels == null || carModels.Length == 0 || pathPoints == null || pathPoints.Length < 2 || carCount <= 0)
            return;
        cumulative = new float[pathPoints.Length];
        for (var i = 1; i < pathPoints.Length; i++)
            cumulative[i] = cumulative[i - 1] + Vector3.Distance(pathPoints[i - 1], pathPoints[i]);
        cars = new Transform[carCount];
        for (var i = 0; i < carCount; i++)
        {
            var model = carModels[i % carModels.Length];
            var go = Instantiate(model, transform);
            go.transform.localScale = model.transform.localScale;
            go.layer = gameObject.layer;
            cars[i] = go.transform;
        }
    }

    // shuttle: the train runs to the far end and back, cosine-eased so it slows at both ends
    private void UpdateCars(float t)
    {
        if (cars == null)
            return;
        var length = cumulative[cumulative.Length - 1];
        var trainLength = carGap * (cars.Length - 1);
        var run = Mathf.Max(length - trainLength - 0.2f, 0.1f);
        var u = Mathf.PingPong(t * pathSpeed / run, 1f);
        var head = 0.1f + run * (0.5f - 0.5f * Mathf.Cos(Mathf.PI * u)) + trainLength;
        for (var i = 0; i < cars.Length; i++)
        {
            SamplePath(head - i * carGap, out var pos, out var tangent);
            var up = Vector3.Cross(Vector3.forward, tangent);
            if (up.sqrMagnitude < 1e-4f)
                up = Vector3.up;
            cars[i].localPosition = pos;
            cars[i].localRotation = Quaternion.LookRotation(tangent, up) * Quaternion.Euler(0f, -90f, 0f) * carModels[i % carModels.Length].transform.localRotation;
        }
    }

    private void SamplePath(float s, out Vector3 position, out Vector3 tangent)
    {
        var n = pathPoints.Length;
        s = Mathf.Clamp(s, 0f, cumulative[n - 1]);
        var i = 1;
        while (i < n - 1 && cumulative[i] < s)
            i++;
        var seg = Mathf.Max(cumulative[i] - cumulative[i - 1], 1e-5f);
        position = Vector3.Lerp(pathPoints[i - 1], pathPoints[i], (s - cumulative[i - 1]) / seg);
        tangent = (pathPoints[i] - pathPoints[i - 1]).normalized;
    }

    private void CreateHangers(Vector3 axis)
    {
        // the two mesh axes perpendicular to the spin axis span the rim's plane
        var a = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        var b = Vector3.Cross(axis, a).normalized;
        hangers = new Transform[hangerCount];
        anchors = new Vector3[hangerCount];
        for (var i = 0; i < hangerCount; i++)
        {
            var angle = 2f * Mathf.PI * i / hangerCount;
            anchors[i] = (a * Mathf.Sin(angle) + b * Mathf.Cos(angle)) * hangerRadius;
            var model = hangerModels[i % hangerModels.Length];
            GameObject go;
            if (hangersRideThePart)
            {
                go = Instantiate(model, part);                      // rides the part, facing along the circle
                go.transform.localPosition = anchors[i];
                go.transform.localRotation = Quaternion.LookRotation(Vector3.Cross(axis, anchors[i]).normalized, axis) * model.transform.localRotation;
            }
            else
            {
                go = Instantiate(model, transform);
                go.transform.localRotation = model.transform.localRotation;
                go.transform.position = part.TransformPoint(anchors[i]);
            }
            go.transform.localScale = model.transform.localScale;
            go.layer = gameObject.layer;
            hangers[i] = go.transform;
        }
    }

    private static Vector3 ThinnestAxis(Vector3 size)
    {
        if (size.x <= size.y && size.x <= size.z)
            return Vector3.right;
        return size.y <= size.z ? Vector3.up : Vector3.forward;
    }
}

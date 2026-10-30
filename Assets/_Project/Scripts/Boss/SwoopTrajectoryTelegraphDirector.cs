using System.Collections.Generic;
using Quantum;
using UnityEngine;

/// <summary>
/// 3D telegraph for SwoopDeliveryData (Dono do Céu's "Rasante do Capitão"): while a boss is aiming and diving, draws the
/// dive as a single wide ribbon lying flat (horizontal across the path) like a ramp: from the hovering boss's feet, in a
/// straight line down to the impact point on the target's floor, then along that floor to the landing - real height,
/// not a decal flattened onto the floor. It samples the same path the simulation moves the boss along
/// (SwoopDeliveryData.EvaluateDiveY) minus the resting pivot, so the flat part lies ON the floor exactly where the
/// dive hits. Strengthens over the aim; during the dive only the part
/// still ahead of the boss is drawn. The only telegraph of the swoop (SwoopDeliveryData.ShowGroundLaneWarning is off).
/// Self-installed, polls the predicted frame; nothing to place in a scene.
/// </summary>
public class SwoopTrajectoryTelegraphDirector : MonoBehaviour
{
    private static SwoopTrajectoryTelegraphDirector instance;

    [SerializeField] private int samples = 28;
    // Same red as every standard telegraph (ConeTelegraph*/CircleTelegraph sprites: 1,0,0 @ 0.39).
    [SerializeField] private Color color = new Color(1f, 0f, 0f, 1f);
    [SerializeField, Tooltip("Ribbon opacity at the start of the aim and once the dive is committed.")]
    private Vector2 alphaRange = new Vector2(0.25f, 0.39f);
    [SerializeField, Tooltip("Ribbon width as a fraction of the lane width (action DamageRange x 2).")]
    private float widthOfLane = 1f;
    [SerializeField] private float groundProbeHeight = 20f;
    [SerializeField, Tooltip("Lift off the floor for the flat part, against z-fighting.")]
    private float floorLift = 0.06f;

    private Material _material;
    private readonly Dictionary<EntityRef, LineRenderer> _lines = new();
    private readonly HashSet<EntityRef> _seen = new();
    private readonly List<EntityRef> _stale = new();
    private Vector3[] _points = System.Array.Empty<Vector3>();
    // Reused every frame; colour keys are fixed (Awake), only the alpha keys follow the aim's strength.
    private readonly Gradient _gradient = new Gradient();
    private readonly GradientColorKey[] _colorKeys = new GradientColorKey[2];
    private readonly GradientAlphaKey[] _alphaKeys = new GradientAlphaKey[3];
    private int _groundMask;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null)
            return;

        var go = new GameObject(nameof(SwoopTrajectoryTelegraphDirector));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SwoopTrajectoryTelegraphDirector>();
    }

    private void Awake()
    {
        _material = new Material(Shader.Find("Sprites/Default"));
        _colorKeys[0] = new GradientColorKey(color, 0f);
        _colorKeys[1] = new GradientColorKey(color, 1f);
        _groundMask = UnityEngine.LayerMask.GetMask("Ground");
    }

    private void LateUpdate()
    {
        QuantumGame game = QuantumRunner.Default != null ? QuantumRunner.Default.Game : null;
        Frame frame = game?.Frames.Predicted;
        _seen.Clear();

        if (frame != null)
        {
            var swoops = frame.Filter<Enemy, SwoopState, Transform3D>();
            while (swoops.Next(out EntityRef boss, out Enemy enemy, out SwoopState state, out Transform3D transform))
            {
                if (state.Stage != SwoopDeliveryData.StageAim && state.Stage != SwoopDeliveryData.StageDive)
                    continue;

                EnemyActionData action = EnemyDecisionUtility.ResolveAction(frame, frame.FindAsset(enemy.EnemyData), enemy.CurrentActionSlot);
                if (action == null || frame.FindAsset(action.Delivery) is not SwoopDeliveryData config)
                    continue;

                _seen.Add(boss);
                Draw(boss, state, transform, config, action);
            }
        }

        _stale.Clear();
        foreach (var pair in _lines)
        {
            if (_seen.Contains(pair.Key) == false)
                _stale.Add(pair.Key);
        }

        for (int i = 0; i < _stale.Count; i++)
        {
            if (_lines[_stale[i]] != null)
                Destroy(_lines[_stale[i]].gameObject);
            _lines.Remove(_stale[i]);
        }
    }

    private void Draw(EntityRef boss, SwoopState state, Transform3D transform, SwoopDeliveryData config, EnemyActionData action)
    {
        if (_lines.TryGetValue(boss, out LineRenderer line) == false || line == null)
        {
            line = CreateLine("SwoopTrajectory");
            _lines[boss] = line;
        }

        // Aim: the boss holds its hover point, so the dive starts where it is now. Dive: the recorded start.
        Vector3 start = state.Stage == SwoopDeliveryData.StageDive ? state.DiveStart.ToUnityVector3() : transform.Position.ToUnityVector3();
        Vector3 end = state.DiveEnd.ToUnityVector3();
        float pivot = state.PivotOffset.AsFloat;
        float impactDistance = state.ImpactDistance.AsFloat;

        if (_points.Length != samples)
            _points = new Vector3[samples];

        Vector3 flatStart = new Vector3(start.x, 0f, start.z);
        Vector3 flatEnd = new Vector3(end.x, 0f, end.z);

        // Dive: only what is still ahead of the boss.
        float from = 0f;
        float length = Vector3.Distance(flatStart, flatEnd);
        if (state.Stage == SwoopDeliveryData.StageDive && length > 0.01f)
        {
            Vector3 position = transform.Position.ToUnityVector3();
            from = Mathf.Clamp01(Vector3.Distance(flatStart, new Vector3(position.x, 0f, position.z)) / length);
        }

        // Same path as SwoopDeliveryData.EvaluateDiveY, at the feet: a straight line from the boss down to the impact
        // floor, then on the floor under each sample.
        Vector3 direction = state.HoverPoint.ToUnityVector3();
        float impactGround = ProbeGround(flatStart + direction * impactDistance, end.y - pivot);

        for (int i = 0; i < samples; i++)
        {
            float t = Mathf.Lerp(from, 1f, i / (float)(samples - 1));
            Vector3 flat = Vector3.Lerp(flatStart, flatEnd, t);
            float traveled = t * length;
            float feet = traveled < impactDistance
                ? Mathf.Lerp(start.y - pivot, impactGround, traveled / impactDistance)
                : ProbeGround(flat, end.y - pivot);
            _points[i] = new Vector3(flat.x, feet + floorLift, flat.z);
        }

        // Fills in over the aim, full once the dive is committed.
        float progress = state.Stage == SwoopDeliveryData.StageAim && state.StageDuration.AsFloat > 0f
            ? 1f - Mathf.Clamp01(state.StageTimer.AsFloat / state.StageDuration.AsFloat)
            : 1f;
        ApplyLine(line, action.DamageRange.AsFloat * 2f * widthOfLane, Mathf.Lerp(alphaRange.x, alphaRange.y, progress));
    }

    private float ProbeGround(Vector3 flat, float fallback)
    {
        if (Physics.Raycast(new Vector3(flat.x, groundProbeHeight, flat.z), Vector3.down, out RaycastHit hit, groundProbeHeight * 2f, _groundMask))
            return hit.point.y;

        return fallback;
    }

    private LineRenderer CreateLine(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        // TransformZ alignment with Z pointing up: the ribbon's width stays horizontal along the whole path, so the
        // descent reads as a ramp instead of a camera-facing band twisting where the slope changes.
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = _material;
        line.useWorldSpace = true;
        line.numCapVertices = 4;
        line.numCornerVertices = 4;
        line.alignment = LineAlignment.TransformZ;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = -1; // under the boss/hero sprites it passes through
        return line;
    }

    private void ApplyLine(LineRenderer line, float width, float alpha)
    {
        line.positionCount = _points.Length;
        line.SetPositions(_points);

        line.widthMultiplier = width;
        // Softer at the boss end so it reads as "from him, down to there".
        _alphaKeys[0] = new GradientAlphaKey(alpha * 0.6f, 0f);
        _alphaKeys[1] = new GradientAlphaKey(alpha, 0.3f);
        _alphaKeys[2] = new GradientAlphaKey(alpha, 1f);
        _gradient.SetKeys(_colorKeys, _alphaKeys);
        line.colorGradient = _gradient;
    }
}

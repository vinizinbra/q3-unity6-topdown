using NaughtyAttributes;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

// Presentation for a SpikeTrap entity (see SpikeTrap.qtn / SpikeTrapSystem). Builds its own visual at
// runtime from the trap's HalfExtents, so one component fits any trap size, map entity or spawned: a grid
// of SOCKETS (one low bevelled stone base per spike, edge to edge) with one tall pyramid
// spike each. Spikes sit hidden in their socket (only a dark hole shows) while Armed, peek out during
// Warning (with an optional red telegraph pulsing on top), snap up when Extended, and sink back during
// Cooldown. Meshes are vertex-coloured with alpha 1, the ToonTerrain "prop" convention, so they render with
// the active tileset's material (same shading + outline as the biome's own props); fallbackMaterial
// covers a scene with no tileset override.
public class SpikeTrapView : CustomQuantumEntityViewComponent
{
    [Header("Layout")]
    [SerializeField, Tooltip("Distance between sockets (one spike each).")]
    private float socketSpacing = 1f;
    [SerializeField, Range(0.3f, 1f), Tooltip("Socket base width as a fraction of the spacing - 1 = bases touch (no ground between them).")]
    private float socketSize = 0.8f;
    [SerializeField] private float socketHeight = 0.14f;
    [SerializeField] private float spikeHeight = 1f;
    [SerializeField, Tooltip("Spike base width in metres - independent of the socket size.")]
    private float spikeWidth = 0.25f;
    [SerializeField, Tooltip("Dark hole size relative to the spike's base - bigger = more contrast on the socket.")]
    private float holeScale = 1.8f;

    [Header("Motion")]
    [SerializeField, Range(0f, 1f), Tooltip("How much of the spike shows during Warning.")]
    private float warningPeek = 0.3f;
    [SerializeField] private float extendSpeed = 10f;
    [SerializeField] private float retractSpeed = 0.1f;

    [Header("Feedback")]
    [SerializeField, Tooltip("Optional - pulsed while the trap is in Warning.")]
    private SpriteRenderer warningTelegraph;
    [SerializeField] private SoundData extendSound;
    [SerializeField] private float shakeImpact = 0.35f;

    [Header("Look")]
    [SerializeField] private Material fallbackMaterial;
    // The toon shader lifts darks a lot, so these sit low on purpose.
    [SerializeField] private Color socketTopColor = new Color32(0x6E, 0x64, 0x5A, 0xFF);
    [SerializeField] private Color socketSideColor = new Color32(0x4A, 0x42, 0x3A, 0xFF);
    [SerializeField] private Color holeColor = new Color32(0x00, 0x00, 0x00, 0xFF);
    [SerializeField] private Color spikeColor = new Color32(0x1A, 0x1B, 0x1E, 0xFF); // charcoal / graphite
    [SerializeField] private Color spikeLitColor = new Color32(0x8A, 0x90, 0x9A, 0xFF); // the camera-facing face

    private Transform _spikes;
    private float _spikeOffset;
    private SpikeTrapPhase _lastPhase;
    private bool _built;

    protected override unsafe void QUpdate(QuantumGame game)
    {
        Frame f = game.Frames.Predicted;

        if (f == null || f.Unsafe.TryGetPointer<SpikeTrap>(_entityRef, out var trap) == false)
            return;

        // Built off the Predicted frame and retried until the component is there (a view can come up a
        // few ticks before its entity's components are readable online).
        if (_built == false)
            Build(trap->HalfExtents.X.AsFloat, trap->HalfExtents.Y.AsFloat);

        SpikeTrapPhase phase = trap->Phase;

        if (phase == SpikeTrapPhase.Extended && _lastPhase != SpikeTrapPhase.Extended)
            OnExtended();

        _lastPhase = phase;

        float hidden = -spikeHeight;
        float target = phase switch
        {
            SpikeTrapPhase.Warning => hidden * (1f - warningPeek),
            SpikeTrapPhase.Extended => 0f,
            _ => hidden,
        };

        float speed = target > _spikeOffset ? extendSpeed : retractSpeed;
        _spikeOffset = Mathf.MoveTowards(_spikeOffset, target, speed * Time.deltaTime);
        _spikes.localPosition = new Vector3(0f, _spikeOffset, 0f);

        if (warningTelegraph != null)
        {
            warningTelegraph.enabled = phase == SpikeTrapPhase.Warning;
            Color color = warningTelegraph.color;
            color.a = 0.3f + 0.3f * Mathf.PingPong(Time.time * 6f, 1f);
            warningTelegraph.color = color;
        }
    }

    private void OnExtended()
    {
        if (extendSound != null)
            AudioManager.PlayAt(extendSound, transform.position);

        if (FollowCamera.I != null)
            FollowCamera.I.ShakeAtPosition(transform.position, shakeImpact);
    }

    // ------------------------------------------------------------- tuning buttons

    // Rebuilds the visual with the current Inspector values. Play mode: right away, keeping the current
    // spike state. Edit mode: a preview (spikes up) sized from this object's QPrototypeSpikeTrap, flagged
    // DontSave so it never ends up in the scene file.
    [Button("Regenerate Spikes")]
    public unsafe void Regenerate()
    {
        if (Application.isPlaying)
        {
            Frame f = _game != null ? _game.Frames.Predicted : null;

            if (f != null && f.Unsafe.TryGetPointer<SpikeTrap>(_entityRef, out var trap))
            {
                float offset = _spikeOffset;
                Build(trap->HalfExtents.X.AsFloat, trap->HalfExtents.Y.AsFloat);
                _spikeOffset = offset;
                _spikes.localPosition = new Vector3(0f, _spikeOffset, 0f);
            }

            return;
        }

        var prototype = GetComponent<QPrototypeSpikeTrap>();

        if (prototype == null)
        {
            LogHelper.Warn("SpikeTrapView", $"{name}: no QPrototypeSpikeTrap to size the preview from.", this);
            return;
        }

        Build(prototype.Prototype.HalfExtents.X.AsFloat, prototype.Prototype.HalfExtents.Y.AsFloat);
        _spikeOffset = 0f;
        _spikes.localPosition = Vector3.zero;

        foreach (Transform root in new[] { transform.Find(SocketsName), _spikes })
        {
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                part.gameObject.hideFlags = HideFlags.DontSave;
        }
    }

    [Button("Toggle Preview Up/Down")]
    private void TogglePreview()
    {
        if (_spikes == null)
            Regenerate();

        if (_spikes == null)
            return;

        _spikeOffset = _spikeOffset < 0f ? 0f : -spikeHeight;
        _spikes.localPosition = new Vector3(0f, _spikeOffset, 0f);
    }

    [Button("Clear Preview")]
    private void ClearPreview()
    {
        if (Application.isPlaying == false)
            DestroyBuilt();
    }

    private const string SocketsName = "Sockets";
    private const string SpikesName = "Spikes";

    private void DestroyBuilt()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;

            if (child.name != SocketsName && child.name != SpikesName)
                continue;

            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }

        _spikes = null;
        _built = false;
    }

    private void Build(float halfX, float halfZ)
    {
        DestroyBuilt();
        _built = true;

        Material material = TilesetPlatformBuilder.TilesetOverride != null && TilesetPlatformBuilder.TilesetOverride.Material != null
            ? TilesetPlatformBuilder.TilesetOverride.Material
            : fallbackMaterial;

        int countX = Mathf.Max(1, Mathf.RoundToInt(halfX * 2f / socketSpacing));
        int countZ = Mathf.Max(1, Mathf.RoundToInt(halfZ * 2f / socketSpacing));
        float stepX = halfX * 2f / countX;
        float stepZ = halfZ * 2f / countZ;
        float step = Mathf.Min(stepX, stepZ);

        var sockets = new GameObject(SocketsName).transform;
        sockets.SetParent(transform, false);
        _spikes = new GameObject(SpikesName).transform;
        _spikes.SetParent(transform, false);

        Mesh socketMesh = BuildSocketMesh(spikeWidth / step);
        Mesh spikeMesh = BuildSpikeMesh();

        for (int ix = 0; ix < countX; ix++)
        {
            for (int iz = 0; iz < countZ; iz++)
            {
                var center = new Vector3(-halfX + stepX * (ix + 0.5f), 0f, -halfZ + stepZ * (iz + 0.5f));
                // Per-axis spacing so sockets stay edge to edge when the trap isn't a whole number of steps.
                AddPart(sockets, "Socket", socketMesh, material, center, new Vector3(stepX, socketHeight, stepZ));
                AddPart(_spikes, "Spike", spikeMesh, material, center + Vector3.up * socketHeight, new Vector3(spikeWidth, spikeHeight, spikeWidth));
            }
        }

        _spikeOffset = -spikeHeight;
        _spikes.localPosition = new Vector3(0f, _spikeOffset, 0f);
    }

    private static void AddPart(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ------------------------------------------------------------- meshes (unit spacing, height 1)

    // A low bevelled base (frustum) with the spike's dark hole on top.
    // holeWidth = the spike's base width as a fraction of the socket (unit) footprint.
    private Mesh BuildSocketMesh(float holeWidth)
    {
        var b = new MeshBuilder();
        float o = socketSize * 0.5f;
        float i = o * 0.68f;            // bevel: the top is noticeably smaller than the base
        float hole = Mathf.Min(holeWidth * 0.5f * holeScale, i * 0.9f);   // stays inside the socket top

        Vector3 Bottom(float x, float z) => new Vector3(x, 0f, z);
        Vector3 Top(float x, float z) => new Vector3(x, 1f, z);

        b.Quad(Bottom(-o, -o), Top(-i, -i), Top(i, -i), Bottom(o, -o), socketSideColor);
        b.Quad(Bottom(o, -o), Top(i, -i), Top(i, i), Bottom(o, o), Color.Lerp(socketSideColor, Color.black, 0.2f));
        b.Quad(Bottom(o, o), Top(i, i), Top(-i, i), Bottom(-o, o), Color.Lerp(socketSideColor, Color.black, 0.35f));
        b.Quad(Bottom(-o, o), Top(-i, i), Top(-i, -i), Bottom(-o, -o), Color.Lerp(socketSideColor, Color.black, 0.1f));

        b.Quad(Top(-i, -i), Top(-i, i), Top(i, i), Top(i, -i), socketTopColor);
        b.Quad(new Vector3(-hole, 1.01f, -hole), new Vector3(-hole, 1.01f, hole),
               new Vector3(hole, 1.01f, hole), new Vector3(hole, 1.01f, -hole), holeColor);

        return b.ToMesh("SpikeTrapSocket");
    }

    // One tall, slim pyramid (unit base, scaled to spikeWidth x spikeHeight); the camera-facing face is
    // lit steel so the charcoal shape stays readable.
    private Mesh BuildSpikeMesh()
    {
        var b = new MeshBuilder();
        const float half = 0.5f;
        var tip = Vector3.up;
        Vector3 a = new Vector3(-half, 0f, -half), b1 = new Vector3(half, 0f, -half);
        Vector3 b2 = new Vector3(half, 0f, half), d = new Vector3(-half, 0f, half);

        b.Tri(a, tip, b1, spikeLitColor);                                     // camera-facing (-Z)
        b.Tri(b1, tip, b2, spikeColor);
        b.Tri(b2, tip, d, Color.Lerp(spikeColor, Color.black, 0.5f));
        b.Tri(d, tip, a, Color.Lerp(spikeColor, spikeLitColor, 0.35f));

        return b.ToMesh("SpikeTrapSpike");
    }

    // Flat-shaded (unshared vertices) vertex-coloured mesh; alpha 1 = ToonTerrain prop, UV0 0 = not emissive.
    private class MeshBuilder
    {
        private readonly System.Collections.Generic.List<Vector3> _vertices = new();
        private readonly System.Collections.Generic.List<Color> _colors = new();
        private readonly System.Collections.Generic.List<int> _triangles = new();

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            int start = _vertices.Count;
            _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
            color.a = 1f;
            _colors.Add(color); _colors.Add(color); _colors.Add(color);
            _triangles.Add(start); _triangles.Add(start + 1); _triangles.Add(start + 2);
        }

        // Corners in clockwise order seen from the side the face points to.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            Tri(a, b, c, color);
            Tri(a, c, d, color);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetUVs(0, new Vector2[_vertices.Count]);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

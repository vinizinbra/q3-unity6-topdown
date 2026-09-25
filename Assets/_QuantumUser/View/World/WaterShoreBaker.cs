using NaughtyAttributes;
using Photon.Deterministic;
using Quantum.Physics3D;
using Quantum;
using QuantumUser.View;
using QuantumUser.View.Util;
using UnityEngine;

// Bakes a "distance to shore" field once per level so a water shader can draw shoreline foam/waves
// WITHOUT the URP camera depth texture (the expensive DepthNormals prepass LakeShader's
// _INTERSECTION_FOAM path needs). The level's landmass is static once generated, so distance-to-land
// is a fixed function of position - compute it once into a small texture, sample it per water
// fragment by world XZ. Same rasterize-every-Chunk-footprint step MinimapWidget uses for its outline
// (see docs/minimap.md), but baked ONCE globally (not per split-screen slot) since it feeds a world
// material, and refined per-texel by an actual ground-collider probe so interior HOLES in a chunk
// (which the declared ChunkSizeWidth/Depth rectangle would otherwise paint as solid land) correctly
// resolve to water.
//
// Output: one R8 texture where R = saturate(worldDistanceToNearestLand / maxShoreDistanceWorld),
// 0 exactly at the coast growing to 1 out in open water. Published as GLOBAL shader params
// (_ShoreField / _ShoreFieldParams) so every water material picks it up with no per-material
// assignment. To actually draw the foam, tick "Enable Shore Field Foam" on the water material (that
// compiles the _SHOREFIELD_FOAM shader variant - see LakeShader.shader).
//
// Place this on a single scene-global GameObject next to the water, NOT under the per-player HUD.
public class WaterShoreBaker : QuantumGlobalMonoBehaviour
{
    [Header("World mapping (match MinimapWidget's own worldExtent/worldCenter)")]
    [SerializeField, Tooltip("Half-size of the known playable world - the level fits inside +-worldExtent on X and Z. Same value MinimapWidget uses.")]
    private float worldExtent = 128f;
    [SerializeField, Tooltip("Center of the known playable world, in world X/Z. Same value MinimapWidget uses.")]
    private Vector2 worldCenter = Vector2.zero;

    [Header("Field resolution / reach")]
    [SerializeField, Tooltip("World units per texel. Finer than the minimap (which is deliberately chunky) so foam hugs the real coast - 1.5 gives a ~170px field at 128 extent (~30KB R8). Lower = sharper shore & holes, bigger texture & more probes.")]
    private float worldUnitsPerTexel = 1.5f;
    [SerializeField, Tooltip("World distance at which the field saturates to 1 (open water). Foam only ever appears within this band of the coast, so it also bounds how far the R8 precision has to stretch - keep it near the widest foam band you want (a few world units).")]
    private float maxShoreDistanceWorld = 12f;

    [Header("Collider solidity (handles holes inside a chunk)")]
    [SerializeField, Tooltip("When on, a footprint texel counts as land only if a Ground-layer collider is actually under it (EnemyMovementUtility.TryFindGroundHeight) - so holes/gaps inside a chunk become water. Off = fill the whole ChunkSizeWidth/Depth rectangle (faster, but ignores holes).")]
    private bool useColliderTest = true;
    [SerializeField, Tooltip("World Y the downward ground probe originates near (the probe covers +-20 around it). Keep close to the level's floor height; 0 is fine for a near-flat level.")]
    private float probeHeightY = 0f;

    [Header("Load-time budget")]
    [SerializeField, Tooltip("Main-thread milliseconds the collider probe pass may spend per frame. The pass runs once, right as the level finishes generating - i.e. behind the loading screen - and done in one go it was the single biggest frame of the whole match start (~0.8s for ~29k probes). Spread over frames, the loading bar keeps animating and shows the bake's real progress (see BakeProgress). Rebake ignores this and runs to completion.")]
    private float probeBudgetMs = 10f;
    [SerializeField, Tooltip("Per-frame probe budget while a loading screen is covering the game (see IsLoadingScreenUp). Behind it only the loading bar has to keep animating, so the bake can take much bigger slices and finish sooner - which matters on slow mobile devices, where the loading screen waits for this bake before handing off.")]
    private float coveredProbeBudgetMs = 30f;

    // Set by LoadingWindow while it's up. Lives here rather than being read off the window, so this
    // View-side baker never depends on a menu type.
    public static bool IsLoadingScreenUp { get; set; }

    // Matches EnemyMovementUtility.TryFindGroundHeight's own +-20 probe range.
    private const float ProbeHalfRange = 20f;
    private const QueryOptions ProbeQueryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics | QueryOptions.HitDynamics;

    private Texture2D _field;
    private int _res;
    private bool _baked;

    // In-progress bake, carried across frames. _probeRow < 0 = not started yet.
    private bool[] _candidate;
    private bool[] _land;
    private int _probeRow = -1;

    // Cached IDs - Shader.SetGlobal* every re-bake, but resolve the property IDs once.
    private static readonly int ShoreFieldId = Shader.PropertyToID("_ShoreField");
    private static readonly int ShoreFieldParamsId = Shader.PropertyToID("_ShoreFieldParams");

    private static WaterShoreBaker _active;

    // True while a baker exists in the loaded scene and hasn't produced its field yet - read by
    // LoadingWindow, whose bar would otherwise hit "world built" before the probe pass has run. No
    // baker in the scene = nothing pending.
    public static bool IsBakePending => _active != null && _active._baked == false;

    // 0..1 progress of the (time-sliced) probe pass, for LoadingWindow's bar. 1 with no baker.
    public static float BakeProgress
    {
        get
        {
            if (_active == null || _active._baked)
                return 1f;

            if (_active._probeRow < 0 || _active._res <= 0)
                return 0f;

            return Mathf.Clamp01(_active._probeRow / (float)_active._res);
        }
    }

    private void Awake()
    {
        _active = this;
    }

    private void OnDestroy()
    {
        if (_active == this)
            _active = null;
    }

    public override void QUpdate(QuantumGame game)
    {
        if (_baked)
            return;

        // Same gate as MinimapWidget's outline: baking off a partially-populated chunk set would
        // lock in a wrong coastline forever, since it only ever runs once. See docs/minimap.md.
        Frame frame = game.Frames.Verified ?? game.Frames.Predicted;
        if (frame == null)
            return;

        UnityEngine.Profiling.Profiler.BeginSample("WaterShoreBaker.Bake");
        if (StepBake(frame, IsLoadingScreenUp ? coveredProbeBudgetMs : probeBudgetMs))
            _baked = true;
        UnityEngine.Profiling.Profiler.EndSample();
    }

    // Re-run the bake against the current live frame - lets worldUnitsPerTexel/maxShoreDistanceWorld/
    // probe settings be tweaked in Play Mode and seen immediately (same live-iteration idea
    // ChunkDetailScatter.Regenerate / LakeVisualBuilder.Generate expose). Play Mode only - there's no
    // frame to probe in Edit Mode. Runs to completion in one go, unlike the load-time bake.
    [Button("Rebake Shore Field")]
    public void Rebake()
    {
        if (_game == null)
        {
            LogHelper.Warn("WaterShoreBaker", "No live Quantum game - enter Play Mode to rebake.", this);
            return;
        }

        Frame frame = _game.Frames.Verified ?? _game.Frames.Predicted;
        if (frame == null)
            return;

        _probeRow = -1;
        if (StepBake(frame, float.PositiveInfinity))
            _baked = true;
    }

    // Advances the bake by up to budgetMs of probing; true once the field is built and published.
    // Every step probes whatever frame is current - the level's colliders are static once
    // generated, so which frame a given row was probed against doesn't matter.
    private unsafe bool StepBake(Frame frame, float budgetMs)
    {
        if (_probeRow < 0)
        {
            if (frame.Global->LevelGenerated == false)
                return false;

            BeginBake(frame);
        }

        // 2. Refine to actual land. With the collider test, a candidate texel is land only if a
        //    Ground collider is under it, so interior holes fall through to water. Probing only
        //    candidate texels keeps the raycast count ~ land area, not the whole grid.
        if (useColliderTest)
        {
            int groundMask = EnemyMovementUtility.GetGroundLayerMask(frame);
            long budgetTicks = float.IsPositiveInfinity(budgetMs)
                ? long.MaxValue
                : (long)(budgetMs * 0.001 * System.Diagnostics.Stopwatch.Frequency);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();

            while (_probeRow < _res)
            {
                int row = _probeRow * _res;
                for (int x = 0; x < _res; x++)
                {
                    if (_candidate[row + x] && IsSolidGround(frame, x, _probeRow, groundMask))
                        _land[row + x] = true;
                }

                _probeRow++;

                if (System.Diagnostics.Stopwatch.GetTimestamp() - start >= budgetTicks)
                    break;
            }

            if (_probeRow < _res)
                return false;
        }
        else
        {
            _land = _candidate;
            _probeRow = _res;
        }

        FinishBake();
        return true;
    }

    // 1. Rasterize every chunk footprint into a candidate grid - the OUTER bound of where land can
    //    be (chunks are min-corner pivoted, never rotated - same as MinimapWidget). Colliders live
    //    inside the footprint, so anything outside every footprint is guaranteed water and never
    //    needs probing.
    private void BeginBake(Frame frame)
    {
        float scale = 1f / worldUnitsPerTexel;
        _res = Mathf.Max(Mathf.CeilToInt(worldExtent * 2f * scale), 1);

        _candidate = new bool[_res * _res];
        var chunks = frame.Filter<Chunk, Transform3D>();
        while (chunks.Next(out EntityRef _, out Chunk chunk, out Transform3D transform))
            RasterizeFootprint(chunk, transform, _candidate, scale);

        _land = new bool[_res * _res];
        _probeRow = 0;
    }

    private void FinishBake()
    {
        // 3. Chamfer distance transform: distance (in texels) from each water cell to nearest land.
        float[] dist = ChamferDistanceToLand(_land);

        // 4. Encode saturate(worldDist / maxShoreDistanceWorld) into R8.
        _field = new Texture2D(_res, _res, TextureFormat.R8, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var px = new Color32[_res * _res];
        float invMax = 1f / Mathf.Max(maxShoreDistanceWorld, 0.001f);
        for (int i = 0; i < px.Length; i++)
        {
            float worldDist = dist[i] * worldUnitsPerTexel;
            byte r = (byte)(Mathf.Clamp01(worldDist * invMax) * 255f);
            px[i] = new Color32(r, 0, 0, 255);
        }
        _field.SetPixels32(px);
        _field.Apply(false);

        // 5. Publish globally - uv = (worldXZ - center) / size + 0.5; worldDist = R * maxDist.
        Shader.SetGlobalTexture(ShoreFieldId, _field);
        Shader.SetGlobalVector(ShoreFieldParamsId,
            new Vector4(worldCenter.x, worldCenter.y, worldExtent * 2f, maxShoreDistanceWorld));

        _candidate = null;
        _land = null;
    }

    // Downward ground probe at this texel's world-center - true if a Ground collider is under it.
    // A single closest-hit Raycast rather than EnemyMovementUtility.TryFindGroundHeight's
    // RaycastAll + Sort: that allocates a hit collection on the frame heap per call, which across
    // ~29k probes cost more than the raycasts themselves (plus a matching Free spike on the next
    // simulated tick). Same range, mask and options, so "solid here" still means what it means to
    // the simulation. The one thing TryFindGroundHeight also does - skipping enemies/players - only
    // matters if one is standing over the texel, so that rare case falls back to it.
    private bool IsSolidGround(Frame frame, int tx, int ty, int groundMask)
    {
        float worldX = (tx + 0.5f - _res * 0.5f) * worldUnitsPerTexel + worldCenter.x;
        float worldZ = (ty + 0.5f - _res * 0.5f) * worldUnitsPerTexel + worldCenter.y;
        FPVector3 origin = new Vector3(worldX, probeHeightY + ProbeHalfRange, worldZ).ToFPVector3();

        Hit3D? hit = frame.Physics3D.Raycast(origin, FPVector3.Down, FP.FromFloat_UNSAFE(ProbeHalfRange * 2f), groundMask, ProbeQueryOptions);
        if (hit.HasValue == false)
            return false;

        EntityRef hitEntity = hit.Value.Entity;
        if (hitEntity != EntityRef.None && (frame.Has<Enemy>(hitEntity) || frame.Has<PlayerLink>(hitEntity)))
        {
            FPVector3 probe = new Vector3(worldX, probeHeightY, worldZ).ToFPVector3();
            return EnemyMovementUtility.TryFindGroundHeight(frame, probe, groundMask, out _);
        }

        return true;
    }

    private void RasterizeFootprint(Chunk chunk, Transform3D transform, bool[] land, float scale)
    {
        float minX = transform.Position.X.AsFloat;
        float minZ = transform.Position.Z.AsFloat;
        float maxX = minX + chunk.ChunkSizeWidth;
        float maxZ = minZ + chunk.ChunkSizeDepth;

        Vector2Int min = WorldToTexel(minX, minZ, scale);
        Vector2Int max = WorldToTexel(maxX, maxZ, scale);

        int x0 = Mathf.Clamp(Mathf.Min(min.x, max.x), 0, _res);
        int y0 = Mathf.Clamp(Mathf.Min(min.y, max.y), 0, _res);
        int x1 = Mathf.Clamp(Mathf.Max(min.x, max.x), 0, _res);
        int y1 = Mathf.Clamp(Mathf.Max(min.y, max.y), 0, _res);

        for (int y = y0; y < y1; y++)
        {
            int row = y * _res;
            for (int x = x0; x < x1; x++)
                land[row + x] = true;
        }
    }

    private Vector2Int WorldToTexel(float worldX, float worldZ, float scale)
    {
        return new Vector2Int(
            Mathf.RoundToInt((worldX - worldCenter.x) * scale + _res * 0.5f),
            Mathf.RoundToInt((worldZ - worldCenter.y) * scale + _res * 0.5f));
    }

    // Two-pass chamfer (1, sqrt2) distance transform - land texels are 0, every other texel gets the
    // distance to the nearest land texel in texel units. O(n), exact enough for foam. Runs once at
    // bake time so the per-pass cost never matters.
    private float[] ChamferDistanceToLand(bool[] land)
    {
        const float ORTH = 1f;
        const float DIAG = 1.41421356f;
        float inf = _res * 2f;

        var d = new float[_res * _res];
        for (int i = 0; i < d.Length; i++)
            d[i] = land[i] ? 0f : inf;

        // Forward pass: top-left -> bottom-right.
        for (int y = 0; y < _res; y++)
        {
            for (int x = 0; x < _res; x++)
            {
                int i = y * _res + x;
                if (d[i] == 0f)
                    continue;

                float v = d[i];
                if (x > 0) v = Mathf.Min(v, d[i - 1] + ORTH);
                if (y > 0)
                {
                    v = Mathf.Min(v, d[i - _res] + ORTH);
                    if (x > 0) v = Mathf.Min(v, d[i - _res - 1] + DIAG);
                    if (x < _res - 1) v = Mathf.Min(v, d[i - _res + 1] + DIAG);
                }
                d[i] = v;
            }
        }

        // Backward pass: bottom-right -> top-left.
        for (int y = _res - 1; y >= 0; y--)
        {
            for (int x = _res - 1; x >= 0; x--)
            {
                int i = y * _res + x;
                if (d[i] == 0f)
                    continue;

                float v = d[i];
                if (x < _res - 1) v = Mathf.Min(v, d[i + 1] + ORTH);
                if (y < _res - 1)
                {
                    v = Mathf.Min(v, d[i + _res] + ORTH);
                    if (x < _res - 1) v = Mathf.Min(v, d[i + _res + 1] + DIAG);
                    if (x > 0) v = Mathf.Min(v, d[i + _res - 1] + DIAG);
                }
                d[i] = v;
            }
        }

        return d;
    }
}

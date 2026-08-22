using System.Collections.Generic;
using Quantum;
using QuantumUser.View;
using UnityEngine;
using UnityEngine.UI;

// Node-based minimap - the static layout (one filled block per Chunk entity, at that chunk's real
// footprint) is baked into a single procedurally-painted Texture2D/RawImage rather than one UI
// Image per chunk. World bounds are fixed/authored (worldExtent), not scanned from placed chunks -
// the level is known to fit inside +-worldExtent, so the texture can be sized and the
// world-to-texel scale derived up front, with zero dependency on how/when chunks actually appear.
// worldUnitsPerTexel (not 1:1) keeps the texture tiny and chunky-pixel-art-looking (e.g. a 20-unit
// chunk becomes a 2x2 block at the default 10).
//
// Level outline: since the level's chunk layout is static once generated, the entire level's
// outline (every occupied texel with an unoccupied one nearby, computed on the rasterized
// occupancy grid - see ComputeLevelOutline) is computed exactly ONCE into a texel mask, using
// every chunk regardless of Discovered state, so the outline's own shape never shifts as more of
// the level gets explored. RepaintSingleChunk then only stamps outlineColor on top for a chunk
// that's actually Discovered (or the current chunk, always Discovered by definition) - so the
// outline reveals progressively alongside discovery, same as everything else on the map.
//
// FilterMode.Point (no bilinear smoothing) gives the flat, blocky look. A handful of small icon
// overlays (Boss/Merchant/LobbyStart - see chunkTypeSprites; deliberately left unassigned for
// Enemy/Traversal) sit on top as separate lightweight UI Images - cheap, since there are only ever
// a few of these per level - plus one live marker per match player.
//
// TWO map surfaces share this one widget: the panned/masked minimap (mapRect) and an optional
// full-map panel (fullMapImage/fullMapRect) showing the whole level at once. The TEXTURE is
// literally shared - both RawImages point at the same Texture2D, so painting updates both for
// free - but icons and player markers are real UI objects, so each surface gets its own clone of
// each, held together in an OverlayPair and driven in lockstep. The only per-surface difference is
// the rect their positions are computed against (the two draw the same texture at different UI
// sizes) plus fullMapOverlayScale. Fully self-contained (reads
// game.Frames.Predicted every QUpdate, same "no external driving" shape as StatusEffectsManager)
// except for localSlotIndex, needed to know which local player's current chunk to highlight - every
// instance otherwise runs the identical frame query regardless of which split-screen slot it lives
// under, so "one per local player" is purely a scene-hierarchy placement concern. See
// docs/minimap.md.
public class MinimapWidget : QuantumGlobalMonoBehaviour
{
    [Header("World mapping")]
    [SerializeField, Tooltip("Half-size of the known playable world - the level is authored to fit inside +-worldExtent on both X and Z (e.g. 128 for a -128..+128 world). No chunk scanning needed since this is a known constant.")]
    private float worldExtent = 128f;
    [SerializeField, Tooltip("Center of the known playable world, in world X/Z.")]
    private Vector2 worldCenter = Vector2.zero;
    [SerializeField, Tooltip("World units per texel - the actual texture resolution (derived at QStart) is (worldExtent*2)/worldUnitsPerTexel. E.g. 10 means a 20x20 chunk paints as a 2x2 block.")]
    private float worldUnitsPerTexel = 10f;

    [Header("View")]
    [SerializeField, Tooltip("Content layer holding the texture/icons/markers - square, centered (0.5, 0.5) pivot. Panned every frame (CenterOnLocalPlayer) to keep this instance's own local player centered, so nest it inside a separate masked container that defines the actual visible viewport and clips whatever overflows.")]
    private RectTransform mapRect;
    [SerializeField, Tooltip("Displays the procedurally-painted map texture - should be sized to fill mapRect.")]
    private RawImage mapImage;
    [SerializeField, Tooltip("Optional - a second RawImage (e.g. the Tab-key full-map panel) shown the WHOLE level at once, unpanned/unmasked. Gets the exact same live texture as mapImage, so it updates automatically with no extra work. Leave unassigned if you only want the small minimap.")]
    private RawImage fullMapImage;
    [SerializeField, Tooltip("Optional content layer for the full-map panel's own icon/player overlays - square, centered (0.5, 0.5) pivot, same size as fullMapImage. Left unassigned, fullMapImage's own RectTransform is used, which is correct as long as it's square and center-pivoted. Only matters when fullMapImage is assigned.")]
    private RectTransform fullMapRect;
    [SerializeField, Tooltip("Uniform scale applied to the full-map panel's own icon/player overlay clones - the big map is usually drawn much larger than the minimap, so its markers often want to be bigger (or smaller) than the shared prefab's authored size.")]
    private float fullMapOverlayScale = 1f;

    [Header("Colors")]
    [SerializeField] private Color undiscoveredColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    [SerializeField] private Color discoveredColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    [SerializeField] private Color currentColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField, Tooltip("Texels outside every chunk's footprint - transparent by default so the map only shows the level's actual shape.")]
    private Color backgroundColor = new Color(0f, 0f, 0f, 0f);

    [Header("Level outline")]
    [SerializeField, Tooltip("Drawn on top of a chunk's own fill along the level's real static boundary (see class comment) - only shown once that chunk is Discovered.")]
    private Color outlineColor = Color.black;
    [SerializeField, Tooltip("Outline stroke thickness in texels. 0 = no outline computed/drawn at all.")]
    private int outlineTexels = 0;

    [Header("Overlays")]
    [SerializeField, Tooltip("One sprite per ChunkType value, in enum order: LobbyStart, Enemy, Boss, Merchant, Traversal, HealingShrine, CursedRift, Blacksmith. Leave an entry unassigned for a type that shouldn't show an icon at all (e.g. Enemy, Traversal).")]
    private Sprite[] chunkTypeSprites;
    [SerializeField] private Image iconPrefab;
    [SerializeField, Tooltip("Simple colored-dot prefab (or similar) representing one player. No per-player identity beyond position for this first pass.")]
    private RectTransform playerMarkerPrefab;

    [Header("Local player")]
    [SerializeField, Tooltip("Which local player slot's current chunk this instance highlights (MyLocalPlayer.Slots index) - 0 for the first local player's own HUD instance, 1 for a second couch co-op player's.")]
    private int localSlotIndex;

    private readonly Dictionary<EntityRef, OverlayPair> _iconOverlays = new();
    private readonly Dictionary<EntityRef, OverlayPair> _playerMarkers = new();
    private readonly HashSet<EntityRef> _seenPlayersThisFrame = new();
    private List<EntityRef> _stalePlayerBuffer;

    // Resolved once in QStart - the full-map panel's own content layer (fullMapRect, or
    // fullMapImage's own RectTransform). Null whenever no full-map panel is wired up, which is
    // what every Full == null check below keys off.
    private RectTransform _fullOverlayRoot;

    // Last UI width each map surface was seen at. A chunk icon is positioned once at spawn, so a
    // surface that was laid out later (the full-map panel is typically inactive until first opened,
    // and can be zero-sized until then) would otherwise leave every icon stuck at its stale spot.
    // Player markers need none of this - they're repositioned every frame anyway.
    private float _lastMiniWidth = -1f;
    private float _lastFullWidth = -1f;

    // An overlay element (chunk-type icon or player marker) exists once per map surface: Mini
    // under mapRect (panned/masked), Full under _fullOverlayRoot (whole level at once). Both are
    // clones of the same prefab driven in lockstep from the same data - the ONLY difference is
    // which rect their position is computed against, since the two surfaces map the same texture
    // at different UI sizes. Full is null when no full-map panel exists.
    private sealed class OverlayPair
    {
        public RectTransform Mini;
        public RectTransform Full;

        // Chunk icons only - the texel rect they were positioned from, kept so they can be placed
        // again if a map surface's own UI size changes (see RefreshIconPositionsIfResized).
        public RectInt TexelRect;

        public void SetActive(bool active)
        {
            if (Mini != null)
                Mini.gameObject.SetActive(active);

            if (Full != null)
                Full.gameObject.SetActive(active);
        }

        public void Destroy()
        {
            if (Mini != null)
                UnityEngine.Object.Destroy(Mini.gameObject);

            if (Full != null)
                UnityEngine.Object.Destroy(Full.gameObject);
        }
    }

    // Cached per-chunk texel rect (computed once, on first sight) plus last-known Discovered value.
    private readonly Dictionary<EntityRef, RectInt> _chunkTexelRects = new();
    private readonly Dictionary<EntityRef, bool> _lastDiscovered = new();
    private EntityRef _lastCurrentChunk;

    // Precomputed once (see class comment) - true at a texel that lies on the level's static
    // outline. Null until ComputeLevelOutline has run.
    private bool[] _outlineMask;

    // Enclosed empty regions (the inner holes the gap-filler fills) - computed once alongside the
    // outline. Each is painted undiscoveredColor at first and reveals (repaints to discoveredColor)
    // only once one of the chunks bordering it is Discovered, mirroring how the chunks themselves
    // reveal. Null until ComputeLevelOutline has run. See ComputeHoleRegions/RevealHolesAdjacentTo.
    private List<HoleRegion> _holeRegions;

    // Texel -> its hole region, so the local player's own texel can be mapped to a hole in O(1) for
    // the "standing on a hole" current-highlight (a gap-filler isn't a Chunk, so ResolveCurrentChunk
    // returns None there and the normal current-chunk highlight has nothing to attach to).
    private Dictionary<int, HoleRegion> _holeTexelToRegion;
    private HoleRegion _currentHoleRegion;

    private sealed class HoleRegion
    {
        public readonly List<int> Texels = new();
        public readonly HashSet<EntityRef> AdjacentChunks = new();
        public bool Revealed;
    }

    private Texture2D _texture;
    private int _textureResolution;
    private float _worldToTexelScale;
    private bool _textureDirty;

    public override void QStart(QuantumGame game)
    {
        _worldToTexelScale = 1f / worldUnitsPerTexel;
        _textureResolution = Mathf.Max(Mathf.CeilToInt(worldExtent * 2f * _worldToTexelScale), 1);

        _texture = new Texture2D(_textureResolution, _textureResolution, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        var clear = new Color[_textureResolution * _textureResolution];
        for (int i = 0; i < clear.Length; i++)
            clear[i] = backgroundColor;
        _texture.SetPixels(clear);
        _texture.Apply(false);

        if (mapImage != null)
            mapImage.texture = _texture;

        // Same live texture on the full-map panel - it draws the whole level unpanned, updating in
        // lockstep with the minimap since both point at the same Texture2D (Apply mutates it in place).
        // Icons/markers are NOT shared this way (they're real UI objects, not texture content), so
        // the panel gets its own parallel set parented under _fullOverlayRoot - see OverlayPair.
        if (fullMapImage != null)
        {
            fullMapImage.texture = _texture;
            _fullOverlayRoot = fullMapRect != null ? fullMapRect : fullMapImage.rectTransform;
        }

        // iconPrefab/playerMarkerPrefab are scene template objects living under this same widget,
        // not Project-window prefab assets - Instantiate() clones them, but the template itself
        // stays in the scene and would otherwise render at its own design-time position forever.
        // Clones inherit whatever active state the template has at spawn time, so this is safe:
        // SpawnIconOverlayIfNeeded already explicitly sets its own clone's active state right
        // after instantiating (chunk.Discovered), and UpdatePlayerMarkers now does the same.
        if (iconPrefab != null)
            iconPrefab.gameObject.SetActive(false);

        if (playerMarkerPrefab != null)
            playerMarkerPrefab.gameObject.SetActive(false);
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;
        if (frame == null)
            return;

        UpdateChunks(frame);
        RefreshIconPositionsIfResized();
        UpdatePlayerMarkers(frame);
        CenterOnLocalPlayer(frame);
    }

    // Shifts the whole content layer (mapRect - texture, icons, and markers all live under it) so
    // this instance's own local player's map position always lands at mapRect's parent's origin.
    // Expects mapRect to be nested inside a separate masked container (fixed position/size, not
    // touched here) that clips whatever of mapRect overflows it - the standard "content pans,
    // viewport mask stays put" minimap technique. The texture itself is never re-baked for this;
    // only mapRect's own anchoredPosition moves.
    private void CenterOnLocalPlayer(Frame frame)
    {
        if (MyLocalPlayer.Instance == null)
            return;

        var slots = MyLocalPlayer.Instance.Slots;
        if (localSlotIndex < 0 || localSlotIndex >= slots.Count || slots[localSlotIndex].IsSet == false)
            return;

        EntityRef playerEntity = slots[localSlotIndex].EntityRef;
        if (frame.TryGet<Transform3D>(playerEntity, out Transform3D playerTransform) == false)
            return;

        Vector2 playerWorldPos = new Vector2(playerTransform.Position.X.AsFloat, playerTransform.Position.Z.AsFloat);
        mapRect.anchoredPosition = -WorldToMapPosition(playerWorldPos, mapRect);
    }

    private unsafe void UpdateChunks(Frame frame)
    {
        EntityRef currentChunk = ResolveCurrentChunk(frame);

        // Pass 1: cache every newly-seen chunk's rect (and detect a Discovered flip on an
        // already-known one) before painting anything - the outline mask needs every chunk's rect
        // to already exist.
        var newlySeen = new List<EntityRef>();
        var discoveredChanged = new List<EntityRef>();

        var chunks = frame.Filter<Chunk, Transform3D>();
        while (chunks.Next(out EntityRef entity, out Chunk chunk, out Transform3D transform))
        {
            if (_chunkTexelRects.ContainsKey(entity) == false)
            {
                _chunkTexelRects[entity] = ComputeTexelRect(chunk, transform);
                _lastDiscovered[entity] = chunk.Discovered;
                newlySeen.Add(entity);
            }
            else if (_lastDiscovered[entity] != chunk.Discovered)
            {
                _lastDiscovered[entity] = chunk.Discovered;
                discoveredChanged.Add(entity);
            }
        }

        // Gated on Global.LevelGenerated (set true only once LevelGenerationSystem has placed
        // every chunk), NOT on "we just saw a new chunk" - game.Frames.Predicted could observe the
        // level mid-populate, and computing the outline from a partial chunk set would lock in a
        // wrong (single-chunk-looking) outline forever, since it only ever runs once
        // (_outlineMask == null). See docs/minimap.md.
        // Runs once the level is fully placed, regardless of outlineTexels - it also computes the
        // inner hole regions (see ComputeHoleRegions), which are wanted even when no outline is drawn.
        bool outlineJustComputed = false;
        if (_outlineMask == null && frame.Global->LevelGenerated)
        {
            ComputeLevelOutline(frame);
            outlineJustComputed = true;
        }

        // Pass 2: paint. A brand-new chunk also needs its icon overlay spawned. If the outline was
        // just computed, every already-painted chunk needs a repaint too (to stamp the outline onto
        // it) even though its own fill/Discovered state didn't change this tick.
        chunks = frame.Filter<Chunk, Transform3D>();
        while (chunks.Next(out EntityRef entity, out Chunk chunk, out Transform3D transform))
        {
            bool isNew = newlySeen.Contains(entity);
            if (isNew == false && discoveredChanged.Contains(entity) == false && outlineJustComputed == false)
                continue;

            if (entity != currentChunk)
                RepaintSingleChunk(entity, currentChunk);

            // A discovered chunk lights up any inner hole it borders (see RevealHolesAdjacentTo).
            if (chunk.Discovered)
                RevealHolesAdjacentTo(entity);

            if (isNew)
                SpawnIconOverlayIfNeeded(entity, chunk, _chunkTexelRects[entity]);
            else if (_iconOverlays.TryGetValue(entity, out OverlayPair icon))
                icon.SetActive(chunk.Discovered);
        }

        if (currentChunk != _lastCurrentChunk || outlineJustComputed)
        {
            EntityRef previousCurrent = _lastCurrentChunk;
            _lastCurrentChunk = currentChunk;

            if (previousCurrent != EntityRef.None && previousCurrent != currentChunk)
                RepaintSingleChunk(previousCurrent, currentChunk);

            if (currentChunk != EntityRef.None)
            {
                RepaintSingleChunk(currentChunk, currentChunk);
                RevealHolesAdjacentTo(currentChunk);
            }
        }

        // Highlight the hole the local player is standing on (if any) - runs every tick since the
        // player can move on/off a hole without any chunk's own state changing. No-op until holes
        // are computed, and cheap when the region hasn't changed.
        UpdateCurrentHole(frame);

        if (_textureDirty)
        {
            _texture.Apply(false);
            _textureDirty = false;
        }
    }

    // Computes the level's static outline exactly once, entirely independent of Discovered state.
    // Standard edge-detection-on-a-pixel-grid: rasterize every chunk's full rect into an occupancy
    // mask first, then mark an occupied texel as outline if any texel within outlineTexels of it
    // (or the texture's own edge) is unoccupied. Deliberately NOT "does the whole edge of this
    // chunk's rect touch another chunk's rect" (checked per chunk-pair before) - that treats each
    // of a chunk's 4 sides as one monolithic decision, so a neighbor that only partially covers a
    // shared side (smaller, offset, an L-shaped arrangement, etc.) would wrongly mark the entire
    // side as "connected." Operating on the rasterized grid directly instead handles any chunk
    // arrangement correctly, no per-chunk-pair adjacency logic needed. See class comment.
    private void ComputeLevelOutline(Frame frame)
    {
        var occupied = new bool[_textureResolution * _textureResolution];

        var chunks = frame.Filter<Chunk, Transform3D>();
        while (chunks.Next(out EntityRef entity, out Chunk _, out Transform3D _))
        {
            if (_chunkTexelRects.TryGetValue(entity, out RectInt rect) == false)
                continue;

            for (int y = rect.y; y < rect.y + rect.height; y++)
            {
                int rowOffset = y * _textureResolution;
                for (int x = rect.x; x < rect.x + rect.width; x++)
                    occupied[rowOffset + x] = true;
            }
        }

        // Merge the level's inner holes (the enclosed empty regions the gap-filler fills) into the
        // solid mass BEFORE the outline is computed, so the outline wraps the outer boundary and
        // never draws a loop around a hole. ComputeHoleRegions also records each hole so it can
        // reveal per-adjacent-chunk discovery.
        ComputeHoleRegions(occupied);

        _outlineMask = new bool[_textureResolution * _textureResolution];

        for (int y = 0; y < _textureResolution; y++)
        {
            for (int x = 0; x < _textureResolution; x++)
            {
                int index = y * _textureResolution + x;
                if (occupied[index] && IsNearUnoccupiedTexel(occupied, x, y))
                    _outlineMask[index] = true;
            }
        }
    }

    private bool IsNearUnoccupiedTexel(bool[] occupied, int x, int y)
    {
        for (int dy = -outlineTexels; dy <= outlineTexels; dy++)
        {
            for (int dx = -outlineTexels; dx <= outlineTexels; dx++)
            {
                int nx = x + dx;
                int ny = y + dy;

                if (nx < 0 || nx >= _textureResolution || ny < 0 || ny >= _textureResolution)
                    return true; // the texture's own edge counts as unoccupied

                if (occupied[ny * _textureResolution + nx] == false)
                    return true;
            }
        }

        return false;
    }

    // Finds every enclosed empty region (an inner hole the gap-filler fills): flood-fills from the
    // texture border through empty texels ("outside"), and anything still empty afterward is a hole.
    // Each hole is marked occupied here (so the outline treats the level as one solid mass), painted
    // undiscoveredColor for now, and recorded with the chunks bordering it so RevealHolesAdjacentTo
    // can light it up once one of those chunks is Discovered. Same border-flood idea
    // LevelGenerationSystem.FillInnerGaps already uses to decide where to spawn a gap-filler.
    private void ComputeHoleRegions(bool[] occupied)
    {
        int res = _textureResolution;

        // Which chunk owns each texel, so a hole's bordering chunks can be identified.
        var chunkAt = new EntityRef[res * res];
        foreach (var kv in _chunkTexelRects)
        {
            RectInt rect = kv.Value;
            for (int y = rect.y; y < rect.y + rect.height; y++)
            {
                int rowOffset = y * res;
                for (int x = rect.x; x < rect.x + rect.width; x++)
                    chunkAt[rowOffset + x] = kv.Key;
            }
        }

        // Flood-fill from the border through empty texels - everything reached is "outside".
        var visited = new bool[res * res];
        var queue = new Queue<int>();
        for (int x = 0; x < res; x++)
        {
            SeedOutside(occupied, visited, queue, x, 0, res);
            SeedOutside(occupied, visited, queue, x, res - 1, res);
        }
        for (int y = 0; y < res; y++)
        {
            SeedOutside(occupied, visited, queue, 0, y, res);
            SeedOutside(occupied, visited, queue, res - 1, y, res);
        }
        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int x = idx % res, y = idx / res;
            SeedOutside(occupied, visited, queue, x + 1, y, res);
            SeedOutside(occupied, visited, queue, x - 1, y, res);
            SeedOutside(occupied, visited, queue, x, y + 1, res);
            SeedOutside(occupied, visited, queue, x, y - 1, res);
        }

        // Group the leftover (enclosed) empty texels into connected regions, collecting each region's
        // texels and the chunks bordering it. `visited` is reused as the region-visited marker.
        _holeRegions = new List<HoleRegion>();
        for (int start = 0; start < occupied.Length; start++)
        {
            if (occupied[start] || visited[start])
                continue;

            var region = new HoleRegion();
            visited[start] = true;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                region.Texels.Add(idx);
                int x = idx % res, y = idx / res;
                VisitHoleNeighbor(occupied, visited, chunkAt, region, queue, x + 1, y, res);
                VisitHoleNeighbor(occupied, visited, chunkAt, region, queue, x - 1, y, res);
                VisitHoleNeighbor(occupied, visited, chunkAt, region, queue, x, y + 1, res);
                VisitHoleNeighbor(occupied, visited, chunkAt, region, queue, x, y - 1, res);
            }

            _holeRegions.Add(region);
        }

        _holeTexelToRegion = new Dictionary<int, HoleRegion>();
        foreach (HoleRegion region in _holeRegions)
        {
            foreach (int idx in region.Texels)
            {
                occupied[idx] = true;
                _holeTexelToRegion[idx] = region;
                _texture.SetPixel(idx % res, idx / res, undiscoveredColor);
            }
        }

        if (_holeRegions.Count > 0)
            _textureDirty = true;
    }

    private void PaintHoleRegion(HoleRegion region, Color color)
    {
        int res = _textureResolution;
        foreach (int idx in region.Texels)
            _texture.SetPixel(idx % res, idx / res, color);
    }

    private static void SeedOutside(bool[] occupied, bool[] visited, Queue<int> queue, int x, int y, int res)
    {
        if (x < 0 || y < 0 || x >= res || y >= res)
            return;

        int idx = y * res + x;
        if (occupied[idx] || visited[idx])
            return;

        visited[idx] = true;
        queue.Enqueue(idx);
    }

    private static void VisitHoleNeighbor(bool[] occupied, bool[] visited, EntityRef[] chunkAt, HoleRegion region, Queue<int> queue, int x, int y, int res)
    {
        if (x < 0 || y < 0 || x >= res || y >= res)
            return;

        int idx = y * res + x;
        if (occupied[idx])
        {
            // An occupied neighbor is a chunk bordering this hole.
            if (chunkAt[idx] != EntityRef.None)
                region.AdjacentChunks.Add(chunkAt[idx]);
            return;
        }

        if (visited[idx])
            return;

        visited[idx] = true;
        queue.Enqueue(idx);
    }

    // Reveals (repaints undiscoveredColor -> discoveredColor) every not-yet-revealed hole region the
    // just-discovered chunk borders. Idempotent via the Revealed flag, so it's safe to call on every
    // repaint of a discovered chunk. No-op until ComputeHoleRegions has run.
    private void RevealHolesAdjacentTo(EntityRef discoveredChunk)
    {
        if (_holeRegions == null)
            return;

        foreach (HoleRegion region in _holeRegions)
        {
            // Already-revealed regions are skipped - which also means a hole the player is currently
            // standing on (Revealed set by UpdateCurrentHole) never gets its live currentColor
            // stomped back to discoveredColor here.
            if (region.Revealed || region.AdjacentChunks.Contains(discoveredChunk) == false)
                continue;

            region.Revealed = true;
            PaintHoleRegion(region, discoveredColor);
            _textureDirty = true;
        }
    }

    // Highlights (currentColor) the hole region the local player is currently standing on, reverting
    // the one just left back to discoveredColor. A gap-filler isn't a Chunk, so ResolveCurrentChunk
    // returns None while on a hole and the normal current-chunk highlight can't cover this case.
    private void UpdateCurrentHole(Frame frame)
    {
        HoleRegion current = ResolveCurrentHole(frame);
        if (current == _currentHoleRegion)
            return;

        if (_currentHoleRegion != null)
            PaintHoleRegion(_currentHoleRegion, discoveredColor);

        _currentHoleRegion = current;

        if (current != null)
        {
            current.Revealed = true; // standing on it counts as discovering it
            PaintHoleRegion(current, currentColor);
        }

        _textureDirty = true;
    }

    private HoleRegion ResolveCurrentHole(Frame frame)
    {
        if (_holeTexelToRegion == null || MyLocalPlayer.Instance == null)
            return null;

        var slots = MyLocalPlayer.Instance.Slots;
        if (localSlotIndex < 0 || localSlotIndex >= slots.Count || slots[localSlotIndex].IsSet == false)
            return null;

        EntityRef playerEntity = slots[localSlotIndex].EntityRef;
        if (frame.TryGet<Transform3D>(playerEntity, out Transform3D playerTransform) == false)
            return null;

        Vector2Int texel = WorldToTexel(new Vector2(playerTransform.Position.X.AsFloat, playerTransform.Position.Z.AsFloat));
        if (texel.x < 0 || texel.x >= _textureResolution || texel.y < 0 || texel.y >= _textureResolution)
            return null;

        return _holeTexelToRegion.TryGetValue(texel.y * _textureResolution + texel.x, out HoleRegion region) ? region : null;
    }

    // Chunks are min-corner pivoted and never rotated (the chunk rotation logic was removed), so
    // Transform3D.Position IS the footprint's world min corner and the authored Width/Depth map
    // straight to world X/Z.
    private static void GetWorldBounds(Chunk chunk, Transform3D transform, out float minX, out float minZ, out float maxX, out float maxZ)
    {
        minX = transform.Position.X.AsFloat;
        minZ = transform.Position.Z.AsFloat;
        maxX = minX + chunk.ChunkSizeWidth;
        maxZ = minZ + chunk.ChunkSizeDepth;
    }

    // Paints this chunk's full fill, then - only if it's Discovered - stamps whichever of its own
    // precomputed outline texels fall inside its rect on top, in outlineColor.
    private void RepaintSingleChunk(EntityRef entity, EntityRef currentChunk)
    {
        if (_chunkTexelRects.TryGetValue(entity, out RectInt texelRect) == false)
            return;

        bool discovered = _lastDiscovered.TryGetValue(entity, out bool d) && d;
        Color fill = entity == currentChunk ? currentColor : (discovered ? discoveredColor : undiscoveredColor);

        PaintRect(texelRect, fill);

        // Outline only shows for a Discovered chunk (the current chunk is always Discovered by
        // definition) - the mask's own shape is still computed from the whole level up front, only
        // its visibility is gated here.
        if (_outlineMask == null || discovered == false)
            return;

        for (int y = texelRect.y; y < texelRect.y + texelRect.height; y++)
        {
            int rowOffset = y * _textureResolution;
            for (int x = texelRect.x; x < texelRect.x + texelRect.width; x++)
            {
                if (_outlineMask[rowOffset + x])
                    _texture.SetPixel(x, y, outlineColor);
            }
        }

        _textureDirty = true;
    }

    // Positions from texelRect's own (rounded) center, NOT the chunk's raw continuous-space
    // footprint center - ComputeTexelRect rounds its min/max corners independently, which at this
    // coarse a scale (2-3 texels per chunk) can shift the painted square's actual center by up to
    // half a texel. Deriving the icon from the same rect the texture itself was painted from is
    // what keeps the two visually aligned.
    private void SpawnIconOverlayIfNeeded(EntityRef entity, Chunk chunk, RectInt texelRect)
    {
        Sprite specialSprite = ResolveChunkTypeSprite(chunk.Type);
        if (specialSprite == null)
            return;

        var pair = new OverlayPair
        {
            TexelRect = texelRect,
            Mini = SpawnIcon(mapRect, specialSprite, texelRect, 1f),
            Full = SpawnIcon(_fullOverlayRoot, specialSprite, texelRect, fullMapOverlayScale)
        };

        pair.SetActive(chunk.Discovered);
        _iconOverlays[entity] = pair;
    }

    // Repositions every chunk icon on whichever map surface just changed UI size - a no-op on
    // every frame neither surface resized (the overwhelmingly common case), which is why this is
    // cheap enough to poll every QUpdate rather than react to a resize event. Matters because an
    // icon is positioned once at spawn: the full-map panel is typically inactive (and possibly
    // zero-sized) until the player first opens it.
    private void RefreshIconPositionsIfResized()
    {
        float miniWidth = mapRect != null ? mapRect.rect.width : 0f;
        float fullWidth = _fullOverlayRoot != null ? _fullOverlayRoot.rect.width : 0f;

        bool miniResized = Mathf.Approximately(miniWidth, _lastMiniWidth) == false;
        bool fullResized = Mathf.Approximately(fullWidth, _lastFullWidth) == false;

        if (miniResized == false && fullResized == false)
            return;

        _lastMiniWidth = miniWidth;
        _lastFullWidth = fullWidth;

        foreach (var pair in _iconOverlays.Values)
        {
            if (miniResized && pair.Mini != null)
                pair.Mini.anchoredPosition = TexelRectCenterToMapPosition(pair.TexelRect, mapRect);

            if (fullResized && pair.Full != null)
                pair.Full.anchoredPosition = TexelRectCenterToMapPosition(pair.TexelRect, _fullOverlayRoot);
        }
    }

    // One icon instance on one map surface. A chunk never moves, so its position is set once here
    // and never touched again - unlike a player marker, which is repositioned every frame.
    // Null root (no full-map panel wired up) simply produces no instance.
    private RectTransform SpawnIcon(RectTransform root, Sprite sprite, RectInt texelRect, float scale)
    {
        if (root == null || iconPrefab == null)
            return null;

        Image iconImage = Instantiate(iconPrefab, root);
        iconImage.sprite = sprite;

        var rect = iconImage.transform as RectTransform;
        rect.anchoredPosition = TexelRectCenterToMapPosition(texelRect, root);
        rect.localScale = Vector3.one * scale;

        return rect;
    }

    // Fills only this sub-rect - never touches the rest of the texture. Multiple calls in the same
    // tick are batched into a single Texture2D.Apply at the end of UpdateChunks.
    private void PaintRect(RectInt rect, Color color)
    {
        if (rect.width <= 0 || rect.height <= 0)
            return;

        var colors = new Color[rect.width * rect.height];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = color;

        _texture.SetPixels(rect.x, rect.y, rect.width, rect.height, colors);
        _textureDirty = true;
    }

    private RectInt ComputeTexelRect(Chunk chunk, Transform3D transform)
    {
        GetWorldBounds(chunk, transform, out float minX, out float minZ, out float maxX, out float maxZ);

        Vector2Int min = WorldToTexel(new Vector2(minX, minZ));
        Vector2Int max = WorldToTexel(new Vector2(maxX, maxZ));

        int x = Mathf.Clamp(min.x, 0, _textureResolution);
        int y = Mathf.Clamp(min.y, 0, _textureResolution);
        int xEnd = Mathf.Clamp(max.x, 0, _textureResolution);
        int yEnd = Mathf.Clamp(max.y, 0, _textureResolution);

        return new RectInt(x, y, Mathf.Max(xEnd - x, 0), Mathf.Max(yEnd - y, 0));
    }

    // "Our" current chunk - whichever chunk contains this instance's own bound local player
    // (MyLocalPlayer.Slots[localSlotIndex]), not every player in the match.
    private EntityRef ResolveCurrentChunk(Frame frame)
    {
        if (MyLocalPlayer.Instance == null)
            return EntityRef.None;

        var slots = MyLocalPlayer.Instance.Slots;
        if (localSlotIndex < 0 || localSlotIndex >= slots.Count || slots[localSlotIndex].IsSet == false)
            return EntityRef.None;

        EntityRef playerEntity = slots[localSlotIndex].EntityRef;
        if (frame.TryGet<Transform3D>(playerEntity, out Transform3D playerTransform) == false)
            return EntityRef.None;

        float playerX = playerTransform.Position.X.AsFloat;
        float playerZ = playerTransform.Position.Z.AsFloat;

        var chunks = frame.Filter<Chunk, Transform3D>();

        while (chunks.Next(out EntityRef entity, out Chunk chunk, out Transform3D transform))
        {
            GetWorldBounds(chunk, transform, out float minX, out float minZ, out float maxX, out float maxZ);

            if (playerX >= minX && playerX <= maxX && playerZ >= minZ && playerZ <= maxZ)
                return entity;
        }

        return EntityRef.None;
    }

    private void UpdatePlayerMarkers(Frame frame)
    {
        var players = frame.Filter<PlayerLink, Transform3D>();

        while (players.Next(out EntityRef entity, out PlayerLink _, out Transform3D transform))
        {
            _seenPlayersThisFrame.Add(entity);

            if (_playerMarkers.TryGetValue(entity, out OverlayPair marker) == false)
            {
                marker = new OverlayPair
                {
                    Mini = SpawnPlayerMarker(frame, entity, mapRect, 1f),
                    Full = SpawnPlayerMarker(frame, entity, _fullOverlayRoot, fullMapOverlayScale)
                };

                marker.SetActive(true); // templates themselves are disabled - see QStart
                _playerMarkers[entity] = marker;
            }

            var worldPos = new Vector2(transform.Position.X.AsFloat, transform.Position.Z.AsFloat);

            // The two surfaces show the same world at different UI sizes, so each marker's own
            // position is resolved against its own root rather than shared.
            if (marker.Mini != null)
                marker.Mini.anchoredPosition = WorldToMapPosition(worldPos, mapRect);

            if (marker.Full != null)
                marker.Full.anchoredPosition = WorldToMapPosition(worldPos, _fullOverlayRoot);
        }

        // Releases any marker whose player wasn't seen this frame (disconnected) - same
        // seen/stale-sweep shape as StatusEffectsManager's own StatusSlotTracker.EndFrame.
        foreach (var pair in _playerMarkers)
        {
            if (_seenPlayersThisFrame.Contains(pair.Key))
                continue;

            (_stalePlayerBuffer ??= new List<EntityRef>()).Add(pair.Key);
        }

        if (_stalePlayerBuffer != null)
        {
            foreach (var entity in _stalePlayerBuffer)
            {
                if (_playerMarkers.TryGetValue(entity, out OverlayPair marker))
                    marker.Destroy();

                _playerMarkers.Remove(entity);
            }

            _stalePlayerBuffer.Clear();
        }

        _seenPlayersThisFrame.Clear();
    }

    // Tints the marker with this player's hero sprite (CharacterData.PawnSprite), resolved the same
    // way PlayerNumberUiWidget resolves RingColor - via the entity's CharacterStats.CharacterData
    // asset. Sprite lives on the marker's own Image, or a child one (so the marker prefab can hold
    // other decoration alongside the sprite). No-op if any link is missing.
    // One player marker instance on one map surface. Null root (no full-map panel wired up)
    // simply produces no instance.
    private RectTransform SpawnPlayerMarker(Frame frame, EntityRef entity, RectTransform root, float scale)
    {
        if (root == null || playerMarkerPrefab == null)
            return null;

        RectTransform marker = Instantiate(playerMarkerPrefab, root);
        marker.localScale = Vector3.one * scale;

        // Hero pick is fixed for a player's lifetime, so resolve the pawn sprite once at marker
        // creation - not every frame. Left unassigned on CharacterData, the marker simply keeps
        // the prefab's own default sprite.
        ApplyPawnSprite(frame, entity, marker);

        return marker;
    }

    private void ApplyPawnSprite(Frame frame, EntityRef entity, RectTransform marker)
    {
        if (frame.TryGet<CharacterStats>(entity, out var stats) == false)
            return;

        CharacterData data = frame.FindAsset(stats.CharacterData);

        if (data == null || data.PawnSprite == null)
            return;

        if (marker.TryGetComponent(out Image image) == false)
            image = marker.GetComponentInChildren<Image>();

        if (image != null)
            image.sprite = data.PawnSprite;
    }

    private Vector2Int WorldToTexel(Vector2 worldXZ)
    {
        Vector2 local = worldXZ - worldCenter;

        return new Vector2Int(
            Mathf.RoundToInt(local.x * _worldToTexelScale + _textureResolution * 0.5f),
            Mathf.RoundToInt(local.y * _worldToTexelScale + _textureResolution * 0.5f));
    }

    // UI-local position (mapRect space) for the icon overlays/player markers - chains the same
    // world-to-texel scale through to UI units, so everything lines up with the painted texture
    // regardless of mapRect's own on-screen pixel size.
    private Vector2 WorldToMapPosition(Vector2 worldXZ, RectTransform root)
    {
        Vector2 local = worldXZ - worldCenter;
        float uiScale = _worldToTexelScale * (root.rect.width / _textureResolution);

        return local * uiScale;
    }

    // UI-local position (mapRect space) for the center of an already-computed texel rect - see
    // SpawnIconOverlayIfNeeded's own comment for why this, not WorldToMapPosition, is what icons
    // need to line up with the painted square.
    private Vector2 TexelRectCenterToMapPosition(RectInt texelRect, RectTransform root)
    {
        float texelCenterX = texelRect.x + texelRect.width * 0.5f;
        float texelCenterY = texelRect.y + texelRect.height * 0.5f;
        float uiScale = root.rect.width / _textureResolution;

        return new Vector2(
            (texelCenterX - _textureResolution * 0.5f) * uiScale,
            (texelCenterY - _textureResolution * 0.5f) * uiScale);
    }

    private Sprite ResolveChunkTypeSprite(ChunkType type)
    {
        int index = (int)type;

        if (chunkTypeSprites == null || index < 0 || index >= chunkTypeSprites.Length)
            return null;

        return chunkTypeSprites[index];
    }
}

using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Procedurally lines up ground-snapped visual markers along a hand-placed BossArenaGate's own
    // PhysicsCollider3D box footprint - a 1x1x10 box gets 10 markers spaced 1 world unit apart
    // along its long (X or Z) footprint axis, each individually raycast down onto the level's real
    // Ground layer so a chunk's actual floor height (which the box's own authored Y rarely matches
    // exactly) doesn't leave any marker floating or buried. Each marker also gets a random
    // width/height scale and a random tilt/yaw (not just Y-scale/Y-rotation - that read as too
    // uniform for a jagged spike row) so the row doesn't look like a mechanically identical repeat,
    // seeded from RuntimeConfig.Seed + this entity's own index so every client/split-screen instance
    // agrees (same determinism convention ChunkDetailScatter already uses). Markers are spawned
    // under a dedicated identity-scale root next to the gate, NOT under this entity's own transform
    // - a hand-placed BossArenaGate box is often authored with a non-uniform scale (e.g. 10x1x1) to
    // stretch its collider footprint along the corridor, and reparenting a rotated child under that
    // kind of parent shears it (Unity can't represent "child rotated relative to a non-uniformly
    // scaled parent" as a plain position/rotation/scale). The whole row is only shown while the
    // gate's own PhysicsCollider3D.Enabled is true - a BossArenaGate starts disabled
    // (BossArenaGateSystem) and only flips on once RunPhaseUtility.BeginBossEncounter seals it, so
    // this row should read as "the corridor is sealed" exactly when the simulation agrees it is.
    public class BossArenaGateVisualsView : CustomQuantumEntityViewComponent
    {
        private const int MaxBuildAttempts = 180;

        [SerializeField, Tooltip("Instantiated once per marker along the box's long axis.")]
        private GameObject markerPrefab;

        [SerializeField, Tooltip("World-space distance between markers along the long axis - a 10-unit-long box with spacing 1 gets 10 markers.")]
        private float markerSpacing = 1f;

        [Header("Randomization")]
        [SerializeField, Tooltip("Random extra scale multiplier applied to each marker's own local Y (height) axis, on top of its prefab's authored scale.")]
        private Vector2 randomHeightScaleRange = new Vector2(0.7f, 1.4f);

        [SerializeField, Tooltip("Random extra scale multiplier applied evenly to each marker's own local X and Z (width/girth) axes, on top of its prefab's authored scale.")]
        private Vector2 randomWidthScaleRange = new Vector2(0.7f, 1.3f);

        [SerializeField, Tooltip("Random yaw range (degrees, rotation around the vertical axis) applied on top of the gate's own rotation, so markers don't all face identically.")]
        private Vector2 randomYawRange = new Vector2(0f, 360f);

        [SerializeField, Tooltip("Random tilt (+/- degrees) applied on the local X and Z axes, on top of the gate's own rotation, so markers don't all stand perfectly upright.")]
        private float randomTiltRange = 15f;

        [Header("Ground Raycast")]
        [SerializeField] private LayerMask groundLayer;
        [SerializeField, Tooltip("Start the downward raycast this far above the box's own center, in case the box overlaps the ground.")]
        private float raycastHeight = 5f;
        [SerializeField] private float maxRaycastDistance = 20f;
        [SerializeField, Tooltip("Small lift above the ground hit point to avoid z-fighting with the floor.")]
        private float groundOffset = 0.02f;

        private readonly List<GameObject> _markers = new List<GameObject>();
        private Transform _markerRoot;
        private bool _built;
        private int _failedAttempts;
        private bool? _lastEnabled;

        public override void DeInitialize(QuantumGame game)
        {
            DestroyMarkers();
            base.DeInitialize(game);
        }

        // Public so this can be re-triggered by hand from the Inspector while in Play Mode, to
        // preview a different random layout without respawning the whole gate entity.
        [Button("Regenerate (Test)")]
        public void Regenerate()
        {
            if (Application.isPlaying == false || _game == null || _entityRef == EntityRef.None)
            {
                LogHelper.Warn("BossArenaGateVisualsView", "Can only regenerate in Play Mode, once this gate's entity has spawned.", this);
                return;
            }

            DestroyMarkers();
            TryBuild(_game);
        }

        private void DestroyMarkers()
        {
            // Destroying the root also destroys every marker parented under it - no need to loop
            // over _markers individually.
            if (_markerRoot != null)
                Destroy(_markerRoot.gameObject);

            _markerRoot = null;
            _markers.Clear();
            _built = false;
            _failedAttempts = 0;
            _lastEnabled = null;
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (_built == false)
            {
                TryBuild(game);
                return;
            }

            if (game.Frames.Predicted.TryGet<PhysicsCollider3D>(_entityRef, out PhysicsCollider3D collider) == false)
                return;

            ApplyEnabledState(collider.Enabled);
        }

        private void TryBuild(QuantumGame game)
        {
            if (markerPrefab == null)
            {
                LogHelper.Error("BossArenaGateVisualsView", $"'{name}' has no markerPrefab assigned.", this);
                _built = true;
                return;
            }

            Frame f = game.Frames.Predicted;

            if (f.TryGet<PhysicsCollider3D>(_entityRef, out PhysicsCollider3D collider) == false)
                return; // entity not readable yet - retry next tick

            if (collider.Shape.Type != Shape3DType.Box)
            {
                LogHelper.Error("BossArenaGateVisualsView", $"'{name}' has a {collider.Shape.Type} collider - only Box is supported.", this);
                _built = true;
                return;
            }

            if (f.TryGet<Transform3D>(_entityRef, out Transform3D transform3D) == false)
                return;

            Vector3 center = (transform3D.Position + collider.Shape.Centroid).ToUnityVector3();
            Vector3 fullSize = collider.Shape.Box.Extents.ToUnityVector3() * 2f;

            bool alongX = fullSize.x >= fullSize.z;
            float length = alongX ? fullSize.x : fullSize.z;
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;

            int count = Mathf.Max(1, Mathf.RoundToInt(length / markerSpacing));
            float start = -length * 0.5f + markerSpacing * 0.5f;

            // Resolved fully before anything is instantiated - a level not finished generating yet
            // (so the Ground layer has nothing to hit below this gate) just retries next tick rather
            // than spawning a row with gaps in it.
            var groundPositions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 queryPosition = center + axis * (start + i * markerSpacing);

                if (TrySnapToGround(queryPosition, out groundPositions[i]) == true)
                    continue;

                if (++_failedAttempts > MaxBuildAttempts)
                {
                    LogHelper.Warn("BossArenaGateVisualsView", $"'{name}' found no ground under its own box after {MaxBuildAttempts} attempts - giving up.", this);
                    _built = true;
                }

                return;
            }

            Quaternion rotation = entityView != null ? entityView.transform.rotation : transform.rotation;
            Transform root = EnsureMarkerRoot(rotation);

            // Deterministic per-gate stream (not UnityEngine.Random, which isn't seedable the same
            // way across clients) - every client/split-screen instance rolls the identical sequence
            // of yaw/scale values, same convention ChunkDetailScatter already uses.
            var rng = new System.Random(CombineSeed(f.RuntimeConfig.Seed, _entityRef.Index));

            var tiltRange = new Vector2(-randomTiltRange, randomTiltRange);

            foreach (Vector3 position in groundPositions)
            {
                // Tilt on X/Z (off-vertical lean) plus yaw on Y (facing) - not yaw alone, so markers
                // don't all stand perfectly upright in a mechanically identical row.
                float tiltX = RandomRange(rng, tiltRange);
                float tiltZ = RandomRange(rng, tiltRange);
                float yaw = RandomRange(rng, randomYawRange);
                Quaternion markerRotation = rotation * Quaternion.Euler(tiltX, yaw, tiltZ);

                // Parented directly under the identity-scale root (see class comment) - no
                // reparent-and-compensate dance needed, since the root never carries a non-uniform
                // scale for Unity to shear against.
                GameObject marker = Instantiate(markerPrefab, position, markerRotation, root);

                // Width (X/Z together, keeps girth circular rather than stretched lopsided) plus
                // height (Y) - not height alone.
                float widthScale = RandomRange(rng, randomWidthScaleRange);
                Vector3 scale = marker.transform.localScale;
                scale.x *= widthScale;
                scale.z *= widthScale;
                scale.y *= RandomRange(rng, randomHeightScaleRange);
                marker.transform.localScale = scale;

                _markers.Add(marker);
            }

            _built = true;
            ApplyEnabledState(collider.Enabled);
        }

        private Transform EnsureMarkerRoot(Quaternion rotation)
        {
            if (_markerRoot != null)
                return _markerRoot;

            GameObject rootObject = new GameObject($"{name} Markers");
            rootObject.transform.SetParent(transform.parent, false);
            rootObject.transform.SetPositionAndRotation(transform.position, rotation);
            rootObject.transform.localScale = Vector3.one;

            _markerRoot = rootObject.transform;
            return _markerRoot;
        }

        private static float RandomRange(System.Random rng, Vector2 range)
        {
            float min = Mathf.Min(range.x, range.y);
            float max = Mathf.Max(range.x, range.y);
            return min + (float)rng.NextDouble() * (max - min);
        }

        // Manual deterministic combine - NOT HashCode.Combine, which mixes in a per-process random
        // seed by design and would give a different layout on every client (see ChunkDetailScatter's
        // own CombineSeed for the same reasoning).
        private static int CombineSeed(int seed, int entityIndex)
        {
            unchecked
            {
                return seed * 486187739 + entityIndex;
            }
        }

        private bool TrySnapToGround(Vector3 position, out Vector3 groundPosition)
        {
            Vector3 origin = position + Vector3.up * raycastHeight;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastHeight + maxRaycastDistance, groundLayer) == true)
            {
                groundPosition = hit.point + Vector3.up * groundOffset;
                return true;
            }

            groundPosition = default;
            return false;
        }

        private void ApplyEnabledState(bool isEnabled)
        {
            if (_lastEnabled.HasValue && _lastEnabled.Value == isEnabled)
                return;

            _lastEnabled = isEnabled;

            foreach (GameObject marker in _markers)
                marker.SetActive(isEnabled);
        }
    }
}

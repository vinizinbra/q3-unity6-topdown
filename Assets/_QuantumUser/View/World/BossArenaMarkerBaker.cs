using NaughtyAttributes;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

// Bakes this chunk's optional BossTeleportPointMarker(s) and BossSpawnPointMarker(s) (each 1 to
// 4) into BossArena.TeleportPoints/TeleportPointCount and BossArena.SpawnPoints/SpawnPointCount -
// see RunPhaseUtility.BeginBossEncounter, which prefers these baked points over its own automatic
// geometric-center fallback whenever they're authored. BossArena is its own component (not fields
// on Chunk itself, which every chunk in the level carries) - QPrototypeBossArena needs to be added
// alongside QPrototypeChunk on the Boss chunk's own prototype for this to have anywhere to write.
// Baking snaps every marker onto the floor beneath it first (see SnapToGround), same idea as
// ChunkRespawnPointBaker/ChunkWaypointBaker.
//
// MUST be run on an instance placed in an open Scene, not on the prefab asset in isolated Prefab
// Mode - see ChunkWaypointBaker's own comment, confirmed on this project that Prefab Mode's
// isolated stage does not reliably surface its own colliders to UnityEngine.Physics queries. Bake
// on a Scene instance (the hand-placed BossArena GameObject in QuantumGameScene.unity), then
// Prefab -> Apply to Prefab if it's ever backed by a real prefab asset.
[RequireComponent(typeof(QPrototypeChunk))]
[RequireComponent(typeof(QPrototypeBossArena))]
public class BossArenaMarkerBaker : MonoBehaviour
{
    // Markers are snapped to sit this far above the floor they land on, not exactly on it - see
    // SnapToGround. Matches ChunkRespawnPointBaker/ChunkWaypointBaker's own GroundOffset.
    private const float GroundOffset = 0.2f;

    [Button]
    public void BakeBossArenaMarkers()
    {
        QPrototypeBossArena bossArenaPrototype = GetComponent<QPrototypeBossArena>();

        BakeTeleportPoints(bossArenaPrototype);
        BakeSpawnPoints(bossArenaPrototype);
    }

    // One point per player slot (up to the [4] cap) so connected players land spread out across
    // the arena instead of stacked on the same spot - see BossEncounter.qtn's own comment on
    // BossArena.TeleportPoints.
    private void BakeTeleportPoints(QPrototypeBossArena bossArenaPrototype)
    {
        BossTeleportPointMarker[] markers = GetComponentsInChildren<BossTeleportPointMarker>();

        if (markers.Length == 0)
        {
            bossArenaPrototype.Prototype.TeleportPointCount = 0;
            LogHelper.Warn("BossArenaMarkerBaker", $"No BossTeleportPointMarker found under {name} - TeleportPointCount cleared, players will teleport to the chunk's geometric center instead.", this);
            return;
        }

        int cap = bossArenaPrototype.Prototype.TeleportPoints.Length;

        if (markers.Length > cap)
        {
            LogHelper.Error("BossArenaMarkerBaker", $"{name} has {markers.Length} BossTeleportPointMarker instances, only {cap} are allowed - remove {markers.Length - cap}.", this);
            return;
        }

        for (int i = 0; i < markers.Length; i++)
        {
            SnapToGround(markers[i].transform);
            bossArenaPrototype.Prototype.TeleportPoints[i] = transform.InverseTransformPoint(markers[i].transform.position).ToFPVector3();
        }

        bossArenaPrototype.Prototype.TeleportPointCount = (byte)markers.Length;

        LogHelper.Log("BossArenaMarkerBaker", $"Baked {markers.Length} boss teleport point(s) on {name}.", this);
    }

    private void BakeSpawnPoints(QPrototypeBossArena bossArenaPrototype)
    {
        BossSpawnPointMarker[] markers = GetComponentsInChildren<BossSpawnPointMarker>();

        if (markers.Length == 0)
        {
            bossArenaPrototype.Prototype.SpawnPointCount = 0;
            LogHelper.Warn("BossArenaMarkerBaker", $"No BossSpawnPointMarker found under {name} - SpawnPointCount cleared, the boss will spawn at the chunk's geometric center instead.", this);
            return;
        }

        int cap = bossArenaPrototype.Prototype.SpawnPoints.Length;

        if (markers.Length > cap)
        {
            LogHelper.Error("BossArenaMarkerBaker", $"{name} has {markers.Length} BossSpawnPointMarker instances, only {cap} are allowed - remove {markers.Length - cap}.", this);
            return;
        }

        for (int i = 0; i < markers.Length; i++)
        {
            SnapToGround(markers[i].transform);
            bossArenaPrototype.Prototype.SpawnPoints[i] = transform.InverseTransformPoint(markers[i].transform.position).ToFPVector3();
        }

        bossArenaPrototype.Prototype.SpawnPointCount = (byte)markers.Length;

        LogHelper.Log("BossArenaMarkerBaker", $"Baked {markers.Length} boss spawn point(s) on {name}.", this);
    }

    // Casts straight down from the marker's authored position to find the floor beneath it, then
    // repositions the marker GroundOffset above that hit point - same as ChunkRespawnPointBaker/
    // ChunkWaypointBaker's own SnapToGround.
    private static void SnapToGround(Transform marker)
    {
        if (Physics.Raycast(marker.position, Vector3.down, out RaycastHit hit) == false)
        {
            LogHelper.Warn("BossArenaMarkerBaker", $"{marker.name}: no ground found below {marker.position} - leaving position untouched.", marker);
            return;
        }

        marker.position = hit.point + Vector3.up * GroundOffset;
    }
}

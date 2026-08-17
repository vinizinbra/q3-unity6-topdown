using UnityEngine;

// Wall counterpart to GroundDetailSlot - see that class's own comment. Marks a prop meant for the
// middle/lower portion of a wall (e.g. moss, pipes, scratches at eye level) - kept as a distinct
// type from WallTopDetailSlot (not a shared component with a Top/Mid enum) so each unambiguously
// draws only from its own WorldTheme.Details pool (WallMidDetails), matching the same Ground/Wall
// split precedent. Both wall slot types still get EnvironmentManager.DetailSpriteMaterial assigned
// by ChunkDetailScatter.
[RequireComponent(typeof(SpriteRenderer))]
public class WallMidDetailSlot : MonoBehaviour
{
    [SerializeField, Tooltip("Intended world-unit size across the assigned sprite's largest dimension - independent of whichever sprite ends up picked or its own pixel size/PPU (ChunkDetailScatter.ResolveUnitScale normalizes that away), so swapping sprites at runtime never changes how big this slot reads in the scene.")]
    private float worldSize = 1f;

    public float WorldSize => worldSize;
}

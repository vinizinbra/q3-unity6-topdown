using UnityEngine;

// Marks a hand-placed ground detail prop - the artist places, positions, and rotates this
// GameObject directly in the chunk prefab (with a placeholder Sprite assigned for preview),
// instead of ChunkDetailScatter computing a position procedurally. At runtime ChunkDetailScatter
// only ever swaps this slot's own SpriteRenderer.sprite (deterministically, from
// WorldTheme.Details.GroundDetails) and rescales it - it never touches position/rotation, which
// stay exactly as authored. See docs/environment-details.md.
[RequireComponent(typeof(SpriteRenderer))]
public class GroundDetailSlot : MonoBehaviour
{
    [SerializeField, Tooltip("Intended world-unit size across the assigned sprite's largest dimension - independent of whichever sprite ends up picked or its own pixel size/PPU (ChunkDetailScatter.ResolveUnitScale normalizes that away), so swapping sprites at runtime never changes how big this slot reads in the scene.")]
    private float worldSize = 1f;

    public float WorldSize => worldSize;
}

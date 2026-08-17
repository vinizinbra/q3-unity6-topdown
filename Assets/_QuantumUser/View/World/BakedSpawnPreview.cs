using UnityEngine;

// Left on every ChunkSpawnBaker-baked child once it's stripped down to a view-only Scene preview
// (see ChunkSpawnBaker.BakeSpawns/ConvertToBakedPreview). Awake only ever fires once the game
// actually starts running (Play Mode or a build) - never for an object just sitting in an
// edit-mode Scene - so this stand-in stays visible for level design in the Editor and disappears
// the instant it would otherwise double up with the real entity TalentGateSystem spawns from the
// baked ChunkSpawnConfig.
public class BakedSpawnPreview : MonoBehaviour
{
    private void Awake()
    {
        Destroy(gameObject);
    }
}

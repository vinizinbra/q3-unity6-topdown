using UnityEngine;

namespace Quantum
{
    // Single named-reference point for an EnemyDataAsset.ViewPrefab's rig transforms - lets
    // EnemyBlobAnimationView/EnemyArmAimView/etc. each hold a sibling reference to this instead of
    // separately declaring and re-wiring their own root/arm Transform fields per enemy type.
    public class EnemyViewRig : MonoBehaviour
    {
        [SerializeField, Tooltip("The sprite EnemyView.SpawnSprite measures (via sprite.bounds, already Pixels-Per-Unit-corrected) to compute this instance's fit scale - so the rig always ends up the same apparent size for a given EnemyDataAsset.Radius regardless of what PPU this sprite happens to be imported at. Leave empty to use whichever SpriteRenderer is on this same GameObject (EnemyRoot).")]
        private SpriteRenderer referenceSprite;
        [SerializeField] private Transform head;
        [SerializeField] private Transform torso;
        [SerializeField] private Transform arm;
        [SerializeField] private Transform gun;

        // EnemyViewRig sits on ViewPrefab's own root, so that root doubles as EnemyRoot - no
        // separate self-reference field to wire. This is the same transform EnemyView.SpawnSprite
        // positions/scales at spawn (bottom-pivot offset, radius scale), so
        // EnemyBlobAnimationView.CacheBaseline (which runs after that, see SetRig) picks up those
        // spawn-time values as its baseline automatically.
        public Transform EnemyRoot => transform;
        public SpriteRenderer ReferenceSprite => referenceSprite != null ? referenceSprite : GetComponent<SpriteRenderer>();
        public Transform Head => head;
        public Transform Torso => torso;
        public Transform Arm => arm;
        public Transform Gun => gun;
    }
}

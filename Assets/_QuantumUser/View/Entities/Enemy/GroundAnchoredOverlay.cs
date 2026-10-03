using UnityEngine;

namespace Quantum
{
    // A view prop parented to an enemy's body sprite that ignores what the body animation does to it:
    // it sits at the sprite's lowest point as measured once at start (kept relative to the entity, so
    // it follows the terrain but not the body rearing/tilting/sinking), keeps a fixed world rotation and the enemy's base size,
    // and stays just in front of the sprite via a small local -Z offset (no sorting order). Made for the
    // Dune Leviathan's sand overlay (the sand its body rises out of) - the body can rear up, tilt,
    // squash or flip facing while the sand stays put at its base. Fades out while the body is off the
    // ground. Generic: drop it on any child of an enemy's body sprite.
    //
    // Runs in LateUpdate so it wins over every body animation (EnemyBlobAnimationView writes the rig's
    // local transform during Update).
    public class GroundAnchoredOverlay : MonoBehaviour
    {
        [SerializeField, Tooltip("World rotation (Euler) the overlay always keeps, whatever the parent's rotation.")]
        private Vector3 worldEuler = Vector3.zero;

        [SerializeField, Tooltip("Size = this object's own authored scale x scaleMultiplier x the enemy's fit scale (EnemyView.FitScale - the size the body is fit to), ignoring the body's squash/stretch and left/right facing mirror.")]
        private bool lockWorldScale = true;

        [SerializeField, Tooltip("Per-axis size tweak on top of the enemy's fit scale - live, so it can be tuned in Play Mode.")]
        private Vector3 scaleMultiplier = Vector3.one;

        [Header("Placement")]
        [SerializeField, Tooltip("Sprite whose lowest point (measured once, on the first frame) the overlay sits at. Leave empty to use the parent EnemyViewRig's ReferenceSprite (or the parent's own SpriteRenderer).")]
        private SpriteRenderer referenceRenderer;

        [SerializeField, Tooltip("Height above the sprite's lowest point.")]
        private float heightOffset = 0f;

        [SerializeField, Tooltip("World X offset from the parent's position (not mirrored with facing).")]
        private float horizontalOffset = 0f;

        [SerializeField, Tooltip("Offset along the parent sprite's local Z - negative = toward the camera, i.e. drawn in front of the sprite.")]
        private float depthOffset = -0.1f;

        [Header("Hide while the body is off the ground")]
        [SerializeField, Tooltip("Fade out while the body leaves the ground (burrow, hop, Jump/Dive steps, death) and fade back in when it lands - see EnemyBlobAnimationView.IsBodyOffGround.")]
        private bool hideWhileOffGround = true;
        [SerializeField] private float hideDuration = 0.12f;
        [SerializeField] private float showDuration = 0.25f;

        private SpriteRenderer _overlayRenderer;
        private Vector3 _authoredScale;
        private EnemyView _enemyView;
        private EnemyBlobAnimationView _blobView;
        private float _visibility = 1f;
        private Color _baseColor = Color.white;

        // Sprite's lowest point at start, relative to the entity root (EnemyView) - see LateUpdate.
        private Transform _entityRoot;
        private float _baseHeight;
        private bool _baseCaptured;

        private void Awake()
        {
            _authoredScale = new Vector3(Mathf.Abs(transform.localScale.x), Mathf.Abs(transform.localScale.y), Mathf.Abs(transform.localScale.z));
            _overlayRenderer = GetComponent<SpriteRenderer>();
            _enemyView = GetComponentInParent<EnemyView>(true);
            _entityRoot = _enemyView != null ? _enemyView.transform : null;
            _blobView = GetComponentInParent<EnemyBlobAnimationView>(true);

            if (_overlayRenderer != null)
                _baseColor = _overlayRenderer.color;

            if (referenceRenderer == null)
            {
                EnemyViewRig rig = GetComponentInParent<EnemyViewRig>(true);
                if (rig != null)
                    referenceRenderer = rig.ReferenceSprite;
            }

            if (referenceRenderer == null && transform.parent != null)
                referenceRenderer = transform.parent.GetComponent<SpriteRenderer>();
        }

        private void LateUpdate()
        {
            Transform parent = transform.parent;
            if (parent == null)
                return;

            // In front of the sprite along its own local Z, then snapped down to its lowest point.
            Vector3 position = parent.position + parent.rotation * new Vector3(0f, 0f, depthOffset);
            position.x += horizontalOffset;

            // Captured on the first frame the sprite is visible (after EnemyView has fit it), relative to
            // the un-animated entity root - so it follows the terrain, never the body's own animation.
            if (_baseCaptured == false && _entityRoot != null && referenceRenderer != null && referenceRenderer.sprite != null)
            {
                _baseHeight = referenceRenderer.bounds.min.y - _entityRoot.position.y;
                _baseCaptured = true;
            }

            if (_baseCaptured == true)
                position.y = _entityRoot.position.y + _baseHeight + heightOffset;

            transform.SetPositionAndRotation(position, Quaternion.Euler(worldEuler));

            UpdateVisibility();

            if (lockWorldScale)
            {
                // The enemy's base size (fit scale), never its animated scale.
                Vector3 worldScale = Vector3.Scale(_authoredScale, scaleMultiplier) * (_enemyView != null ? _enemyView.FitScale : 1f);
                Vector3 parentScale = parent.lossyScale;
                Vector3 current = transform.localScale;
                transform.localScale = new Vector3(
                    SafeDivide(worldScale.x, parentScale.x, current.x),
                    SafeDivide(worldScale.y, parentScale.y, current.y),
                    SafeDivide(worldScale.z, parentScale.z, current.z));
            }
        }

        // Fade out when the body leaves the ground, fade back in when it lands.
        private void UpdateVisibility()
        {
            if (_overlayRenderer == null)
                return;

            bool hide = hideWhileOffGround && _blobView != null && _blobView.IsBodyOffGround;
            float duration = hide ? hideDuration : showDuration;
            _visibility = Mathf.MoveTowards(_visibility, hide ? 0f : 1f, duration > 0f ? Time.deltaTime / duration : 1f);

            // Writes the full authored colour every frame (not just alpha) - the overlay owns its look.
            Color color = _baseColor;
            color.a = _baseColor.a * Mathf.SmoothStep(0f, 1f, _visibility);
            _overlayRenderer.color = color;
            _overlayRenderer.enabled = _visibility > 0.001f;
        }

        // Divides out the parent's scale including its sign, so a mirrored parent (facing left) doesn't
        // mirror the overlay. A parent squashed to ~0 (death/burrow shrink) leaves the last scale alone.
        private static float SafeDivide(float world, float parent, float current)
        {
            return Mathf.Abs(parent) < 0.0001f ? current : world / parent;
        }
    }
}

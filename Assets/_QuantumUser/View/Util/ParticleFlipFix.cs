using UnityEngine;

namespace Quantum
{
    // Drop this on a particle (or any child) that lives under a rig which flips itself by negating a
    // local scale axis to face left/right - EnemyBlobAnimationView negates the root's localScale.x,
    // WeaponView negates localScale.y (see MuzzleMirrorFix for the same idea, minus the Z fix below).
    // A child inherits that negative scale, which mirrors its world axes and flips the particle's
    // emission/shape sideways when the owner turns around.
    //
    // Every LateUpdate (after the parent has applied that frame's flip) this negates its own local
    // scale on the same axis whenever the parent is currently mirrored, so the two negatives cancel
    // and this transform's world-space axes always come out un-mirrored regardless of facing.
    //
    // It also re-pins its original local Z every frame: a mirrored parent (or an authored rig that
    // nudges depth as it flips) can otherwise drag the particle off the depth it was placed at,
    // which in this top-down game reads as the effect popping in front of / behind things it
    // shouldn't. Leave keepOriginalZ on to lock the authored depth.
    public class ParticleFlipFix : MonoBehaviour
    {
        private enum MirrorAxis { X, Y }

        [SerializeField, Tooltip("Which local axis the parent flips for its facing mirror. X for enemy rigs (EnemyBlobAnimationView's root, localScale.x) and most characters. Y for the player's WeaponView (localScale.y).")]
        private MirrorAxis mirrorAxis = MirrorAxis.X;

        [SerializeField, Tooltip("Re-pin this transform's authored local Z every frame so a flip can't drag the particle off its intended depth/sorting position.")]
        private bool keepOriginalZ = true;

        private Transform _target;
        private Vector3 _restLocalScale;
        private float _restLocalZ;

        private void Awake()
        {
            _target = transform;
            _restLocalScale = _target.localScale;
            _restLocalZ = _target.localPosition.z;
        }

        private void LateUpdate()
        {
            if (_target.parent == null)
                return;

            CompensateParentMirror();

            if (keepOriginalZ)
                KeepOriginalZ();
        }

        private void CompensateParentMirror()
        {
            Vector3 parentScale = _target.parent.lossyScale;
            bool parentMirrored = mirrorAxis == MirrorAxis.X ? parentScale.x < 0f : parentScale.y < 0f;

            Vector3 scale = _restLocalScale;
            if (mirrorAxis == MirrorAxis.X)
                scale.x = parentMirrored ? -_restLocalScale.x : _restLocalScale.x;
            else
                scale.y = parentMirrored ? -_restLocalScale.y : _restLocalScale.y;

            _target.localScale = scale;
        }

        private void KeepOriginalZ()
        {
            Vector3 localPos = _target.localPosition;
            if (localPos.z != _restLocalZ)
            {
                localPos.z = _restLocalZ;
                _target.localPosition = localPos;
            }
        }
    }
}

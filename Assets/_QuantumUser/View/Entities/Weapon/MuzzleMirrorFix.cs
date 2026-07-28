using UnityEngine;

namespace Quantum
{
    // A flipped parent (WeaponView.ApplyAim mirrors localScale.y to -1 when aiming left;
    // EnemyBlobAnimationView mirrors root's localScale.x for facing) is inherited by every child
    // underneath it too - including whatever sits at the muzzle (flash particle, tracer origin,
    // etc) - which mirrors its emission direction and breaks any transform.right/up/forward math
    // done from it. Which axis carries the flip differs per rig (Y for the player's weapon, X for
    // enemy rigs - see mirrorAxis below), but the fix is identical either way.
    //
    // Put this on the muzzle child directly. Every LateUpdate (after the parent has applied that
    // frame's flip) it mirrors its own local scale on the same axis right back whenever the
    // parent is currently mirrored, so the two negatives cancel and this transform's lossyScale -
    // and therefore its world-space axes - always come out un-mirrored, regardless of which way
    // the rig is currently facing.
    public class MuzzleMirrorFix : MonoBehaviour
    {
        private enum MirrorAxis { X, Y }

        [SerializeField, Tooltip("Which local axis the parent flips for its own facing mirror. Y for the player's WeaponView (localScale.y). X for enemy rigs (EnemyBlobAnimationView's root/arm, localScale.x).")]
        private MirrorAxis mirrorAxis = MirrorAxis.Y;

        private Vector3 restLocalScale;
        private Transform target;

        private void Awake()
        {
            target = transform;
            restLocalScale = target.localScale;
        }

        private void LateUpdate()
        {
            CompensateParentMirror();
        }

        private void CompensateParentMirror()
        {
            if (target.parent == null)
                return;

            Vector3 parentScale = target.parent.lossyScale;
            bool parentMirrored = mirrorAxis == MirrorAxis.X ? parentScale.x < 0f : parentScale.y < 0f;

            Vector3 scale = restLocalScale;
            if (mirrorAxis == MirrorAxis.X)
                scale.x = parentMirrored ? -restLocalScale.x : restLocalScale.x;
            else
                scale.y = parentMirrored ? -restLocalScale.y : restLocalScale.y;

            target.localScale = scale;
        }
    }
}

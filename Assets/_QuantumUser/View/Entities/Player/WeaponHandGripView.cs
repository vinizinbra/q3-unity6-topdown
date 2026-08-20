using UnityEngine;

namespace Quantum
{
    // Snaps the character's two hand blobs onto whichever weapon WeaponViewController has
    // currently spawned, reading its grip anchors (WeaponView.RightHandGrip*) fresh every frame
    // rather than caching a reference at spawn time - so hands keep tracking correctly across a
    // future weapon swap without this needing its own event subscription. Position, rotation and
    // scale all come from the weapon, i.e. the hands are driven as if parented to it; they aren't
    // actually parented because they belong to the character rig and have to outlive any one
    // weapon instance.
    // Sits alongside BlobAnimationView on the character rig, not on the weapon prefab itself.
    // Plain MonoBehaviour, not a CustomQuantumEntityViewComponent - it never touches Quantum
    // frame data, just two transforms and WeaponViewController's current instance.
    public class WeaponHandGripView : MonoBehaviour
    {
        [Header("Rig (assign once)")]
        [SerializeField] private Transform rightHand;
        [SerializeField] private Transform leftHand;
        [SerializeField, Tooltip("Assign explicitly in the prefab - the WeaponViewController on this same rig.")]
        private WeaponViewController weaponInstantiator;

        // The scale each hand authors in the rig, captured before any weapon has touched it -
        // WeaponView's own grip scale is a multiplier over this, not a replacement, so a weapon
        // that leaves it at 1 keeps whatever the rig itself set.
        private Vector3 rightHandRestScale = Vector3.one;
        private Vector3 leftHandRestScale = Vector3.one;

        private void Awake()
        {
            if (rightHand != null) rightHandRestScale = rightHand.localScale;
            if (leftHand != null) leftHandRestScale = leftHand.localScale;
        }

        private void Update()
        {
            WeaponView weaponView = weaponInstantiator != null ? weaponInstantiator.CurrentWeaponView : null;
            if (weaponView == null) return;

            // The hand adopts the weapon's full rotation (camera billboard + the aim spin
            // ApplyAim applies), exactly as if it were parented to the weapon - the same frame its
            // grip position already comes from. The authored grip rotation then layers on inside
            // that frame, so it reads as "how the hand is angled ON the gun" rather than an angle
            // in the rig's own space.
            Quaternion weaponRotation = weaponView.transform.rotation;
            bool flipped = weaponView.Flipped;

            ApplyGrip(rightHand, weaponView.RightHandGripPosition, weaponView.RightHandGripRotation, weaponView.RightHandGripScale, rightHandRestScale, weaponRotation, flipped);
            ApplyGrip(leftHand, weaponView.LeftHandGripPosition, weaponView.LeftHandGripRotation, weaponView.LeftHandGripScale, leftHandRestScale, weaponRotation, flipped);
        }

        // Written fresh from the weapon's rotation and the authored value every frame, never
        // accumulated onto whatever the hand already had - so localRotation holds the authored
        // angle instead of spinning. Post-multiplying makes the authored euler a rotation in the
        // WEAPON's space (what a child transform's localRotation would be), so zero simply means
        // "held exactly like the gun". scaleMultiplier is componentwise over restScale, so
        // (1,1,1) is a no-op.
        private static void ApplyGrip(Transform hand, Vector3 position, Vector3 localRotation, Vector3 scaleMultiplier, Vector3 restScale, Quaternion weaponRotation, bool flipped)
        {
            if (hand == null) return;

            hand.position = position;
            hand.rotation = weaponRotation * Quaternion.Euler(localRotation);

            // Mirrored on Y, the same axis ApplyAim flips the weapon itself on - now that the hand
            // carries the weapon's rotation it shares the weapon's local frame, where X runs along
            // the barrel and Y is what mirrors across it. (The weapon's own flip is a localScale
            // sign, not part of its rotation, so it isn't inherited above and has to be redone
            // here.) Pairs with the authored rotation's own Z mirror in
            // WeaponView.MirrorGripRotation: scale mirrors the sprite, rotation mirrors the angle
            // it's held at, and only both together read as a true mirror.
            Vector3 scale = Vector3.Scale(restScale, ResolveScaleMultiplier(scaleMultiplier));
            if (flipped) scale.y = -scale.y;
            hand.localScale = scale;
        }

        // An all-zero multiplier means "never authored", not "shrink the hand to nothing" - the
        // weapon prefabs that predate WeaponAnimationParams' grip scale have no value for it in
        // their serialized anim block at all, and a zero-scale hand is invisible rather than
        // visibly wrong, so it would read as the hands having silently disappeared.
        private static Vector3 ResolveScaleMultiplier(Vector3 multiplier)
        {
            return multiplier == Vector3.zero ? Vector3.one : multiplier;
        }
    }
}

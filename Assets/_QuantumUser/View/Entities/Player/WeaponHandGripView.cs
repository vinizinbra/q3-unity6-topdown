using UnityEngine;

namespace Quantum
{
    // Snaps the character's two hand blobs onto whichever weapon WeaponViewController has
    // currently spawned, reading its grip anchors (WeaponView.RightHandGrip/LeftHandGrip) fresh
    // every frame rather than caching a reference at spawn time - so hands keep tracking
    // correctly across a future weapon swap without this needing its own event subscription.
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

        private void Update()
        {
            WeaponView weaponView = weaponInstantiator != null ? weaponInstantiator.CurrentWeaponView : null;
            if (weaponView == null) return;

            // Only X (the camera-pitch billboard component) is copied from the weapon - Y/Z stay
            // whatever the hand rig itself authors, so this doesn't fight the character's own hand
            // animation/facing.
            float weaponPitch = weaponView.transform.eulerAngles.x;

            if (rightHand != null)
            {
                rightHand.position = weaponView.RightHandGripPosition;
                Vector3 rightEuler = rightHand.eulerAngles;
                rightEuler.x = weaponPitch;
                rightHand.eulerAngles = rightEuler;
            }

            if (leftHand != null)
            {
                leftHand.position = weaponView.LeftHandGripPosition;
                Vector3 leftEuler = leftHand.eulerAngles;
                leftEuler.x = weaponPitch;
                leftHand.eulerAngles = leftEuler;
            }
        }
    }
}

using UnityEngine;

namespace Quantum
{
    // Every weapon-specific animation tuning value WeaponView reads, grouped into one
    // serializable field so the whole set can be right-click Copy'd on one WeaponView and
    // Paste'd onto another's, instead of copying the whole component.
    [System.Serializable]
    public class WeaponAnimationParams
    {
        [Header("Position Offset")]
        [Tooltip("Screen-space offset (right, up) blended in while aiming directly right. Mirrored automatically when flipped/aiming left.")]
        public Vector2 rightOffset;
        [Tooltip("Screen-space offset (right, up) blended in while aiming directly up.")]
        public Vector2 upOffset;
        [Tooltip("Screen-space offset (right, up) blended in while aiming directly down.")]
        public Vector2 downOffset;

        [Header("Hand Grips")]
        [Tooltip("Local position (relative to this weapon) where the right hand blob should sit while held. Z defaults to -0.01 so the hand renders in front of the gun sprite instead of behind it - this object is billboarded to face the camera, so local Z tracks camera depth.")]
        public Vector3 rightHandGrip = new Vector3(0f, 0f, -0.01f);
        [Tooltip("Local position (relative to this weapon) where the left hand blob (off-hand support) should sit while held. Same Z convention as rightHandGrip.")]
        public Vector3 leftHandGrip = new Vector3(0f, 0f, -0.01f);

        [Header("Shoot Recoil")]
        [Tooltip("Screen-space distance the gun snaps back on each shot, opposite the currently-held aim direction.")]
        public float recoilKickDistance = 0.12f;
        [Tooltip("Degrees the muzzle kicks up on each shot (auto-mirrored when the gun is flipped, so it always reads as 'up' on screen).")]
        public float recoilRotationKick = 6f;
        [Tooltip("How long each shot's kick takes to fully settle back to rest. Each shot starts its own independent punch rather than accumulating with any still-decaying kick from a previous shot, so keep this at or below the weapon's fire interval if rapid fire shouldn't visibly reset mid-decay.")]
        public float recoilDuration = 0.15f;
        [Tooltip("Oscillations per second as the kick settles.")]
        public float recoilFrequency = 16f;
        [Range(0f, 1f), Tooltip("0 = full recoil (kicks back, swings past rest, settles), 1 = no recoil (eases straight back to rest with no overshoot). Shared by the position, rotation, and knockback kicks below.")]
        public float recoilAsymmetry = 0.3f;

        [Header("Shoot Knockback")]
        [Tooltip("Distance the gun punches back along the camera's forward axis (away from the viewer) on each shot - a depth kick distinct from the screen-space position/rotation kick above.")]
        public float knockbackDistance = 0.1f;
        [Range(0f, 1f), Tooltip("Fraction the gun squashes down in scale at the peak of the knockback punch.")]
        public float knockbackScalePunch = 0.1f;
    }
}

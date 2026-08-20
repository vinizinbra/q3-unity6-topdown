using UnityEngine;

namespace Quantum
{
    // One (strength, duration, frequency) punch, shaped after PrimeTween's own ShakeSettings -
    // WeaponView.Shoot() feeds these straight into a Tween.PunchCustom call. Strength is a Vector3
    // even for the rotation punches below (only .x is used, same convention WeaponView.Shoot's
    // own rotationKick/knockbackPunch already use for a scalar carried through a Vector3 punch).
    [System.Serializable]
    public class CharacterPunchSettings
    {
        public Vector3 Strength;
        public float Duration = 0.1f;
        public float Frequency = 20f;
    }

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
        [Tooltip("Local rotation (euler degrees) the right hand is forced to while holding this weapon, e.g. (0, 0, -20) cocks the hand down along the barrel. Applied in the WEAPON's own space (the hand follows the gun's rotation), and an absolute override rather than an offset - zero means held exactly like the gun. Z is auto-mirrored when the gun is flipped, so it keeps reading the same way aiming left.")]
        public Vector3 rightHandGripRotation;
        [Tooltip("Multiplier on the right hand's own authored rest scale while this weapon is held, e.g. (1.2, 1.2, 1.2) fattens the hand on a heavy weapon. 1 = the rig's own scale, untouched.")]
        public Vector3 rightHandGripScale = Vector3.one;
        [Tooltip("Local position (relative to this weapon) where the left hand blob (off-hand support) should sit while held. Same Z convention as rightHandGrip.")]
        public Vector3 leftHandGrip = new Vector3(0f, 0f, -0.01f);
        [Tooltip("Local rotation (euler degrees) the left hand is forced to. Same convention as rightHandGripRotation.")]
        public Vector3 leftHandGripRotation;
        [Tooltip("Multiplier on the left hand's own authored rest scale. Same convention as rightHandGripScale.")]
        public Vector3 leftHandGripScale = Vector3.one;

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

        [Header("Character Shoot Punch (kicked into the shooter's own BlobAnimationView, not this weapon's transform - tune per weapon since a shotgun should knock the body around more than a pistol)")]
        [Tooltip("Head position kick, local space (e.g. (0, 0.04, 0) nods the head up).")]
        public CharacterPunchSettings shakePositionHead = new CharacterPunchSettings { Strength = new Vector3(0f, 0.04f, 0f) };
        [Tooltip("Whole-body Z-axis twist in degrees (only Strength.x is used). Auto-flipped by facing so it always kicks away from the muzzle.")]
        public CharacterPunchSettings shakeRotationBody = new CharacterPunchSettings { Strength = new Vector3(-6f, 0f, 0f) };
        [Tooltip("Head-only Z-axis twist in degrees (only Strength.x is used), layered independently from the body twist above. Also auto-flipped by facing.")]
        public CharacterPunchSettings shakeRotationHead = new CharacterPunchSettings();
        [Tooltip("Whole-body fractional scale punch per axis, e.g. (0.06, -0.06, 0.06) squashes horizontally/depth-wise while stretching taller. Unlike the rotation punch above, root's scale is untouched by Run's own lean/rock sway, so this is the channel that still reads clearly while moving.")]
        public CharacterPunchSettings shakeScaleBody = new CharacterPunchSettings { Strength = new Vector3(0.06f, -0.08f, 0.06f), Duration = 0.08f };
        [Tooltip("Head-only fractional scale punch per axis, layered independently from the body scale punch above.")]
        public CharacterPunchSettings shakeScaleHead = new CharacterPunchSettings();
    }
}

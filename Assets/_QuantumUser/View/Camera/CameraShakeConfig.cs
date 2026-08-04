using Quantum;
using UnityEngine;

namespace QuantumUser.View
{
    // View-only tuning for the camera shake fired on EventPlayerFired (see WeaponCameraShakeListener).
    // Mirrors EffectConfig's per-tier field layout, but lives here rather than as a Quantum AssetObject
    // since shake is presentational and has no bearing on deterministic simulation.
    [CreateAssetMenu(fileName = "CameraShakeConfig", menuName = "RiftRaiders/Camera/Camera Shake Config")]
    public class CameraShakeConfig : ScriptableObject
    {
        [Header("Small")]
        public float SmallAmplitude = 0.1f;
        public float SmallDuration = 0.1f;
        public float SmallFrequency = 20f;

        [Header("Medium")]
        public float MediumAmplitude = 0.2f;
        public float MediumDuration = 0.18f;
        public float MediumFrequency = 20f;

        [Header("Strong")]
        public float StrongAmplitude = 0.35f;
        public float StrongDuration = 0.3f;
        public float StrongFrequency = 20f;

        public void GetShake(WeaponShakeTier tier, out float amplitude, out float duration, out float frequency)
        {
            switch (tier)
            {
                case WeaponShakeTier.Small:
                    amplitude = SmallAmplitude;
                    duration = SmallDuration;
                    frequency = SmallFrequency;
                    break;

                case WeaponShakeTier.Strong:
                    amplitude = StrongAmplitude;
                    duration = StrongDuration;
                    frequency = StrongFrequency;
                    break;

                default:
                    amplitude = MediumAmplitude;
                    duration = MediumDuration;
                    frequency = MediumFrequency;
                    break;
            }
        }
    }
}

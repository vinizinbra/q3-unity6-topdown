using System.Collections.Generic;
using UnityEngine;

namespace Quantum
{
    // Orients each particle of a Shuriken ParticleSystem towards its own frame-to-frame movement
    // direction instead of a fixed/random start rotation. Velocity isn't read from
    // Particle.velocity (which ignores forces like Drag/Noise/collisions) - it's derived by
    // tracking every particle's previous position (keyed by its randomSeed, since GetParticles'
    // array order isn't stable across frames as particles are born/die) and dividing by
    // deltaTime, so it reflects what the particle is actually doing right now.
    //
    // Requires the system's Main module to have "3D Start Rotation" enabled (auto-enabled in
    // Awake) so each particle carries a full Vector3 rotation instead of a single float. Each
    // axis can be toggled independently - a disabled axis is left untouched, so e.g. a sprite
    // that should only spin around Z as it moves can leave X/Y alone.
    [RequireComponent(typeof(ParticleSystem))]
    public class ParticleVelocityRotation : MonoBehaviour
    {
        [SerializeField] private bool _rotateX;
        [SerializeField] private bool _rotateY;
        [SerializeField] private bool _rotateZ = true;

        [Tooltip("Reference 'up' axis used to build the look rotation from velocity direction.")]
        [SerializeField] private Vector3 _upHint = Vector3.up;

        [Tooltip("Added to the computed euler angles - use to align a mesh/sprite's authored forward axis to the velocity direction.")]
        [SerializeField] private Vector3 _eulerOffset;

        [Tooltip("Below this speed, a particle's rotation is left as-is (avoids jitter while nearly stationary).")]
        [SerializeField] private float _minSpeed = 0.01f;

        private ParticleSystem _particleSystem;
        private ParticleSystem.Particle[] _particleBuffer;
        private Dictionary<uint, Vector3> _previousPositions = new();
        private Dictionary<uint, Vector3> _scratchPositions = new();

        private void Awake()
        {
            _particleSystem = GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = _particleSystem.main;
            main.startRotation3D = true;
        }

        private void LateUpdate()
        {
            EnsureBufferCapacity();

            int count = _particleSystem.GetParticles(_particleBuffer);
            float minSpeedSqr = _minSpeed * _minSpeed;

            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle particle = _particleBuffer[i];

                Vector3 velocity = _previousPositions.TryGetValue(particle.randomSeed, out Vector3 previousPosition) == true
                    ? (particle.position - previousPosition) / Time.deltaTime
                    : Vector3.zero;

                _scratchPositions[particle.randomSeed] = particle.position;

                if (velocity.sqrMagnitude >= minSpeedSqr)
                {
                    Vector3 targetEuler = Quaternion.LookRotation(velocity.normalized, _upHint).eulerAngles + _eulerOffset;
                    Vector3 rotation = particle.rotation3D;

                    if (_rotateX == true)
                        rotation.x = targetEuler.x;
                    if (_rotateY == true)
                        rotation.y = targetEuler.y;
                    if (_rotateZ == true)
                        rotation.z = targetEuler.z;

                    particle.rotation3D = rotation;
                    _particleBuffer[i] = particle;
                }
            }

            _particleSystem.SetParticles(_particleBuffer, count);

            (_previousPositions, _scratchPositions) = (_scratchPositions, _previousPositions);
            _scratchPositions.Clear();
        }

        private void EnsureBufferCapacity()
        {
            int maxParticles = _particleSystem.main.maxParticles;

            if (_particleBuffer == null || _particleBuffer.Length < maxParticles)
                _particleBuffer = new ParticleSystem.Particle[maxParticles];
        }
    }
}

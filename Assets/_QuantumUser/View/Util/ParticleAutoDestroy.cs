using UnityEngine;

namespace Quantum
{
    // Added programmatically (AddComponent) to a plain-Instantiate'd (non-pooled) particle instance
    // that needs to clean itself up once it naturally finishes - e.g. SkillActionFxView's Parented
    // spawn mode. Unlike ParticleGracefulStop (see Enemy/), this never force-stops emission early -
    // it only waits for every particle system in the hierarchy to report no live particles, then
    // destroys the GameObject. Prefab must not loop, or this never fires.
    public class ParticleAutoDestroy : MonoBehaviour
    {
        private ParticleSystem[] _systems;

        private void Start()
        {
            _systems = GetComponentsInChildren<ParticleSystem>(includeInactive: true);
        }

        private void Update()
        {
            foreach (ParticleSystem system in _systems)
            {
                if (system.IsAlive(true) == true)
                    return;
            }

            Destroy(gameObject);
        }
    }
}

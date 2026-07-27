using UnityEngine;

namespace Quantum
{
    // Added programmatically (AddComponent, not hand-authored on a prefab - see
    // EnemyAttackVisualsView.ClearParentedParticle) whenever a phase-triggered parented particle
    // needs to stop cleanly instead of being cut off mid-emission by a plain Destroy(). Stopping
    // just means "stop spawning new particles" (ParticleSystemStopBehavior.StopEmitting) -
    // already-emitted particles keep simulating/fading naturally, and the GameObject only actually
    // destroys itself once every system (including children, e.g. a sub-emitter) reports no live
    // particles left.
    //
    // Unparents itself (keeping current world position) the moment stopping starts, so it settles
    // in place and finishes on its own rather than being dragged along by the enemy's next action.
    public class ParticleGracefulStop : MonoBehaviour
    {
        private ParticleSystem[] _systems;
        private bool _stopping;

        public void StopAndDestroyWhenFinished()
        {
            if (_stopping == true)
                return;

            _stopping = true;
            transform.SetParent(null, worldPositionStays: true);

            _systems = GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            foreach (ParticleSystem system in _systems)
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void Update()
        {
            if (_stopping == false)
                return;

            foreach (ParticleSystem system in _systems)
            {
                if (system.IsAlive(true) == true)
                    return; // at least one system (or its children) still has live particles
            }

            Destroy(gameObject);
        }
    }
}

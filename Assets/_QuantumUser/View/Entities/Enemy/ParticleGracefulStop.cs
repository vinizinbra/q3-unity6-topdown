using QuantumUser.View.Util;
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
    // TrailRenderers under it get the same treatment: emitting is switched off and the object lives
    // on for the longest TrailRenderer.time so the ribbon fades out instead of vanishing (a
    // projectile trail authored as a TrailRenderer, see ProjectileView.trailRenderer).
    //
    // Unparents itself (keeping current world position) the moment stopping starts, so it settles
    // in place and finishes on its own rather than being dragged along by the enemy's next action.
    public class ParticleGracefulStop : MonoBehaviour
    {
        private ParticleSystem[] _systems;
        private bool _stopping;
        private float _stopTime;

        // TrailRenderer point ages run on scaled time, so this is Time.time, not unscaled.
        private float _trailLingerUntil;

        public void StopAndDestroyWhenFinished()
        {
            if (_stopping == true)
                return;

            _stopping = true;
            _stopTime = Time.unscaledTime;
            transform.SetParent(null, worldPositionStays: true);

            _systems = GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            foreach (ParticleSystem system in _systems)
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            float longestTrail = 0f;
            foreach (TrailRenderer trail in GetComponentsInChildren<TrailRenderer>(includeInactive: true))
            {
                trail.emitting = false;
                longestTrail = Mathf.Max(longestTrail, trail.time);
            }
            _trailLingerUntil = Time.time + longestTrail;

            LogHelper.Log("ProjFlow", $"GracefulStop {name}: stop emitting, systems={_systems.Length} trailLinger={longestTrail:F2}s active={gameObject.activeInHierarchy} t={_stopTime:F3}", this);
        }

        private void OnDestroy()
        {
            if (_stopping == true)
                LogHelper.Log("ProjFlow", $"GracefulStop {name}: destroyed after {Time.unscaledTime - _stopTime:F3}s", this);
        }

        private void Update()
        {
            if (_stopping == false)
                return;

            if (Time.time < _trailLingerUntil)
                return; // a TrailRenderer ribbon is still fading

            foreach (ParticleSystem system in _systems)
            {
                if (system.IsAlive(true) == true)
                    return; // at least one system (or its children) still has live particles
            }

            Destroy(gameObject);
        }
    }
}

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

        // TrailRenderer point ages run on scaled time, so this is Time.time, not unscaled.
        private float _trailLingerUntil;

        // Return mode (StopAndReturnWhenFinished): the GameObject belongs to a POOLED projectile view
        // (ProjectileView/ProjectileViewUpdater) and must survive the fade, so instead of being
        // destroyed it is handed back to the parent it came from and only this component goes away.
        private bool _returnMode;
        private Transform _home;
        private System.Action _onReturned;

        public void StopAndDestroyWhenFinished()
        {
            Begin();
        }

        // Same fade as StopAndDestroyWhenFinished, but the object is re-parented under `home` when it
        // finishes (its local pose is the caller's business - ProjectileViewSnapshot.Restore replays
        // it) and this component is removed so the same object can be faded again on its next use.
        // `onReturned` fires once it is back (or once it is clear it never will be).
        public void StopAndReturnWhenFinished(Transform home, System.Action onReturned)
        {
            if (_stopping == true)
                return;

            _returnMode = true;
            _home = home;
            _onReturned = onReturned;
            Begin();
        }

        private void Begin()
        {
            if (_stopping == true)
                return;

            _stopping = true;
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
        }

        private void Update()
        {
            if (_stopping == false)
                return;

            if (Time.time < _trailLingerUntil)
                return; // a TrailRenderer ribbon is still fading

            foreach (ParticleSystem system in _systems)
            {
                if (system != null && system.IsAlive(true) == true)
                    return; // at least one system (or its children) still has live particles
            }

            if (_returnMode == false)
            {
                Destroy(gameObject);
                return;
            }

            Return();
        }

        private void Return()
        {
            System.Action onReturned = _onReturned;
            _onReturned = null;

            // The pooled view this belonged to is gone (scene unload / abandoned after a timeout):
            // there is nowhere to go home to, so it is just a leftover now.
            if (_home == null)
            {
                onReturned?.Invoke();
                Destroy(gameObject);
                return;
            }

            foreach (ParticleSystem system in _systems)
            {
                if (system != null)
                    system.Clear(withChildren: false);
            }

            foreach (TrailRenderer trail in GetComponentsInChildren<TrailRenderer>(includeInactive: true))
                trail.Clear();

            transform.SetParent(_home, worldPositionStays: false);

            onReturned?.Invoke();
            Destroy(this);
        }
    }
}

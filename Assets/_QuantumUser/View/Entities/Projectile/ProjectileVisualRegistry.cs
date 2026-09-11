using System.Collections.Generic;
using UnityEngine;

namespace Quantum
{
    // Answers one question for ProjectileGhostTrailManager: "did this projectile entity ever get a
    // real ProjectileView?" If yes, ProjectileVisualController owns the shot's ending and the
    // manager must stay out of it; if no, the manager draws the fallback ghost trail.
    //
    // Entries are grace-stamped rather than removed, for two reasons:
    //   - ProjectileView.DeInitialize runs in a Unity frame's view pass, which QuantumGame always
    //     runs BEFORE dispatching that frame's events, so the destroy event for a shot that had a
    //     view arrives after its DeInitialize and must still see the entry.
    //   - EventProjectileDestroyed is not a synced event: a mispredicted destroy gets dispatched
    //     again with a different tick/position, and the ghost path stamps the entity itself so that
    //     second dispatch draws nothing - one trail, one impact, per shot.
    // EntityRef carries a generation, so a stale entry can never match a recycled entity index.
    public static class ProjectileVisualRegistry
    {
        private const float GraceSeconds = 1f;
        private const int PurgeThreshold = 64;

        private static readonly Dictionary<EntityRef, float> _expiresAt = new();
        private static readonly List<EntityRef> _purgeBuffer = new();

        public static void Register(EntityRef entity)
        {
            _expiresAt[entity] = float.MaxValue;
        }

        public static void MarkGone(EntityRef entity)
        {
            _expiresAt[entity] = Time.unscaledTime + GraceSeconds;

            if (_expiresAt.Count > PurgeThreshold)
                Purge();
        }

        public static bool Contains(EntityRef entity)
        {
            return _expiresAt.TryGetValue(entity, out float expiresAt) && Time.unscaledTime < expiresAt;
        }

        public static void Clear()
        {
            _expiresAt.Clear();
        }

        private static void Purge()
        {
            float now = Time.unscaledTime;
            _purgeBuffer.Clear();

            foreach (var kvp in _expiresAt)
            {
                if (now >= kvp.Value)
                    _purgeBuffer.Add(kvp.Key);
            }

            foreach (EntityRef entity in _purgeBuffer)
                _expiresAt.Remove(entity);
        }
    }
}

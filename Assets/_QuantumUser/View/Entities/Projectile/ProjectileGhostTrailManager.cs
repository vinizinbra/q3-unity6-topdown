using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Draws the shot for a projectile that never got an entity view.
    //
    // QuantumEntityViewUpdater only diffs views against the FINAL Predicted/Verified frame of each
    // Unity frame's tick batch, so a bullet fast enough to be created and destroyed inside that batch
    // (or inside a resimulation of a remote player's late-arriving input) never exists as far as the
    // view layer is concerned: ProjectileView.Initialize never runs, ProjectileVisualController is
    // never created, and the shot is simply invisible. The destroy event still fires, though -
    // QuantumGame dispatches every tick's events after the view pass - and it now carries everything
    // needed to draw the shot after the fact: where it left from, where it landed, and which
    // ProjectileDataAsset it was.
    //
    // For such a shot this plays ONLY the projectile prefab's own trail(s) - its trailParticle
    // (pooled through EffectsManager) and/or its trailRenderer (pooled here) - fully restarted at the
    // muzzle so nothing from a previous pooled life streaks across, flies them to the hit point over
    // ghostFlightDuration, plays the prefab's impact effect, and lets them fade. Shots that DID get a
    // view are left entirely to ProjectileVisualController - ProjectileVisualRegistry is what tells
    // the two cases apart.
    //
    // Known, accepted: a PREDICTED destroy that later rolls back (the entity actually survives) shows
    // a ghost and then the real bullet - unavoidable without a verified-only event. A re-dispatch of
    // the same destroy (a mispredicted position) is filtered by the registry, so each shot gets one
    // trail and one impact at most.
    //
    // Out of scope by design: the elemental trail (ProjectileElementalFxView reads
    // Projectile.Element off the live entity; the event carries no element) and pierce/ricochet
    // EventProjectileImpacted points (no entity, no view). Lives on the EffectsManager GameObject in
    // QuantumGameScene.
    public class ProjectileGhostTrailManager : MonoBehaviour
    {
        [SerializeField, Tooltip("Seconds the ghost trail takes from the muzzle to the hit point for a shot whose view never existed. Frame-rate independent; 0.033 is about two frames at 60fps - fast enough to read as a near-instant bullet, slow enough that the trail actually draws a streak.")]
        private float ghostFlightDuration = 0.033f;

        private readonly struct Template
        {
            public readonly ParticleSystem Trail;
            public readonly TrailRenderer Line;
            public readonly ParticleSystem Impact;

            public Template(ParticleSystem trail, TrailRenderer line, ParticleSystem impact)
            {
                Trail = trail;
                Line = line;
                Impact = impact;
            }

            public bool HasTrail => Trail != null || Line != null;
        }

        // Resolving ProjectileDataAsset -> EntityPrototype -> EntityView -> prefab -> ProjectileView
        // walks three asset lookups; cached per data asset, misses included, so a fast weapon doesn't
        // repeat it (or re-warn) on every shot.
        private readonly Dictionary<AssetGuid, Template> _templates = new();

        // EffectsManager only pools ParticleSystems, so TrailRenderer ghosts are pooled here - same
        // "reuse the first inactive copy, else grow by one" shape as HitscanViewBase.Acquire.
        private readonly Dictionary<TrailRenderer, List<TrailRenderer>> _linePools = new();

        private void Awake()
        {
            QuantumEvent.Subscribe<EventProjectileDestroyed>(this, OnProjectileDestroyed);
        }

        private void OnDestroy()
        {
            QuantumEvent.UnsubscribeListener(this);
            ProjectileVisualRegistry.Clear();
        }

        private void OnProjectileDestroyed(EventProjectileDestroyed e)
        {
            if (ProjectileVisualRegistry.Contains(e.Entity))
            {
                LogHelper.Log("ProjFlow", $"[{e.Entity}] GHOST skipped - entity had a view (normal path) t={Time.unscaledTime:F3}", this);
                return; // had a real view - ProjectileVisualController owns this shot's ending
            }

            // Stamped BEFORE drawing anything, so a second dispatch of this same destroy (see the
            // class comment) is dropped even if something below early-outs.
            ProjectileVisualRegistry.MarkGone(e.Entity);

            if (EffectsManager.Instance == null)
                return;

            Template template = ResolveTemplate(e.ProjectileData);

            Vector3 end = e.Position.ToUnityVector3();
            Vector3 simSpawn = e.SpawnPosition.ToUnityVector3();
            Vector3 start = ProjectileView.ResolveVisualSpawnPosition(e.Owner, simSpawn);

            Vector3 delta = end - start;
            if (template.HasTrail == false || delta.sqrMagnitude < 0.0001f)
            {
                LogHelper.Log("ProjFlow", $"[{e.Entity}] GHOST impact only: hasTrail={template.HasTrail} dist={delta.magnitude:F2}", this);
                PlayImpact(template.Impact, end);
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);

            LogHelper.Log("ProjFlow", $"[{e.Entity}] GHOST spawn particle={(template.Trail != null ? template.Trail.name : "none")} line={(template.Line != null ? template.Line.name : "none")} origin={(start == simSpawn ? "SpawnPosition" : "muzzle")} start={start} end={end} dist={delta.magnitude:F2} dur={ghostFlightDuration}s t={Time.unscaledTime:F3}", this);

            if (template.Trail != null)
                SpawnGhostParticle(e.Entity, template.Trail, start, rotation, end);

            if (template.Line != null)
                SpawnGhostLine(template.Line, start, rotation, end);

            // Independent of either trail's own tween so the impact plays exactly once however many
            // trails this prefab has.
            ParticleSystem impact = template.Impact;
            Tween.Delay(ghostFlightDuration, () => PlayImpact(impact, end), useUnscaledTime: true);
        }

        private void SpawnGhostParticle(EntityRef entity, ParticleSystem template, Vector3 start, Quaternion rotation, Vector3 end)
        {
            ParticleSystem trail = EffectsManager.Instance.GetHeldInstance(template);
            if (trail == null)
                return;

            StartCoroutine(WatchLifetime(entity, trail, Time.unscaledTime));

            // The pool activates the instance before handing it over, so a Play-On-Awake trail may
            // already have emitted at wherever it was last parked. Move first, then restart the whole
            // system from t=0 - that is the "completely resimulate it at the muzzle" step, and it
            // wipes anything emitted before the move.
            trail.transform.SetPositionAndRotation(start, rotation);
            trail.Simulate(0f, withChildren: true, restart: true);
            trail.Play(withChildren: true);

            // useUnscaledTime, same as ProjectileVisualController's impact tween: this is what
            // releases the held instance, and a client-local timeScale ramp (Level-Up screen) must
            // not be able to stall it.
            Tween.Position(trail.transform, end, ghostFlightDuration, Ease.Linear, useUnscaledTime: true)
                .OnComplete(() => StartCoroutine(ReleaseParticleAfterSimulation(template, trail)));
        }

        // One frame late on purpose. OnComplete runs during Update, BEFORE this frame's particle
        // simulation, and the projectile trails here emit by rate-over-distance: stopping emission
        // right there means the move that just landed the transform on the hit point is never
        // simulated with emission on. At high frame rates the earlier tween frames had already
        // emitted, so it went unnoticed; at 30fps (or any hitch) the tween completes on its very
        // first update, nothing was ever emitted, IsAlive is already false and the instance is
        // pooled without having drawn a single particle. Yielding once lets the system simulate the
        // final start->end delta first - Unity spreads rate-over-distance particles along it.
        private IEnumerator ReleaseParticleAfterSimulation(ParticleSystem template, ParticleSystem trail)
        {
            yield return null;

            LogHelper.Log("ProjFlow", $"GHOST release {trail.name}: particles={trail.particleCount} alive={trail.IsAlive(true)} t={Time.unscaledTime:F3}", this);

            // Same as ProjectileVisualController.FadeOutRootAsTrail: the root system is the bullet
            // body and must vanish at the impact; ReleaseHeldInstance then only lets the children
            // (sparks/glow) fade before the instance goes back to the pool.
            trail.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (EffectsManager.Instance != null)
                EffectsManager.Instance.ReleaseHeldInstance(template, trail);
        }

        private void SpawnGhostLine(TrailRenderer template, Vector3 start, Quaternion rotation, Vector3 end)
        {
            TrailRenderer line = AcquireLine(template);

            // Clear AFTER the move - a re-activated pooled ribbon still holds its last position and
            // would otherwise draw a segment from wherever it was parked straight to the muzzle. This
            // is the TrailRenderer counterpart of the particle's Simulate(0)/Play restart.
            line.transform.SetPositionAndRotation(start, rotation);
            line.Clear();
            line.emitting = true;

            Tween.Position(line.transform, end, ghostFlightDuration, Ease.Linear, useUnscaledTime: true)
                .OnComplete(() => StartCoroutine(RetireLine(line)));
        }

        // Emitting is switched off one frame late for the same reason as the particle release:
        // TrailRenderer adds its points in the render step, after the tween's OnComplete, so the
        // final position has to be alive for one frame to become a vertex. The ribbon then fades on
        // scaled time (that is what TrailRenderer ages its points with) and the instance goes back
        // to the pool once nothing of it is left to see.
        private IEnumerator RetireLine(TrailRenderer line)
        {
            yield return null;

            line.emitting = false;

            yield return new WaitForSeconds(line.time);

            if (line == null)
                yield break;

            line.Clear();
            line.gameObject.SetActive(false);
        }

        private TrailRenderer AcquireLine(TrailRenderer template)
        {
            if (_linePools.TryGetValue(template, out List<TrailRenderer> pool) == false)
            {
                pool = new List<TrailRenderer>();
                _linePools.Add(template, pool);
            }

            for (int i = pool.Count - 1; i >= 0; i--)
            {
                if (pool[i] == null)
                {
                    pool.RemoveAt(i);
                    continue;
                }

                if (pool[i].gameObject.activeSelf == false)
                {
                    pool[i].gameObject.SetActive(true);
                    return pool[i];
                }
            }

            TrailRenderer instance = Instantiate(template, transform);
            instance.gameObject.SetActive(true);
            pool.Add(instance);
            return instance;
        }

        private static void PlayImpact(ParticleSystem impact, Vector3 position)
        {
            if (impact == null || EffectsManager.Instance == null)
                return;

            // Same call as ProjectileVisualController.PlayImpactEffect - the authored scale has to be
            // passed explicitly or the pooled instance plays at 1.
            EffectsManager.Instance.PlayEffect(impact, position, Quaternion.identity, impact.transform.localScale);
        }

        // Diagnostic: reports when the pooled instance actually goes inactive (pool release) or is
        // destroyed, with the elapsed time since spawn.
        private IEnumerator WatchLifetime(EntityRef entity, ParticleSystem trail, float spawnTime)
        {
            while (trail != null && trail.gameObject.activeInHierarchy)
                yield return null;

            float elapsed = Time.unscaledTime - spawnTime;
            LogHelper.Log("ProjFlow", $"[{entity}] GHOST {(trail == null ? "instance DESTROYED" : "instance pooled (inactive)")} after {elapsed:F3}s", this);
        }

        // Same chain QuantumEntityViewUpdater itself walks to instantiate a view, just started from
        // the data asset instead of a live entity's View component: the projectile prefab has
        // QuantumEntityPrototype + QuantumEntityView on the same object, and baking appends a
        // ViewPrototype pointing at that self view to the prototype's component set.
        private Template ResolveTemplate(AssetRef<ProjectileDataAsset> dataRef)
        {
            if (_templates.TryGetValue(dataRef.Id, out Template cached))
                return cached;

            Template template = default;
            ProjectileView view = ResolvePrefabView(dataRef);

            if (view != null)
                template = new Template(view.TrailParticle, view.TrailRenderer, view.DestroyEffectPrefab);
            else
                LogHelper.Warn("ProjectileGhostTrail", $"No ProjectileView prefab resolvable for projectile data {dataRef.Id} - " +
                    "a shot of this type that dies before its view exists will draw nothing.", this);

            _templates[dataRef.Id] = template;
            return template;
        }

        private static ProjectileView ResolvePrefabView(AssetRef<ProjectileDataAsset> dataRef)
        {
            ProjectileDataAsset data = QuantumUnityDB.GetGlobalAsset(dataRef);
            if (data == null)
                return null;

            EntityPrototype prototype = QuantumUnityDB.GetGlobalAsset(data.Prototype);
            if (prototype == null || prototype.Container.Components == null)
                return null;

            foreach (ComponentPrototype component in prototype.Container.Components)
            {
                if (component is not Prototypes.ViewPrototype viewPrototype)
                    continue;

                EntityView entityView = QuantumUnityDB.GetGlobalAsset(viewPrototype.Current);

                // Prefab can legitimately be null under lazy view loading (QuantumEntityViewUpdater.
                // LoadMissingPrefab) - nothing to draw from in that case.
                return entityView != null && entityView.Prefab != null
                    ? entityView.Prefab.GetComponent<ProjectileView>()
                    : null;
            }

            return null;
        }
    }
}

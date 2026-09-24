using System.Collections.Generic;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Draws the shot for a projectile that never got an entity view.
    //
    // QuantumEntityViewUpdater only diffs views against the FINAL Predicted/Verified frame of each
    // Unity frame's tick batch, so a bullet fast enough to be created and destroyed inside that batch
    // (a point-blank hit at 20 ticks/s, a frame hitch, or a remote player's late-arriving shot that
    // lives and dies entirely inside a resimulation) never exists as far as the view layer is
    // concerned: ProjectileView.Initialize never runs, and the shot is simply invisible. The destroy
    // event still fires, though - QuantumGame dispatches every tick's events after the view pass -
    // and it carries everything needed to draw the shot after the fact: where it left from, where it
    // landed, how fast it was going, and which ProjectileDataAsset/WeaponDataAsset it was.
    //
    // For such a shot this instantiates a copy of the projectile prefab's own visual root - the exact
    // same object a live ProjectileView would have detached - and hands it to the same
    // ProjectileVisualController, starting at the muzzle and going straight into BeginImpact. So a
    // shot with no view gets the same mesh, trails, impact effect, per-weapon tint and graceful trail
    // fade as one that had a view. (This used to fly only a pooled copy of the trail particle for two
    // frames, which in practice emitted nothing at all - the bullet body is a single burst and the
    // trails emit by distance.) Shots that DID get a view are left entirely to their own
    // ProjectileVisualController - ProjectileVisualRegistry is what tells the two cases apart.
    //
    // Known, accepted: a PREDICTED destroy that later rolls back (the entity actually survives) shows
    // a ghost and then the real bullet - unavoidable without a verified-only event. A re-dispatch of
    // the same destroy (a mispredicted position) is filtered by the registry, so each shot gets one
    // visual and one impact at most.
    //
    // Out of scope by design: the sibling views that read a live entity (ProjectileElementalFxView's
    // elemental trail, ProjectileDataVisualsView's sprite/sparkTrail) and pierce/ricochet
    // EventProjectileImpacted points (no entity, no view). Lives on the EffectsManager GameObject in
    // QuantumGameScene.
    public class ProjectileGhostTrailManager : MonoBehaviour
    {
        [SerializeField, Tooltip("Minimum seconds the ghost visual takes from the muzzle to the hit point for a shot whose view never existed, on top of the prefab's own distance/speed clamp. Its rate-over-distance trails need a few rendered frames of movement to emit anything; ~0.06 is about four frames at 60fps.")]
        private float ghostFlightDuration = 0.06f;

        // Resolving ProjectileDataAsset -> EntityPrototype -> EntityView -> prefab -> ProjectileView
        // walks three asset lookups; cached per data asset so a fast weapon doesn't repeat it (or
        // re-warn) on every shot.
        private readonly Dictionary<AssetGuid, ProjectileView> _templates = new();

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

            ProjectileView template = ResolveTemplate(e.ProjectileData);
            Transform templateRoot = template != null ? template.VisualRootTemplate : null;

            Vector3 end = e.Position.ToUnityVector3();
            Vector3 simSpawn = e.SpawnPosition.ToUnityVector3();
            Vector3 start = ProjectileView.ResolveVisualSpawnPosition(e.Owner, simSpawn);

            WeaponDataAsset weaponData = e.WeaponData.IsValid ? QuantumUnityDB.GetGlobalAsset(e.WeaponData) : null;

            if (templateRoot == null)
            {
                // Nothing to fly - still land the impact so the hit reads.
                if (template != null)
                    PlayImpactOnly(template.DestroyEffectPrefab, end, weaponData);
                return;
            }

            Vector3 delta = end - start;
            Quaternion rotation = delta.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(delta.normalized, Vector3.up)
                : templateRoot.rotation;

            LogHelper.Log("ProjFlow", $"[{e.Entity}] GHOST spawn visual={templateRoot.name} origin={(start == simSpawn ? "SpawnPosition" : "muzzle")} start={start} end={end} dist={delta.magnitude:F2} speed={e.Speed.AsFloat:F1} t={Time.unscaledTime:F3}", this);

            GameObject instance = Instantiate(templateRoot.gameObject, start, rotation);
            Transform root = instance.transform;

            ParticleSystem weaponExtraParticle = ProjectileView.AttachWeaponExtraParticle(weaponData, root);

            // BuildSettings on the PREFAB ASSET points its trail references into the asset - remap
            // them onto the same objects inside this copy.
            ProjectileVisualController.Settings settings = template.BuildSettings(null, weaponExtraParticle);
            settings.TrailParticle = RemapInto(templateRoot, root, settings.TrailParticle);
            settings.TrailRenderer = RemapInto(templateRoot, root, settings.TrailRenderer);
            settings.MinFlightDuration = ghostFlightDuration;

            ProjectileVisualController visual = ProjectileVisualController.Detach(root, e.Entity, start, rotation, settings);
            ApplyWeaponImpactOverrides(visual, weaponData);
            visual.BeginImpact(end, e.Speed.AsFloat);
        }

        // Same per-weapon destroy-effect resolution ProjectileDataVisualsView registers on a live
        // view's controller - read straight off WeaponDataAsset.ProjectileVisuals here, since there
        // is no sibling view to do it. No weaponData (a skill or enemy attack) leaves every override
        // unset, which reproduces the prefab's own authored colors/scale.
        private static void ApplyWeaponImpactOverrides(ProjectileVisualController visual, WeaponDataAsset weaponData)
        {
            if (weaponData == null)
                return;

            ProjectileVisualsConfig visuals = weaponData.ProjectileVisuals;

            Color root = visuals.ProjectileDestroyColor;
            root.a = 1f;
            visual.SetDestroyEffectColorOverride(root);

            Color child = visuals.ProjectileDestroyGlowColor;
            child.a = 1f;
            visual.SetDestroyEffectChildColorOverride(child);

            visual.SetDestroyEffectScaleOverride(visuals.ProjectileDestroyScale);
        }

        private static void PlayImpactOnly(ParticleSystem impact, Vector3 position, WeaponDataAsset weaponData)
        {
            if (weaponData == null)
            {
                ProjectileVisualController.PlayDestroyEffect(impact, position, null, null, null);
                return;
            }

            ProjectileVisualsConfig visuals = weaponData.ProjectileVisuals;
            Color root = visuals.ProjectileDestroyColor;
            root.a = 1f;
            Color child = visuals.ProjectileDestroyGlowColor;
            child.a = 1f;

            ProjectileVisualController.PlayDestroyEffect(impact, position, root, child, visuals.ProjectileDestroyScale);
        }

        // Finds the counterpart of a component under templateRoot inside a fresh Instantiate of it, by
        // walking the same child-index path. Null if the component doesn't live under templateRoot at
        // all - it then simply isn't part of the ghost's visual.
        private static T RemapInto<T>(Transform templateRoot, Transform instanceRoot, T component) where T : Component
        {
            if (component == null)
                return null;

            var path = new List<int>();
            Transform current = component.transform;
            while (current != templateRoot)
            {
                if (current == null)
                    return null;

                path.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            Transform target = instanceRoot;
            for (int i = path.Count - 1; i >= 0; i--)
                target = target.GetChild(path[i]);

            return target.GetComponent<T>();
        }

        // A null view is NOT cached - ResolvePrefabView can legitimately return null from a transient
        // lazy-load race (QuantumEntityViewUpdater.LoadMissingPrefab, entityView.Prefab still null the
        // first time this asset resolves), and caching that miss would make every later ghost of the
        // same ProjectileDataAsset draw nothing too, even once the prefab is loaded.
        private ProjectileView ResolveTemplate(AssetRef<ProjectileDataAsset> dataRef)
        {
            if (_templates.TryGetValue(dataRef.Id, out ProjectileView cached))
                return cached;

            ProjectileView view = ResolvePrefabView(dataRef);
            if (view == null)
            {
                LogHelper.Warn("ProjectileGhostTrail", $"No ProjectileView prefab resolvable for projectile data {dataRef.Id} - " +
                    "a shot of this type that dies before its view exists will draw nothing this time. Not cached, will retry next time.", this);
                return null;
            }

            _templates[dataRef.Id] = view;
            return view;
        }

        // Same chain QuantumEntityViewUpdater itself walks to instantiate a view, just started from
        // the data asset instead of a live entity's View component: the projectile prefab has
        // QuantumEntityPrototype + QuantumEntityView on the same object, and baking appends a
        // ViewPrototype pointing at that self view to the prototype's component set.
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

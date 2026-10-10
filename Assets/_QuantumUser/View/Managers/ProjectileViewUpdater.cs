using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // QuantumEntityViewUpdater that keeps finished PROJECTILE views and hands them back out instead
    // of destroying and re-instantiating one per shot. Every other entity's view goes through the
    // stock path untouched.
    //
    // Why not the SDK's QuantumEntityViewPool: that one is a single pool for every view the updater
    // creates (enemies, pickups, heroes, ...), none of which were written to survive reuse, and it
    // can't see the position a view is created at, so non-pooled prefabs would be born at the origin
    // and then moved. Overriding the two creation hooks here lets only the views that opted in
    // (anything carrying a ProjectileView) take the pooled path, with the exact position/rotation the
    // stock path would have used.
    //
    // A pooled view is not returned at once when its entity dies: ProjectileView holds it until the
    // detached visual and any fading trail pieces have come home - see its class comment. This class
    // only knows "give me a view" / "I'm done with this view" / "the view says it can be reused".
    //
    // Do NOT declare Awake/Start/OnDestroy/Update/LateUpdate here: the base class has its own, and
    // Unity only calls the most derived one of a given name, which would silently stop the base
    // updater from running. Anything needing setup is created lazily instead.
    public class ProjectileViewUpdater : QuantumEntityViewUpdater
    {
        [SerializeField, Tooltip("Reuse finished projectile views instead of instantiating one per shot. Turn off to fall back to the stock create/destroy path if a pooled projectile ever looks wrong (stale tint, missing trail, wrong sprite).")]
        private bool poolProjectileViews = true;

        [SerializeField, Min(0), Tooltip("Idle views kept per projectile prefab. Anything returned beyond this is destroyed. A 15-projectile burst needs roughly that many alive at once; the pool grows to the peak and stays there.")]
        private int maxPooledPerPrefab = 64;

        private readonly Dictionary<GameObject, bool> _isProjectilePrefab = new Dictionary<GameObject, bool>();
        private readonly Dictionary<GameObject, Stack<QuantumEntityView>> _free = new Dictionary<GameObject, Stack<QuantumEntityView>>();
        private Transform _poolRoot;

        // Diagnostics only - read through the Log Pool Stats button. "Created" is the expensive path
        // (a real Instantiate); with pooling working "Reused" should dwarf it after the first bursts.
        private int _createdCount;
        private int _reusedCount;
        private int _droppedCount;

        [Button("Log Pool Stats")]
        private void LogPoolStats()
        {
            int free = 0;
            foreach (KeyValuePair<GameObject, Stack<QuantumEntityView>> pair in _free)
            {
                free += pair.Value.Count;
                LogHelper.Log("ProjectilePool", $"{pair.Key.name}: {pair.Value.Count} idle");
            }

            LogHelper.Log("ProjectilePool", $"pooling={(poolProjectileViews ? "ON" : "OFF")} created={_createdCount} reused={_reusedCount} " +
                $"dropped(over cap)={_droppedCount} idleTotal={free}");
        }

        protected override QuantumEntityView CreateEntityViewInstance(EntityView asset, Vector3? position = null, Quaternion? rotation = null)
        {
            GameObject prefab = asset.Prefab;

            if (poolProjectileViews == false || prefab == null || IsProjectilePrefab(prefab) == false)
                return base.CreateEntityViewInstance(asset, position, rotation);

            if (TryTakeFree(prefab, out QuantumEntityView reused))
            {
                // The base updater renames every view to "<prefab> <entity>" after this returns,
                // stripping only "(Clone)": a reused name would grow by one suffix per reuse.
                reused.gameObject.name = prefab.name;

                Transform t = reused.transform;

                // Same place the stock path would have put it: the configured view parent, or the
                // scene root (SDK's Instantiate has no parent).
                t.SetParent(ViewParentTransform, worldPositionStays: false);

                if (position.HasValue)
                    t.position = position.Value;
                if (rotation.HasValue)
                    t.rotation = rotation.Value;

                reused.gameObject.SetActive(true);
                _reusedCount++;
                return reused;
            }

            QuantumEntityView prefabView = prefab.GetComponent<QuantumEntityView>();
            QuantumEntityView created = position.HasValue && rotation.HasValue
                ? Instantiate(prefabView, position.Value, rotation.Value)
                : Instantiate(prefabView);

            _createdCount++;
            created.GetComponent<ProjectileView>().EnablePooling(released => ReturnToPool(prefab, released));
            return created;
        }

        protected override void DestroyEntityViewInstance(QuantumEntityView instance)
        {
            if (instance != null && instance.TryGetComponent(out ProjectileView projectileView) && projectileView.IsPooled)
            {
                projectileView.RequestRelease();
                return;
            }

            base.DestroyEntityViewInstance(instance);
        }

        private bool IsProjectilePrefab(GameObject prefab)
        {
            if (_isProjectilePrefab.TryGetValue(prefab, out bool isProjectile) == false)
            {
                isProjectile = prefab.GetComponent<ProjectileView>() != null;
                _isProjectilePrefab[prefab] = isProjectile;
            }

            return isProjectile;
        }

        private bool TryTakeFree(GameObject prefab, out QuantumEntityView view)
        {
            if (_free.TryGetValue(prefab, out Stack<QuantumEntityView> stack))
            {
                while (stack.Count > 0)
                {
                    view = stack.Pop();

                    // Destroyed while idle (scene teardown) - skip it.
                    if (view != null)
                        return true;
                }
            }

            view = null;
            return false;
        }

        // ProjectileView says it is fully back together and already inactive.
        private void ReturnToPool(GameObject prefab, ProjectileView projectileView)
        {
            if (projectileView == null)
                return;

            QuantumEntityView view = projectileView.GetComponent<QuantumEntityView>();

            // This updater went away while the view was still finishing its release.
            if (this == null)
            {
                Destroy(view.gameObject);
                return;
            }

            if (_free.TryGetValue(prefab, out Stack<QuantumEntityView> stack) == false)
            {
                stack = new Stack<QuantumEntityView>();
                _free[prefab] = stack;
            }

            if (stack.Count >= maxPooledPerPrefab)
            {
                _droppedCount++;
                Destroy(view.gameObject);
                return;
            }

            view.transform.SetParent(GetPoolRoot(), worldPositionStays: false);
            stack.Push(view);
        }

        private Transform GetPoolRoot()
        {
            if (_poolRoot == null)
            {
                _poolRoot = new GameObject("ProjectileViewPool").transform;
                _poolRoot.SetParent(transform, worldPositionStays: false);
            }

            return _poolRoot;
        }
    }
}

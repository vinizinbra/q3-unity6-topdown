namespace QuantumUser.View.Managers
{
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Pool;

    // Pools arbitrary GameObject prefabs keyed by prefab reference, for views that instantiate a
    // sub-prefab dynamically per entity spawn (e.g. EnemyView instantiating EnemyDataAsset.
    // ViewPrefab as a child) instead of paying Instantiate/Destroy on every spawn/despawn. Unlike
    // EffectsManager, needs no Inspector configuration (no prewarm lists, nothing per-prefab to
    // tune), so it lazily creates its own host GameObject on first use instead of requiring manual
    // scene placement - Unity's fake-null equality on a destroyed UnityEngine.Object means the
    // Instance getter self-heals correctly across scene reloads without any extra bookkeeping.
    //
    // Known limitation: pooled instances aren't refreshed if you edit a ViewPrefab mid-Play-mode -
    // same caveat EffectsManager's disablePooling flag exists to work around, just without an
    // equivalent toggle here since there's no scene-placed component to hang it off.
    public class ViewPrefabPool : MonoBehaviour
    {
        private static ViewPrefabPool _instance;

        public static ViewPrefabPool Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new GameObject(nameof(ViewPrefabPool)).AddComponent<ViewPrefabPool>();

                return _instance;
            }
        }

        private readonly Dictionary<GameObject, ObjectPool<GameObject>> pools = new Dictionary<GameObject, ObjectPool<GameObject>>();

        public GameObject Get(GameObject prefab, Transform parent)
        {
            if (prefab == null) return null;

            GameObject instance = GetOrCreatePool(prefab).Get();
            instance.transform.SetParent(parent, false);
            return instance;
        }

        // prefab must be the same reference passed to Get(), since pools are keyed by prefab
        // reference.
        public void Release(GameObject prefab, GameObject instance)
        {
            if (prefab == null || instance == null) return;

            GetOrCreatePool(prefab).Release(instance);
        }

        private ObjectPool<GameObject> GetOrCreatePool(GameObject prefab)
        {
            if (pools.TryGetValue(prefab, out var pool))
                return pool;

            pool = new ObjectPool<GameObject>(
                createFunc: () => Instantiate(prefab),
                actionOnGet: instance => instance.SetActive(true),
                actionOnRelease: instance =>
                {
                    instance.SetActive(false);
                    instance.transform.SetParent(transform, false);
                },
                actionOnDestroy: Destroy);

            pools.Add(prefab, pool);
            return pool;
        }
    }
}

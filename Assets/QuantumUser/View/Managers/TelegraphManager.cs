namespace QuantumUser.View.Managers
{
    using System;
    using System.Collections.Generic;
    using Quantum;
    using UnityEngine;
    using UnityEngine.Pool;

    // Per-shape fallback prefab - a TelegraphData asset that leaves TelegraphPrefab unset uses
    // whatever's configured here for its Shape instead, so authoring a new TelegraphData for a
    // specific attack doesn't require re-assigning the same prefab every time. Set once per shape
    // here; only set TelegraphPrefab explicitly on a TelegraphData when that specific attack needs
    // something different from the shape's usual look (e.g. a boss's unique warning color).
    [Serializable]
    public class TelegraphShapeDefault
    {
        public TelegraphShape Shape;
        public GameObject TelegraphPrefab;
    }

    // Pools ground-telegraph instances (Quantum.EnemyAttackVisualsView) keyed by prefab
    // reference - same shape as EffectsManager, avoids Instantiate/Destroy on every attack
    // windup, which can be frequent (every enemy's Anticipation phase spawns one). Unlike
    // EffectsManager's "play once, auto-release when finished" particles, a telegraph has an
    // explicit owner that calls Get/Release itself as attacks start/end (Quantum.TelegraphFade
    // calls Release once its fade-out finishes).
    public class TelegraphManager : MonoBehaviour
    {
        public static TelegraphManager Instance;

        [SerializeField, Tooltip("Pools pre-warmed on Awake so the first telegraph of a given prefab during combat doesn't pay an Instantiate cost.")]
        private List<GameObject> prewarmPrefabs = new List<GameObject>();
        [SerializeField, Tooltip("Instances created up front per prewarmed prefab.")]
        private int prewarmCountPerPrefab = 4;

        [SerializeField, Tooltip("Per-shape fallback prefab used whenever a TelegraphData asset leaves its own TelegraphPrefab unset.")]
        private List<TelegraphShapeDefault> shapeDefaults = new List<TelegraphShapeDefault>();

        private readonly Dictionary<GameObject, ObjectPool<GameObject>> pools = new Dictionary<GameObject, ObjectPool<GameObject>>();

        private void Awake()
        {
            Instance = this;

            foreach (var prefab in prewarmPrefabs)
                Prewarm(prefab, prewarmCountPerPrefab);
        }

        public bool TryGetDefaultPrefab(TelegraphShape shape, out GameObject prefab)
        {
            for (int i = 0; i < shapeDefaults.Count; i++)
            {
                if (shapeDefaults[i].Shape == shape)
                {
                    prefab = shapeDefaults[i].TelegraphPrefab;
                    return prefab != null;
                }
            }

            prefab = null;
            return false;
        }

        public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null)
                return null;

            var instance = GetOrCreatePool(prefab).Get();
            instance.transform.SetPositionAndRotation(position, rotation);
            return instance;
        }

        public void Release(GameObject prefab, GameObject instance)
        {
            if (prefab == null || instance == null)
                return;

            GetOrCreatePool(prefab).Release(instance);
        }

        private void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null) return;

            var pool = GetOrCreatePool(prefab);
            var buffer = new GameObject[count];
            for (int i = 0; i < count; i++)
                buffer[i] = pool.Get();
            for (int i = 0; i < count; i++)
                pool.Release(buffer[i]);
        }

        private ObjectPool<GameObject> GetOrCreatePool(GameObject prefab)
        {
            if (pools.TryGetValue(prefab, out var pool))
                return pool;

            pool = new ObjectPool<GameObject>(
                createFunc: () => Instantiate(prefab, transform),
                actionOnGet: instance => instance.SetActive(true),
                actionOnRelease: instance => instance.SetActive(false),
                actionOnDestroy: instance => Destroy(instance));

            pools.Add(prefab, pool);
            return pool;
        }
    }
}

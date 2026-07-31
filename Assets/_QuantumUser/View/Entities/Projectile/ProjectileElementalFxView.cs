using PrimeTween;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Generic per-element trailing particle for any non-Neutral projectile (Fire/Ice/Rock/Void -
    // see ElementType.qtn and Projectile.Element). Sibling of ProjectileView, not merged into it -
    // same one-MonoBehaviour-per-visual-concern split EnemyAllyLinkView uses.
    //
    // The held particle instance lives under EffectsManager's own hierarchy (see
    // EffectsManager.GetHeldInstance), not parented to this projectile's GameObject, specifically so
    // it survives ProjectileView.PlayDestroyEffectAndDestroy's Destroy(gameObject) - which fires
    // shortly (<=0.2s) after impact, well before this effect's own resting grace period is up. The
    // catch-up tween and the delayed release below are both targeted on the particle instance itself
    // (not on this component/transform) for the same reason: PrimeTween auto-kills a tween when its
    // target is destroyed, and this component dies together with the projectile.
    public class ProjectileElementalFxView : CustomQuantumEntityViewComponent
    {
        [Header("Per-element particle (Neutral = none)")]
        [SerializeField, Tooltip("Looping prefab pulled from EffectsManager's pool - lifetime is owned by this component via GetHeldInstance/ReleaseHeldInstance, not EffectsManager.PlayEffect's fire-and-forget shape. Leave a slot empty to skip that element entirely.")]
        private ParticleSystem fireParticlePrefab;
        [SerializeField]
        private ParticleSystem iceParticlePrefab;
        [SerializeField]
        private ParticleSystem rockParticlePrefab;
        [SerializeField]
        private ParticleSystem voidParticlePrefab;

        [Header("Impact")]
        [SerializeField, Tooltip("How long the particle takes to catch up from wherever it was following to the resolved hit point once the projectile is destroyed. Independent of, and not synced with, ProjectileView's own sprite catch-up tween.")]
        private float catchUpDuration = 0.1f;
        [SerializeField, Tooltip("How long the particle keeps playing at rest on the resolved hit point before being released back to the pool.")]
        private float restGracePeriod = 0.75f;

        // Own copy, independent of the base class's _entityRef - same DeInitialize-ordering race
        // ProjectileView documents on its own _ownEntityRef.
        private EntityRef _ownEntityRef;
        private ParticleSystem _prefab;
        private ParticleSystem _instance;
        private bool _resolved;
        private bool _following;

        public override void Awake()
        {
            base.Awake();
            QuantumEvent.Subscribe<EventProjectileDestroyed>(this, OnProjectileDestroyed);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);

            // Safety net for a teardown that never routes through OnProjectileDestroyed (e.g. a
            // scene unload while the projectile is still in flight) - without this the held instance
            // would never return to EffectsManager's pool. A no-op on the normal path, since
            // OnProjectileDestroyed already clears _instance before this component is destroyed.
            if (_instance != null && EffectsManager.Instance != null)
                EffectsManager.Instance.ReleaseHeldInstance(_prefab, _instance);
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);
            _ownEntityRef = _entityRef;
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (_following)
                _instance.transform.position = transform.position;

            if (_resolved)
                return;

            var frame = game.Frames.Predicted;
            if (frame.Has<Projectile>(_entityRef) == false)
                return;

            // Element is captured once at fire time and never changes for the life of the
            // projectile (see Projectile.qtn), so this only ever needs to resolve once.
            _resolved = true;

            ElementType element = frame.Get<Projectile>(_entityRef).Element;
            _prefab = ResolveParticlePrefab(element);

            if (_prefab == null || EffectsManager.Instance == null)
                return;

            _instance = EffectsManager.Instance.GetHeldInstance(_prefab);
            if (_instance == null)
                return;

            _instance.transform.SetPositionAndRotation(transform.position, Quaternion.identity);
            _instance.Play();
            _following = true;
        }

        private void OnProjectileDestroyed(EventProjectileDestroyed e)
        {
            if (e.Entity != _ownEntityRef || _instance == null)
                return;

            _following = false;

            ParticleSystem instance = _instance;
            ParticleSystem prefab = _prefab;
            _instance = null;
            _prefab = null;

            Tween.Position(instance.transform, e.Position.ToUnityVector3(), catchUpDuration, Ease.Linear)
                .OnComplete(() => Tween.Delay(instance.gameObject, restGracePeriod, () =>
                {
                    if (EffectsManager.Instance != null)
                        EffectsManager.Instance.ReleaseHeldInstance(prefab, instance);
                }));
        }

        private ParticleSystem ResolveParticlePrefab(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return fireParticlePrefab;
                case ElementType.Ice: return iceParticlePrefab;
                case ElementType.Rock: return rockParticlePrefab;
                case ElementType.Void: return voidParticlePrefab;
                default: return null; // Neutral - no elemental particle
            }
        }
    }
}

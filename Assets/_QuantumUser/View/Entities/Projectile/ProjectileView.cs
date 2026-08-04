using PrimeTween;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Rotates the projectile's transform to face its current travel direction in world space -
    // same convention ProjectileSystem.Advance uses for Transform3D.Rotation (LookRotation
    // toward Velocity, world up), so the view matches the mesh's actual 3D orientation instead
    // of flattening it toward the camera.
    //
    // Also owns the projectile's death: a fast projectile can hit on its very first simulated
    // tick, so f.Destroy(entity) can land before this view was ever rendered even once - the
    // GameObject would be created and torn down between the same two Unity frames, invisible.
    // ManualDisposal (set below) tells QuantumEntityViewUpdater not to destroy this GameObject
    // itself; instead this tweens to the real resolved hit point (EventProjectileDestroyed.Position,
    // which ProjectileSystem never actually writes back into Transform3D on the hit path) before
    // playing the impact effect and destroying itself. This is also why the impact effect used to
    // live on WeaponView/EnemyAttackVisualsView instead of here - a listener on the projectile's
    // own view used to lose the race against its own teardown and could miss the event entirely.
    public class ProjectileView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Hidden while the projectile is still counting down ProjectileDataAsset.SpawnDelay. Leave empty if this projectile has no delay/windup.")]
        private Renderer visualRenderer;

        [Header("Death")]
        [SerializeField, Tooltip("Pooled particle prefab played (via EffectsManager) once this projectile reaches its resolved hit position - hit or expired. Leave empty for no effect.")]
        private ParticleSystem destroyEffectPrefab;
        [SerializeField, Tooltip("Trail particle child that should keep playing/fading out after this projectile is destroyed, instead of being cut off mid-emission by Destroy(gameObject). Must be a separate child object, not on this same GameObject. Leave empty if this projectile has no trail.")]
        private ParticleSystem trailParticle;
        [SerializeField, Tooltip("Clamped bounds on how long the catch-up-to-hit-point tween can take, regardless of the projectile's actual last known speed - guards against a near-zero speed (e.g. an already-grounded/settled projectile) producing a near-infinite tween.")]
        private float minCatchUpDuration = 0.03f;
        [SerializeField]
        private float maxCatchUpDuration = 0.2f;

        // Own copy of the entity ref, independent of the base class's _entityRef - DeInitialize
        // (fired by entityView.OnEntityDestroyed) clears that one, and its ordering relative to the
        // EventProjectileDestroyed subscription below isn't guaranteed, so filtering the event on
        // _entityRef could race against DeInitialize clearing it out from under us.
        private EntityRef _ownEntityRef;
        private float _lastSpeed;

        public override void Awake()
        {
            base.Awake();

            if (entityView == null)
            {
                LogHelper.Error("ProjectileView", $"{name}: no QuantumEntityView found on itself or its parents, so ManualDisposal can't be set. " +
                    "This projectile will be destroyed the instant it dies in simulation instead of tweening to its resolved hit point first - " +
                    "on a fast projectile that can mean it's created and destroyed before ever rendering a single frame. Fix the prefab hierarchy.", this);
            }
            else
            {
                entityView.ManualDisposal = true;
            }

            QuantumEvent.Subscribe<EventProjectileDestroyed>(this, OnProjectileDestroyed);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);
            _ownEntityRef = _entityRef;
        }

        protected override void QUpdate(QuantumGame game)
        {
            var frame = game.Frames.Predicted;

            if (frame.Has<Projectile>(_entityRef) == false)
            {
                // A planted AreaHitData bomb (see ProjectileSystem.TryPlant/AreaHitData.
                // PlantedFuseTime) swaps off Projectile onto DestroyAfterTime while staying alive -
                // EventProjectileDestroyed will never fire for it afterward (DestroyAfterTimeSystem
                // knows nothing about Projectile), so the ManualDisposal tween-to-hit-point path this
                // class exists for would otherwise leak this GameObject forever once it's eventually
                // destroyed. Hand cleanup back to QuantumEntityViewUpdater's normal auto-destroy,
                // which is all a stationary, already-rendered bomb needs - frame.Exists guards this
                // against the entity's own genuine destruction (handled by OnProjectileDestroyed
                // below, unaffected by this flag either way) rather than a live transition.
                if (entityView != null && frame.Exists(_entityRef) == true)
                    entityView.ManualDisposal = false;

                return;
            }

            Projectile projectile = frame.Get<Projectile>(_entityRef);
            _lastSpeed = projectile.Velocity.Magnitude.AsFloat;

            if (visualRenderer != null)
                visualRenderer.enabled = projectile.RemainingSpawnDelay <= 0;

            Vector3 velocity = projectile.Velocity.ToUnityVector3();
            if (velocity.sqrMagnitude < 0.0001f)
                return;

            transform.rotation = Quaternion.LookRotation(velocity, Vector3.up);
        }

        private void OnProjectileDestroyed(EventProjectileDestroyed e)
        {
            if (e.Entity != _ownEntityRef)
                return;

            if (visualRenderer != null)
                visualRenderer.enabled = true;

            Vector3 hitPoint = e.Position.ToUnityVector3();
            float distance = Vector3.Distance(transform.position, hitPoint);
            float duration = _lastSpeed > 0.0001f
                ? Mathf.Clamp(distance / _lastSpeed, minCatchUpDuration, maxCatchUpDuration)
                : minCatchUpDuration;

            Tween.Position(transform, hitPoint, duration, Ease.Linear)
                .OnComplete(() => PlayDestroyEffectAndDestroy(hitPoint));
        }

        private void PlayDestroyEffectAndDestroy(Vector3 hitPoint)
        {
            if (this == null)
                return;

            if (destroyEffectPrefab != null && EffectsManager.Instance != null)
                EffectsManager.Instance.PlayEffect(destroyEffectPrefab, hitPoint, Quaternion.identity);

            if (trailParticle != null)
                trailParticle.gameObject.AddComponent<ParticleGracefulStop>().StopAndDestroyWhenFinished();

            Destroy(gameObject);
        }
    }
}

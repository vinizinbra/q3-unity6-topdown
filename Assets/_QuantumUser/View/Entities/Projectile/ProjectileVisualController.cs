using PrimeTween;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Owns a projectile's DETACHED visual root for that visual's entire life - flight catch-up,
    // impact, and cleanup - independently of the entity view that spawned it. See ProjectileView
    // for why the visual is detached from the entity's GameObject in the first place.
    //
    // Added at runtime (AddComponent, never authored on a prefab - same idiom as
    // ParticleGracefulStop), because once detached this is a plain GameObject: nothing about it is
    // a Quantum view anymore, and that is exactly the point. QuantumEntityViewUpdater destroys the
    // entity's own GameObject the moment the simulation destroys the entity, with no ManualDisposal
    // involved; this outlives it by however long the last few frames of the shot need - the fast
    // catch-up onto the resolved hit point, the impact effect, the trail fading out.
    //
    // Three independent ways this ends, in order of preference:
    //   1. EventProjectileDestroyed - the real one. Lerps to the resolved hit point, plays the
    //      impact effect, lets the trail fade, destroys itself.
    //   2. ProjectileView.DeInitialize -> NotifyEntityGone: the entity view was torn down without
    //      that event ever arriving (a disconnect walks every view directly; a reconnect resync
    //      makes the entity simply absent from the snapshot AND cancels pending events). Waits one
    //      frame for case 1 to still land - it is dispatched slightly later in the same Unity frame,
    //      see ProjectileView - then vanishes with no impact effect, because nothing was hit.
    //   3. orphanTimeout - nothing pushed a target for that many seconds. Unreachable in theory,
    //      since 2 covers every teardown the updater knows about; kept because the alternative to a
    //      few wasted seconds is a bullet frozen in mid-air for the rest of the match.
    public class ProjectileVisualController : MonoBehaviour
    {
        public struct Settings
        {
            // Multiplier on the projectile's own live speed, used as the catch-up rate while the
            // visual is still behind the simulated entity. 1 would never converge (it moves exactly
            // as fast as the thing it is chasing); 2 pays back a 4-unit deficit on a 40 u/s bolt in
            // 0.1s.
            public float CatchUpSpeedMultiplier;
            public float MinImpactDuration;
            public float MaxImpactDuration;
            public float OrphanTimeout;
            public ParticleSystem DestroyEffectPrefab;
            public ParticleSystem TrailParticle;
        }

        // Floor on the catch-up rate, in world units per second. A projectile whose own speed has
        // dropped to nothing - a thrown bomb resting on the ground, a windup sitting out its
        // SpawnDelay - would otherwise never close a gap it still had open, since the rate is
        // derived from that speed.
        private const float MinimumCatchUpSpeed = 2f;

        private Settings _settings;
        private EntityRef _entity;

        private Renderer[] _renderers;
        private ParticleSystem[] _particles;
        private TrailRenderer[] _trails;

        private Vector3 _targetPosition;
        private float _speed;
        private float _lastPushTime;
        private bool _visible = true;

        private bool _impacting;
        private bool _entityGone;
        private int _entityGoneFrame;

        // visualRoot is unparented here rather than by the caller so that everything about the
        // hand-off happens in one place: detach, land on the muzzle, wipe whatever the teleport
        // would otherwise have streaked across the screen, start listening for the death event.
        public static ProjectileVisualController Detach(Transform visualRoot, EntityRef entity,
            Vector3 spawnPosition, Quaternion spawnRotation, in Settings settings)
        {
            visualRoot.SetParent(null, worldPositionStays: true);

            var controller = visualRoot.gameObject.AddComponent<ProjectileVisualController>();
            controller.Initialize(entity, spawnPosition, spawnRotation, settings);
            return controller;
        }

        private void Initialize(EntityRef entity, Vector3 spawnPosition, Quaternion spawnRotation,
            in Settings settings)
        {
            _entity = entity;
            _settings = settings;

            _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            _particles = GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            _trails = GetComponentsInChildren<TrailRenderer>(includeInactive: true);

            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            _targetPosition = spawnPosition;
            _lastPushTime = Time.unscaledTime;

            // AFTER the reposition, never before: this GameObject was instantiated wherever the
            // entity already was (potentially meters downrange, see ProjectileView) and only then
            // moved back to the muzzle, so any trail/particle carries a streak across that whole
            // teleport unless it is wiped once it is standing in the right place.
            ClearEmitters();

            QuantumEvent.Subscribe<EventProjectileDestroyed>(this, OnProjectileDestroyed);
        }

        private void OnDestroy()
        {
            QuantumEvent.UnsubscribeListener(this);
        }

        // Called every view update by ProjectileView while the entity still exists. Position and
        // rotation come from the entity's own GameObject, so they are already interpolated by
        // QuantumEntityView - this only decides how fast to close the remaining gap.
        public void Push(Vector3 position, Quaternion rotation, float speed, bool visible)
        {
            _targetPosition = position;
            _speed = speed;
            _lastPushTime = Time.unscaledTime;

            SetVisible(visible);

            if (_impacting == false)
                transform.rotation = rotation;
        }

        // The entity view is being torn down. Case 2 above - see this class's own doc comment.
        public void NotifyEntityGone()
        {
            _entityGone = true;
            _entityGoneFrame = Time.frameCount;
        }

        private void Update()
        {
            if (_impacting == true)
                return; // the impact tween owns this transform now

            if (_entityGone == true && Time.frameCount > _entityGoneFrame)
            {
                Finish(playEffect: false);
                return;
            }

            if (Time.unscaledTime - _lastPushTime > _settings.OrphanTimeout)
            {
                LogHelper.Warn("ProjectileVisual", $"{name}: nothing has pushed a target for " +
                    $"{_settings.OrphanTimeout}s and no destroy event arrived - cleaning up a visual that would " +
                    "otherwise hang in the air. Worth investigating if this shows up often.", this);
                Finish(playEffect: false);
                return;
            }

            // Scaled time deliberately: this is the world moving, not UI - a client-local Level-Up
            // screen ramping Time.timeScale toward 0 should slow a bullet in flight exactly like it
            // slows everything else. The impact tween below is the opposite case and opts out.
            float step = Mathf.Max(_speed * _settings.CatchUpSpeedMultiplier, MinimumCatchUpSpeed) * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, _targetPosition, step);
        }

        private void OnProjectileDestroyed(EventProjectileDestroyed e)
        {
            if (e.Entity != _entity || _impacting == true)
                return;

            _impacting = true;
            SetVisible(true);

            Vector3 hitPoint = e.Position.ToUnityVector3();
            float distance = Vector3.Distance(transform.position, hitPoint);

            // Covers the whole remaining gap - the visual is behind by however much of the catch-up
            // it had not paid back yet, plus whatever this last tick's real movement was, and the
            // hit point is a resolved position ProjectileSystem never writes back into Transform3D.
            // Clamped either way: a settled/near-stationary projectile would otherwise divide by a
            // near-zero speed and take practically forever.
            float duration = _speed > 0.0001f
                ? Mathf.Clamp(distance / _speed, _settings.MinImpactDuration, _settings.MaxImpactDuration)
                : _settings.MinImpactDuration;

            if (distance > 0.0001f)
                transform.rotation = Quaternion.LookRotation((hitPoint - transform.position).normalized, Vector3.up);

            // useUnscaledTime, unlike the flight catch-up above - this tween is the only thing that
            // will ever destroy this GameObject, so a scaled-time tween starting right as a
            // client-local choice screen ramps timeScale to 0 would stall for as long as that screen
            // stays open. The orphan timeout would eventually mop it up, but a bullet hanging in the
            // air for 3s is exactly what this whole class exists to prevent.
            Tween.Position(transform, hitPoint, duration, Ease.Linear, useUnscaledTime: true)
                .OnComplete(() => Finish(playEffect: true));
        }

        private void Finish(bool playEffect)
        {
            if (this == null)
                return;

            if (playEffect == true && _settings.DestroyEffectPrefab != null && EffectsManager.Instance != null)
                EffectsManager.Instance.PlayEffect(_settings.DestroyEffectPrefab, transform.position, Quaternion.identity);

            // Only on a real impact: unparents itself and finishes emitting where the shot landed.
            // A teardown/orphan cleanup deliberately leaves nothing behind - there was no impact to
            // linger over, and on a disconnect the whole scene is on its way out anyway.
            if (playEffect == true && _settings.TrailParticle != null)
                _settings.TrailParticle.gameObject.AddComponent<ParticleGracefulStop>().StopAndDestroyWhenFinished();

            Destroy(gameObject);
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible)
                return;

            _visible = visible;

            // Renderers rather than SetActive on the root: this component's own Update has to keep
            // running while a projectile sits out its ProjectileDataAsset.SpawnDelay, and an
            // inactive GameObject would stop the catch-up, the orphan timeout and the impact tween
            // along with the visuals.
            foreach (Renderer r in _renderers)
                r.enabled = visible;

            foreach (ParticleSystem ps in _particles)
            {
                if (visible == true)
                    ps.Play(withChildren: false);
                else
                    ps.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            foreach (TrailRenderer tr in _trails)
            {
                tr.Clear();
                tr.emitting = visible;
            }
        }

        private void ClearEmitters()
        {
            foreach (ParticleSystem ps in _particles)
                ps.Clear(withChildren: false);

            foreach (TrailRenderer tr in _trails)
                tr.Clear();
        }
    }
}

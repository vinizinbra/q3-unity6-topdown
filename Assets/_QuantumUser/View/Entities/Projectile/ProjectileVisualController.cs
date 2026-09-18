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
            public TrailRenderer TrailRenderer;

            // Runtime-instantiated Damage Echo "ghost" particle (see ProjectileView.
            // AttachEchoGhostParticle) - null for every normal projectile. Treated identically to
            // TrailParticle on a real impact: already-emitted particles keep fading via
            // ParticleGracefulStop instead of being cut off mid-emission by Finish's own
            // Destroy(gameObject).
            public ParticleSystem EchoGhostParticle;

            // Runtime-instantiated per-weapon extra particle (WeaponDataAsset.ProjectileVisuals.
            // ProjectileExtraParticle, see ProjectileView.AttachWeaponExtraParticle) - null unless
            // that weapon has one configured. Same graceful-fade-on-impact treatment as
            // EchoGhostParticle/TrailParticle, not cut off mid-emission.
            public ParticleSystem WeaponExtraParticle;
        }

        // Floor on the catch-up rate, in world units per second. A projectile whose own speed has
        // dropped to nothing - a thrown bomb resting on the ground, a windup sitting out its
        // SpawnDelay - would otherwise never close a gap it still had open, since the rate is
        // derived from that speed.
        private const float MinimumCatchUpSpeed = 2f;

        private Settings _settings;
        private EntityRef _entity;

        // Extra trailing particles (ProjectileDataVisualsView's sparkTrail/glow) that need the exact
        // same "keep fading, don't cut off mid-emission" treatment as _settings.TrailParticle, set via
        // SetExtraTrailParticles once that sibling view has resolved which of its slots are actually
        // enabled for this shot's weapon. Deliberately NOT part of Settings/passed at Detach time -
        // ProjectileDataVisualsView.Initialize (which knows the enabled/disabled state) and
        // ProjectileView.Initialize (which calls Detach) are sibling components with no guaranteed
        // call order, so this can arrive either before or after Detach; ProjectileView buffers it and
        // forwards it here once both are ready.
        private ParticleSystem[] _extraTrailParticles;

        // ProjectileDataVisualsView's own sprite - normally already covered for free by _renderers
        // (GetComponentsInChildren<Renderer> below already walks the whole detached hierarchy, and
        // SpriteRenderer is a Renderer), but registered explicitly too rather than assumed, so the
        // sprite's own show/hide-with-SpawnDelay and destroy-exactly-at-Finish timing doesn't silently
        // depend on it happening to live under visualRoot in every prefab's hierarchy.
        private Renderer[] _extraRenderers;

        // Per-weapon override of the shared destroyEffectPrefab's tint - ProjectileDataVisualsView
        // sets this from WeaponDataAsset.ProjectileVisuals.ProjectileDestroyColor (alpha forced to 1)
        // whenever a resolved WeaponData exists, so it's set for every weapon-fired shot. Null (unset
        // - a skill, an enemy attack, anything with no ProjectileDataVisualsView/WeaponData at all)
        // falls back to the PREFAB ASSET's own authored startColor in PlayImpactEffect, not a pooled
        // instance's - destroyEffectPrefab is pooled and shared across every caller, weapon or not, so
        // this must never leave a null override reading as "keep whatever the last play happened to
        // tint it".
        private Color? _destroyEffectColorOverride;

        // Same idea, for every particle system under destroyEffectPrefab EXCEPT the root (e.g.
        // GenericProjectileDestroy's Sparks/Glow children) - ProjectileDataVisualsView sets this from
        // WeaponDataAsset.ProjectileVisuals.ProjectileDestroyGlowColor. Same null-falls-back-to-the-
        // prefab-asset's-own-authored-color reasoning as _destroyEffectColorOverride.
        private Color? _destroyEffectChildColorOverride;

        // Multiplier on destroyEffectPrefab's own authored transform.localScale - ProjectileDataVisualsView
        // sets this from WeaponDataAsset.ProjectileVisuals.ProjectileDestroyScale. Null (unset) is
        // equivalent to (1,1,1) - PlayImpactEffect always resolves a concrete scale to pass to the
        // pooled EffectsManager instance either way (that API has no "leave it as-is" option, unlike a
        // per-projectile particle that just never gets its localScale touched).
        private Vector3? _destroyEffectScaleOverride;

        public void SetExtraTrailParticles(ParticleSystem[] particles)
        {
            _extraTrailParticles = particles;
        }

        public void SetExtraRenderers(Renderer[] renderers)
        {
            _extraRenderers = renderers;
        }

        public void SetDestroyEffectColorOverride(Color? color)
        {
            _destroyEffectColorOverride = color;
        }

        public void SetDestroyEffectChildColorOverride(Color? color)
        {
            _destroyEffectChildColorOverride = color;
        }

        public void SetDestroyEffectScaleOverride(Vector3? scale)
        {
            _destroyEffectScaleOverride = scale;
        }

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

            LogHelper.Log("ProjFlow", $"[{_entity}] VISUAL detached at {spawnPosition} particles={_particles.Length} trail={(settings.TrailParticle != null ? settings.TrailParticle.name : "none")} t={Time.unscaledTime:F3}", this);

            QuantumEvent.Subscribe<EventProjectileDestroyed>(this, OnProjectileDestroyed);
            QuantumEvent.Subscribe<EventProjectileImpacted>(this, OnProjectileImpacted);
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
            // Checked BEFORE the _impacting early-out below, or an impact tween that never reaches
            // OnComplete (killed externally) would leave the visual hanging forever with nothing
            // able to clean it up.
            if (Time.unscaledTime - _lastPushTime > _settings.OrphanTimeout)
            {
                LogHelper.Warn("ProjectileVisual", $"{name}: nothing has pushed a target for " +
                    $"{_settings.OrphanTimeout}s and no destroy event arrived - cleaning up a visual that would " +
                    "otherwise hang in the air. Worth investigating if this shows up often.", this);
                Finish(playEffect: false);
                return;
            }

            if (_impacting == true)
                return; // the impact tween owns this transform now

            if (_entityGone == true && Time.frameCount > _entityGoneFrame)
            {
                LogHelper.Log("ProjFlow", $"[{_entity}] VISUAL entity gone, NO destroy event arrived -> Finish(no effect), trail killed with root t={Time.unscaledTime:F3}", this);
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

            LogHelper.Log("ProjFlow", $"[{_entity}] VISUAL destroy event: dist={distance:F2} speed={_speed:F1} tween={duration:F3}s entityGone={_entityGone} t={Time.unscaledTime:F3}", this);

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

            LogHelper.Log("ProjFlow", $"[{_entity}] VISUAL Finish playEffect={playEffect} -> trail {(playEffect && _settings.TrailParticle != null ? "handed to ParticleGracefulStop" : "destroyed with root")} t={Time.unscaledTime:F3}", this);

            if (playEffect == true)
                PlayImpactEffect(transform.position);

            // Only on a real impact: unparents itself and finishes emitting where the shot landed.
            // A teardown/orphan cleanup deliberately leaves nothing behind - there was no impact to
            // linger over, and on a disconnect the whole scene is on its way out anyway.
            if (playEffect == true)
            {
                if (_settings.EchoGhostParticle != null)
                    _settings.EchoGhostParticle.gameObject.AddComponent<ParticleGracefulStop>().StopAndDestroyWhenFinished();

                if (_settings.WeaponExtraParticle != null)
                    _settings.WeaponExtraParticle.gameObject.AddComponent<ParticleGracefulStop>().StopAndDestroyWhenFinished();

                // A trail that IS this root (e.g. SniperProjectile, whose only child is the
                // BulletMeshSmallFire system - so ProjectileView.ResolveVisualRoot resolves the
                // trail's own GameObject as the visual root): Destroy(gameObject) below would kill it
                // the instant the impact tween ends, no fade at all. Instead this GameObject itself
                // stays alive to fade out, minus everything that isn't a trail, and only this
                // controller goes away now.
                if (IsRoot(_settings.TrailParticle) || IsRoot(_settings.TrailRenderer))
                {
                    FadeOutRootAsTrail();
                    return;
                }

                if (_settings.TrailParticle != null)
                    _settings.TrailParticle.gameObject.AddComponent<ParticleGracefulStop>().StopAndDestroyWhenFinished();

                // Skipped when it already lives under the trail particle - that one's graceful stop
                // covers every TrailRenderer beneath it.
                if (_settings.TrailRenderer != null &&
                    (_settings.TrailParticle == null || _settings.TrailRenderer.transform.IsChildOf(_settings.TrailParticle.transform) == false))
                    _settings.TrailRenderer.gameObject.AddComponent<ParticleGracefulStop>().StopAndDestroyWhenFinished();

                if (_extraTrailParticles != null)
                {
                    foreach (ParticleSystem particle in _extraTrailParticles)
                    {
                        if (particle != null)
                            particle.gameObject.AddComponent<ParticleGracefulStop>().StopAndDestroyWhenFinished();
                    }
                }
            }

            Destroy(gameObject);
        }

        private bool IsRoot(Component component)
        {
            return component != null && component.gameObject == gameObject;
        }

        // The bullet BODY must vanish at the impact, not drift on for its lifetime; only the
        // sparks/glow/smoke around it get to fade. "Body" is the trail's root system plus every
        // system under it that renders a Mesh - the Epic Toon FX missiles are not consistent about
        // where that lives: BulletMeshSmallFire's mesh IS its root, RocketMeshMissileFire's is a
        // child named Mesh (World space, ~1s lifetime), which used to be left behind on every kill
        // and wall hit. StopEmitting afterwards (ParticleGracefulStop/ReleaseHeldInstance) is a
        // no-op on an already-cleared system. Shared with ProjectileGhostTrailManager.
        public static void ClearBodyParticles(ParticleSystem trailRoot)
        {
            foreach (ParticleSystem system in trailRoot.GetComponentsInChildren<ParticleSystem>(includeInactive: true))
            {
                bool isBody = system == trailRoot
                    || (system.TryGetComponent(out ParticleSystemRenderer renderer) && renderer.renderMode == ParticleSystemRenderMode.Mesh);

                if (isBody)
                    system.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void FadeOutRootAsTrail()
        {
            foreach (Renderer r in _renderers)
            {
                if (r is not ParticleSystemRenderer && r is not TrailRenderer)
                    r.enabled = false;
            }

            foreach (Light light in GetComponentsInChildren<Light>(includeInactive: true))
                light.enabled = false;

            if (IsRoot(_settings.TrailParticle))
                ClearBodyParticles(_settings.TrailParticle);

            // Stops every TrailRenderer under here too and lingers for its ribbon to fade.
            gameObject.AddComponent<ParticleGracefulStop>().StopAndDestroyWhenFinished();

            // OnDestroy unsubscribes the events; the GameObject outlives this component.
            Destroy(this);
        }

        // A pierce/ricochet the projectile SURVIVED (see DirectHitData.ApplyHit) - unlike
        // OnProjectileDestroyed this never sets _impacting, tweens the transform, or destroys
        // anything, since the same live projectile keeps flying past this contact. Without this the
        // weapon's own impact particle (destroyEffectPrefab) only ever played once, at the shot's
        // true final position - every intermediate pierce/bounce point showed nothing.
        //
        // Also SNAPS transform.position onto the exact hit point, not just the particle - Push (via
        // QUpdate) already ran earlier this same Unity frame and may have moved _targetPosition past
        // it: the entity's own Transform3D is already interpolating toward wherever it ends up AFTER
        // a same-tick Ricochet redirect (see DirectHitData.TryRicochet/ProjectileSystem's own
        // multi-segment tick loop), which can read as a smooth curve that cuts the corner of the
        // real L-shaped path instead of ever visibly touching the enemy - worse the faster the shot
        // travels, since one tick then covers more ground. Snapping here corrects for exactly that
        // sub-tick kink; the ongoing MoveTowards catch-up (Update, unaffected by this) resumes toward
        // the new target from this exact point on the very next frame. Rotation needs no equivalent
        // fix - Push already ran first this frame and already turned it to the new post-redirect
        // heading (see Projectile.Velocity, already updated by the time this event lands).
        private void OnProjectileImpacted(EventProjectileImpacted e)
        {
            if (e.Entity != _entity)
                return;

            Vector3 position = e.Position.ToUnityVector3();

            transform.position = position;
            _targetPosition = position;

            PlayImpactEffect(position);
        }

        private void PlayImpactEffect(Vector3 position)
        {
            PlayDestroyEffect(_settings.DestroyEffectPrefab, position,
                _destroyEffectColorOverride, _destroyEffectChildColorOverride, _destroyEffectScaleOverride);
        }

        // Shared with ProjectileGhostTrailManager.PlayImpact - a shot whose view never existed (see
        // that class's own comment) has no ProjectileVisualController instance to read overrides off,
        // but needs the EXACT same resolution logic so its destroy effect reads identically to a shot
        // that did get a view. Each override is independently nullable rather than one combined
        // "resolved config" - a caller may have only some of the three (or, for the ghost path, none
        // at all if the shot carries no WeaponData).
        //
        // Always the TINTED (two-tone) overload, never the plain one - destroyPrefab (e.g.
        // GenericProjectileDestroy) is POOLED across every projectile that uses it, weapon-fired or
        // not. If this only tinted when an override was set, a pooled instance last played with one
        // weapon's colors would keep bleeding that tint into a later untinted play (an enemy attack or
        // a weapon with no override reusing the same pooled instance) forever, since the plain overload
        // never touches startColor to reset it. Falling back to the PREFAB ASSET's own authored colors
        // (never a pooled INSTANCE's, which may already be mutated by an earlier tinted play) when
        // there's no per-weapon override keeps every non-opted-in caller's effect exactly as originally
        // authored - root and child resolved independently, since GenericProjectileDestroy's Red/Blue
        // team-color variants only have ONE child (Glow, no Sparks) while the base variant has two, so
        // index 1 isn't always "Sparks". Scale multiplies onto the prefab's own authored
        // transform.localScale rather than replacing it, so an unset override (or one explicitly set
        // to (1,1,1)) always reproduces the prefab's own size exactly.
        public static void PlayDestroyEffect(ParticleSystem destroyPrefab, Vector3 position,
            Color? rootColorOverride, Color? childColorOverride, Vector3? scaleOverride)
        {
            if (destroyPrefab == null || EffectsManager.Instance == null)
                return;

            ParticleSystem[] prefabSystems = destroyPrefab.GetComponentsInChildren<ParticleSystem>(true);
            Color rootColor = rootColorOverride ?? prefabSystems[0].main.startColor.color;
            Color childColor = childColorOverride ??
                (prefabSystems.Length > 1 ? prefabSystems[1].main.startColor.color : rootColor);
            Vector3 scale = Vector3.Scale(destroyPrefab.transform.localScale, scaleOverride ?? Vector3.one);

            EffectsManager.Instance.PlayEffect(destroyPrefab, position, Quaternion.identity, scale, rootColor, childColor);
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible)
                return;

            _visible = visible;
            LogHelper.Log("ProjFlow", $"[{_entity}] VISUAL SetVisible({visible}) {(visible ? "Play" : "Stop+CLEAR particles")} t={Time.unscaledTime:F3}", this);

            // Renderers rather than SetActive on the root: this component's own Update has to keep
            // running while a projectile sits out its ProjectileDataAsset.SpawnDelay, and an
            // inactive GameObject would stop the catch-up, the orphan timeout and the impact tween
            // along with the visuals.
            foreach (Renderer r in _renderers)
                r.enabled = visible;

            if (_extraRenderers != null)
            {
                foreach (Renderer r in _extraRenderers)
                    if (r != null) r.enabled = visible;
            }

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

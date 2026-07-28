namespace QuantumUser.View.Managers
{
    using System.Collections;
    using System.Collections.Generic;
    using Quantum;
    using UnityEngine;
    using UnityEngine.Pool;

    // Plays one-shot particle effects (e.g. projectile destroy VFX) from a pool keyed by prefab
    // reference, so bursts of short-lived effects (many projectiles dying per second) don't hit
    // Instantiate/Destroy on every play. Pooled prefabs must not loop - PlayEffect returns an
    // instance to its pool once ParticleSystem.IsAlive() goes false.
    public class EffectsManager : MonoBehaviour
    {
        public static EffectsManager Instance;

        [SerializeField, Tooltip("Pools pre-warmed on Awake so the first plays during combat don't pay an Instantiate cost.")]
        private List<ParticleSystem> prewarmPrefabs = new List<ParticleSystem>();
        [SerializeField, Tooltip("Instances created up front per prewarmed prefab.")]
        private int prewarmCountPerPrefab = 4;
        [SerializeField, Tooltip("Bypasses pooling entirely - every PlayEffect call instantiates a fresh instance and destroys it when finished, instead of reusing one from a pool. Turn on while iterating on an effect prefab so edits show up on the next play without restarting Play Mode; leave off otherwise.")]
        private bool disablePooling;

        [Header("Area Blast")]
        [SerializeField, Tooltip("Fallback blast VFX used when the detonating AreaHitData doesn't author its own BlastEffectPrefab.")]
        private ParticleSystem defaultAreaBlastEffect;

        [Header("Root")]
        [SerializeField, Tooltip("Played whenever any entity gets Rooted, from any source (see StatusEffectUtility.ApplyRoot/OnEntityRooted). Falls back to defaultAreaBlastEffect if left empty.")]
        private ParticleSystem rootEffectPrefab;
        [SerializeField, Tooltip("Added to the ground-snapped spawn position, e.g. to lift the effect above the floor and avoid z-fighting.")]
        private Vector3 rootEffectOffset = new(0f, 0.02f, 0f);

        [Header("Enemy Death")]
        [SerializeField, Tooltip("Played whenever a Filler-tier enemy explodes instead of playing its lingering die animation (see DamageUtility.ApplyDamage/EnemyExploded). Shared across every enemy type - tinted by bloodColor, set per-world via SetBloodColor (see EnvironmentManager). Falls back to defaultAreaBlastEffect if left empty.")]
        private ParticleSystem deathEffect;
        [SerializeField, Tooltip("Scorch/blast decal spawned at the raycast-detected ground point below an exploding Filler-tier enemy, tinted the same as deathEffect. Skipped entirely if no Ground-layer geometry is found within deathDecalMaxGroundDistance (e.g. the enemy died mid-air over a pit). Leave empty to skip the decal.")]
        private ParticleSystem deathDecalEffect;
        [SerializeField, Tooltip("Max vertical distance below the explosion position to accept Ground-layer geometry for deathDecalEffect placement.")]
        private float deathDecalMaxGroundDistance = 3f;

        // Set by EnvironmentManager.Load (WorldTheme.Enemy.BloodColor) - one color for every enemy
        // in the current world, not per enemy type (see project_world_system_design memory: replaces
        // the old per-EnemyDataAsset ExplosionColor).
        private Color bloodColor = Color.red;

        private readonly Dictionary<ParticleSystem, ObjectPool<ParticleSystem>> pools = new Dictionary<ParticleSystem, ObjectPool<ParticleSystem>>();

        // How far above/below the root position to search for real ground via Physics.Raycast -
        // a rooted enemy that landed against a wall (e.OnWall) can still be slightly airborne, so
        // this can't just trust e.Position's Y. Same rationale/shape as
        // EnemyAttackVisualsView.SnapToGround.
        private const float RootGroundSnapRayHeight = 20f;

        // Lazily computed, not a static field initializer - LayerMask.GetMask (via NameToLayer)
        // isn't allowed to run in a MonoBehaviour's static constructor, only from Awake/Start
        // onward. Same lazy-cache shape as EnemyAttackVisualsView.GroundLayerMask.
        private static int? _groundLayerMask;

        private static int GroundLayerMask
        {
            get
            {
                _groundLayerMask ??= UnityEngine.LayerMask.GetMask("Ground");
                return _groundLayerMask.Value;
            }
        }

        private void Awake()
        {
            Instance = this;

            if (!disablePooling)
                foreach (var prefab in prewarmPrefabs)
                    Prewarm(prefab, prewarmCountPerPrefab);

            QuantumEvent.Subscribe<EventAreaDetonated>(this, OnAreaDetonated);
            QuantumEvent.Subscribe<EventExplodeOnDeathDetonated>(this, OnExplodeOnDeathDetonated);
            QuantumEvent.Subscribe<EventVortexExploded>(this, OnVortexExploded);
            QuantumEvent.Subscribe<EventVortexMiniExploded>(this, OnVortexMiniExploded);
            QuantumEvent.Subscribe<EventJuggernautDischarged>(this, OnJuggernautDischarged);
            QuantumEvent.Subscribe<EventJuggernautEndExploded>(this, OnJuggernautEndExploded);
            QuantumEvent.Subscribe<EventJuggernautLanded>(this, OnJuggernautLanded);
            QuantumEvent.Subscribe<EventEntityRooted>(this, OnEntityRooted);
            QuantumEvent.Subscribe<EventEnemyExploded>(this, OnEnemyExploded);
            QuantumEvent.Subscribe<EventSentryOverloadDetonated>(this, OnSentryOverloadDetonated);
        }

        private void OnDestroy()
        {
            QuantumEvent.UnsubscribeListener(this);
        }

        // AreaHitData is still the source of truth for its own blast prefab, but not for the radius -
        // e.Radius is what Detonate() actually used for the damage overlap, which a BlastRadiusUpgrade
        // can push past whatever's authored on the asset.
        private void OnAreaDetonated(EventAreaDetonated e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            AreaHitData hitData = frame.FindAsset(e.HitData);
            ParticleSystem prefab = hitData.BlastEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // The mark can come from any hero's upgrade (see MarkExplosiveDeath/ExplodeOnDeath) - there's
        // no single upgrade asset behind it to resolve a bespoke blast prefab from, so this always
        // plays the shared default area blast effect, using its own authored color like every other
        // generic blast (see PlayEffect - only OnEnemyExploded's deathEffect/deathDecalEffect tint).
        private void OnExplodeOnDeathDetonated(EventExplodeOnDeathDetonated e)
        {
            PlayEffect(defaultAreaBlastEffect, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Same reasoning as OnAreaDetonated - Source always comes from exactly one
        // VortexExplodeOnDestroySkillAction asset (unlike ExplodeOnDeath, which any hero's upgrade
        // can trigger), which is where BlastEffectPrefab lives (see
        // VortexExplodeOnDestroySkillAction.View.cs).
        private void OnVortexExploded(EventVortexExploded e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            VortexExplodeOnDestroySkillAction action = frame.FindAsset(e.Source);
            ParticleSystem prefab = action.BlastEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // One of many small blasts while the vortex is alive (see VortexRandomExplosionUpgrade) -
        // same resolution as OnVortexExploded, off VortexRandomExplosionSkillAction.BlastEffectPrefab
        // instead.
        private void OnVortexMiniExploded(EventVortexMiniExploded e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            VortexRandomExplosionSkillAction action = frame.FindAsset(e.Source);
            ParticleSystem prefab = action.BlastEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Same resolution as OnVortexExploded - Source always comes from exactly one
        // SentryAddOverloadSkillAction asset, which is where BlastEffectPrefab lives (see
        // SentryAddOverloadSkillAction.View.cs).
        private void OnSentryOverloadDetonated(EventSentryOverloadDetonated e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            SentryAddOverloadSkillAction action = frame.FindAsset(e.Source);
            ParticleSystem prefab = action.BlastEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Same resolution as OnVortexExploded - Source always comes from exactly one
        // JuggernautSkillData asset, which is where DischargeEffectPrefab lives (see
        // JuggernautSkillData.View.cs).
        private void OnJuggernautDischarged(EventJuggernautDischarged e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            JuggernautSkillData skill = frame.FindAsset(e.Source);
            ParticleSystem prefab = skill.DischargeEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Same resolution as OnJuggernautDischarged - Source always comes from exactly one
        // JuggernautEndExplosionSkillAction asset, which is where BlastEffectPrefab lives (see
        // JuggernautEndExplosionSkillAction.View.cs).
        private void OnJuggernautEndExploded(EventJuggernautEndExploded e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            JuggernautEndExplosionSkillAction action = frame.FindAsset(e.Source);
            ParticleSystem prefab = action.BlastEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Same resolution as OnJuggernautEndExploded - Source always comes from exactly one
        // JuggernautLandingImpactSkillAction asset, which is where ImpactEffectPrefab lives (see
        // JuggernautLandingImpactSkillAction.View.cs). Radius is the LANDED ENEMY's own real collider
        // radius - see JuggernautLandingImpactSystem/Events.qtn.
        private void OnJuggernautLanded(EventJuggernautLanded e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            JuggernautLandingImpactSkillAction action = frame.FindAsset(e.Source);
            ParticleSystem prefab = action.ImpactEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Generic - fires for every Root proc regardless of source (see StatusEffectUtility.ApplyRoot).
        // The prefab isn't authored per-asset - Root always plays the same effect no matter which
        // hero/upgrade triggered it - so it lives directly on this manager (rootEffectPrefab).
        private void OnEntityRooted(EventEntityRooted e)
        {
            ParticleSystem prefab = rootEffectPrefab ?? defaultAreaBlastEffect;
            Vector3 position = SnapRootPositionToGround(e.Position.ToUnityVector3()) + rootEffectOffset;
            // rootEffectPrefab is authored at a reference diameter of 1 (radius 0.5), so it's
            // scaled by the target's full diameter, not its radius.
            float scale = e.Radius.AsFloat * 2f;

            Debug.Log($"[EffectsManager] OnEntityRooted entity={e.Entity} radius={e.Radius.AsFloat} scale={scale} rawPosition={e.Position.ToUnityVector3()} snappedPosition={position} prefab={(prefab != null ? prefab.name : "null")}");

            PlayEffect(prefab, position, Quaternion.identity, Vector3.one * scale);
        }

        // Filler-tier enemy death replacement for the lingering die animation (EnemyBlobAnimationView
        // never even sees EnemyActionPhase.Dead for these - DamageUtility destroys the entity the same
        // tick it fires this). Radius is the dying enemy's REAL collider radius, not an authored
        // value (see EnemyExploded in Events.qtn) - tinted by bloodColor, the same for every enemy in
        // the current world, unlike OnAreaDetonated/OnVortexExploded's per-asset BlastEffectPrefab.
        private void OnEnemyExploded(EventEnemyExploded e)
        {
            ParticleSystem prefab = deathEffect ?? defaultAreaBlastEffect;
            Vector3 position = e.Position.ToUnityVector3();
            float scale = e.Radius.AsFloat * 2f;

            PlayEffect(prefab, position, Quaternion.identity, Vector3.one * scale, bloodColor);

            if (deathDecalEffect != null && TryFindGroundBelow(position, deathDecalMaxGroundDistance, out Vector3 groundPoint))
                PlayEffect(deathDecalEffect, groundPoint, Quaternion.identity, Vector3.one * scale, bloodColor);
        }

        // Called by EnvironmentManager.Load - WorldTheme.Enemy.BloodColor becomes the tint for every
        // enemy's death VFX/decal in the current world until the next Load.
        public void SetBloodColor(Color color)
        {
            bloodColor = color;
        }

        // Final visual correction: e.Position comes from the sim's own ground-corrected
        // Transform3D.Position (see JuggernautLandingImpactSystem.CorrectPosition), but a launched
        // enemy rooted mid-air against a wall may still not be flush with the Unity-rendered floor.
        // Real UnityEngine.Physics raycast, not Quantum's - purely a view-layer placement fix, no
        // simulation involvement. Leaves position.y untouched if nothing on the Ground layer is
        // found beneath/above it. Same shape as EnemyAttackVisualsView.SnapToGround.
        private static Vector3 SnapRootPositionToGround(Vector3 position)
        {
            Vector3 rayOrigin = position + Vector3.up * RootGroundSnapRayHeight;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, RootGroundSnapRayHeight * 2f, GroundLayerMask))
                position.y = hit.point.y;

            return position;
        }

        // Same raycast-from-above shape as SnapRootPositionToGround, but reports whether the hit is
        // close enough to count as "on the ground" instead of unconditionally snapping to it - an
        // enemy exploding mid-air (e.g. knocked off a ledge) shouldn't leave a scorch decal floating
        // at some distant floor below it.
        private static bool TryFindGroundBelow(Vector3 position, float maxDistance, out Vector3 groundPoint)
        {
            Vector3 rayOrigin = position + Vector3.up * RootGroundSnapRayHeight;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, RootGroundSnapRayHeight * 2f, GroundLayerMask)
                && Mathf.Abs(hit.point.y - position.y) <= maxDistance)
            {
                groundPoint = hit.point;
                return true;
            }

            groundPoint = default;
            return false;
        }

        public void PlayEffect(ParticleSystem prefab, Vector3 position, Quaternion rotation)
        {
            PlayEffect(prefab, position, rotation, Vector3.one);
        }

        // scale lets one prefab authored at a reference size (e.g. a radius-1 blast) cover every
        // radius it's reused for - pooled instances default back to Vector3.one on Get so a scaled
        // play can't leak its size onto the next unscaled one drawn from the same pool. Plays with
        // whatever color the prefab itself was authored with - only OnEnemyExploded's
        // deathEffect/deathDecalEffect override that via the tinted overload below.
        public void PlayEffect(ParticleSystem prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            ParticleSystem instance = GetPooledInstance(prefab, position, rotation, scale, out ObjectPool<ParticleSystem> pool);
            if (instance == null) return;

            instance.Play(true);
            StartCoroutine(ReleaseWhenFinished(instance, pool));
        }

        // Tinted variant used only by OnEnemyExploded (deathEffect/deathDecalEffect), which is the
        // only place per-instance color still applies - see EnemyDataAsset.ExplosionColor.
        public void PlayEffect(ParticleSystem prefab, Vector3 position, Quaternion rotation, Vector3 scale, Color color)
        {
            ParticleSystem instance = GetPooledInstance(prefab, position, rotation, scale, out ObjectPool<ParticleSystem> pool);
            if (instance == null) return;

            // GetComponentsInChildren includes instance's own ParticleSystem, so this single loop
            // tints every sub-emitter in the hierarchy, not just the root - a pooled instance last
            // played with a tint can't leak it onto a later untinted play otherwise.
            foreach (var system in instance.GetComponentsInChildren<ParticleSystem>())
            {
                var main = system.main;
                main.startColor = color;
            }

            instance.Play(true);
            StartCoroutine(ReleaseWhenFinished(instance, pool));
        }

        // "Held" variant for an effect that needs to stay alive and be repositioned/rescaled
        // externally for an open-ended duration (e.g. EnemyAllyLinkView's endpoint particles),
        // unlike PlayEffect's fire-and-forget shape. Deliberately does NOT auto-release when
        // IsAlive() goes false - a held instance is legitimately kept looping by its owner - so the
        // prefab CAN loop here, unlike every PlayEffect prefab. Caller must pair this with
        // ReleaseHeldInstance or the instance leaks out of the pool for good.
        public ParticleSystem GetHeldInstance(ParticleSystem prefab)
        {
            if (prefab == null) return null;

            return disablePooling
                ? Instantiate(prefab, transform)
                : GetOrCreatePool(prefab).Get();
        }

        // Pairs with GetHeldInstance - prefab must be the same reference passed there, since pools
        // are keyed by prefab reference.
        public void ReleaseHeldInstance(ParticleSystem prefab, ParticleSystem instance)
        {
            if (instance == null) return;

            instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (disablePooling || prefab == null)
            {
                Destroy(instance.gameObject);
                return;
            }

            GetOrCreatePool(prefab).Release(instance);
        }

        private ParticleSystem GetPooledInstance(ParticleSystem prefab, Vector3 position, Quaternion rotation, Vector3 scale, out ObjectPool<ParticleSystem> pool)
        {
            pool = null;
            if (prefab == null) return null;

            ParticleSystem instance;
            if (disablePooling)
            {
                instance = Instantiate(prefab, transform);
            }
            else
            {
                pool = GetOrCreatePool(prefab);
                instance = pool.Get();
            }
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = scale;

            return instance;
        }

        private void Prewarm(ParticleSystem prefab, int count)
        {
            if (prefab == null) return;

            var pool = GetOrCreatePool(prefab);
            var buffer = new ParticleSystem[count];
            for (int i = 0; i < count; i++)
                buffer[i] = pool.Get();
            for (int i = 0; i < count; i++)
                pool.Release(buffer[i]);
        }

        private ObjectPool<ParticleSystem> GetOrCreatePool(ParticleSystem prefab)
        {
            if (pools.TryGetValue(prefab, out var pool))
                return pool;

            pool = new ObjectPool<ParticleSystem>(
                createFunc: () => Instantiate(prefab, transform),
                actionOnGet: instance => instance.gameObject.SetActive(true),
                actionOnRelease: instance => instance.gameObject.SetActive(false),
                actionOnDestroy: instance => Destroy(instance.gameObject));

            pools.Add(prefab, pool);
            return pool;
        }

        private IEnumerator ReleaseWhenFinished(ParticleSystem instance, ObjectPool<ParticleSystem> pool)
        {
            while (instance != null && instance.IsAlive(true))
                yield return null;

            if (instance == null) yield break;

            if (pool != null)
                pool.Release(instance);
            else
                Destroy(instance.gameObject);
        }
    }
}

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

        [Header("Shockwave")]
        [SerializeField, Tooltip("Played whenever a ShockwaveReleased event fires (currently only Empty Chamber, see docs/weapon-perks.md) - generic and source-agnostic, not per-asset. Falls back to defaultAreaBlastEffect if left empty. Authored at a reference radius of 1, scaled by e.Radius (not diameter) - same convention as the other radius-scaled handlers below.")]
        private ParticleSystem shockwaveEffectPrefab;

        [SerializeField, Tooltip("Played once at BOTH affected enemies' positions whenever Kai's Undertow ascension resolves a fresh pull target (UndertowTriggered) - a small, fixed-scale impact/mark flash, separate from the ongoing tether line itself (see KaiUndertowLinksView, which polls simulation state directly rather than reacting to this event). Falls back to defaultAreaBlastEffect (at a small fixed scale) if left empty.")]
        private ParticleSystem undertowMarkEffectPrefab;

        [Header("ExplodeOnDeath (Rift-Marked)")]
        [SerializeField, Tooltip("Played on ExplodeOnDeathDetonated when e.RiftMarked is true (see docs/elemental-reactions.md and ExplodeOnDeathConfig.RiftMarkRadiusMultiplier/RiftMarkDamageMultiplier) instead of the flat defaultAreaBlastEffect every other ExplodeOnDeath kill uses, so the bigger/harder rift-boosted blast reads as visually distinct rather than just a scaled-up copy. Falls back to defaultAreaBlastEffect, tinted riftMarkedExplodeFallbackColor, if left empty - same dedicated-slot-with-tinted-fallback pattern as detonationEffectPrefab below.")]
        private ParticleSystem riftMarkedExplodeEffectPrefab;
        [SerializeField, Tooltip("Tint applied only when falling back to defaultAreaBlastEffect (riftMarkedExplodeEffectPrefab left empty) - matches detonationFallbackColor/ResonanceFxView's riftMarkColor and the Rift Mark hot-pink #FD3971 presentation rule. Ignored once a dedicated prefab is authored.")]
        private Color riftMarkedExplodeFallbackColor = new Color32(0xFD, 0x39, 0x71, 0xFF);

        [Header("Detonation")]
        [SerializeField, Tooltip("Played on DetonationReleased (Fire+RiftMark reaction - see docs/elemental-reactions.md and StatusEffectUtility.TryTriggerDetonation). Falls back to defaultAreaBlastEffect, tinted detonationFallbackColor, if left empty - so it already reads distinctly hot-pink even before a bespoke prefab is authored.")]
        private ParticleSystem detonationEffectPrefab;
        [SerializeField, Tooltip("Tint applied only when falling back to defaultAreaBlastEffect (detonationEffectPrefab left empty) - matches ResonanceFxView's own riftMarkColor and the Rift Mark hot-pink #FD3971 presentation rule (purple is reserved for Void). Ignored once a dedicated prefab is authored, since that plays with its own authored color.")]
        private Color detonationFallbackColor = new Color32(0xFD, 0x39, 0x71, 0xFF);

        [Header("Singularity")]
        [SerializeField, Tooltip("Played on SingularityTriggered (Void+RiftMark reaction - see docs/elemental-reactions.md and StatusEffectUtility.TryTriggerSingularity). Falls back to defaultAreaBlastEffect, tinted singularityFallbackColor, if left empty - same pattern as detonationEffectPrefab above.")]
        private ParticleSystem singularityEffectPrefab;
        [SerializeField, Tooltip("Tint applied only when falling back to defaultAreaBlastEffect (singularityEffectPrefab left empty) - stays Void's own purple/dark tone rather than Rift Mark's hot-pink, since this reaction's whole identity is Void reacting, not the mark itself. Ignored once a dedicated prefab is authored.")]
        private Color singularityFallbackColor = new Color(0.35f, 0.15f, 0.5f);

        [Header("Overflowing Rift")]
        [SerializeField, Tooltip("Played on OverflowingRiftTriggered (Overflowing Rift mutation - see docs/rift-mutations.md) - a small, restrained pulse when a Rift Mark application lands against an already-2-stack target, deliberately NOT comparable in strength to a full reaction VFX. Falls back to defaultAreaBlastEffect, tinted overflowingRiftFallbackColor, if left empty.")]
        private ParticleSystem overflowingRiftPulsePrefab;
        [SerializeField, Tooltip("Tint applied only when falling back to defaultAreaBlastEffect (overflowingRiftPulsePrefab left empty) - hot-pink, same Rift Mark color rule as detonationFallbackColor/riftMarkColor.")]
        private Color overflowingRiftFallbackColor = new Color32(0xFD, 0x39, 0x71, 0xFF);

        [Header("Groundbreaker")]
        [SerializeField, Tooltip("Played on GroundbreakerSlammed (Brute's Groundbreaker Ascension - see docs/brute-ascensions.md) at his landing point. Authored at a reference radius of 1 and scaled by e.Radius, so one prefab covers all three ranks (3 / 3 / 4.5) rather than needing three. Falls back to defaultAreaBlastEffect, tinted groundbreakerFallbackColor, if left empty - same dedicated-slot-with-tinted-fallback pattern as the reaction VFX above.")]
        private ParticleSystem groundbreakerImpactPrefab;
        [SerializeField, Tooltip("Optional ground crack/dust decal stamped at the raycast-detected ground point under the landing, radius-scaled like the burst itself. Same optional-decal shape as deathDecalEffect - skipped entirely if left empty, or if no Ground-layer geometry is found within groundbreakerDecalMaxGroundDistance.")]
        private ParticleSystem groundbreakerDecalPrefab;
        [SerializeField, Tooltip("Max vertical distance below the landing position to accept Ground-layer geometry for groundbreakerDecalPrefab placement. Small by design - Groundbreaker only fires on a landing, so real ground is always right there; this exists to avoid stamping a crack on some distant floor if he lands on a thin platform over a pit.")]
        private float groundbreakerDecalMaxGroundDistance = 2f;
        [SerializeField, Tooltip("Tint applied only when falling back to defaultAreaBlastEffect (groundbreakerImpactPrefab left empty) - a dusty earth tone, since this is a terrain impact rather than an explosion or a rift reaction. Ignored once a dedicated prefab is authored.")]
        private Color groundbreakerFallbackColor = new Color(0.72f, 0.6f, 0.42f);

        [Header("Wall Slam")]
        [SerializeField, Tooltip("Played on WallSlammed at the wall CONTACT point, oriented into the surface (see WallSlamUtility) - generic and source-agnostic, so both Brute's Iron Shoulder dash and his Groundbreaker landing use it with no per-source hookup. Falls back to defaultAreaBlastEffect if left empty.")]
        private ParticleSystem wallSlamEffectPrefab;
        [SerializeField, Tooltip("Uniform scale for wallSlamEffectPrefab when the Stun did NOT land (a hard-CC immunity window, or an ImmuneToHardCC tier - the target still hit the wall). This event carries no radius, so scale is authored rather than derived, same reasoning as meleeHitEffectScale.")]
        private float wallSlamEffectScale = 1f;
        [SerializeField, Tooltip("Uniform scale used instead when the Stun genuinely LANDED - the moment that actually rewards the player (and the one that opens Groundbreaker rank 3's Exposed window), so it reads heavier than a wall contact that got resisted.")]
        private float wallSlamStunnedEffectScale = 1.6f;

        [Header("Melee Hit")]
        [SerializeField, Tooltip("Played whenever a HitEffectApplied event fires from a non-enemy Owner (a player skill/weapon hitting something) - generic and source-agnostic, not per-asset. Falls back to defaultAreaBlastEffect if left empty. Enemy-caused hits are handled separately by EnemyAttackVisualsView (per-delivery HitImpactPrefab), so this only covers the previously-uncovered player-hit case.")]
        private ParticleSystem meleeHitEffectPrefab;
        [SerializeField, Tooltip("Uniform scale used for meleeHitEffectPrefab (or its fallback) - this event carries no radius of its own to derive a scale from.")]
        private float meleeHitEffectScale = 1f;

        [Header("Heal / Shield Grant")]
        [SerializeField, Tooltip("Played at the target whenever EntityHealed fires, from any source (PortableSpeakerSkillAction, HealEffectData, HealthRegenSystem, ...) - generic and source-agnostic, not per-asset. The floating heal number (DamageFeedbackManager) and hit-flash (HitFeedback) already cover this event too; this is just the particle. Leave empty to skip the particle - unlike the blast-style handlers above, this deliberately does NOT fall back to defaultAreaBlastEffect, since a combat blast reads wrong for a heal.")]
        private ParticleSystem healGrantEffectPrefab;
        [SerializeField, Tooltip("Played at the target whenever EntityShielded fires, from any source (BodyguardSkillAction, Lux's Shield Battery aura, ShieldEffectData) - generic and source-agnostic, not per-asset. Leave empty to skip the particle, same no-fallback reasoning as healGrantEffectPrefab.")]
        private ParticleSystem shieldGrantEffectPrefab;
        [SerializeField, Tooltip("Played at the target whenever ShieldBroken fires (Shield.Current hitting 0 - see DamageUtility.AbsorbWithShield), from any source (player or enemy). Leave empty to skip the particle, same no-fallback reasoning as healGrantEffectPrefab.")]
        private ParticleSystem shieldBreakEffectPrefab;

        [Header("Quantum Rounds")]
        [SerializeField, Tooltip("Uniform scale used for QuantumRoundsTriggered's impact spark - the prefab itself is resolved per-asset off Source.ImpactEffectPrefab (see QuantumRoundsWeaponPerkData.View.cs/OnQuantumRoundsTriggered below), falling back to defaultAreaBlastEffect if that's left empty. This event carries no radius of its own to derive a scale from, same reasoning as meleeHitEffectScale/projectileReflectedEffectScale.")]
        private float quantumRoundsEffectScale = 1f;

        [Header("Projectile Reflect")]
        [SerializeField, Tooltip("Played whenever a ProjectileReflected event fires (Kai's Reflect dash ascension, see MirrorStepSkillAction) - a single point 'parry' spark at the reflected projectile's position, not radius-scaled. Falls back to defaultAreaBlastEffect (at a small fixed scale) if left empty.")]
        private ParticleSystem projectileReflectedEffectPrefab;
        [SerializeField, Tooltip("Uniform scale used for projectileReflectedEffectPrefab (or its fallback) - this effect has no radius of its own to derive a scale from.")]
        private float projectileReflectedEffectScale = 1f;

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
            QuantumEvent.Subscribe<EventVortexImploded>(this, OnVortexImploded);
            QuantumEvent.Subscribe<EventUndertowTriggered>(this, OnUndertowTriggered);
            QuantumEvent.Subscribe<EventJuggernautDischarged>(this, OnJuggernautDischarged);
            QuantumEvent.Subscribe<EventJuggernautEndExploded>(this, OnJuggernautEndExploded);
            QuantumEvent.Subscribe<EventJuggernautLanded>(this, OnJuggernautLanded);
            QuantumEvent.Subscribe<EventEnemyExploded>(this, OnEnemyExploded);
            QuantumEvent.Subscribe<EventSentryOverloadDetonated>(this, OnSentryOverloadDetonated);
            QuantumEvent.Subscribe<EventShockwaveReleased>(this, OnShockwaveReleased);
            QuantumEvent.Subscribe<EventGroundbreakerSlammed>(this, OnGroundbreakerSlammed);
            QuantumEvent.Subscribe<EventWallSlammed>(this, OnWallSlammed);
            QuantumEvent.Subscribe<EventWeaponExplosionReleased>(this, OnWeaponExplosionReleased);
            QuantumEvent.Subscribe<EventDetonationReleased>(this, OnDetonationReleased);
            QuantumEvent.Subscribe<EventSingularityTriggered>(this, OnSingularityTriggered);
            QuantumEvent.Subscribe<EventOverflowingRiftTriggered>(this, OnOverflowingRiftTriggered);
            QuantumEvent.Subscribe<EventQuantumRoundsTriggered>(this, OnQuantumRoundsTriggered);
            QuantumEvent.Subscribe<EventProjectileReflected>(this, OnProjectileReflected);
            QuantumEvent.Subscribe<EventHitEffectApplied>(this, OnHitEffectApplied);
            QuantumEvent.Subscribe<EventEntityHealed>(this, OnEntityHealed);
            QuantumEvent.Subscribe<EventEntityShielded>(this, OnEntityShielded);
            QuantumEvent.Subscribe<EventShieldBroken>(this, OnShieldBroken);
            QuantumEvent.Subscribe<EventEnemySelfDestructBeginVisual>(this, OnEnemySelfDestructBeginVisual);
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
            Vector3 position = e.Position.ToUnityVector3();
            Vector3 scale = Vector3.one * e.Radius.AsFloat;

            if (e.RiftMarked == true)
            {
                if (riftMarkedExplodeEffectPrefab != null)
                {
                    PlayEffect(riftMarkedExplodeEffectPrefab, position, Quaternion.identity, scale);
                    return;
                }

                PlayEffect(defaultAreaBlastEffect, position, Quaternion.identity, scale, riftMarkedExplodeFallbackColor);
                return;
            }

            PlayEffect(defaultAreaBlastEffect, position, Quaternion.identity, scale);
        }

        // Same reasoning as OnAreaDetonated - Source always comes from exactly one
        // VortexCollapseSkillAction asset (unlike ExplodeOnDeath, which any hero's upgrade
        // can trigger), which is where BlastEffectPrefab lives (see
        // VortexCollapseSkillAction.View.cs).
        private void OnVortexExploded(EventVortexExploded e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            VortexCollapseSkillAction action = frame.FindAsset(e.Source);
            ParticleSystem prefab = action.BlastEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Kai's Compression rank 3 "Implosion" - a smaller blast every third Vortex pulse, while the
        // vortex is still alive. Same resolution as OnVortexExploded, off
        // CompressionSkillAction.BlastEffectPrefab instead.
        private void OnVortexImploded(EventVortexImploded e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            CompressionSkillAction action = frame.FindAsset(e.Source);
            ParticleSystem prefab = action.BlastEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Kai's Undertow ascension - a small, fixed-scale impact/mark flash on BOTH the struck enemy
        // and its pull target (Source/Target here are always genuine enemies, never Kai/the owner -
        // see UndertowTriggered's own comment in Events.qtn), separate from the ongoing tether line
        // (which KaiUndertowLinksView polls live off simulation state - see that class - rather than
        // reacting to this event).
        private void OnUndertowTriggered(EventUndertowTriggered e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            ParticleSystem prefab = undertowMarkEffectPrefab ?? defaultAreaBlastEffect;
            Vector3 scale = Vector3.one * 0.5f;

            if (frame.Exists(e.Source) == true)
            {
                Vector3 sourcePosition = EnemyMovementUtility.ResolveEntityCenter(frame, e.Source).ToUnityVector3();
                PlayEffect(prefab, sourcePosition, Quaternion.identity, scale);
            }

            if (frame.Exists(e.Target) == true)
            {
                Vector3 targetPosition = EnemyMovementUtility.ResolveEntityCenter(frame, e.Target).ToUnityVector3();
                PlayEffect(prefab, targetPosition, Quaternion.identity, scale);
            }
        }

        // Same resolution as OnVortexExploded - Source always comes from exactly one
        // SentryOverloadCoreSkillAction asset, which is where BlastEffectPrefab lives (see
        // SentryOverloadCoreSkillAction.View.cs).
        private void OnSentryOverloadDetonated(EventSentryOverloadDetonated e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            SentryOverloadCoreSkillAction action = frame.FindAsset(e.Source);
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
        // AftershockSkillAction asset (the Ascension that grants this event), which is where
        // BlastEffectPrefab lives (see AftershockSkillAction.View.cs).
        private void OnJuggernautEndExploded(EventJuggernautEndExploded e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            AftershockSkillAction upgrade = frame.FindAsset(e.Source);
            ParticleSystem prefab = upgrade.BlastEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Same resolution as OnJuggernautEndExploded - Source always comes from exactly one
        // ConcussiveImpactSkillAction asset, which is where ImpactEffectPrefab lives (see
        // ConcussiveImpactSkillAction.View.cs). Radius is the LANDED ENEMY's own real collider
        // radius - see JuggernautLandingImpactSystem/Events.qtn.
        private void OnJuggernautLanded(EventJuggernautLanded e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            ConcussiveImpactSkillAction upgrade = frame.FindAsset(e.Source);
            ParticleSystem prefab = upgrade.ImpactEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Generic - fires for any radial-push moment regardless of source (Empty Chamber, Kai's Dash
        // Shockwave, Zara's Resonance pulse - see HitEffectUtility.ApplyShockwave/
        // WeaponSystem.ApplyMagazineEmptiedPerks). Same "no single asset to resolve a bespoke prefab
        // from" reasoning as OnExplodeOnDeathDetonated - the prefab lives directly on this manager,
        // not per-perk.
        //
        // Skips entirely for EVERY Zara Resonance pulse (owner carries Resonance), not just her Remix
        // ones: ResonanceFxView (attached to her own entity) already plays her dedicated pulse VFX on
        // every pulse - tinted by the Remix status when e.Effect is valid, default-colored otherwise -
        // so playing this generic one on top would double it. Empty Chamber carries no Resonance, so
        // it still plays here as before. (e.Effect.IsValid alone used to only cover the Remix case,
        // leaving normal pulses double-playing.)
        private void OnShockwaveReleased(EventShockwaveReleased e)
        {
            Frame frame = e.Game.Frames.Predicted;

            if (e.Effect.IsValid == true || (frame != null && frame.Has<Resonance>(e.Entity) == true))
                return;

            ParticleSystem prefab = shockwaveEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Brute's Groundbreaker Ascension (see docs/brute-ascensions.md) - fires once per qualifying
        // landing, whether or not anything was caught, so a big drop into an empty room still reads.
        // Same "no single asset to resolve a bespoke prefab from" reasoning as the reaction handlers
        // above: it's a PassiveUpgradeData, whose Apply gets no self AssetRef to travel with the event,
        // so the prefab lives on this manager rather than per-asset (the shape Undertow/Detonation/
        // Singularity/Overflowing Rift already use for exactly the same reason).
        //
        // e.Position is Brute's own landing transform, which sits at his feet - close enough for the
        // burst, but the decal is ground-probed separately so a crack can't float above a slope.
        private void OnGroundbreakerSlammed(EventGroundbreakerSlammed e)
        {
            Vector3 position = e.Position.ToUnityVector3();
            Vector3 scale = Vector3.one * e.Radius.AsFloat;

            if (groundbreakerImpactPrefab != null)
                PlayEffect(groundbreakerImpactPrefab, position, Quaternion.identity, scale);
            else
                PlayEffect(defaultAreaBlastEffect, position, Quaternion.identity, scale, groundbreakerFallbackColor);

            if (groundbreakerDecalPrefab != null
                && TryFindGroundBelow(position, groundbreakerDecalMaxGroundDistance, out Vector3 groundPoint))
                PlayEffect(groundbreakerDecalPrefab, groundPoint, Quaternion.identity, scale);
        }

        // Generic - fires for every WallSlamUtility.TryWallSlam that actually found a wall, regardless
        // of which knockback source produced it (Brute's Iron Shoulder dash, his Groundbreaker landing,
        // anything added later). Same source-agnostic reasoning as OnShockwaveReleased.
        //
        // Oriented INTO the wall off e.PushDirection, so the burst sprays against the surface rather
        // than playing a symmetric puff; e.Position is already the wall contact point, not the target's
        // own position. e.Stunned picks the heavier variant - a wall hit that got resisted by a hard-CC
        // immunity window (or an ImmuneToHardCC tier) shouldn't read as the same payoff as one that
        // landed, since only the landed case opens Groundbreaker rank 3's Exposed window.
        private void OnWallSlammed(EventWallSlammed e)
        {
            ParticleSystem prefab = wallSlamEffectPrefab ?? defaultAreaBlastEffect;
            float scale = e.Stunned == true ? wallSlamStunnedEffectScale : wallSlamEffectScale;

            Vector3 push = e.PushDirection.ToUnityVector3();

            // A degenerate direction can't produce a rotation - LookRotation logs an error and returns
            // identity for a zero vector. The simulation already rejects a zero push before raycasting,
            // so this is belt-and-braces rather than an expected case.
            Quaternion rotation = push.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(push, Vector3.up)
                : Quaternion.identity;

            PlayEffect(prefab, e.Position.ToUnityVector3(), rotation, Vector3.one * scale);
        }

        // Generic - fires for any weapon-perk explosion that has no dedicated VFX of its own
        // (currently Cataclysm Round and Explosive Sequence, see HitEffectUtility.ApplyExplosion).
        // Always plays defaultAreaBlastEffect directly, unlike OnShockwaveReleased - neither perk
        // has (or needs) its own bespoke prefab field, same reasoning OnExplodeOnDeathDetonated
        // already uses.
        private void OnWeaponExplosionReleased(EventWeaponExplosionReleased e)
        {
            PlayEffect(defaultAreaBlastEffect, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Fire+RiftMark reaction (see docs/elemental-reactions.md and
        // StatusEffectUtility.TryTriggerDetonation) - unlike OnWeaponExplosionReleased above, this one
        // gets its own dedicated prefab slot rather than always falling through to the shared blast,
        // since it's meant to read as a distinct effect. Until detonationEffectPrefab is authored,
        // falls back to defaultAreaBlastEffect tinted detonationFallbackColor via the same tinted
        // PlayEffect overload OnEnemyExploded uses, so it's still visually distinct in the meantime.
        private void OnDetonationReleased(EventDetonationReleased e)
        {
            Vector3 position = e.Position.ToUnityVector3();
            Vector3 scale = Vector3.one * e.Radius.AsFloat;

            if (detonationEffectPrefab != null)
            {
                PlayEffect(detonationEffectPrefab, position, Quaternion.identity, scale);
                return;
            }

            PlayEffect(defaultAreaBlastEffect, position, Quaternion.identity, scale, detonationFallbackColor);
        }

        // Void+RiftMark reaction (see docs/elemental-reactions.md and
        // StatusEffectUtility.TryTriggerSingularity) - pulls every enemy in range toward the
        // reaction's target; this is purely the visual, the actual pull impulse already happened in
        // simulation. Same dedicated-slot-with-tinted-fallback pattern as OnDetonationReleased above.
        private void OnSingularityTriggered(EventSingularityTriggered e)
        {
            Vector3 position = e.Position.ToUnityVector3();
            Vector3 scale = Vector3.one * e.Radius.AsFloat;

            if (singularityEffectPrefab != null)
            {
                PlayEffect(singularityEffectPrefab, position, Quaternion.identity, scale);
                return;
            }

            PlayEffect(defaultAreaBlastEffect, position, Quaternion.identity, scale, singularityFallbackColor);
        }

        // Overflowing Rift mutation (see docs/rift-mutations.md and
        // RiftMarkApplicationUtility.ApplyRequest) - fires when an application lands against a target
        // already at max Rift Mark stacks instead of being wasted. Deliberately restrained: same
        // dedicated-slot-with-tinted-fallback pattern as every other reaction VFX here, but callers
        // are expected to author a small, low-key prefab, not a full reaction-strength blast.
        private void OnOverflowingRiftTriggered(EventOverflowingRiftTriggered e)
        {
            Vector3 position = e.Position.ToUnityVector3();
            Vector3 scale = Vector3.one * e.Radius.AsFloat;

            if (overflowingRiftPulsePrefab != null)
            {
                PlayEffect(overflowingRiftPulsePrefab, position, Quaternion.identity, scale);
                return;
            }

            PlayEffect(defaultAreaBlastEffect, position, Quaternion.identity, scale, overflowingRiftFallbackColor);
        }

        // Same resolution as OnGroundPoundTriggered - Source always comes from exactly one
        // QuantumRoundsWeaponPerkData asset (baked unconditionally alongside HasQuantumRounds in
        // QuantumRoundsWeaponPerkData.Apply), which is where ImpactEffectPrefab lives (see
        // QuantumRoundsWeaponPerkData.View.cs). Point spark, not radius-scaled - uses
        // quantumRoundsEffectScale instead, same reasoning as OnProjectileReflected below.
        private void OnQuantumRoundsTriggered(EventQuantumRoundsTriggered e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            QuantumRoundsWeaponPerkData perk = frame.FindAsset(e.Source);
            ParticleSystem prefab = perk.ImpactEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * quantumRoundsEffectScale);
        }

        // Point spark for Kai's Reflect dash ascension (see MirrorStepSkillAction) - no
        // radius on the event itself, so this uses its own fixed authored scale instead of
        // e.Radius-driven scaling like every other generic handler above.
        private void OnProjectileReflected(EventProjectileReflected e)
        {
            ParticleSystem prefab = projectileReflectedEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * projectileReflectedEffectScale);
        }

        // Generic - fires for every HitEffectUtility.ApplyToTarget/DamageUtility hit, both enemy- and
        // player-caused. Enemy-caused hits already get their own per-delivery impact via
        // EnemyAttackVisualsView.OnHitEffectApplied (EnemyDeliveryData.HitImpactPrefab), which self-
        // filters to hits it owns - this handler covers the other half (a player skill/weapon hitting
        // something), which had no visual at all before. Skipping enemy owners here avoids playing
        // this generic effect on top of that per-delivery one for the same hit.
        //
        // Also skips MultiTarget hits entirely - those come from an overlap query that can (and
        // regularly does) catch several entities in one action, e.g. an AreaHitData bomb or Zara's
        // Resonance pulse. Playing this generic spark once per target hit would stack N of them on
        // top of the action's own single dedicated blast VFX (AreaDetonated/ShockwaveReleased/...)
        // - the exact "several generic hit effects on one area hit" bug this guard exists to
        // prevent. A multi-target action that wants its own per-target impact needs a dedicated
        // hookup, same as everything else in this file already gets.
        private void OnHitEffectApplied(EventHitEffectApplied e)
        {
            if (e.MultiTarget == true)
                return;

            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            if (frame.Has<Enemy>(e.Owner) == true)
                return;

            ParticleSystem prefab = meleeHitEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * meleeHitEffectScale);
        }

        // Generic - fires for every EntityHealed regardless of source (regen tick, PortableSpeakerSkillAction,
        // HealEffectData, ...), same reasoning OnHitEffectApplied uses for player hits. Position is read
        // from the TARGET's own live Transform3D, not off the event (EntityHealed carries no position of
        // its own, unlike the hit/blast events above) - a heal always lands on an existing entity, unlike
        // a hit which can connect against level geometry. No defaultAreaBlastEffect fallback, unlike
        // every blast-style handler above - skips entirely if healGrantEffectPrefab is unset, since a
        // combat blast reads wrong for a heal.
        private void OnEntityHealed(EventEntityHealed e)
        {
            if (healGrantEffectPrefab == null)
                return;

            Frame frame = e.Game.Frames.Predicted;
            if (frame == null || frame.Has<Transform3D>(e.Target) == false)
                return;

            PlayEffect(healGrantEffectPrefab, frame.Get<Transform3D>(e.Target).Position.ToUnityVector3(), Quaternion.identity);
        }

        // Shield counterpart to OnEntityHealed - same shape, same no-fallback reasoning.
        private void OnEntityShielded(EventEntityShielded e)
        {
            if (shieldGrantEffectPrefab == null)
                return;

            Frame frame = e.Game.Frames.Predicted;
            if (frame == null || frame.Has<Transform3D>(e.Target) == false)
                return;

            PlayEffect(shieldGrantEffectPrefab, frame.Get<Transform3D>(e.Target).Position.ToUnityVector3(), Quaternion.identity);
        }

        // Fired the exact tick Shield.Current crosses from >0 to <=0 (see DamageUtility.
        // AbsorbWithShield/Shield.qtn's OnShieldBroken signal) - a "pop" moment distinct from
        // EntityShielded's own grant particle above, generic across every target (player or enemy).
        // Same live-Transform3D/no-fallback shape as OnEntityHealed/OnEntityShielded.
        private void OnShieldBroken(EventShieldBroken e)
        {
            if (shieldBreakEffectPrefab == null)
                return;

            Frame frame = e.Game.Frames.Predicted;
            if (frame == null || frame.Has<Transform3D>(e.Target) == false)
                return;

            PlayEffect(shieldBreakEffectPrefab, frame.Get<Transform3D>(e.Target).Position.ToUnityVector3(), Quaternion.identity);
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

        // Companion to OnEnemyExploded above, but for a specific enemy ACTION's own authored
        // BeginStep particle (e.g. Suicider's explosion) rather than the generic per-tier death
        // burst - see EnemySelfDestructBeginVisual's own qtn comment for the full reasoning. This
        // deliberately lives on a scene-persistent manager, NOT the per-entity EnemyAttackVisualsView
        // that owns BeginStep for every other (non-instant-death) case - a Filler/Normal tier
        // self-destruct has its own view torn down (OnDestroy -> QuantumEvent.UnsubscribeListener)
        // before Quantum ever dispatches this same-tick event, so subscribing on that per-entity
        // component silently never receives its own event. Resolves everything from the event's own
        // payload (frame.FindAsset(e.Action) is static asset data, safe regardless of whether the
        // raising entity still exists) rather than re-reading any live entity state.
        private void OnEnemySelfDestructBeginVisual(EventEnemySelfDestructBeginVisual e)
        {
            Frame frame = e.Game.Frames.Predicted;
            if (frame == null || e.Action.IsValid == false)
                return;

            EnemyActionData actionData = frame.FindAsset(e.Action);
            AttackVisualStep step = actionData?.BeginStep;

            if (step == null || step.ParticlePrefab == null)
                return;

            Vector3 worldPosition = e.Position.ToUnityVector3() + step.Offset;
            Quaternion rotation = step.AlignToEnemyDirection == true
                ? Quaternion.Euler(0f, e.FacingAngle.AsFloat, 0f) * Quaternion.Euler(step.RotationOffset)
                : Quaternion.Euler(step.RotationOffset);
            Vector3 scale = step.ParticlePrefab.transform.localScale * step.Scale;

            PlayEffect(step.ParticlePrefab, worldPosition, rotation, scale);
        }

        // Raycast-from-above ground probe (same shape a since-removed OnEntityRooted placement-fix
        // used to share - see git history if that's needed again), but reports whether the hit is
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

            // GetComponentsInChildren(true) includes instance's own ParticleSystem AND every
            // sub-emitter in the hierarchy - true = INACTIVE children too, so a child that starts
            // disabled (e.g. Zara's pulse ring toggled on mid-play) still gets tinted instead of
            // keeping its authored color. A pooled instance last played with a tint can't leak it
            // onto a later untinted play otherwise.
            foreach (var system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.startColor = color;
            }

            instance.Play(true);
            StartCoroutine(ReleaseWhenFinished(instance, pool));
        }

        // Sorting-order variant used by AttackVisualStep.OverrideSortingOrder (see
        // EnemyAttackVisualsView.SpawnStepParticle) - null leaves every renderer at whatever it was
        // pooled/authored with, exactly like the untinted PlayEffect overload above.
        public void PlayEffect(ParticleSystem prefab, Vector3 position, Quaternion rotation, Vector3 scale, int? sortingOrder)
        {
            ParticleSystem instance = GetPooledInstance(prefab, position, rotation, scale, out ObjectPool<ParticleSystem> pool);
            if (instance == null) return;

            if (sortingOrder.HasValue == true)
            {
                // GetComponentsInChildren(true) covers the root's own ParticleSystemRenderer AND
                // every child's, inactive ones included - same "force every sub-emitter, not just
                // the root" reasoning as the tinted overload's color loop below.
                foreach (var renderer in instance.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    renderer.sortingOrder = sortingOrder.Value;
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
        // are keyed by prefab reference. Stops emission only (not StopEmittingAndClear) and defers
        // the actual pool release/deactivate until already-alive particles finish dying out on their
        // own - same "wait for IsAlive() to go false" shape ReleaseWhenFinished uses for PlayEffect -
        // so a status effect ending reads as a fade-out instead of the whole instance vanishing
        // mid-particle.
        public void ReleaseHeldInstance(ParticleSystem prefab, ParticleSystem instance)
        {
            if (instance == null) return;

            instance.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            ObjectPool<ParticleSystem> pool = (disablePooling || prefab == null) ? null : GetOrCreatePool(prefab);
            StartCoroutine(ReleaseWhenFinished(instance, pool));
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

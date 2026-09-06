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
    //
    // DefaultExecutionOrder pins Awake() (which sets Instance) ahead of every default-order script,
    // same reason GroundBlobManager/BuildingShadowManager/SpriteManager are pinned: Awake order
    // between two independent MonoBehaviours is otherwise undefined, and EnvironmentManager.Load
    // pushes the world's blood colour in from ITS Awake. Losing that race left every world running
    // the serialized default red until something called Load() again by hand.
    [DefaultExecutionOrder(-1000)]
    public class EffectsManager : MonoBehaviour
    {
        public static EffectsManager Instance;

        [SerializeField, Tooltip("Pools pre-warmed on Awake so the first plays during combat don't pay an Instantiate cost.")]
        private List<ParticleSystem> prewarmPrefabs = new List<ParticleSystem>();
        [SerializeField, Tooltip("Instances created up front per prewarmed prefab.")]
        private int prewarmCountPerPrefab = 4;
        [SerializeField, Tooltip("Bypasses pooling entirely - every PlayEffect call instantiates a fresh instance and destroys it when finished, instead of reusing one from a pool. Turn on while iterating on an effect prefab so edits show up on the next play without restarting Play Mode; leave off otherwise.")]
        private bool disablePooling;

        [SerializeField, Tooltip("Extra vertical nudge added on top of EnemyMovementUtility.ResolveEntityCenter's own result for every per-entity spark this manager resolves a live body-center for (see ResolveCenter) - accessory-blocked/free-hit-guard/heal/shield/revive/shield-break. Compensates for the shared player KCCSettings.Height being unusually squat (1.0 vs. the addon's own 1.75 default), without touching that physics-relevant asset directly. 0 = trust ResolveEntityCenter as-is.")]
        private float centerHeightOffset = 0f;

        [Header("Area Blast")]
        [SerializeField, Tooltip("Fallback blast VFX used when the detonating AreaHitData doesn't author its own BlastEffectPrefab.")]
        private ParticleSystem defaultAreaBlastEffect;

        [Header("Shockwave")]
        [SerializeField, Tooltip("Played whenever a ShockwaveReleased event fires (currently only Empty Chamber, see docs/weapon-perks.md) - generic and source-agnostic, not per-asset. Falls back to defaultAreaBlastEffect if left empty. Authored at a reference radius of 1, scaled by e.Radius (not diameter) - same convention as the other radius-scaled handlers below.")]
        private ParticleSystem shockwaveEffectPrefab;

        [SerializeField, Tooltip("Played once at BOTH affected enemies' positions whenever Kai's Undertow ascension resolves a fresh pull target (UndertowTriggered) - a small, fixed-scale impact/mark flash, separate from the ongoing tether line itself (see KaiUndertowLinksView, which polls simulation state directly rather than reacting to this event). Falls back to defaultAreaBlastEffect (at a small fixed scale) if left empty.")]
        private ParticleSystem undertowMarkEffectPrefab;

        [Header("Shock (Jolt)")]
        [SerializeField, Tooltip("Played on JoltTriggered - a one-shot spark every time Electrified's periodic Jolt actually fires (see docs/elemental-reactions.md's \"Shock (Electrified)\" section), distinct from StatusEffectsManager's own ambient electrifiedParticlePrefab/staggerParticlePrefab trackers (which only show the status is currently active, not the instant of each individual Jolt). Also fires once per secondary enemy staggered by Shatter's AoE (see StatusEffectUtility.TryTriggerShatter) - same ApplyStagger primitive, same spark. Falls back to defaultAreaBlastEffect, tinted joltFallbackColor, at joltScaleMultiplier, if left empty.")]
        private ParticleSystem joltEffectPrefab;
        [SerializeField, Tooltip("Local position offset added to e.Position before playing joltEffectPrefab (or its fallback) - e.g. to nudge the spark up toward chest/head height instead of the entity's feet-level Transform3D.")]
        private Vector3 joltPositionOffset;
        [SerializeField, Tooltip("Euler rotation offset applied to joltEffectPrefab (or its fallback) - this event carries no direction of its own to orient from, so this is purely an authored tilt/spin.")]
        private Vector3 joltRotationOffset;
        [SerializeField, Tooltip("Per-axis scale for joltEffectPrefab (or its fallback) - this event carries no radius of its own to derive a uniform scale from, and a per-axis vector lets the spark read stretched/squashed rather than only uniformly bigger or smaller.")]
        private Vector3 joltScaleMultiplier = Vector3.one * 0.6f;
        [SerializeField, Tooltip("Tint applied only when falling back to defaultAreaBlastEffect (joltEffectPrefab left empty) - electric yellow-white, same family as Overload's own fallback tint. Ignored once a dedicated prefab is authored.")]
        private Color joltFallbackColor = new Color(1f, 0.95f, 0.4f);

        [Header("Thermal Shock (Burn + Chill)")]
        [SerializeField, Tooltip("Played on ThermalShockTriggered (Burn+Chill reaction - see docs/elemental-reactions.md and StatusEffectUtility.TryTriggerThermalShock) at the struck enemy's own position - a brief concentrated single-target impact, not radius-scaled (the event carries no Radius). Falls back to defaultAreaBlastEffect, tinted thermalShockFallbackColor, at thermalShockEffectScale, if left empty.")]
        private ParticleSystem thermalShockEffectPrefab;
        [SerializeField, Tooltip("Uniform scale for thermalShockEffectPrefab (or its fallback) - this event carries no radius of its own to derive a scale from, same reasoning as projectileReflectedEffectScale.")]
        private float thermalShockEffectScale = 1f;
        [SerializeField, Tooltip("Tint applied only when falling back to defaultAreaBlastEffect (thermalShockEffectPrefab left empty) - orange+blue, read as a short white-hot flash. Ignored once a dedicated prefab is authored.")]
        private Color thermalShockFallbackColor = new Color(1f, 0.55f, 0.15f);

        [Header("Overload (Burn + Shock)")]
        [SerializeField, Tooltip("Played at e.OriginPosition on OverloadTriggered (Burn+Shock reaction - see docs/elemental-reactions.md and StatusEffectUtility.TryTriggerOverload) - the flash where the chain starts. Falls back to defaultAreaBlastEffect, tinted overloadFallbackColor, at overloadEffectScale, if left empty.")]
        private ParticleSystem overloadOriginParticlePrefab;
        [SerializeField, Tooltip("A LOOPING particle system that actually travels from e.From toward the target's live position on every OverloadChainLink (see TravelOverloadSegment) - played, animated over ElementalReactionConfig.OverloadChainDelay real seconds (read live off the same asset the simulation itself uses, not a separately authored view-side duration - see OnOverloadChainLink), then stopped (existing particles allowed to fade) once it arrives. Author this prefab's own Particle System with Looping enabled and Play On Awake off - PlayEffect/pooling calls Play()/Stop() explicitly. Ignored entirely if overloadChainLinePrefab is assigned. Falls back to defaultAreaBlastEffect (a plain point flash at e.To, no travel) if both are left empty.")]
        private ParticleSystem overloadTravelParticlePrefab;
        [SerializeField, Tooltip("Alternative to overloadTravelParticlePrefab - ONE LineRenderer (world-space) instance per chain, spanning every entity the chain has hit so far (see BeginOverloadChainLine/AppendOverloadChainLink) - positionCount tracks visited-count exactly, one point per enemy (origin included), not a travel/growth animation; a fresh hop just adds its own point immediately. Every point re-resolves its owning entity's LIVE position every frame (RunOverloadChainLine), so the whole chain visually follows if any of its enemies keep moving. Takes priority over overloadTravelParticlePrefab when assigned; not pooled (the chain is cooldown-gated and capped at 8 visited slots, nowhere near projectile-hit frequency). Leave empty to keep the traveling-particle look.")]
        private LineRenderer overloadChainLinePrefab;
        [SerializeField, Tooltip("Interior points inserted between each pair of CONSECUTIVE enemies in the chain, each nudged by a random perpendicular jitter (see UpdateOverloadChainLinePositions) so the beam reads as a jagged shock-lightning bolt between hops instead of a dead-straight segment. 0 disables jitter entirely. The anchor points themselves (one per enemy, incl. the origin) are NEVER jittered - only what's drawn between them.")]
        private int overloadChainLineJitterSegments = 4;
        [SerializeField, Tooltip("Max perpendicular random offset (world units) applied to each interior jittered point between two enemies - larger reads as a wilder/more erratic bolt, 0 collapses back to straight segments.")]
        private float overloadChainLineJitter = 0.2f;
        [SerializeField, Tooltip("Real seconds between re-randomizing the jitter offsets (see RunOverloadChainLine) - short (0.01-0.05s) reads as an electric crackle; anchor points still re-track their entity's live position every single frame regardless, only the JITTER shape itself refreshes on this slower timer (refreshing every frame would still look jittery, just needlessly - the crackle reads the same at a much cheaper update rate).")]
        private float overloadChainLineJitterRefreshInterval = 0.03f;
        [SerializeField, Tooltip("Real seconds since Overload's last hop before its chain line is considered finished and starts fading (see RunOverloadChainLine) - should comfortably exceed ElementalReactionConfig.OverloadChainDelay so a chain that's still actively hopping never times out between two hops, only once it has genuinely ended (no further target found in range, or the current end of the chain was destroyed). Ignored if overloadChainLinePrefab is left empty.")]
        private float overloadChainLineIdleTimeout = 0.4f;
        [SerializeField, Tooltip("Real seconds overloadChainLinePrefab's ALPHA (startColor/endColor, not width) fades to 0 once its chain is considered finished (see overloadChainLineIdleTimeout), before the instance is destroyed - purely cosmetic, all the chain's damage already landed by then. Ignored if overloadChainLinePrefab is left empty.")]
        private float overloadChainLineFadeDuration = 0.12f;
        [SerializeField, Tooltip("Optional - played at e.To on every OverloadChainLink for a small extra punch where the chain actually lands. Leave empty to skip entirely; overloadTravelParticlePrefab/overloadChainLinePrefab already cover the link visually on their own.")]
        private ParticleSystem overloadImpactParticlePrefab;
        [SerializeField, Tooltip("Uniform scale for overloadOriginParticlePrefab/overloadImpactParticlePrefab (or their fallback) - neither event carries a radius of its own, same reasoning as projectileReflectedEffectScale.")]
        private float overloadEffectScale = 1f;
        [SerializeField, Tooltip("Tint applied only when falling back to defaultAreaBlastEffect (overloadOriginParticlePrefab/overloadImpactParticlePrefab left empty) - electric yellow-white, kept visually distinct from Thermal Shock's orange+blue and Shatter's icy blue. Ignored once a dedicated prefab is authored.")]
        private Color overloadFallbackColor = new Color(1f, 0.95f, 0.4f);

        [Header("Shatter (Chill + Shock)")]
        [SerializeField, Tooltip("Played on ShatterTriggered (Chill+Shock reaction - see docs/elemental-reactions.md and StatusEffectUtility.TryTriggerShatter) at the reaction's center (the strongly-staggered primary target, which never itself moves) - a short radial 'crack', not an explosion or a pull. Authored at a reference radius of 1 and scaled by e.Radius (the real ShatterRadius) so the visual reads at the actual gameplay extent. Falls back to defaultAreaBlastEffect, tinted shatterFallbackColor, if left empty.")]
        private ParticleSystem shatterEffectPrefab;
        [SerializeField, Tooltip("Tint applied only when falling back to defaultAreaBlastEffect (shatterEffectPrefab left empty) - icy blue with yellow lightning accents, read as a brief angular crack rather than an implosion/explosion. Ignored once a dedicated prefab is authored.")]
        private Color shatterFallbackColor = new Color(0.35f, 0.7f, 1f);

        [Header("Groundbreaker")]
        [SerializeField, Tooltip("Played on GroundbreakerSlammed (Brute's Groundbreaker Ascension - see docs/brute-ascensions.md) at his landing point. Authored at a reference radius of 1 and scaled by e.Radius, so one prefab covers all three ranks (3 / 3 / 4.5) rather than needing three. Falls back to defaultAreaBlastEffect, tinted groundbreakerFallbackColor, if left empty - same dedicated-slot-with-tinted-fallback pattern as the reaction VFX above.")]
        private ParticleSystem groundbreakerImpactPrefab;
        [SerializeField, Tooltip("Optional ground crack/dust decal stamped at the raycast-detected ground point under the landing, radius-scaled like the burst itself. Same optional-decal shape as deathDecalEffect - skipped entirely if left empty, or if no Ground-layer geometry is found within groundbreakerDecalMaxGroundDistance.")]
        private ParticleSystem groundbreakerDecalPrefab;
        [SerializeField, Tooltip("Max vertical distance below the landing position to accept Ground-layer geometry for groundbreakerDecalPrefab placement. Small by design - Groundbreaker only fires on a landing, so real ground is always right there; this exists to avoid stamping a crack on some distant floor if he lands on a thin platform over a pit.")]
        private float groundbreakerDecalMaxGroundDistance = 2f;
        [SerializeField, Tooltip("Tint applied only when falling back to defaultAreaBlastEffect (groundbreakerImpactPrefab left empty) - a dusty earth tone, since this is a terrain impact rather than an explosion or a rift reaction. Ignored once a dedicated prefab is authored.")]
        private Color groundbreakerFallbackColor = new Color(0.72f, 0.6f, 0.42f);

        [Header("Fall Death")]
        [SerializeField, Tooltip("Played on FallDeathTriggered (PlayerFallSystem/EnemyFallSystem, see CLAUDE.md's KCC fall-velocity notes) at the position an entity fell below LevelConfig.FallDeathHeight - before any respawn teleport, so it plays where they vanished, not where they land. Unlike most other radius-scaled effects on this manager, scale is the PREFAB's OWN authored localScale multiplied by e.Radius (KCC radius for a player, PhysicsCollider3D radius for an enemy) rather than an absolute reference-radius-1 override, so the artist's authored proportions survive the per-entity scaling. Falls back to defaultAreaBlastEffect, tinted fallDeathFallbackColor, if left empty - same dedicated-slot-with-tinted-fallback pattern as the reaction VFX above.")]
        private ParticleSystem fallDeathParticlePrefab;
        [SerializeField, Tooltip("Tint applied only when falling back to defaultAreaBlastEffect (fallDeathParticlePrefab left empty) - a dark void tone, since this reads as vanishing off the map rather than an explosion or terrain impact. Ignored once a dedicated prefab is authored.")]
        private Color fallDeathFallbackColor = new Color(0.15f, 0.15f, 0.2f);

        [Header("Wall Slam")]
        [SerializeField, Tooltip("Played on WallSlammed at the wall CONTACT point, oriented into the surface (see WallSlamUtility) - generic and source-agnostic, so both Brute's Iron Shoulder dash and his Groundbreaker landing use it with no per-source hookup. Falls back to defaultAreaBlastEffect if left empty.")]
        private ParticleSystem wallSlamEffectPrefab;
        [SerializeField, Tooltip("Uniform scale for wallSlamEffectPrefab when the Stun did NOT land (a hard-CC immunity window, or an ImmuneToHardCC tier - the target still hit the wall). This event carries no radius, so scale is authored rather than derived, same reasoning as selfHitEffectScale.")]
        private float wallSlamEffectScale = 1f;
        [SerializeField, Tooltip("Uniform scale used instead when the Stun genuinely LANDED - the moment that actually rewards the player (and the one that opens Groundbreaker rank 3's Exposed window), so it reads heavier than a wall contact that got resisted.")]
        private float wallSlamStunnedEffectScale = 1.6f;

        [Header("Enemy Attack Anticipation")]
        [SerializeField, Tooltip("Billboard particle prefab (e.g. an exclamation mark, with its own Billboard component) shown above an enemy's head for its entire attack windup (Preparation+Telegraph) - a generic readiness cue for every enemy/action/delivery, not authored per-EnemyActionData. EnemyAttackVisualsView spawns/releases an instance via GetAnticipationIconInstance/ReleaseAnticipationIconInstance below (bound to this field, same held-and-externally-repositioned shape GetHeldInstance/ReleaseHeldInstance already provide for EnemyAllyLinkView's tether particles) rather than owning its own prefab reference, so the icon is configured here alongside every other combat VFX. Must not auto-destroy/one-shot itself. Leave empty to skip.")]
        private ParticleSystem anticipationIconEffectPrefab;
        [SerializeField, Tooltip("Extra offset EnemyAttackVisualsView adds on top of the auto-resolved head height (AnticipationIconOffset below, read by that component's own UpdateAnticipationIcon) - scaled by the enemy's own live collider radius (so one authored value reads correctly on a Filler and a Boss alike), then X is mirrored (not rotated) by which way the enemy is currently facing, matching its sprite's own left/right flip; Y/Z stay as authored (also radius-scaled). Values are therefore in \"radius units\", not world units - nudge higher/forward if the icon reads as clipping into the art. Lives here rather than on EnemyAttackVisualsView so it's tweakable in the same place as the prefab itself.")]
        private Vector3 anticipationIconOffset = Vector3.up * 0.3f;
        [SerializeField, Tooltip("Minimum real-time (Time.time-based, not simulation ticks) EnemyAttackVisualsView keeps the icon visible once shown, even if the enemy's own windup (AnticipationTime, Preparation+Telegraph) ends sooner - a fast enemy's short windup could otherwise flash the icon for a single frame, unreadable as an actual warning cue. The icon keeps tracking the enemy's head for the remainder even after the windup itself has ended.")]
        private float minimumAnticipationIconDuration = 0.4f;

        public Vector3 AnticipationIconOffset => anticipationIconOffset;
        public float MinimumAnticipationIconDuration => minimumAnticipationIconDuration;

        [Header("Accessory Guard")]
        [SerializeField, Tooltip("GENERIC fallback played where a BROKEN accessory's debris comes to rest (see docs/accessory-guard.md) - the durability-0 block still knocks the accessory off and flies it on the normal arc, and this is the \"it shattered\" payoff at the landing point. A hero can override it per-accessory via CharacterData.Accessory.BrokenEffectPrefab; this covers everyone who doesn't. Leave empty to skip the particle entirely; deliberately no fallback to defaultAreaBlastEffect, since an explosion reads wrong for a hat breaking.")]
        private ParticleSystem accessoryBrokenEffectPrefab;
        [SerializeField, Tooltip("Uniform scale for accessoryBrokenEffectPrefab - this event carries no radius of its own to derive one from, same as selfHitEffectScale.")]
        private float accessoryBrokenEffectScale = 1f;

        [SerializeField, Tooltip("Played where a dropped accessory is picked back up by its owner (EventAccessoryRecovered) - the \"got it back\" payoff that closes the go-and-fetch loop. Leave empty to skip; no fallback, since a combat blast reads wrong for a pickup.")]
        private ParticleSystem accessoryRecoveredEffectPrefab;
        [SerializeField, Tooltip("Uniform scale for accessoryRecoveredEffectPrefab.")]
        private float accessoryRecoveredEffectScale = 1f;

        [SerializeField, Tooltip("Played at the point of impact when the Accessory Guard eats a hit outright (EventAccessoryBlocked). Falls back to the generic melee hit spark, then to defaultAreaBlastEffect - a block IS an impact, so the normal hit VFX reads correctly here; it's the TINT below that says \"stopped\" rather than \"hurt\". Left empty, the fallback chain still gives every block a visual.")]
        private ParticleSystem accessoryBlockedEffectPrefab;
        [SerializeField, Tooltip("Uniform scale for accessoryBlockedEffectPrefab - this event carries no radius of its own, same as selfHitEffectScale.")]
        private float accessoryBlockedEffectScale = 1f;
        [SerializeField, Tooltip("Tint applied to the block impact. Blue by default: a blocked hit must never read as damage, and this is the same colour language the guard uses everywhere else (HitFeedback.blockFlashColor, CharacterUiWidget's guard fill). Uses the tinted PlayEffect overload, so one shared spark prefab covers both a normal hit and a block.")]
        private Color accessoryBlockedEffectColor = new Color(0.25f, 0.6f, 1f);

        [Header("Free Hit Guard")]
        [SerializeField, Tooltip("Played at the point of impact when a Free Hit Guard negates a hit (EventFreeHitGuardConsumed - Brute's Bodyguard today). Its own prefab rather than sharing the accessory block's: both are 'that hit was stopped cold', but they are different mechanics with different sources, and a player should be able to tell at a glance which one just saved them. Falls back to the generic melee hit spark, then defaultAreaBlastEffect, so a guard is never silent unauthored.")]
        private ParticleSystem freeHitGuardEffectPrefab;
        [SerializeField, Tooltip("Uniform scale for freeHitGuardEffectPrefab - this event carries no radius of its own, same as selfHitEffectScale.")]
        private float freeHitGuardEffectScale = 1f;

        // Deliberately NO tint field here, unlike the accessory block above. This prefab is authored
        // for this one purpose, so its colours are the artist's to own - runtime-tinting a
        // purpose-built effect only ever fights the authoring. The "this was negated, not damage"
        // colour signal is carried by the CHARACTER flash instead (HitFeedback.freeHitGuardFlashColor,
        // cyan rather than the normal white), which is the part that would otherwise read as a hit.

        [Header("Enemy Hit (we hit an enemy)")]
        [SerializeField, Tooltip("Played at the impact point whenever WE (a player/ally - a non-enemy owner) land a NON-critical hit on an enemy. Driven off EventEntityDamaged - the SAME per-hit event the floating damage numbers (DamageFeedbackManager) and character hit-flash (HitFeedback) already react to - so it fires once per damage instance a bullet/melee/skill connects (including once per enemy caught in an area blast, and once per damage-over-time tick). Falls back to defaultAreaBlastEffect if left empty. Enemy-on-enemy and self-inflicted (Silent) damage never trigger it.")]
        private ParticleSystem enemyHitEffectPrefab;
        [SerializeField, Tooltip("Uniform scale for enemyHitEffectPrefab - EventEntityDamaged carries no radius to derive one from.")]
        private float enemyHitEffectScale = 1f;

        [SerializeField, Tooltip("Played instead of enemyHitEffectPrefab when the hit on the enemy was a CRITICAL - read straight off EventEntityDamaged.IsCritical, the exact crit the damage number shows. Falls back to enemyHitEffectPrefab, then defaultAreaBlastEffect, if left empty.")]
        private ParticleSystem enemyCriticalHitEffectPrefab;
        [SerializeField, Tooltip("Uniform scale for enemyCriticalHitEffectPrefab (or its fallback).")]
        private float enemyCriticalHitEffectScale = 1f;

        [Header("Self Hit (an enemy hits us)")]
        [SerializeField, Tooltip("Played at the player's centroid when an ENEMY lands a hit on a player - one generic 'you got hit' spark for every enemy (the per-enemy EnemyDeliveryData.HitImpactPrefab path was removed in favour of this). Also stands in for a hit fully negated by the Accessory Guard / Free Hit Guard, which fire no EntityDamaged of their own (see OnAccessoryBlocked/OnFreeHitGuardConsumed). Falls back to defaultAreaBlastEffect if left empty.")]
        private ParticleSystem selfHitEffectPrefab;
        [SerializeField, Tooltip("Uniform scale for selfHitEffectPrefab.")]
        private float selfHitEffectScale = 1f;

        [Header("Heal / Shield Grant")]
        [SerializeField, Tooltip("Played at the target whenever EntityHealed fires, from any source (PortableSpeakerSkillAction, HealEffectData, HealthRegenSystem, ...) - generic and source-agnostic, not per-asset. The floating heal number (DamageFeedbackManager) and hit-flash (HitFeedback) already cover this event too; this is just the particle. Leave empty to skip the particle - unlike the blast-style handlers above, this deliberately does NOT fall back to defaultAreaBlastEffect, since a combat blast reads wrong for a heal.")]
        private ParticleSystem healGrantEffectPrefab;
        [SerializeField, Tooltip("Played at the target whenever EntityShielded fires, from any source (BodyguardSkillAction, Lux's Shield Battery aura, ShieldEffectData) - generic and source-agnostic, not per-asset. Leave empty to skip the particle, same no-fallback reasoning as healGrantEffectPrefab.")]
        private ParticleSystem shieldGrantEffectPrefab;
        [SerializeField, Tooltip("Played at the target whenever ShieldBroken fires (Shield.Current hitting 0 - see DamageUtility.AbsorbWithShield), from any source (player or enemy). Leave empty to skip the particle, same no-fallback reasoning as healGrantEffectPrefab.")]
        private ParticleSystem shieldBreakEffectPrefab;

        [Header("Quantum Rounds")]
        [SerializeField, Tooltip("Uniform scale used for QuantumRoundsTriggered's impact spark - the prefab itself is resolved per-asset off Source.ImpactEffectPrefab (see QuantumRoundsWeaponPerkData.View.cs/OnQuantumRoundsTriggered below), falling back to defaultAreaBlastEffect if that's left empty. This event carries no radius of its own to derive a scale from, same reasoning as selfHitEffectScale/projectileReflectedEffectScale.")]
        private float quantumRoundsEffectScale = 1f;

        [Header("Projectile Reflect")]
        [SerializeField, Tooltip("Played whenever a ProjectileReflected event fires (Kai's Reflect dash ascension, see MirrorStepSkillAction) - a single point 'parry' spark at the reflected projectile's position, not radius-scaled. Falls back to defaultAreaBlastEffect (at a small fixed scale) if left empty.")]
        private ParticleSystem projectileReflectedEffectPrefab;
        [SerializeField, Tooltip("Uniform scale used for projectileReflectedEffectPrefab (or its fallback) - this effect has no radius of its own to derive a scale from.")]
        private float projectileReflectedEffectScale = 1f;

        [Header("Revive")]
        [SerializeField, Tooltip("Played at the target's position whenever EventPlayerRevived fires (teammate-hold, self-revive, or the auto-revive-on-secure sweep - see docs/revive.md) - generic and source-agnostic, the same for every hero, so it lives here rather than per-view (see BlobAnimationView.OnPlayerRevived for that same event's own punch-scale reaction). Leave empty to skip the particle.")]
        private ParticleSystem reviveEffectPrefab;
        [SerializeField, Tooltip("Uniform scale for reviveEffectPrefab - this event carries no radius of its own to derive one from, same as selfHitEffectScale.")]
        private float reviveEffectScale = 1f;

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
            QuantumEvent.Subscribe<EventJoltTriggered>(this, OnJoltTriggered);
            QuantumEvent.Subscribe<EventThermalShockTriggered>(this, OnThermalShockTriggered);
            QuantumEvent.Subscribe<EventOverloadTriggered>(this, OnOverloadTriggered);
            QuantumEvent.Subscribe<EventOverloadChainLink>(this, OnOverloadChainLink);
            QuantumEvent.Subscribe<EventShatterTriggered>(this, OnShatterTriggered);
            QuantumEvent.Subscribe<EventQuantumRoundsTriggered>(this, OnQuantumRoundsTriggered);
            QuantumEvent.Subscribe<EventProjectileReflected>(this, OnProjectileReflected);
            QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
            QuantumEvent.Subscribe<EventEntityHealed>(this, OnEntityHealed);
            QuantumEvent.Subscribe<EventEntityShielded>(this, OnEntityShielded);
            QuantumEvent.Subscribe<EventShieldBroken>(this, OnShieldBroken);
            QuantumEvent.Subscribe<EventEnemySelfDestructBeginVisual>(this, OnEnemySelfDestructBeginVisual);
            QuantumEvent.Subscribe<EventAccessoryBroken>(this, OnAccessoryBroken);
            QuantumEvent.Subscribe<EventAccessoryRecovered>(this, OnAccessoryRecovered);
            QuantumEvent.Subscribe<EventAccessoryBlocked>(this, OnAccessoryBlocked);
            QuantumEvent.Subscribe<EventFreeHitGuardConsumed>(this, OnFreeHitGuardConsumed);
            QuantumEvent.Subscribe<EventPlayerRevived>(this, OnPlayerRevived);
            QuantumEvent.Subscribe<EventFallDeathTriggered>(this, OnFallDeathTriggered);
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

            // The prefab's own authored rotation (e.g. tilted flat to lie on the ground plane), not
            // world-identity - GetPooledInstance's SetPositionAndRotation would otherwise silently
            // discard whatever orientation the artist actually set up on it.
            PlayEffect(prefab, e.Position.ToUnityVector3(), prefab.transform.rotation, Vector3.one * e.Radius.AsFloat);
        }

        // The mark can come from any hero's upgrade (see MarkExplosiveDeath/ExplodeOnDeath) - there's
        // no single upgrade asset behind it to resolve a bespoke blast prefab from, so this always
        // plays the shared default area blast effect, using its own authored color like every other
        // generic blast (see PlayEffect - only OnEnemyExploded's deathEffect/deathDecalEffect tint).
        private void OnExplodeOnDeathDetonated(EventExplodeOnDeathDetonated e)
        {
            Vector3 position = e.Position.ToUnityVector3();
            Vector3 scale = Vector3.one * e.Radius.AsFloat;

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
        // Shockwave - see HitEffectUtility.ApplyShockwave/
        // WeaponSystem.ApplyMagazineEmptiedPerks). Same "no single asset to resolve a bespoke prefab
        // from" reasoning as OnExplodeOnDeathDetonated - the prefab lives directly on this manager,
        // not per-perk.
        //
        // Skipped whenever the shockwave carries its own resolved effect (e.Effect valid) - that case
        // has a dedicated, effect-tinted visual of its own and playing the generic one on top would
        // double it. (Zara's own Resonance pulse used to be excluded here too; Resonance no longer
        // exists - see Flow.qtn - so only the effect-carrying case remains.)
        // leaving normal pulses double-playing.)
        private void OnShockwaveReleased(EventShockwaveReleased e)
        {
            Frame frame = e.Game.Frames.Predicted;

            if (e.Effect.IsValid == true)
                return;

            ParticleSystem prefab = shockwaveEffectPrefab ?? defaultAreaBlastEffect;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat);
        }

        // Brute's Groundbreaker Ascension (see docs/brute-ascensions.md) - fires once per qualifying
        // landing, whether or not anything was caught, so a big drop into an empty room still reads.
        // Same "no single asset to resolve a bespoke prefab from" reasoning as the reaction handlers
        // above: it's a PassiveUpgradeData, whose Apply gets no self AssetRef to travel with the event,
        // so the prefab lives on this manager rather than per-asset (the shape Undertow/Thermal
        // Shock/Overload/Shatter already use for exactly the same reason).
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

        // Fires for both a falling player (PlayerFallSystem) and a falling Boss/Elite/Persistent
        // enemy (EnemyFallSystem) the instant they drop below LevelConfig.FallDeathHeight. e.Radius
        // is that entity's own KCC (player) or PhysicsCollider3D (enemy) radius. Unlike most other
        // radius-scaled effects here (which override localScale to the reference-radius-1
        // convention), this one preserves whatever scale the prefab is authored at and multiplies
        // IT by e.Radius, so a non-uniform/pre-scaled prefab's own proportions survive.
        private void OnFallDeathTriggered(EventFallDeathTriggered e)
        {
            ParticleSystem prefab = fallDeathParticlePrefab != null ? fallDeathParticlePrefab : defaultAreaBlastEffect;
            if (prefab == null) return;

            Vector3 position = e.Position.ToUnityVector3();
            Vector3 scale = prefab.transform.localScale * e.Radius.AsFloat;

            if (fallDeathParticlePrefab != null)
                PlayEffect(prefab, position, Quaternion.identity, scale);
            else
                PlayEffect(prefab, position, Quaternion.identity, scale, fallDeathFallbackColor);
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
        // Fired by AccessoryGuardSystem the moment the broken accessory's debris LANDS (not when the
        // hit was taken), so the shatter plays where the player can actually see it come apart - see
        // DroppedAccessory.Broken. Falls back to the owner's own position only when no debris ever
        // flew (no prototype assigned), which AccessoryGuardUtility.TryBlock raises directly.
        private void OnAccessoryBroken(EventAccessoryBroken e)
        {
            // A break leaves NOTHING in the world to explain itself - unlike a normal block, no
            // collectible spawns and no radar arrow points anywhere, so the only way back (paying the
            // Merchant at the Store) would go uncommunicated. Local player(s) only: a teammate's break
            // is theirs to act on. Raised before the prefab lookup below, which early-returns when no
            // shatter effect is authored.
            if (MyLocalPlayer.Instance != null && MyLocalPlayer.Instance.IsLocalEntity(e.Owner))
                ToastManager.Instance?.Show("ACCESSORY DESTROYED\nBuy a new one at the Store");

            // Per-hero override first, generic fallback second - so authoring nothing per hero still
            // gives every accessory a break effect, and a hero only needs its own when a shared puff
            // genuinely doesn't fit (feathers vs. shards). Same default-with-override shape
            // EnemyDataAsset.ViewPrefab/FactionSkins already uses.
            ParticleSystem prefab = ResolveAccessoryBrokenEffect(e) ?? accessoryBrokenEffectPrefab;

            if (prefab == null)
                return;

            PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity,
                Vector3.one * accessoryBrokenEffectScale);
        }

        // A hit the accessory ate outright never reaches EventEntityDamaged (DamageUtility returns
        // before firing it - the hit is NEGATED, not mitigated), so nothing in the normal hit-VFX path
        // runs for it. Without this a block lands with no impact at the contact point at all, which
        // reads as the attack having missed rather than having been stopped - the same gap
        // HitFeedback/HurtOverlayUiWidget already fill on the character and screen respectively.
        //
        // Deliberately reuses the ordinary hit spark rather than authoring a bespoke one: a block IS
        // an impact and should hit as hard visually. The BLUE tint is what distinguishes it, via the
        // existing tinted PlayEffect overload - so one shared prefab covers both cases and they can
        // never drift apart in feel.
        //
        // e.Position is AccessoryGuardUtility's own ground/feet-level anchor (shared with where the
        // knocked-off accessory collectible lands, which genuinely wants ground level) - re-resolved
        // to e.Owner's live body CENTER here instead (see ResolveCenter below), so the impact reads
        // on the body like every other hit spark rather than at the feet. Falls back to e.Position
        // only if the frame/entity can't be resolved.
        private void OnAccessoryBlocked(EventAccessoryBlocked e)
        {
            Vector3 position = ResolveCenter(e.Game.Frames.Predicted, e.Owner, e.Position.ToUnityVector3());

            PlayNegatedHitImpact(accessoryBlockedEffectPrefab, position,
                accessoryBlockedEffectScale, accessoryBlockedEffectColor);

            // A block IS a hit against us that just got absorbed - play the generic self-hit spark
            // here too, explicitly and unconditionally, rather than only as PlayNegatedHitImpact's
            // distant fallback (which only ever fires if accessoryBlockedEffectPrefab itself is
            // empty). This is deliberately in ADDITION to the accessory-specific impact above, not a
            // replacement for it - the accessory's own block visual and "you took a hit" both read
            // as true at once when the accessory breaks.
            if (selfHitEffectPrefab != null)
                PlayEffect(selfHitEffectPrefab, position, Quaternion.identity, Vector3.one * selfHitEffectScale);
        }

        // Free Hit Guard (Brute's Bodyguard today - see StatusEffects.qtn) negates a hit the same way
        // an accessory block does, and for the same reason produces no EventEntityDamaged. It gets its
        // OWN prefab/scale/tint rather than sharing the accessory's: they are different mechanics from
        // different sources, and which one just saved you is worth being able to read at a glance.
        // Only the plumbing below is shared.
        //
        // e.Position is DamageUtility's own raw Transform3D.Position (feet-level) - re-resolved to
        // e.Target's live body CENTER here, same reasoning as OnAccessoryBlocked above.
        private void OnFreeHitGuardConsumed(EventFreeHitGuardConsumed e)
        {
            Vector3 position = ResolveCenter(e.Game.Frames.Predicted, e.Target, e.Position.ToUnityVector3());

            // Untinted - plays in whatever colours its prefab was authored with. See the field's own
            // comment for why this one doesn't get a tint the way the accessory block does.
            PlayNegatedHitImpact(freeHitGuardEffectPrefab, position,
                freeHitGuardEffectScale, null);
        }

        // Shared center resolution for every per-entity spark in this file that needs to land on the
        // BODY rather than at the feet/collider-origin. EnemyMovementUtility.ResolveEntityCenter
        // already does this correctly for both enemies (Transform3D.Position IS their collider center
        // by convention) and players (KCC.Position + KCCSettings.Height/2) - centerHeightOffset below
        // is an extra tunable nudge on top of that for players specifically, since the one shared
        // KCCSettings.Height (1.0) every hero's KCC currently uses is unusually squat next to the
        // addon's own default (1.75), so the resolved center alone can still read close to the feet
        // for how tall the sprites actually render. Falls back to `fallback` (the event's own raw
        // position) only if the frame/entity can't be resolved (e.g. already destroyed this tick).
        private Vector3 ResolveCenter(Frame frame, EntityRef entity, Vector3 fallback)
        {
            // Has<Transform3D>, not just Exists - EnemyMovementUtility.ResolveEntityCenter does a
            // hard f.Get<Transform3D> internally, which throws if the entity lacks one.
            if (frame == null || frame.Has<Transform3D>(entity) == false)
                return fallback;

            Vector3 center = EnemyMovementUtility.ResolveEntityCenter(frame, entity).ToUnityVector3();
            center.y += centerHeightOffset;
            return center;
        }

        // Shared plumbing for every "this hit was stopped cold" impact. A negated hit returns from
        // DamageUtility before EventEntityDamaged fires, so nothing in the normal hit-VFX path runs for
        // it - without an explicit play here the attack reads as having MISSED rather than having been
        // stopped, which is the same gap HitFeedback/HurtOverlayUiWidget fill on the character and
        // screen respectively.
        // color is optional: pass one to tint a SHARED/borrowed prefab into reading as a negation (the
        // accessory block, which leans on the generic hit spark), or null to play a purpose-built
        // prefab in its own authored colours (the Free Hit Guard). Tinting an effect authored for one
        // job only fights the artist.
        private void PlayNegatedHitImpact(ParticleSystem prefab, Vector3 position, float scale, Color? color)
        {
            // Same fallback chain OnEntityDamaged uses for the self-hit case, one step longer - a negated hit should never
            // be silent just because no bespoke prefab was authored for it yet. Borrowing the ordinary
            // hit spark is fine as a stopgap: a block IS an impact and should land as hard visually.
            ParticleSystem resolved = prefab ?? selfHitEffectPrefab ?? defaultAreaBlastEffect;

            if (resolved == null)
                return;

            if (color.HasValue == true)
            {
                PlayEffect(resolved, position, Quaternion.identity, Vector3.one * scale, color.Value);
                return;
            }

            PlayEffect(resolved, position, Quaternion.identity, Vector3.one * scale);
        }

        // Resolved through the OWNER's own hero data, the same lookup DroppedAccessoryView uses for
        // the collectible sprite - so this stays generic (no hero is named here) and a hero that
        // authors nothing simply falls through to the shared effect.
        private static ParticleSystem ResolveAccessoryBrokenEffect(EventAccessoryBroken e)
        {
            Frame frame = e.Game != null ? e.Game.Frames.Predicted : null;

            if (frame == null || e.Owner == EntityRef.None)
                return null;

            if (frame.TryGet<CharacterStats>(e.Owner, out var stats) == false || stats.CharacterData.IsValid == false)
                return null;

            CharacterData data = frame.FindAsset(stats.CharacterData);
            return data != null ? data.Accessory.BrokenEffectPrefab : null;
        }

        // Fired by AccessoryGuardUtility.Recover at the collectible's own resting position (not the
        // player's), so the burst plays exactly where the accessory was snatched up.
        private void OnAccessoryRecovered(EventAccessoryRecovered e)
        {
            if (accessoryRecoveredEffectPrefab == null)
                return;

            PlayEffect(accessoryRecoveredEffectPrefab, e.Position.ToUnityVector3(), Quaternion.identity,
                Vector3.one * accessoryRecoveredEffectScale);
        }

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

        // One-shot spark every time Electrified's periodic Jolt actually fires (see
        // docs/elemental-reactions.md's "Shock (Electrified)" section) - distinct from
        // StatusEffectsManager's own ambient electrifiedParticlePrefab/staggerParticlePrefab trackers,
        // which only show a status is currently active rather than marking each individual Jolt.
        private void OnJoltTriggered(EventJoltTriggered e)
        {
            Vector3 position = e.Position.ToUnityVector3() + joltPositionOffset;
            Quaternion rotation = Quaternion.Euler(joltRotationOffset);

            if (joltEffectPrefab != null)
            {
                PlayEffect(joltEffectPrefab, position, rotation, joltScaleMultiplier);
                return;
            }

            PlayEffect(defaultAreaBlastEffect, position, rotation, joltScaleMultiplier, joltFallbackColor);
        }

        // Burn+Chill reaction (see docs/elemental-reactions.md and
        // StatusEffectUtility.TryTriggerThermalShock) - a single-target burst at the struck enemy's
        // own position, no radius to scale by (unlike an AoE blast). Falls back to
        // defaultAreaBlastEffect tinted thermalShockFallbackColor at a fixed reference scale, same
        // dedicated-slot-with-tinted-fallback pattern every other reaction VFX here uses.
        private void OnThermalShockTriggered(EventThermalShockTriggered e)
        {
            Vector3 position = e.Position.ToUnityVector3();
            Vector3 scale = Vector3.one * thermalShockEffectScale;

            if (thermalShockEffectPrefab != null)
            {
                PlayEffect(thermalShockEffectPrefab, position, Quaternion.identity, scale);
                return;
            }

            PlayEffect(defaultAreaBlastEffect, position, Quaternion.identity, scale, thermalShockFallbackColor);
        }

        // Burn+Shock reaction (see docs/elemental-reactions.md and
        // StatusEffectUtility.TryTriggerOverload) - the flash where the chain originates, at the
        // entity that actually triggered the reaction. The chain's subsequent hops are each their own
        // OverloadChainLink event (see OnOverloadChainLink below), fired one every OverloadChainDelay
        // real seconds rather than all in the same frame.
        private void OnOverloadTriggered(EventOverloadTriggered e)
        {
            Vector3 position = e.OriginPosition.ToUnityVector3();
            Vector3 scale = Vector3.one * overloadEffectScale;

            if (overloadOriginParticlePrefab != null)
                PlayEffect(overloadOriginParticlePrefab, position, Quaternion.identity, scale);
            else
                PlayEffect(defaultAreaBlastEffect, position, Quaternion.identity, scale, overloadFallbackColor);

            if (overloadChainLinePrefab != null)
                BeginOverloadChainLine(e.Origin, position);
        }

        // One per chain hop (see StatusEffectUtility.TryAdvanceOverloadChain/
        // StatusEffectSystem.TickOverloadChain). If overloadChainLinePrefab is driving this chain's
        // visual (see BeginOverloadChainLine), the hop just appends its own point to that one
        // persistent line - no per-hop travel/growth, the line always directly connects every entity
        // the chain has hit so far. Otherwise falls back to stretching overloadTravelParticlePrefab from
        // e.From to e.To, or (neither authored) a plain point flash at e.To.
        private void OnOverloadChainLink(EventOverloadChainLink e)
        {
            Vector3 to = e.To.ToUnityVector3();

            if (overloadChainLinePrefab != null && _overloadChainLines.TryGetValue(e.Origin, out var chain))
            {
                AppendOverloadChainLink(chain, e.Target, to);

                if (overloadImpactParticlePrefab != null)
                    PlayEffect(overloadImpactParticlePrefab, to, Quaternion.identity, Vector3.one * overloadEffectScale);

                return;
            }

            Vector3 from = e.From.ToUnityVector3();

            if (overloadTravelParticlePrefab != null)
            {
                StartCoroutine(TravelOverloadSegment(e.Target, from, to, ResolveOverloadChainDelay(e.Game.Frames.Predicted)));
                return;
            }

            Vector3 delta = to - from;
            Quaternion rotation = delta.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(delta, Vector3.up)
                : Quaternion.identity;

            PlayEffect(defaultAreaBlastEffect, to, rotation, Vector3.one * overloadEffectScale, overloadFallbackColor);

            if (overloadImpactParticlePrefab != null)
                PlayEffect(overloadImpactParticlePrefab, to, Quaternion.identity, Vector3.one * overloadEffectScale);
        }

        // Real seconds between hops, read live off the SAME asset the simulation itself uses
        // (RuntimeConfig.ElementalReactionConfig.OverloadChainDelay) rather than a separately authored
        // view-side duration that could silently drift out of sync with it - a mismatch is exactly what
        // makes one hop's travel still animating when the next hop's damage has already landed and its
        // own link event has already fired. Only still needed by TravelOverloadSegment's own travel
        // duration (the persistent chain line has no travel/growth phase to time).
        private static float ResolveOverloadChainDelay(Frame frame)
        {
            float duration = 0.15f;

            if (frame != null && frame.RuntimeConfig.ElementalReactionConfig.IsValid == true)
            {
                ElementalReactionConfig config = frame.FindAsset(frame.RuntimeConfig.ElementalReactionConfig);
                if (config != null)
                    duration = config.OverloadChainDelay.AsFloat;
            }

            return duration;
        }

        // One persistent overloadChainLinePrefab instance per Overload chain (keyed by Origin, the
        // entity OverloadTriggered fired on) - one ANCHOR point per entity in hop order
        // (BeginOverloadChainLine seeds anchor 0 with the origin; AppendOverloadChainLink appends one
        // more per hop), with overloadChainLineJitterSegments jittered interior points inserted between
        // each consecutive anchor pair so the chain reads as a jagged bolt rather than dead-straight
        // segments (see UpdateOverloadChainLinePositions) - positionCount is anchors +
        // (anchors-1)*jitterSegments, never a travel/growth animation: a fresh hop's anchor (and its new
        // segment's interior points) appears immediately. RunOverloadChainLine below keeps every anchor
        // pinned to its owning entity's live position every frame, and re-randomizes the jitter itself
        // on a slower timer (overloadChainLineJitterRefreshInterval) for as long as the chain is still
        // hopping, then fades and destroys the line once OverloadChainLink stops arriving (see
        // overloadChainLineIdleTimeout) - there's no explicit "chain ended" event, since the sim itself
        // only knows a chain stopped by HopsRemaining silently reaching 0. The fade ONLY ever starts
        // after that idle timeout is reached - never while the chain is still actively hopping - so a
        // still-live chain never partially fades/flickers.
        private readonly Dictionary<EntityRef, OverloadChainLineState> _overloadChainLines = new Dictionary<EntityRef, OverloadChainLineState>();

        private class OverloadChainLineState
        {
            public LineRenderer Line;
            public Coroutine Coroutine;
            public readonly List<EntityRef> Visited = new List<EntityRef>();
            public readonly List<Vector3> LastKnownPositions = new List<Vector3>();

            // One random offset (-1..1, scaled by overloadChainLineJitter and a per-segment
            // perpendicular direction at rebuild time) per interior point across the WHOLE chain, laid
            // out hop-major (hop 0's segments, then hop 1's, ...) - see
            // UpdateOverloadChainLinePositions for how these turn into actual world positions.
            public readonly List<float> JitterOffsets = new List<float>();

            public float LastHopRealTime;
            public float JitterRefreshElapsed;
        }

        private void BeginOverloadChainLine(EntityRef origin, Vector3 originPosition)
        {
            // A fresh trigger can stomp an in-progress chain's own sim state on the same origin
            // (TryTriggerOverload unconditionally resets StatusEffects.OverloadChain* - see its own
            // comment) - if that just happened, the old line's coroutine has no way to know its chain
            // was cut short, so replace it outright rather than running two lines under one key.
            if (_overloadChainLines.TryGetValue(origin, out var stale))
            {
                StopCoroutine(stale.Coroutine);
                Destroy(stale.Line.gameObject);
                _overloadChainLines.Remove(origin);
            }

            LineRenderer line = Instantiate(overloadChainLinePrefab);
            line.positionCount = 1;
            line.SetPosition(0, originPosition);

            var state = new OverloadChainLineState { Line = line, LastHopRealTime = Time.time };
            state.Visited.Add(origin);
            state.LastKnownPositions.Add(originPosition);

            _overloadChainLines[origin] = state;
            state.Coroutine = StartCoroutine(RunOverloadChainLine(origin, state));
        }

        private void AppendOverloadChainLink(OverloadChainLineState state, EntityRef target, Vector3 initialPosition)
        {
            state.Visited.Add(target);
            state.LastKnownPositions.Add(initialPosition);

            int segments = Mathf.Max(0, overloadChainLineJitterSegments);
            for (int i = 0; i < segments; i++)
                state.JitterOffsets.Add(Random.Range(-1f, 1f));

            state.Line.positionCount = ResolveOverloadChainLinePositionCount(state.Visited.Count, segments);
            state.LastHopRealTime = Time.time;

            // Rebuild immediately rather than waiting for RunOverloadChainLine's next tick - positions
            // added by growing positionCount default to (0,0,0), which would otherwise flash the new
            // segment at the world origin for a frame.
            UpdateOverloadChainLinePositions(state, true, overloadChainLineJitter);
        }

        private static int ResolveOverloadChainLinePositionCount(int anchorCount, int jitterSegments)
        {
            return anchorCount + Mathf.Max(0, anchorCount - 1) * jitterSegments;
        }

        // Keeps every anchor of the chain's line pinned to its owning entity's live position (so the
        // whole chain visually follows if any of its enemies keep moving), until overloadChainLineIdleTimeout
        // real seconds pass with no new AppendOverloadChainLink call - at that point the chain is
        // considered finished, the line's ALPHA fades to 0 over overloadChainLineFadeDuration (still
        // live-tracking/crackling through the fade, so it doesn't freeze mid-bolt), then is destroyed.
        // Fading alpha (startColor/endColor), not widthMultiplier - a beam shrinking thinner reads as it
        // physically retracting/deflating, alpha dropping reads as it dissipating in place, which is
        // what a lightning chain winding down should look like. The fade block only ever runs AFTER the
        // idle-timeout while loop below exits - never interleaved with an active chain - so a chain
        // that's still genuinely hopping is never seen partially fading.
        private IEnumerator RunOverloadChainLine(EntityRef origin, OverloadChainLineState state)
        {
            float idleTimeout = Mathf.Max(0.05f, overloadChainLineIdleTimeout);
            float jitterInterval = Mathf.Max(0.01f, overloadChainLineJitterRefreshInterval);

            while (Time.time - state.LastHopRealTime < idleTimeout)
            {
                bool refreshJitter = TickJitterRefresh(state, jitterInterval);
                UpdateOverloadChainLinePositions(state, refreshJitter, overloadChainLineJitter);
                yield return null;
            }

            _overloadChainLines.Remove(origin);

            float fade = Mathf.Max(0f, overloadChainLineFadeDuration);
            if (fade > 0f)
            {
                // Captured once, faded down from whatever alpha the prefab was authored at - so a
                // prefab authored partially-transparent still fades to fully invisible rather than
                // snapping to some assumed starting alpha.
                Color startColor = state.Line.startColor;
                Color endColor = state.Line.endColor;
                float startAlpha = startColor.a;
                float endAlpha = endColor.a;
                float fadeElapsed = 0f;

                while (fadeElapsed < fade)
                {
                    fadeElapsed += Time.deltaTime;

                    bool refreshJitter = TickJitterRefresh(state, jitterInterval);
                    UpdateOverloadChainLinePositions(state, refreshJitter, overloadChainLineJitter);

                    float t = fadeElapsed / fade;
                    startColor.a = Mathf.Lerp(startAlpha, 0f, t);
                    endColor.a = Mathf.Lerp(endAlpha, 0f, t);
                    state.Line.startColor = startColor;
                    state.Line.endColor = endColor;
                    yield return null;
                }
            }

            Destroy(state.Line.gameObject);
        }

        private static bool TickJitterRefresh(OverloadChainLineState state, float jitterInterval)
        {
            state.JitterRefreshElapsed += Time.deltaTime;

            if (state.JitterRefreshElapsed < jitterInterval)
                return false;

            state.JitterRefreshElapsed = 0f;
            return true;
        }

        // Rebuilds every point of the chain's LineRenderer. Anchors (one per visited entity) always
        // re-resolve their LIVE position, every call - moving enemies must never lag. The jitter OFFSETS
        // (state.JitterOffsets) only get re-randomized when refreshJitter is true (see
        // overloadChainLineJitterRefreshInterval's own comment) - reusing the same offsets on the
        // frames in between is what makes this read as a crackling bolt instead of continuously
        // wiggling. Each interior point sits at its even fraction along the straight line between its
        // two anchors, then nudged by its own jitter offset along a perpendicular (Cross with
        // Vector3.up, i.e. sideways in the ground plane) to that segment's direction.
        private static void UpdateOverloadChainLinePositions(OverloadChainLineState state, bool refreshJitter, float jitter)
        {
            for (int i = 0; i < state.Visited.Count; i++)
                state.LastKnownPositions[i] = ResolveLiveTargetPosition(state.Visited[i], state.LastKnownPositions[i]);

            if (refreshJitter == true)
            {
                for (int i = 0; i < state.JitterOffsets.Count; i++)
                    state.JitterOffsets[i] = Random.Range(-1f, 1f);
            }

            int segments = state.Visited.Count > 1
                ? (state.JitterOffsets.Count / (state.Visited.Count - 1))
                : 0;

            int index = 0;
            state.Line.SetPosition(index++, state.LastKnownPositions[0]);

            for (int hop = 0; hop < state.Visited.Count - 1; hop++)
            {
                Vector3 from = state.LastKnownPositions[hop];
                Vector3 to = state.LastKnownPositions[hop + 1];
                Vector3 perpendicular = Vector3.Cross(to - from, Vector3.up).normalized;

                for (int s = 1; s <= segments; s++)
                {
                    float t = (float)s / (segments + 1);
                    float offset = state.JitterOffsets[hop * segments + (s - 1)];
                    Vector3 point = Vector3.Lerp(from, to, t) + perpendicular * offset * jitter;
                    state.Line.SetPosition(index++, point);
                }

                state.Line.SetPosition(index++, to);
            }
        }

        // Plays overloadTravelParticlePrefab as an actual traveling instance rather than a static
        // stretch or a one-shot burst of points - starts it looping at `from`, animates its transform
        // toward the target over `duration` real seconds, then stops emission (existing particles
        // still fade out naturally) and fires the optional impact particle once it has genuinely
        // arrived, before releasing the pooled instance back once it's fully finished.
        //
        // `target` is re-resolved to its LIVE center position every frame of the travel (falling back
        // to the static `to` snapshot only if the target no longer exists, e.g. it died mid-travel) -
        // animating toward a fixed snapshot instead would leave the spark arriving at wherever the
        // enemy USED to be the instant this hop's damage landed, visibly detached from the sprite by
        // the time it actually gets there if the enemy kept moving during the travel.
        private IEnumerator TravelOverloadSegment(EntityRef target, Vector3 from, Vector3 to, float duration)
        {
            ParticleSystem instance = GetPooledInstance(overloadTravelParticlePrefab, from, Quaternion.identity, Vector3.one, out ObjectPool<ParticleSystem> pool);
            if (instance == null)
                yield break;

            instance.Play(true);

            float elapsed = 0f;
            duration = Mathf.Max(0.01f, duration);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                instance.transform.position = Vector3.Lerp(from, ResolveLiveTargetPosition(target, to), Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            Vector3 finalPosition = ResolveLiveTargetPosition(target, to);
            instance.transform.position = finalPosition;
            instance.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            if (overloadImpactParticlePrefab != null)
                PlayEffect(overloadImpactParticlePrefab, finalPosition, Quaternion.identity, Vector3.one * overloadEffectScale);

            yield return ReleaseWhenFinished(instance, pool);
        }

        // QuantumRunner.Default, not an event's own e.Game - this runs across several Unity frames
        // inside a coroutine, well after whichever event originally triggered it has finished
        // dispatching, same live-read idiom TelegraphGrow.ResolveAnticipationMultiplier already uses
        // for the same reason. Falls back to the static snapshot if the runner/frame isn't available
        // or the target has since been destroyed (e.g. died mid-travel).
        private static Vector3 ResolveLiveTargetPosition(EntityRef target, Vector3 fallback)
        {
            if (target == EntityRef.None)
                return fallback;

            QuantumGame game = QuantumRunner.Default != null ? QuantumRunner.Default.Game : null;
            Frame frame = game?.Frames.Predicted;

            if (frame == null || frame.Exists(target) == false)
                return fallback;

            return EnemyMovementUtility.ResolveEntityCenter(frame, target).ToUnityVector3();
        }

        // Chill+Shock reaction (see docs/elemental-reactions.md and
        // StatusEffectUtility.TryTriggerShatter) - an AoE control burst at the reaction's center (the
        // strongly-staggered primary target, which never itself moves). e.Radius is the real
        // ShatterRadius, so the effect visually approximates the actual stagger-affected area - this
        // is purely the visual, the stagger itself already landed in simulation. Falls back to
        // defaultAreaBlastEffect tinted shatterFallbackColor, same dedicated-slot-with-tinted-fallback
        // pattern every other reaction VFX here uses.
        private void OnShatterTriggered(EventShatterTriggered e)
        {
            Vector3 position = e.Position.ToUnityVector3();
            Vector3 scale = Vector3.one * e.Radius.AsFloat;

            if (shatterEffectPrefab != null)
            {
                PlayEffect(shatterEffectPrefab, position, Quaternion.identity, scale);
                return;
            }

            PlayEffect(defaultAreaBlastEffect, position, Quaternion.identity, scale, shatterFallbackColor);
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

        // Generic - fires for every DamageUtility.ApplyDamage hit, both enemy- and player-dealt.
        // Distinguishes two cases by component presence (Players/Enemies split, same convention
        // MatchesTargetMask uses): WE hit an enemy (enemyHitEffectPrefab, or
        // enemyCriticalHitEffectPrefab on e.IsCritical), or an ENEMY hits us
        // (selfHitEffectPrefab - the generic "you got hit" spark that replaced the old per-delivery
        // EnemyDeliveryData.HitImpactPrefab path once EnemyDeliveryData.View.cs was removed).
        // Enemy-on-enemy and Silent (self-inflicted, e.g. SentryDecaySystem) damage trigger neither.
        //
        // Unlike the old HitEffectApplied-driven version, no MultiTarget exclusion is needed -
        // EntityDamaged already fires once per genuine damage instance (deduped via HitIndex, see
        // that field's own comment in Events.qtn), including once per enemy caught in an area blast
        // and once per damage-over-time tick, so every enemy an AoE catches gets its own hit spark
        // rather than the blast's own VFX standing in for all of them.
        private void OnEntityDamaged(EventEntityDamaged e)
        {
            if (e.Silent == true)
                return;

            // Non-Neutral Element marks a status/reaction damage instance (Burn/Poison DOT ticks,
            // Thermal Shock/Overload/Shatter procs - see StatusEffectSystem.TickBurn/
            // StatusEffectUtility and Element's own comment in Events.qtn), every one of which already
            // has its own dedicated VFX elsewhere in this file (or, for a bare Burn tick, none at all
            // by design). Playing the generic melee/bullet hit spark on top of those either doubles
            // the effect or, for Burn specifically, plays alone every tick and reads as an ongoing
            // direct attack rather than a status ticking.
            if (e.Element != ElementType.Neutral)
                return;

            Frame frame = e.Game.Frames.Predicted;
            if (frame == null) return;

            bool targetIsEnemy = frame.Has<Enemy>(e.Target);
            bool ownerIsEnemy = frame.Has<Enemy>(e.Owner);

            if (targetIsEnemy == true && ownerIsEnemy == false)
            {
                ParticleSystem prefab = e.IsCritical == true
                    ? enemyCriticalHitEffectPrefab != null ? enemyCriticalHitEffectPrefab : enemyHitEffectPrefab ?? defaultAreaBlastEffect
                    : enemyHitEffectPrefab ?? defaultAreaBlastEffect;
                float scale = e.IsCritical == true ? enemyCriticalHitEffectScale : enemyHitEffectScale;

                PlayEffect(prefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * scale);
                return;
            }

            if (targetIsEnemy == false && ownerIsEnemy == true)
            {
                // ResolveCenter, not raw e.Position - e.Position is Transform3D.Position at the
                // moment of the hit (see Events.qtn), which for a PLAYER target is mirrored straight
                // from KCC.Position (feet-level), unlike an enemy target where it's already the
                // collider center by this project's own convention - see ResolveCenter's own comment.
                ParticleSystem prefab = selfHitEffectPrefab ?? defaultAreaBlastEffect;
                Vector3 position = ResolveCenter(frame, e.Target, e.Position.ToUnityVector3());
                PlayEffect(prefab, position, Quaternion.identity, Vector3.one * selfHitEffectScale);
            }
        }

        // Generic - fires for every EntityHealed regardless of source (regen tick, PortableSpeakerSkillAction,
        // HealEffectData, ...), same reasoning OnEntityDamaged uses for the hit particles. Position is read
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

            PlayEffect(healGrantEffectPrefab, ResolveCenter(frame, e.Target, default), Quaternion.identity);
        }

        // Shield counterpart to OnEntityHealed - same shape, same no-fallback reasoning.
        private void OnEntityShielded(EventEntityShielded e)
        {
            if (shieldGrantEffectPrefab == null)
                return;

            Frame frame = e.Game.Frames.Predicted;
            if (frame == null || frame.Has<Transform3D>(e.Target) == false)
                return;

            PlayEffect(shieldGrantEffectPrefab, ResolveCenter(frame, e.Target, default), Quaternion.identity);
        }

        // Generic - fires for every PlayerRevived regardless of source (teammate hold, self-revive,
        // the auto-revive-on-secure sweep - see docs/revive.md), the same for every hero. Position
        // is read off the TARGET's own live Transform3D - the event carries no position of its own,
        // same shape as OnEntityHealed/OnEntityShielded.
        private void OnPlayerRevived(EventPlayerRevived e)
        {
            if (reviveEffectPrefab == null)
                return;

            Frame frame = e.Game.Frames.Predicted;
            if (frame == null || frame.Has<Transform3D>(e.Target) == false)
                return;

            PlayEffect(reviveEffectPrefab, ResolveCenter(frame, e.Target, default), Quaternion.identity, Vector3.one * reviveEffectScale);
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

            PlayEffect(shieldBreakEffectPrefab, ResolveCenter(frame, e.Target, default), Quaternion.identity);
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

        // Thin wrappers around GetHeldInstance/ReleaseHeldInstance bound to anticipationIconEffectPrefab -
        // EnemyAttackVisualsView spawns/releases this per Enemy.Phase edge (not a Quantum event, unlike
        // every PlayEffect handler above), so it calls these instead of holding its own prefab reference,
        // keeping the actual asset configured here with every other combat VFX. Returns null (silently
        // showing nothing) if the prefab is left unassigned - same degrade-gracefully contract every
        // other optional prefab field on this manager already has.
        public ParticleSystem GetAnticipationIconInstance()
        {
            return anticipationIconEffectPrefab != null ? GetHeldInstance(anticipationIconEffectPrefab) : null;
        }

        public void ReleaseAnticipationIconInstance(ParticleSystem instance)
        {
            if (instance != null)
                ReleaseHeldInstance(anticipationIconEffectPrefab, instance);
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

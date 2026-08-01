namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine;
    using UnityEngine.Serialization;

    // Drives EnemyTierResistanceConfig lookups (StatusEffectUtility.GetTierResistance) - how much
    // of an incoming stun/root/slow/burn/break/knockback actually lands scales with this,
    // not with any field on this asset itself.
    public enum EnemyTier
    {
        Filler,
        Normal,
        Specialist,
        Heavy,
        Elite,
        Boss
    }

    // Which of the three broad movement modes an enemy starts in - drives EnemySystem's
    // GravityScale toggle and the full-3D-vs-XZ-flattened movement branch (see
    // EnemyMovementUtility). Airborne is a genuinely new third state (e.g. launched/leaping but
    // not actually Flying) with no consumer yet - reserved for a future Delivery/interrupt
    // interaction, not read anywhere today.
    public enum EnemyHeightState
    {
        Grounded,
        Airborne,
        Flying
    }

    // Replaces the old EnemyMovementType+FlightHeight pair. Answers "is this enemy targetable by/
    // affected by X", not "can this enemy physically climb over that ledge" - the latter is a
    // separate, still-unbuilt shared traversal helper (see EnemyMovementUtility.CanCrossLedge),
    // since they're different axes: an enemy can be untargetable by melee while still being able
    // to cross every obstacle a Grounded enemy can.
    [Serializable]
    public struct EnemyHeightData
    {
        public EnemyHeightState InitialState;

        // Only meaningful while InitialState == Flying - how high above a chase/attack target
        // this enemy hovers (see EnemyMovementUtility.ResolveDestination).
        public FP FlightHeight;

        // Only meaningful while InitialState == Flying - see EnemyMovementUtility.
        // ComputeFlyingHoverVelocity. HoverCheckInterval: seconds between re-checking the ground
        // directly beneath this enemy, rather than every tick (cheap either way - a single
        // raycast - but re-checking mid-correction would just move the target height out from
        // under the spring below instead of letting it settle). HoverSpringFrequency/
        // HoverSpringDamping: how the enemy's actual height eases toward that target between
        // checks - same damped-harmonic-oscillator shape used throughout the View layer (e.g.
        // WeaponView's recoil), just here in FP for determinism. Low damping/frequency reads as
        // "floaty," overshooting and settling gently; high values read as rigid/locked-in.
        public FP HoverCheckInterval;
        public FP HoverSpringFrequency;
        public FP HoverSpringDamping;

        // Both checked together by EnemyMovementUtility.MoveInDirection (via CanCrossLedge) to
        // decide whether this enemy hops over a low obstacle blocking its path instead of walking
        // into it - mirrors the player's own auto-mantle
        // (PlayerMovementProcessor.TryDetectMantle/DoJump), just applied directly to
        // PhysicsBody3D.Velocity.Y since enemies integrate through PhysicsSystem3D, not KCC.
        public bool CanJump;

        public bool CanFly;
        public bool CanBeLaunched;
        public bool CanBeGrounded;

        // Whether this enemy can cross a ledge/obstacle the way the player can - see
        // EnemyMovementUtility.CanCrossLedge, which mirrors the player's own mantle geometry check.
        // Checked alongside CanJump (both must be true) - kept as two separate flags since a future
        // non-jumping traversal method (e.g. phasing through, for a ghost-type enemy) could set
        // this without CanJump.
        public bool CanCrossObstacles;

        // Ankle-height-blocked + ledge-height-clear geometry probe, same three tunables as
        // MovementDataAsset's own mantle fields (AnkleProbeHeight/MaxLedgeHeight/
        // MantleProbeDistance) - only meaningful while CanJump && CanCrossObstacles.
        public FP AnkleProbeHeight;
        public FP MaxLedgeHeight;
        public FP MantleProbeDistance;

        // Upward velocity applied the instant a climbable obstacle is detected - only meaningful
        // while CanJump && CanCrossObstacles. Gravity (already active on a Grounded enemy) arcs it
        // back down on its own; no separate jump duration/height field needed.
        public FP JumpVelocity;

        public bool AffectedByGroundHazards;
        public bool AffectedByShockwaves;
        public bool TargetableByMelee;
        public bool TargetableByProjectiles;

        // Only checked while InitialState == Grounded (Flying/Airborne enemies have nothing
        // beneath them to check). True makes EnemyMovementUtility.MoveInDirection refuse to step
        // toward a direction with no ground ahead (see HasGroundAhead) instead of walking off the
        // edge - same probe shape as the player's own auto-hop trigger
        // (PlayerMovementProcessor.HasGroundAhead/MovementDataAsset), just without the jump: this
        // enemy simply stops at the edge rather than hopping across the gap.
        public bool AvoidLedges;
        public FP EdgeProbeDistance;
        public FP EdgeCheckDistance;
    }

    // Flat flags for cross-cutting behavior that doesn't warrant its own polymorphic profile -
    // each is checked directly wherever the matching mechanic already lives (e.g.
    // KnockbackResistance in DamageUtility.ResolveKnockbackScale) rather than through any
    // indirection here. No Flying value - that's EnemyHeightData.InitialState, and keeping it out
    // here avoids two fields ever disagreeing about whether an enemy flies.
    public enum EnemyTrait
    {
        ContactDamage,
        GroundHazardImmunity,
        KnockbackResistance,
        LaunchResistance,
        FrontalDamageReduction
    }

    // Director-facing spend/refund/placement data - see EnemyLifecycleSystem, CombatDirectorUtility,
    // GroupSpawnerUtility.
    [Serializable]
    public struct EnemyEconomyData
    {
        // Always counted Relevant (see EnemyLifecycleSystem) regardless of distance/combat state -
        // so this enemy never auto-retires. Independent of EnemyTier.Elite, which is also always
        // Relevant but for a different reason (a notable fight, not "must never leave").
        public bool Persistent;

        // Domain 3 (Group Spawner) placement rules for this enemy type - see GroupSpawnerUtility
        // and EnemySpawnProfile's own comment. Left unset here (no sensible universal default,
        // same reasoning as Movement/Targeting below) - GroupSpawnerUtility logs an error and fails
        // that member's placement if a Director-spawned enemy has no profile assigned.
        [ExpandableAsset] public AssetRef<EnemySpawnProfile> SpawnProfile;
    }

    // Core body/combat stats - movement speed/traversal, physical size, health, shield, and
    // passive defense all live here as one group since together they answer a single question,
    // "how tough/fast/mobile is this enemy," rather than four separately-toggled concerns.
    [Serializable]
    public struct EnemyStatsData
    {
        public FP MoveSpeed;

        // Which of the three broad movement modes this enemy starts in, plus every
        // traversal/height-gated tunable (flying hover, ledge-jump/mantle, ledge-avoidance) - see
        // EnemyHeightData's own comment. Grouped as its own nested struct rather than flattened
        // directly into EnemyStatsData since it's already a coherent, self-documented unit
        // (EnemyMovementUtility reads it as one block via data.Stats.Height).
        public EnemyHeightData Height;

        // How this enemy decides which direction to move - see EnemyMovementData. Left unset here
        // (no sensible universal default); every enemy asset must wire it explicitly.
        [ExpandableAsset] public AssetRef<EnemyMovementData> Movement;

        // Overrides the generic enemy prototype's PhysicsCollider3D sphere radius on spawn (see
        // EnemySystem.SeedRadius), then further scaled by this enemy's EnemyTierStatsConfig.
        // ScaleMultiplier - lets one shared generic prototype serve every enemy type/size instead
        // of each needing its own hand-authored collider, while tougher tiers still read as
        // visibly bigger by default. Also drives the spawned sprite's fit scale (EnemyView) so the
        // visual matches the actual collision size.
        public FP Radius;

        // Multiplies this tier's EnemyTierStatsConfig.Shield baseline (EnemySystem.SeedShield),
        // the same way EnemyTierStatsConfig.ScaleMultiplier multiplies Radius above - the tier sets
        // the baseline shield amount, this just scales it per enemy. A Shield component is added
        // dynamically only if the resulting amount is greater than 0, so 0 (the default) opts an
        // enemy out entirely; unlike the player's own Shield (which requires one already authored
        // on the hero's prefab, see CharacterSystem.SeedShield), no per-prefab authoring is needed
        // for a shielded enemy variant. Recharge delay/rate are not authored here at all - purely
        // tier-driven, see EnemyTierStatsConfig.ShieldRechargeDelay/Rate.
        public FP ShieldMultiplier;

        // Inert today - no consumer reads this yet. Each trait gets wired into its own mechanic as
        // that mechanic is touched (e.g. KnockbackResistance into DamageUtility.ResolveKnockbackScale).
        public EnemyTrait[] Traits;

        // Only meaningful with Traits containing FrontalDamageReduction - see
        // DamageUtility.ResolveFrontalDamageMultiplier. Amount uses the same 0-1 convention as
        // CharacterStats.DamageReduction; ArcDegrees is the full angular width (not half-width)
        // centered on this enemy's current facing (Aim.Angle).
        public FP FrontalDamageReductionAmount;
        public FP FrontalDamageReductionArcDegrees;

        // Steers this enemy's chosen move direction away from a wall directly ahead (see
        // EnemyMovementUtility.SteerAroundWalls) before PhysicsSystem3D ever has to resolve the
        // collision, instead of it pushing straight into the wall and stalling/juddering
        // against it every tick. Independent of Height.AvoidLedges (a vertical "don't walk off
        // the edge" check) and orthogonal to InitialState - a Flying enemy at head height wants
        // this just as much as a Grounded one. Off by default, same reasoning as every other
        // opt-in traversal flag here - only enemies actually navigating tight corridors need it.
        public bool AvoidWalls;
        public FP WallAvoidProbeDistance;

        // If the direct line to the target is wall-blocked (a corridor turn, not just a corner
        // post AvoidWalls' own local steering can already route around), routes through this
        // enemy's current chunk's baked waypoint graph instead of walking straight into the wall -
        // see EnemyPathfindingUtility. Overrides whatever Stats.Movement would have computed only
        // while blocked; the instant line-of-sight comes back (checked fresh every tick, not just
        // once) the detour is dropped and normal movement resumes, even mid-detour. Flipping this
        // on is the only authoring needed - EnemySystem.SeedWaypointPath dynamically adds the
        // EnemyWaypointPath component this needs to cache the detour between ticks, the same way
        // SeedShield adds Shield only for enemies that actually have one. Falls back to
        // Stats.Movement's own direction (walking straight at the wall, today's behavior) if no
        // chunk/route can be found for a detour.
        public bool UseWaypointDetour;
        public FP WaypointArrivalDistance;

        public readonly bool HasTrait(EnemyTrait trait) => Array.IndexOf(Traits, trait) >= 0;
    }

    // Aggro/targeting tuning - see EnemySystem.ResolveInitialTarget and EnemyTargetingData.
    [Serializable]
    public struct EnemyAIData
    {
        // How this enemy decides who it targets - see EnemyTargetingData. Left unset here (no
        // sensible universal default); every enemy asset must wire this explicitly.
        [ExpandableAsset] public AssetRef<EnemyTargetingData> Targeting;

        public FP DetectionRange;
        public FP LeashRange;
    }

    // The action(s) this enemy can perform - see EnemyDecisionUtility.
    [Serializable]
    public struct EnemyActionsData
    {
        // Every enemy has this - its always-available default action. No sensible universal
        // default; every enemy asset must wire it explicitly. FormerlySerializedAs preserves any
        // pre-multi-action asset's old "Attack" reference across that earlier rename.
        [FormerlySerializedAs("Attack")]
        [ExpandableAsset] public AssetRef<EnemyActionData> BasicAction;

        // Optional additional actions beyond BasicAction, chosen between via
        // EnemyDecisionUtility.TrySelectAction - empty for a simple single-action enemy (the
        // common case). Capped at 7 (EnemyActionSlots.SkillCooldowns' fixed size) - raise that
        // array's size if a design genuinely needs more concurrent skills than that. An enemy
        // using this must also carry the optional EnemyActionSlots component on its prototype (see
        // that component's own comment for why it's separate from Enemy itself).
        [ExpandableAsset] public List<AssetRef<EnemyActionData>> SkillActions;
    }

    public partial class EnemyDataAsset : AssetObject
    {
        public string EnemyName;
        public EnemyTier Tier = EnemyTier.Filler;

        public EnemyEconomyData Economy = new EnemyEconomyData();

        public EnemyStatsData Stats = new EnemyStatsData
        {
            MoveSpeed = 3,
            Radius = 1,
            Traits = new EnemyTrait[0],
            FrontalDamageReductionAmount = FP._0_50,
            FrontalDamageReductionArcDegrees = 120,
            WallAvoidProbeDistance = 1,
            WaypointArrivalDistance = 1,
            Height = new EnemyHeightData
            {
                InitialState = EnemyHeightState.Grounded,
                FlightHeight = 2,
                HoverCheckInterval = 1,
                HoverSpringFrequency = 1,
                HoverSpringDamping = FP._0_50,
                CanBeGrounded = true,
                TargetableByMelee = true,
                TargetableByProjectiles = true,
                AnkleProbeHeight = FP._0_25,
                MaxLedgeHeight = 1,
                MantleProbeDistance = FP._0_75,
                JumpVelocity = 8,
                AvoidLedges = true,
                EdgeProbeDistance = FP._0_50,
                EdgeCheckDistance = 1,
            },
        };

        public EnemyAIData AI = new EnemyAIData { DetectionRange = 10, LeashRange = 15 };

        public EnemyActionsData Actions = new EnemyActionsData { SkillActions = new() };

        public FP DeathLingerTime = 3;

        // --- MIGRATION BRIDGE - do not author against these ---
        // Unchanged old top-level fields (MoveSpeed/Radius/MaxHealth/Height/Movement were flat
        // before too, never grouped until now - bridged the same way as everything else here since
        // Stats folds them all in for the first time), kept only so existing .asset instances still
        // deserialize their pre-reorg values. Run "Tools > Quantum > Migrate EnemyDataAsset Fields"
        // once in the Editor to copy these into the grouped fields above, then delete this whole
        // block and EnemyDataAssetMigration.cs (see docs in that file).
        [HideInInspector] public FP Cost = 1;
        [HideInInspector] public bool Persistent;
        [HideInInspector] public AssetRef<EnemySpawnProfile> SpawnProfile;
        [HideInInspector] public FP MoveSpeed = 3;
        [HideInInspector] public FP Radius = 1;
        [HideInInspector] public FP MaxHealth = 50;
        [HideInInspector] public FP MaxShield;
        [HideInInspector] public FP ShieldRechargeDelay = 2;
        [HideInInspector] public FP ShieldRechargeRate = 5;
        [HideInInspector] public EnemyTrait[] Traits = new EnemyTrait[0];
        [HideInInspector] public FP FrontalDamageReductionAmount = FP._0_50;
        [HideInInspector] public FP FrontalDamageReductionArcDegrees = 120;
        [HideInInspector] public EnemyHeightData Height = new EnemyHeightData { InitialState = EnemyHeightState.Grounded };
        [HideInInspector] public AssetRef<EnemyMovementData> Movement;
        [HideInInspector] public AssetRef<EnemyTargetingData> Targeting;
        [HideInInspector] public FP DetectionRange = 10;
        [HideInInspector] public FP LeashRange = 15;
        [HideInInspector] public bool CanBeInterruptedByKnockback = true;
        [HideInInspector] public FP KnockbackRecoveryTime = FP._0_25;
        [FormerlySerializedAs("Attack"), HideInInspector] public AssetRef<EnemyActionData> BasicAction;
        [HideInInspector] public List<AssetRef<EnemyActionData>> SkillActions = new();
    }
}

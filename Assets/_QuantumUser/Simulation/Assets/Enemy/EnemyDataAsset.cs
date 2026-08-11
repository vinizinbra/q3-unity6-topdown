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
    // separate traversal question (see EnemyMovementUtility.TryFindClimbLanding), since they're
    // different axes: an enemy can be untargetable by melee while still being able to cross every
    // obstacle a Grounded enemy can.
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

        public bool CanFly;
        public bool CanBeLaunched;
        public bool CanBeGrounded;

        // Extra clearance added on top of this enemy's own collider radius (EnemyMovementUtility.
        // ResolveEntityRadius) for the climb probe (TryFindClimbLanding) specifically - cast from
        // this enemy's own center pivot, so without this a wide enemy's own body could still be
        // overlapping the ledge at the sampled point even though the ray from its center cleared it.
        // Split from the old single ProbeThreshold (FormerlySerializedAs preserves every existing
        // asset's tuned value here) since climb and gap/fall are different-enough geometry questions
        // to want independent margins - a ledge worth climbing and a gap worth jumping aren't
        // necessarily found at the same clearance distance.
        [FormerlySerializedAs("ProbeThreshold")]
        public FP ClimbProbeThreshold;

        // Same idea as ClimbProbeThreshold, for the "no ground ahead" family instead (HasGroundAhead/
        // TryFindGapLanding/HasGroundWithinFallDistance - CanJumpGaps and CanFallFromCliff both
        // branch off the same edge check). Not migrated from the old ProbeThreshold value (Unity's
        // FormerlySerializedAs only ever targets one field) - re-author this on any enemy that had a
        // hand-tuned ProbeThreshold and also uses CanJumpGaps/CanFallFromCliff, or it reverts to this
        // field's own default.
        public FP GapProbeThreshold;

        // Peak height of the visual arc bump a traversal hop (CanClimbCliffs/CanJumpGaps) adds on
        // top of its own straight lerp from takeoff to landing - see EnemyMovementUtility.
        // TickTraversalJump. Purely cosmetic: the lerp itself already carries the entity to the
        // exact landing point regardless of this value, so raising/lowering it only changes how
        // high the hop looks, never where or when it lands.
        public FP ArcHeight;

        // Blanket speed scale for a climb/gap traversal hop (EnemyMovementUtility.
        // BeginTraversalJump), applied on top of whatever distance/speed-derived pace it would
        // otherwise pick - crank it up for a snappier-feeling hop, down for a slower, more
        // deliberate one, independent of this enemy's own walking MoveSpeed. Unlike ArcHeight above,
        // this DOES change timing (a shorter TraversalJumpDuration), not just how it looks. <= 0
        // (every pre-existing asset authored before this field existed included) reads as 1 - no
        // change - same "unset multiplier defaults safely" convention Projectile.qtn's own
        // MaxDistanceMultiplier already uses, so nothing already in the game silently speeds up or
        // stalls the instant this field exists.
        public FP TraversalJumpSpeedMultiplier;

        // Whether this enemy can climb a blocking obstacle up to CliffHeight tall instead of
        // walking into it - see EnemyMovementUtility.TryFindClimbLanding, which mirrors the
        // player's own mantle geometry check (ankle-height blocked + CliffHeight-high clear). No
        // separate jump-VELOCITY field the way the old CanJump/JumpVelocity pair this replaces had:
        // EnemyMovementUtility.BeginTraversalJump is a kinematic hop straight onto the landing point
        // TryFindClimbLanding found, not a physics launch, so any authored CliffHeight is always
        // reachable, never just attempted - TraversalJumpSpeedMultiplier above only paces how fast
        // it gets there, never whether it does.
        public bool CanClimbCliffs;
        public FP CliffHeight;

        public bool AffectedByGroundHazards;
        public bool AffectedByShockwaves;
        public bool TargetableByMelee;
        public bool TargetableByProjectiles;

        // Only checked while InitialState == Grounded (Flying/Airborne enemies have nothing
        // beneath them to check). When EnemyMovementUtility.HasGroundAhead finds no ground under
        // this enemy's next step, MoveInDirection tries, in order: jump the gap (CanJumpGaps, up to
        // GapDistance - same kinematic-hop reachability guarantee as CliffHeight above) or fall down
        // it (CanFallFromCliff, up to FallHeight - a plain, unboosted step off the edge under normal
        // gravity, not a hop). If neither applies, it stops rather than walking into open air - the
        // old AvoidLedges flag this replaces is gone because that's now simply what happens when
        // both of these are off.
        public bool CanJumpGaps;
        public FP GapDistance;

        public bool CanFallFromCliff;
        public FP FallHeight;
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

        // Multiplies EnemyTierStatsConfig.Resolve(f, Tier).Cost (see EnemyDataAsset.ResolveCost) -
        // Cost itself stays purely tier-driven (docs/survival-director.md), this just lets one
        // archetype within a tier spend more/less Director budget than its tier's baseline without
        // needing its own tier. 1 (default) leaves tier cost unchanged, same "unset multiplier
        // defaults safely" convention EnemyHeightData.TraversalJumpSpeedMultiplier already uses.
        public FP CostMultiplier;
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
        // against it every tick. Independent of Height's own gap/cliff handling (a vertical "what
        // happens at the edge" question) and orthogonal to InitialState - a Flying enemy at head
        // height wants this just as much as a Grounded one. Off by default, same reasoning as every
        // other opt-in traversal flag here - only enemies actually navigating tight corridors need it.
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

        public EnemyEconomyData Economy = new EnemyEconomyData { CostMultiplier = FP._1 };

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
                ClimbProbeThreshold = FP._0_50,
                GapProbeThreshold = FP._0_50,
                ArcHeight = FP._0_50,
                TraversalJumpSpeedMultiplier = FP._1,
                TargetableByMelee = true,
                TargetableByProjectiles = true,
            },
        };

        public EnemyAIData AI = new EnemyAIData { DetectionRange = 10, LeashRange = 15 };

        public EnemyActionsData Actions = new EnemyActionsData { SkillActions = new() };

        public FP DeathLingerTime = 3;

        // Single place every Director budget site (EnemyGroupConfig.ComputeCost,
        // CombatDirectorUtility.ComputeRelevantPressure, EnemyLifecycleSystem.Retire) reads this
        // enemy's actual cost from, so Economy.CostMultiplier can't drift out of sync between them.
        public FP ResolveCost(Frame f) => EnemyTierStatsConfig.Resolve(f, Tier).Cost * Economy.CostMultiplier;

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

namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Ordered least to most tracking: UpdateTargetDirectionWhileActive implies tracking through
    // Preparation/Telegraph too (no action in this codebase tracks while Active but not before).
    public enum DirectionUpdateMode { DoNotUpdateTargetDirection, UpdateTargetDirectionOnAnticipation, UpdateTargetDirectionWhileActive }

    // Gate beyond "off cooldown" for an action to become eligible - Cooldown is the implicit
    // default every action already has via CooldownTime; the others layer an additional condition
    // on top. Read by the decision/selection logic once an enemy can have more than one action -
    // inert today (single-action model).
    public enum EnemyTriggerType { Cooldown, OnDeath, OnProximity, OnHealthThreshold }

    public struct EnemyTriggerData
    {
        public EnemyTriggerType Type;
        public FP Radius;        // OnProximity
        public FP HealthPercent; // OnHealthThreshold
    }

    // When Enemy.SkillTargetPosition/Aim stops re-tracking the target during the windup - only
    // consulted while action.DirectionTracking allows tracking at all (DoNotUpdateTargetDirection
    // already means "never track", so it short-circuits before this is even read - see
    // EnemyDeliveryData.OnAnticipating). Explicit ordinals: LocksAtTelegraphEnd = 0 so any
    // EnemyActionData authored before this field was wired up (it used to be a dead flag - every
    // asset defaults to whatever ordinal 0 is) keeps behaving exactly as it already did - tracking
    // the whole windup - unless someone deliberately picks a different value.
    public enum EnemyAimLockTiming
    {
        LocksAtTelegraphEnd = 0,      // tracks through the whole windup, freezing the instant Begin() is about to fire
        LocksAtTelegraphStart = 1,    // tracks during Preparation, freezes the instant the windup crosses into Telegraph
        LocksAtAnticipationStart = 2, // frozen from tick one - same effect as DirectionTracking.DoNotUpdateTargetDirection, expressed here instead
        LocksAtPercent = 3,           // freezes once elapsed windup crosses EnemyActionData.AimLockPercent - independent of TelegraphStartPercent/Phase
    }

    // Where an area effect (GroundAreaDeliveryData) and its paired Circle/Cone telegraph are both
    // centered - a single shared choice so the visual decal and the actual hit area can never
    // silently disagree about where the attack lands (previously GroundAreaDeliveryData had its
    // own Origin field that only the delivery itself read - EnemyAttackVisualsView.
    // ComputeTelegraphPose's Circle branch had no way to know about it, so the telegraph always
    // showed on the target even when the delivery was actually centered on the enemy). TargetAnchor:
    // the locked Enemy.SkillTargetPosition - "detonate at a point." Self: the enemy's own live
    // position - e.g. a creeper-style suicide exploder (see GroundAreaDeliveryData.SelfDestructs),
    // or any ConeShaped delivery (Cone only has a sensible pointing direction when centered on the
    // enemy, not on the point it's already centered on).
    public enum EnemyActionOrigin { TargetAnchor, Self }

    // One enemy action's shared tuning + composition refs. No longer itself polymorphic - that
    // execution logic moved to EnemyDeliveryData (see Delivery/) so the same tuning
    // (DamageRange/Damage/...) can pair with any delivery type without re-authoring it
    // per subclass. View-only fields live in the companion EnemyActionData.View.cs partial. An
    // EnemyDataAsset points at one or more of these (see EnemyDataAsset.BasicAction/SkillActions);
    // each one points at exactly one EnemyDeliveryData that owns the actual Begin/Tick logic.
    public unsafe partial class EnemyActionData : AssetObject
    {
        [FoldoutGroup("Base")]
        public string Name;

        // Usually equals DamageRange; set larger for deliveries that need room to build up before
        // connecting (e.g. Charge).
        [FoldoutGroup("Base")]
        public FP EngageRange = 2;

        // For an action whose EnemyActionData.View.cs Telegraph is a Circle/Cone, this drives the
        // decal's actual radius too (damageRange * TelegraphData.RadiusMultiplier, resolved in
        // EnemyAttackVisualsView.ComputeTelegraphPose) - so the decal shown to the player can never
        // silently drift out of sync with the real hit area the way an independently authored
        // telegraph radius could.
        [FoldoutGroup("Base")]
        public FP DamageRange = 2;

        [FoldoutGroup("Base")]
        public FP Damage = 10;

        // Time facing the target before Begin() is called.
        [FoldoutGroup("Base")]
        public FP AnticipationTime = FP._0_50;

        // How long the windup keeps re-aiming at the target's live position before committing -
        // see DirectionUpdateMode. Charge/Leap-style deliveries expect this authored as
        // DoNotUpdateTargetDirection on the action asset (previously a constructor default when
        // those were AttackData subclasses directly - now just an authoring choice, since
        // EnemyDeliveryData subclasses no longer inherit these shared fields to default).
        [FoldoutGroup("Base")]
        public DirectionUpdateMode DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionWhileActive;

        // True: captured target/anchor points use the enemy's own ground Y instead of the target's
        // raw Y. Set false only for a Flying enemy that should track height.
        [FoldoutGroup("Base")]
        public bool IgnoreY = true;

        // See EnemyActionOrigin's own comment. Only consumed by GroundAreaDeliveryData and its
        // paired Circle/Cone telegraph - harmless/unused for every other delivery type.
        [FoldoutGroup("Base")]
        public EnemyActionOrigin Origin = EnemyActionOrigin.TargetAnchor;

        // Drives EnemyActionPhase.Recovery's StateTimer.
        [FoldoutGroup("Base")]
        public FP DownTime = 1;

        // Tracked on Enemy.AttackCooldownRemaining.
        [FoldoutGroup("Base")]
        public FP CooldownTime = 1;

        // Percent (0-1) through AnticipationTime at which the windup becomes Telegraph (visible/
        // committed) rather than Preparation - see EnemyActionPhase. Halfway by default; set to 1
        // to opt out (Telegraph never triggers, matching this system's pre-Stage-6 behavior).
        [FoldoutGroup("Base")]
        public FP TelegraphStartPercent = FP._0_50;

        // Gate beyond "off cooldown" for choosing this action - inert until multi-action selection
        // exists (see EnemyTriggerType).
        [FoldoutGroup("Base")]
        public EnemyTriggerData Trigger;

        [FoldoutGroup("Base")]
        public EnemyAimLockTiming AimLock = EnemyAimLockTiming.LocksAtTelegraphEnd;

        // Only consulted when AimLock == LocksAtPercent: fraction (0-1) of AnticipationTime spent
        // still tracking the target's live position before freezing for the rest of the windup -
        // an arbitrary cutoff independent of TelegraphStartPercent/Phase, for an action that wants
        // e.g. "keep tracking for the first 30% of the windup, then commit" without that also
        // being when Telegraph shows.
        [FoldoutGroup("Base")]
        public FP AimLockPercent = FP._1;

        // Feeds the BaseWeight term of the multi-action decision scorer - inert until that exists.
        [FoldoutGroup("Base")]
        public int SelectionWeight = 1;

        // Whether a knockback interrupt (see EnemySystem.OnEnemyKnockedBack) cancels this specific
        // action while it's mid-windup vs. mid-delivery - independent of
        // EnemyDataAsset.CanBeInterruptedByKnockback, which gates whether a knockback affects this
        // enemy AT ALL (its physical resilience); these gate whether landing one ALSO throws away
        // whatever this action was doing (per-action, since a heavy slam and a quick jab might
        // reasonably differ). Telegraph defaults true (matches this system's original blanket
        // behavior); Active defaults false - a committed delivery normally sees itself through, and
        // today's kinematic deliveries (Charge/Leap) can't receive a real impulse while Active
        // anyway (see DamageUtility.ApplyResolvedImpulse), so this only matters for a future
        // non-kinematic multi-tick delivery.
        [FoldoutGroup("Base")]
        public bool InterruptibleDuringTelegraph = true;
        [FoldoutGroup("Base")]
        public bool InterruptibleDuringActive;

        [ExpandableAsset] public AssetRef<EnemyDeliveryData> Delivery;

        // Applied via HitEffectUtility.ApplyToTarget by this action's EnemyDeliveryData, in place
        // of calling DamageUtility.ApplyDamage/ApplyKnockback directly - the same shared Hit Effect
        // system weapon perks/projectiles already use, so enemy hits can proc Burn/Void/Slow/
        // Stun/shield-grant status the same way those do. A DamageEffectData (DamageMultiplier
        // = 1) + KnockbackEffectData (picking whichever KnockbackTier fits this action) pair
        // reproduces a flat damage/knockback hit exactly - author both onto every action that
        // deals damage/knockback.
        [ExpandableAsset] public List<AssetRef<HitEffectData>> Effects = new();
    }
}

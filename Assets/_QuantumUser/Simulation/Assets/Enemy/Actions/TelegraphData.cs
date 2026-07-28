namespace Quantum
{
    using UnityEngine;

    // Full authoring vocabulary for what a telegraph can be. Circle, Cone, ChargeLane, and
    // Rectangle render today (see EnemyAttackVisualsView.SpawnTelegraph) - Cone reuses Circle's
    // exact positioning math (a filled-sector sprite, not procedural geometry) anchored at the
    // enemy instead of the target; Rectangle reuses ChargeLane's exact box math, just different
    // naming intent. The remaining seven (AimLine, LandingMarker, ProjectilePath, HeightShadow,
    // EnemyPose, SoundCue, CountdownFill) are declared so an EnemyActionData can be authored
    // against the full intended schema, but picking one silently does nothing until its own
    // rendering path is built - AimLine/LandingMarker/ProjectilePath/HeightShadow would need new
    // decal geometry/tracking logic in SpawnTelegraph; EnemyPose/SoundCue/CountdownFill don't even
    // fit that decal-prefab pipeline at all (EnemyPose's intent is already covered by
    // AttackVisualStep's body animation; SoundCue/CountdownFill would need their own audio/UI
    // presentation paths, not a spawned GameObject).
    public enum TelegraphShape
    {
        Circle,
        Cone,
        Rectangle,
        AimLine,
        LandingMarker,
        ChargeLane,
        ProjectilePath,
        EnemyPose,
        SoundCue,
        HeightShadow,
        CountdownFill
    }

    public enum TelegraphLineLength { ToTarget, FixedDistance }

    // Ground indicator (e.g. a decal) shown for the span between two phase edges - a charge-up
    // warning line for Charge, a landing-zone circle for Leap, etc. Its own reusable Quantum asset
    // (referenced via AssetRef<TelegraphData> from EnemyActionData.View.cs), the same "shared,
    // pick one instance for many actions" shape as HitEffectData/EnemyDeliveryData, rather than a
    // value inlined per-action - so a common telegraph style can be authored once and reused.
    // Sizing is tuned directly here rather than derived from delivery-specific fields, so this
    // stays usable by any EnemyActionData. Conditional field display lives in TelegraphDataEditor
    // (Assets/_QuantumUser/Editor/).
    public class TelegraphData : AssetObject
    {
        public TelegraphShape Shape = TelegraphShape.Circle;
        public AttackPhase StartPhase = AttackPhase.Anticipation;
        public AttackPhase EndPhase = AttackPhase.Begin;

        [Tooltip("Recompute position/rotation/scale every frame while active instead of a single snapshot at spawn - for a telegraph that should visibly track a moving target during the windup (e.g. a sniper's laser sight). Only as live as the anchor it reads actually is: Enemy.SkillTargetPosition itself stops updating once the delivery's own EnemyAimLockTiming locks it, so this naturally freezes at that same point with no extra configuration needed here.")]
        public bool LiveTracking = false;

        [Tooltip("LiveTracking only - how quickly the telegraph eases toward its newly computed pose each frame, instead of snapping straight to it. The anchor it reads only updates once per simulation tick (not per render frame), so snapping directly to it reads as jittery/stepped at typical render framerates - this smooths that out the same way EnemyArmAimView smooths its own continuous aim. Higher = snappier/more jitter, lower = smoother/more lag.")]
        public float LiveTrackingSmoothing = 15f;

        [Tooltip("Ground indicator prefab - a SpriteRenderer facing the camera by default (like any 2D sprite); EnemyAttackVisualsView reorients the spawned instance to lie flat on the ground automatically, you don't need to pre-rotate it. Assumed unit-sized (1x1 at scale 1) - scaled by From/To/Width or RadiusMultiplier below. Leave empty for no telegraph.")]
        public GameObject TelegraphPrefab;

        [Tooltip("True (default): a floor decal - anchor points project onto the ground beneath the enemy/target (EnemyDataAsset.Height.FlightHeight subtracted for a Flying enemy) and the final position snaps to the real Unity ground collider. False: floats at the enemy's own actual height instead - for an attack that happens at altitude rather than on the ground (e.g. a flying charge/dash or a flying sniper's shot), so the telegraph shows where the attack actually passes through instead of projecting a misleading line onto the floor below it.")]
        public bool SnapToGround = true;

        // ChargeLane-only: direction always points from the enemy toward the resolved anchor
        // point. ToTarget shortens as the target gets closer; FixedDistance always reaches
        // FixedDistanceValue regardless, for showing a delivery's full potential reach (e.g.
        // matching ChargeDeliveryData.DashDistance). FromOffset/ToOffset nudge either endpoint
        // along that direction afterward either way.
        public TelegraphLineLength LineLength = TelegraphLineLength.ToTarget;
        public float FixedDistanceValue = 5f;
        [Tooltip("Moves the line's start point along the direction, away from the enemy (positive) or into the enemy (negative).")]
        public float FromOffset = 0f;
        [Tooltip("Moves the line's end point along the direction, past the computed end point (positive) or short of it (negative).")]
        public float ToOffset = 0f;
        public float Width = 1f;

        // Circle/Cone-only. The actual radius is always derived from the paired EnemyActionData's
        // DamageRange (see that field's own comment) rather than authored independently here - a
        // telegraph can never silently drift out of sync with the real hit area this way.
        [Tooltip("Circle/Cone radius = the paired EnemyActionData.DamageRange * this multiplier. 1 = decal exactly matches the real hit area (the common case). Raise/lower only when the telegraph should deliberately read as larger/smaller than the actual range (e.g. drawn bigger for readability).")]
        public float RadiusMultiplier = 1f;

        [Tooltip("Fallback only, rarely needed: seconds for a TelegraphPrefab-authored TelegraphGrow child (see that class) to grow from 0 up to its resting scale. EnemyAttackVisualsView auto-derives this from Enemy.StateTimer at spawn time instead (the same field every action sets to its own real duration - AnticipationTime, DashDuration, JumpDuration, a lobbed projectile's flight time, etc.), so this is only used if that comes back <= 0.")]
        public float GrowthDuration = 1f;

        [Tooltip("Seconds to fade the sprite's alpha in from 0 once spawned.")]
        public float FadeInDuration = 0.15f;
        [Tooltip("Seconds to fade the sprite's alpha out to 0 before it's destroyed at EndPhase.")]
        public float FadeOutDuration = 0.15f;
    }
}

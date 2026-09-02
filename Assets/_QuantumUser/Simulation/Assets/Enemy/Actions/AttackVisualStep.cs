namespace Quantum
{
    using System;
    using UnityEngine;

    // ArmSwingBack/ArmSnap are the arm-only counterparts of SwingBack/Snap below - same math,
    // retargeted from EnemyBlobAnimationView's root to its optional arm transform, for enemies
    // whose attack reads as an arm swing rather than a whole-body tell. PunchScale is a uniform
    // scale bump/ring, distinct from Pulse - Pulse drives _squashT (volume-preserving
    // squash/stretch, one axis grows while the other shrinks), PunchScale offsets overall scale
    // directly (all axes together), closer to a classic hit-impact "punch". ArmPunch is PunchScale's
    // same idea retargeted to the arm alone (ArmSwingBackParams/ArmSnapParams's own ArmScale fields
    // cover a scale riding those two tells; ArmPunch is for a standalone punch step that wants
    // scale+rotation+a short impact rattle together, without a paired windup step). Jump is the one
    // type that actually offsets root on local Y (a real hop arc, same bobTarget channel Die/Run
    // already ride) rather than squashing/rotating in place - every other type deliberately stays
    // grounded (see e.g. Crouch/Slam's SinkAmount, which is local Z specifically to avoid this).
    // Dive combines that same hop with a rotate-and-sink second half (rockTarget toward
    // RotateDegrees while depthTarget sinks, same local-Z sink idiom Crouch/Slam/Burrow already
    // use) - reads as diving headfirst into the ground rather than a plain squash; pairs naturally
    // with BurrowDeliveryData's own Dive sub-phase, authored on BeginStep. Appended at the end so
    // existing authored assets keep their serialized enum indices.
    public enum AttackAnimationType { None, Shake, SwingBack, Pulse, Crouch, Inflate, Lunge, Slam, Snap, Chomp, Spin, ArmSwingBack, ArmSnap, PunchScale, ArmPunch, Jump, Dive }

    // SkillTargetPosition (not OnTarget - renamed for clarity) resolves via
    // EnemyAttackVisualsView.TryGetAnchorPosition: prefers Enemy.SkillTargetPosition (the anchor
    // locked during the windup per EnemyAimLockTiming), falling back to the target entity's live
    // position only if that's unset - so this is NOT "the target's current live position," despite
    // what "OnTarget" used to imply.
    public enum ParticleAnchor { OnSelf, SkillTargetPosition }

    // Anticipation/Begin/OnGoing/End mirror EnemyActionData.View.cs's four steps. Spawned/Destroyed
    // are separate, for a delivery that hands off to a spawned child entity (Enemy.SkillProjectile) -
    // lets a Telegraph track "until the thing I spawned is gone" instead of the enemy's own
    // OnGoing/End. No corresponding AttackVisualStep fields exist for these (Telegraph-only).
    public enum AttackPhase { Anticipation, Begin, OnGoing, End, Spawned, Destroyed }

    // Each AttackAnimationType's own tunables, nested rather than flattened onto AttackVisualStep -
    // AttackVisualStepDrawer (Assets/_QuantumUser/Editor/) shows/hides the whole group as one unit
    // based on AnimationType. Lives in that PropertyDrawer rather than NaughtyAttributes because
    // Quantum's CustomEditor for AssetObject draws via plain DrawDefaultInspector, which never
    // gives NaughtyAttributes' ShowIf/Foldout logic a chance to run.

    [Serializable]
    public class ShakeParams
    {
        [Tooltip("Jitter oscillations per second.")] public float Frequency = 18f;
        [Tooltip("Rotation jitter amplitude in degrees.")] public float RockDegrees = 6f;
    }

    [Serializable]
    public class SwingBackParams
    {
        [Tooltip("How far the body leans away from its facing direction at the peak.")] public float Degrees = 20f;
    }

    [Serializable]
    public class PulseParams
    {
        [Tooltip("Squash pulses per second.")] public float Frequency = 6f;
        [Tooltip("How large the pulse swing gets by the end of the step.")] public float MaxSquash = 0.25f;
    }

    [Serializable]
    public class CrouchParams
    {
        [Tooltip("How compressed the body gets at the peak of the sink.")] public float Squash = 0.5f;
        [Tooltip("How far the body sinks at the peak - applied along local Z (depth) by EnemyBlobAnimationView, not vertically, so it doesn't visually push the body below the real ground plane.")] public float SinkAmount = 0.15f;
    }

    [Serializable]
    public class InflateParams
    {
        [Tooltip("How much the body swells/stretches out by the peak.")] public float Amount = 0.4f;
    }

    [Serializable]
    public class LungeParams
    {
        [Tooltip("Instant stretch pop at the moment this step starts.")] public float Stretch = 0.35f;
    }

    [Serializable]
    public class SlamParams
    {
        [Tooltip("How compressed the body gets at the moment of impact.")] public float Squash = 0.4f;
        [Tooltip("How far the body sinks at the moment of impact - applied along local Z (depth) by EnemyBlobAnimationView, not vertically, so it doesn't visually push the body below the real ground plane.")] public float SinkAmount = 0.2f;
    }

    [Serializable]
    public class SnapParams
    {
        [Tooltip("How far the body whips toward its facing direction at the moment of the strike.")] public float Degrees = 35f;
    }

    [Serializable]
    public class ChompParams
    {
        [Tooltip("Number of bite pulses across the step's duration.")] public float Pulses = 2f;
        [Tooltip("Squash amplitude of each bite pulse.")] public float Squash = 0.3f;
    }

    [Serializable]
    public class SpinParams
    {
        [Tooltip("Total rotation the body spins through during the step.")] public float Degrees = 360f;
    }

    [Serializable]
    public class ArmSwingBackParams
    {
        [Tooltip("How far the arm pulls back away from its facing direction at the peak.")] public float Degrees = 25f;
        [Tooltip("How much of this same pull-back also rocks the body, so it doesn't stay dead-still while only the arm moves. 0 = arm only, 1 = body rocks by the same degrees as the arm.")] public float BodyFollow = 0.3f;
        [Tooltip("Scale offset riding the same pull-back envelope as Degrees, applied on top of the arm's own authored scale - e.g. 0.15 grows the arm to 115% by the peak of the pull-back (winding up/tensing), negative shrinks it (compressing before a strike). 0 = rotation only, original behavior.")] public float ArmScale = 0f;
    }

    [Serializable]
    public class ArmSnapParams
    {
        [Tooltip("How far the arm whips toward its facing direction at the moment of the strike.")] public float Degrees = 45f;
        [Tooltip("How much of this same whip also rocks the body, so it doesn't stay dead-still while only the arm moves. 0 = arm only, 1 = body rocks by the same degrees as the arm.")] public float BodyFollow = 0.3f;
        [Tooltip("Scale offset riding the same whip-crack envelope as Degrees, applied on top of the arm's own authored scale - e.g. 0.15 punches the arm to 115% right as it snaps forward. 0 = rotation only, original behavior.")] public float ArmScale = 0f;
    }

    [Serializable]
    public class PunchScaleParams
    {
        [Tooltip("Peak scale offset at the start of the ring - e.g. 0.2 punches up to 120% before ringing back down to neutral.")] public float Strength = 0.2f;
        [Tooltip("Ring oscillations across the step's duration - higher rings faster/shorter, lower reads as a single slow bounce.")] public float Frequency = 3f;
    }

    // For a standalone punch step (not paired with its own windup step the way ArmSwingBack/ArmSnap
    // usually are) that wants scale+rotation+a short impact rattle all together - Degrees/ScaleRing
    // read as one whip-crack+ring pair sharing the step's own decay-from-start envelope, same as
    // Snap/ArmSnap's Degrees and PunchScale's Strength/Frequency respectively; ImpactShake is the
    // new piece, a brief extra jitter that only shows up right at the very start of the step (the
    // moment of impact), distinct from Shake's constant coil for the whole step. No Z-depth field
    // (unlike Snap/Lunge, which don't have one either) - this is a 2D sprite game, a local-Z punch
    // offset is never actually visible, so it's not worth the field.
    [Serializable]
    public class ArmPunchParams
    {
        [Tooltip("How far the arm whips toward its facing direction at the moment of impact, same whip-crack envelope as ArmSnap.Degrees.")] public float Degrees = 30f;
        [Tooltip("Peak scale offset on the same ring formula as PunchScale, applied to the arm alone instead of the whole body.")] public float ScaleStrength = 0.15f;
        [Tooltip("Ring oscillations for ScaleStrength above, same meaning as PunchScale.Frequency.")] public float ScaleFrequency = 3f;
        [Tooltip("Extra rotational jitter layered on top of the punch's own rotation, only during a short window right at the moment of impact (not the whole step) - sells a solid hit landing rather than a clean swing. 0 = no shake.")] public float ImpactShakeDegrees = 4f;
        [Tooltip("Jitter oscillations per second for the impact shake.")] public float ImpactShakeFrequency = 30f;
        [Tooltip("How much of this punch's rotation also rocks the body, same convention as ArmSwingBack/ArmSnap's BodyFollow. 0 = arm only.")] public float BodyFollow = 0.2f;
    }

    [Serializable]
    public class JumpParams
    {
        [Tooltip("Peak local-Y hop height at the middle of the step - the actual world-space arc (if any) is the simulation's own doing (e.g. BurrowDeliveryData's resurface), this is purely the character-animation pop layered on top.")] public float Height = 0.4f;
        [Tooltip("Squash compression ramping in toward the end of the step, as if landing from the hop.")] public float LandSquash = 0.3f;
    }

    [Serializable]
    public class DiveParams
    {
        [Tooltip("Peak local-Y hop height across roughly the first half of the step, before the rotate-and-sink.")] public float JumpHeight = 0.3f;
        [Tooltip("How far the body rotates (toward facing direction) as it dives, ramping in across the second half of the step.")] public float RotateDegrees = 90f;
        [Tooltip("How far the body sinks (local Z, not Y - same reasoning as Crouch/Slam's own SinkAmount) by the end of the dive.")] public float SinkAmount = 0.3f;
    }

    // One phase's worth of visual configuration (body animation + optional particle) -
    // EnemyActionData.View.cs has four of these, all sharing this reusable shape. Conditional field
    // display and the "Body Animation"/"Particle" foldouts live in AttackVisualStepDrawer
    // (Assets/_QuantumUser/Editor/).
    [Serializable]
    public class AttackVisualStep
    {
        public AttackAnimationType AnimationType = AttackAnimationType.None;
        [Tooltip("How long this step's body animation plays before easing back to neutral / handing off to idle-run.")]
        public float Duration = 0.3f;

        [Tooltip("Optional - swaps the enemy's body SpriteRenderer (EnemyViewRig.ReferenceSprite) to this sprite for this step, independent of AnimationType. Leave empty to leave whatever sprite is currently showing untouched. Reverts to the enemy's real spawn sprite once the attack fully ends (EnemyAttackVisualsView's attackNoLongerActive edge), regardless of which step last set it.")]
        public Sprite BodySprite;
        [Tooltip("Only applied while BodySprite above is showing (same duration/revert window) - additive root.localPosition offset, authored as if always facing right. X is mirrored by the enemy's current facing sign, same convention every other left/right-sensitive channel here uses.")]
        public Vector3 BodySpriteOffset;

        [Tooltip("0 = no camera shake. Above 0, this step shakes FollowCamera from its own resolved position (self, or the step's own anchor/target if Anchor below is set to SkillTargetPosition) - attenuated by distance from the camera (FollowCamera.stepShakeFalloffRadius) and scaled by this value (FollowCamera.stepShakeAmplitudePerImpact), independent of AnimationType/particle. A distant hit on another part of the map naturally shakes little to nothing.")]
        public float ShakeImpact = 0f;

        public ShakeParams Shake = new ShakeParams();
        public SwingBackParams SwingBack = new SwingBackParams();
        public PulseParams Pulse = new PulseParams();
        public CrouchParams Crouch = new CrouchParams();
        public InflateParams Inflate = new InflateParams();
        public LungeParams Lunge = new LungeParams();
        public SlamParams Slam = new SlamParams();
        public SnapParams Snap = new SnapParams();
        public ChompParams Chomp = new ChompParams();
        public SpinParams Spin = new SpinParams();
        public ArmSwingBackParams ArmSwingBack = new ArmSwingBackParams();
        public ArmSnapParams ArmSnap = new ArmSnapParams();
        public PunchScaleParams PunchScale = new PunchScaleParams();
        public ArmPunchParams ArmPunch = new ArmPunchParams();
        public JumpParams Jump = new JumpParams();
        public DiveParams Dive = new DiveParams();

        [Tooltip("Only shown for animation types that rotate the body (Shake/SwingBack/Snap/Spin/ArmSwingBack/ArmSnap). Off (default) rotates around root's own base/ground-contact pivot, same as idle wobble/run rock/die topple - correct for a rock/lean tell. On instead compensates position so the body rotates in place around a centered point, using PivotHeightOverride below (or EnemyBlobAnimationView's own rig-level default/auto height if left at 0) - what a full spin needs to avoid arcing around the feet.")]
        public bool CenterPivot = false;
        [Tooltip("Only used when CenterPivot is on. Local-space height, in root's own unscaled units, from root's base pivot up to the point to rotate around. 0 = fall back to EnemyBlobAnimationView's own default (its own field, or auto-detected from the rig's reference sprite bounds if that's also 0).")]
        public float PivotHeightOverride = 0f;

        [Tooltip("Leave empty for no particle on this step.")]
        public ParticleSystem ParticlePrefab;
        public ParticleAnchor Anchor = ParticleAnchor.OnSelf;
        [Tooltip("Relative to the enemy's own current facing (full Aim.Angle direction, not just a left/right mirror), not raw world space - Z is forward along that facing, X is to its right, Y is world-up. Rotates with the enemy so e.g. a muzzle offset stays on the correct side no matter which way it's currently facing.")]
        public Vector3 Offset;
        [Tooltip("Attaches to the anchor and follows it for the phase's duration (e.g. a charge trail), instead of a one-shot burst left behind at a fixed point.")]
        public bool Parented;
        [Tooltip("Rotate the particle to face the enemy's current Aim direction (world-flat, same facing convention the body/arm use) instead of spawning at identity rotation. RotationOffset below is applied on top either way.")]
        public bool AlignToEnemyDirection;
        [Tooltip("Additional Euler rotation applied on top of the base rotation (identity, or the enemy's facing direction if AlignToEnemyDirection is on).")]
        public Vector3 RotationOffset;
        [Tooltip("Uniform scale multiplier applied to the particle's own authored scale. 1 = unchanged.")]
        public float Scale = 1f;

        [Tooltip("Forces SortingOrder below onto every ParticleSystemRenderer in the spawned instance - the root AND every child particle system - instead of leaving each at whatever it was authored with.")]
        public bool OverrideSortingOrder;
        [Tooltip("Sorting order applied to every ParticleSystemRenderer in the hierarchy when OverrideSortingOrder is on.")]
        public int SortingOrder;
    }
}

namespace Quantum
{
    using System;
    using UnityEngine;

    // ArmSwingBack/ArmSnap are the arm-only counterparts of SwingBack/Snap below - same math,
    // retargeted from EnemyBlobAnimationView's root to its optional arm transform, for enemies
    // whose attack reads as an arm swing rather than a whole-body tell. Appended at the end so
    // existing authored assets keep their serialized enum indices.
    public enum AttackAnimationType { None, Shake, SwingBack, Pulse, Crouch, Inflate, Lunge, Slam, Snap, Chomp, Spin, ArmSwingBack, ArmSnap }

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
        [Tooltip("How far the body sinks down at the peak.")] public float SinkAmount = 0.15f;
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
        [Tooltip("Quick forward punch along local Z as the stretch fires.")] public float Depth = 0.1f;
    }

    [Serializable]
    public class SlamParams
    {
        [Tooltip("How compressed the body gets at the moment of impact.")] public float Squash = 0.4f;
        [Tooltip("How far the body sinks down at the moment of impact.")] public float SinkAmount = 0.2f;
    }

    [Serializable]
    public class SnapParams
    {
        [Tooltip("How far the body whips toward its facing direction at the moment of the strike.")] public float Degrees = 35f;
        [Tooltip("Forward punch along local Z as the snap fires, in sync with the same whip-crack envelope as Degrees.")] public float Depth = 0.1f;
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
    }

    [Serializable]
    public class ArmSnapParams
    {
        [Tooltip("How far the arm whips toward its facing direction at the moment of the strike.")] public float Degrees = 45f;
        [Tooltip("How much of this same whip also rocks the body, so it doesn't stay dead-still while only the arm moves. 0 = arm only, 1 = body rocks by the same degrees as the arm.")] public float BodyFollow = 0.3f;
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

        [Tooltip("Leave empty for no particle on this step.")]
        public ParticleSystem ParticlePrefab;
        public ParticleAnchor Anchor = ParticleAnchor.OnSelf;
        public Vector3 Offset;
        [Tooltip("Attaches to the anchor and follows it for the phase's duration (e.g. a charge trail), instead of a one-shot burst left behind at a fixed point.")]
        public bool Parented;
        [Tooltip("Rotate the particle to face the enemy's current Aim direction (world-flat, same facing convention the body/arm use) instead of spawning at identity rotation. RotationOffset below is applied on top either way.")]
        public bool AlignToEnemyDirection;
        [Tooltip("Additional Euler rotation applied on top of the base rotation (identity, or the enemy's facing direction if AlignToEnemyDirection is on).")]
        public Vector3 RotationOffset;
        [Tooltip("Uniform scale multiplier applied to the particle's own authored scale. 1 = unchanged.")]
        public float Scale = 1f;
    }
}

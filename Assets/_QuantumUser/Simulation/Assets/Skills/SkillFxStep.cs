namespace Quantum
{
    using System;
    using UnityEngine;

    // How SkillActionFxView spawns/tracks a SkillFxStep's ParticlePrefab - see that class.
    public enum SkillFxSpawnMode
    {
        // Fire-and-forget via EffectsManager's pool - a world-position snapshot at the moment this
        // step fires, no relationship to the caster afterward. Prefab must not loop (same contract
        // as every other EffectsManager.PlayEffect caller).
        OneShot,

        // Instantiated as a real child of the caster's own Transform (not pooled) so it visually
        // follows for as long as it plays, then self-destroys once every particle system in it goes
        // quiet (see ParticleAutoDestroy) - e.g. a channel-up flourish that shouldn't need to be
        // duration-matched by hand. Prefab should NOT loop, or it never self-destroys.
        Parented,

        // Pulled from EffectsManager's pool via GetHeldInstance (looping is fine here, unlike the
        // two modes above) - either parented onto the caster's own transform so it follows, or left
        // exactly where it spawned, depending on this step's own Parent flag (e.g. a zone effect
        // left behind at the cast position wants Parent off; a channel aura wants it on). Held
        // either way until this same action's End phase fires - at which point it stops emitting,
        // waits for whatever particles are still alive to finish naturally, then (if parented)
        // unparents and releases back to the pool. Nothing else asks SkillActionFxView to release
        // it, so this only makes sense on a step whose action also has SkillActionPhase.End in its
        // Phase flags.
        HeldUntilEnd,
    }

    // Which live direction SkillActionFxView rotates a step's particle to face - see SkillFxStep.Alignment.
    public enum SkillFxAlignment
    {
        None,
        // Caster's current Aim.Angle - works for any skill action, Dash or Hero Skill alike.
        AimDirection,
        // The Dash slot's own StartPosition->TargetPosition (see DashSkillData.Begin) - only
        // meaningful on a Dash Ascension. Falls back to AimDirection if that delta isn't resolved
        // yet (right at this activation's own Begin - see SkillActionFxView.ResolveDashAngle).
        DashDirection,
    }

    // One configurable particle moment - one field of this type lives on SkillActionData.View.cs
    // per lifecycle phase (BeginFx/OnGoingFx/EndFx), the skill-upgrade equivalent of
    // AttackVisualStep (see EnemyActionData.View.cs) for enemies. Kept deliberately smaller than
    // AttackVisualStep - no animation-type param blocks - skill upgrades don't drive the caster's
    // own attack animation, just a feedback particle.
    [Serializable]
    public class SkillFxStep
    {
        [Tooltip("Leave empty for no particle on this step.")]
        public ParticleSystem ParticlePrefab;

        public SkillFxSpawnMode SpawnMode = SkillFxSpawnMode.OneShot;

        [Tooltip("HeldUntilEnd only: parents the held instance onto the caster's transform so it follows until released. Off leaves it fixed at the position it spawned at (e.g. a zone effect left behind) - still released the same way once this action's End phase fires. Ignored by OneShot (never parented) and Parented (always parented) - see SkillFxSpawnMode.")]
        public bool Parent = true;

        [Tooltip("World-space offset from the caster's position (or, in Parented mode / HeldUntilEnd with Parent on, local offset from the caster's transform).")]
        public Vector3 Offset;

        [Tooltip("Full scale OVERRIDE, not a multiplier on the prefab's own authored scale - same 'authored at a reference radius of 1' convention EffectsManager's own radius-scaled handlers use. Only meaningful for an action that overrides EffectRadius (see SkillActionData.EffectRadius) to return its own Radius field. Off = Scale below multiplies the prefab's own authored scale instead.")]
        public bool ScaleByRadius;

        [Tooltip("ScaleByRadius on: multiplies EffectRadius as the sole scale (radius * Scale is a full override of the prefab's own authored scale). ScaleByRadius off: multiplies the prefab's own authored scale instead (1 = prefab's own authored size, unchanged).")]
        public float Scale = 1f;

        [Tooltip("Rotates the particle to face a live direction instead of spawning at identity rotation - AimDirection or DashDirection (see SkillFxAlignment). In HeldUntilEnd mode this is re-applied every frame; OneShot/Parented snapshot it once at spawn.")]
        public SkillFxAlignment Alignment;
    }
}

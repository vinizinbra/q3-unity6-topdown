namespace Quantum
{
    using System;
    using UnityEngine;

    // View-only half of SkillActionData (see the partial declaration in SkillActionData.cs) - lives
    // on the shared abstract base since every concrete action/upgrade (SpawnEntitySkillAction,
    // JuggernautLandingRootSkillAction, IncreaseDamageSkillAction, ...) wants the same "what does
    // this actually do" documentation slot, not a per-subclass field. Same shape as
    // SkillData.Description. Player-facing when offered as a LevelUpPoolKind.SkillUpgrade card, via
    // GetDescription() (see SkillActionData.cs) - not just a designer note, since the template
    // substitution below keeps it accurate to this asset's own live-tuned numbers.
    public partial class SkillActionData
    {
        [TextArea(2, 4)]
        [Tooltip("Effect text - also shown to players as a level-up choice card's description (see GetDescription). Supports {0}, {1}, etc. placeholders filled in from this action's own live values via DescriptionArgs (override in a subclass), so a retuned number can't drift out of sync with the sentence describing it. Plain text with no placeholders works too.")]
        public string Description;

        // Override in a concrete SkillActionData subclass to supply the values its own Description
        // template references via {0}, {1}, etc. - see GetFormattedDescription.
        protected virtual object[] DescriptionArgs => Array.Empty<object>();

        public string GetFormattedDescription() => DescriptionUtility.Format(Description, DescriptionArgs);

        // Per-phase feedback particle config, dispatched by SkillActionFxView off the
        // SkillActionBeginExecuted/OnGoingExecuted/EndExecuted events SkillSystem.Invoke fires
        // automatically for any phase whose step actually has a prefab (see HasFx) - no per-action
        // Execute() override needs to fire its own event just to get a feedback particle. The skill
        // upgrade equivalent of EnemyActionData.View.cs's BeginStep/OnGoingStep/EndStep.
        [Header("Feedback FX")]
        [Tooltip("Particle played when this action's Begin phase executes.")]
        public SkillFxStep BeginFx;
        [Tooltip("Particle played when this action's OnGoing phase executes (every due tick - see Interval). HeldUntilEnd mode only acquires once and no-ops on repeat ticks while already held.")]
        public SkillFxStep OnGoingFx;
        [Tooltip("Particle played when this action's End phase executes. Also where any BeginFx/OnGoingFx step spawned as HeldUntilEnd gets released, regardless of this field's own configuration.")]
        public SkillFxStep EndFx;

        // SkillSystem checks this before firing a phase's event at all - keeps every action with no
        // FX authored (most of them) from producing event traffic for nothing, same "don't fire if
        // there's nothing to play" reasoning EffectsManager's own handlers already apply on the
        // receiving end, just moved a step earlier here since this is a per-tick OnGoing check too.
        //
        // End is special-cased: it must also return true whenever BeginFx/OnGoingFx is
        // HeldUntilEnd, even with no EndFx of its own configured - that held instance was acquired
        // off THIS action's own Begin/OnGoing event, so nothing else will ever ask
        // SkillActionFxView to release it. Without this, an action authored with only (say)
        // OnGoingFx = HeldUntilEnd and no EndFx never fires SkillActionEndExecuted at all, so the
        // particle spawns but is never released back to EffectsManager's pool.
        public bool HasFx(SkillActionPhase phase)
        {
            if (ResolveFxStep(phase)?.ParticlePrefab != null)
                return true;

            if (phase == SkillActionPhase.End)
                return IsHeldUntilEnd(BeginFx) || IsHeldUntilEnd(OnGoingFx);

            return false;
        }

        private static bool IsHeldUntilEnd(SkillFxStep step) =>
            step != null && step.ParticlePrefab != null && step.SpawnMode == SkillFxSpawnMode.HeldUntilEnd;

        public SkillFxStep ResolveFxStep(SkillActionPhase phase)
        {
            switch (phase)
            {
                case SkillActionPhase.Begin: return BeginFx;
                case SkillActionPhase.OnGoing: return OnGoingFx;
                case SkillActionPhase.End: return EndFx;
                default: return null;
            }
        }
    }
}

namespace Quantum
{
    using System.Collections.Generic;

    // Ordered list of sub-action steps, run one after another - built last since it composes every
    // other delivery type. Each step's own EnemyActionData supplies Damage/DamageRange/Knockback/
    // Effects and points at whatever concrete Delivery actually executes it; a step's own
    // AnticipationTime/Telegraph/Trigger are NOT used here - the outer action's single windup
    // already telegraphed the whole sequence once, so a full re-windup per step would double up on
    // that. Requires the optional EnemySequenceState component on any prototype that uses this (see
    // that component's own comment) to track which step is running - the same "only enemies that
    // actually need this pay for it" reasoning as EnemyActionSlots. Consecutive instant steps (both
    // Begin() returning true) resolve within the same tick, back to back, by design - no per-step
    // delay field exists (nothing in the roster needs one yet); add one if a future sequence wants
    // a beat between instant steps.
    public unsafe class SequenceDeliveryData : EnemyDeliveryData
    {
        [ExpandableAsset] public List<AssetRef<EnemyActionData>> Steps = new();

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<EnemySequenceState>(filter.Entity, out var sequence) == false)
            {
                Log.Error($"[Enemy] {filter.Entity} has a SequenceDeliveryData action but no EnemySequenceState component - add it to the prototype");
                return true;
            }

            sequence->CurrentStepIndex = 0;
            return RunSteps(f, ref filter, data, sequence, target, isFirstTickOfStep: true);
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<EnemySequenceState>(filter.Entity, out var sequence) == false)
                return true;

            return RunSteps(f, ref filter, data, sequence, target, isFirstTickOfStep: false);
        }

        public override void OnInterrupted(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action)
        {
            if (f.Unsafe.TryGetPointer<EnemySequenceState>(filter.Entity, out var sequence) == false)
                return;

            EnemyActionData step = ResolveStep(f, sequence->CurrentStepIndex);

            if (step == null || step.Delivery.IsValid == false)
                return;

            f.FindAsset(step.Delivery).OnInterrupted(f, ref filter, data, step);
        }

        // Advances through Steps starting at CurrentStepIndex, calling each one's own Begin() (on
        // its first tick) or Tick() (every tick after) until one reports unfinished (stay here,
        // return false) or the list runs out (whole sequence done, return true).
        private bool RunSteps(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemySequenceState* sequence, EntityRef target, bool isFirstTickOfStep)
        {
            while (sequence->CurrentStepIndex < Steps.Count)
            {
                EnemyActionData step = ResolveStep(f, sequence->CurrentStepIndex);

                if (step == null || step.Delivery.IsValid == false)
                {
                    Log.Error($"[Enemy] {filter.Entity} sequence step {sequence->CurrentStepIndex} has no valid Delivery - skipping");
                    sequence->CurrentStepIndex++;
                    isFirstTickOfStep = true;
                    continue;
                }

                EnemyDeliveryData delivery = f.FindAsset(step.Delivery);
                bool finished = isFirstTickOfStep == true
                    ? delivery.Begin(f, ref filter, data, step, target)
                    : delivery.Tick(f, ref filter, data, step, target);

                if (finished == false)
                    return false;

                sequence->CurrentStepIndex++;
                isFirstTickOfStep = true;
            }

            return true;
        }

        private EnemyActionData ResolveStep(Frame f, int index)
        {
            if (index < 0 || index >= Steps.Count)
                return null;

            AssetRef<EnemyActionData> stepRef = Steps[index];
            return stepRef.IsValid == true ? f.FindAsset(stepRef) : null;
        }
    }
}

namespace Quantum
{
    using System;
    using Photon.Deterministic;

    [Flags]
    public enum SkillActionPhase
    {
        Begin   = 1 << 0,
        OnGoing = 1 << 1,
        End     = 1 << 2
    }

    // Composable behavior mixed onto a SkillData via its Actions list - see SkillData. Phase is
    // configurable per asset instance and controls which lifecycle point(s) Execute fires at, so
    // "spawn on Begin" vs "spawn on End" is the same SpawnEntitySkillAction class with a different
    // Phase value, not two classes - retargeting when a behavior fires never needs new C#. Combine
    // flags (e.g. Begin | End) for an action whose single Execute call needs to run paired logic
    // at both ends - SkillSystem always invokes Execute with
    // exactly one bit set in firedPhase (never a combination), so Execute can branch on it safely.
    public abstract unsafe partial class SkillActionData : AssetObject
    {
        // Ascending execution order within one phase - see SkillSystem.InvokeActions. Lower runs
        // first; every action defaults to 0, so leaving it alone reproduces plain list-order
        // execution. Set explicitly (e.g. a negative value) when an action's effect - like
        // IncreaseAreaSkillAction writing SkillSlot.AreaMultiplier - has to be visible to another
        // action already resolving the same phase, regardless of where either sits in the
        // Actions/Upgrades list.
        public int Priority = 0;

        public SkillActionPhase Phase = SkillActionPhase.Begin;

        // Paces an OnGoing action, which fires every tick otherwise. Zero or less means every tick;
        // the once-only phases ignore it.
        public FP Interval;

        // Lets an action be switched off without pulling it out of Actions/Upgrades or deleting the
        // asset - flip this to rule one in or out while testing/balancing instead of restructuring
        // the list (and losing whatever else referenced this same asset instance).
        public bool Activated = true;

        public abstract void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase);

        public bool ShouldExecute(Frame f, SkillSlot* slot, SkillActionPhase firedPhase)
        {
            if (Activated == false)
                return false;

            if ((Phase & firedPhase) == 0)
                return false;

            if (firedPhase == SkillActionPhase.OnGoing)
                return IsDueThisTick(f, slot);

            return true;
        }

        // Paced off the slot's own clock rather than a countdown stored here: actions are shared
        // assets with no per-entity state, and two actions on one skill have to be able to run
        // different intervals off the same activation. Crossing an interval boundary since last tick
        // is what fires it, so the cadence never drifts with DeltaTime. Overridable so a subclass can
        // pace off a different accumulator with the same trick - SpawnEntitySkillAction runs it off
        // distance travelled instead of time.
        protected virtual bool IsDueThisTick(Frame f, SkillSlot* slot)
        {
            if (Interval <= FP._0)
                return true;

            FP elapsed = slot->ActiveTime;

            return FPMath.FloorToInt(elapsed / Interval) > FPMath.FloorToInt((elapsed - f.DeltaTime) / Interval);
        }
    }
}

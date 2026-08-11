using Quantum;

// RageOverdrive's SkillProgressUiWidget: Stacks/MaxStacks live directly on the component, so no
// per-hero resolution is needed beyond the presence check. "Complete" is the same live max-Rage
// condition RageOverdriveUtility.IsAtMaxRage checks Simulation-side, re-derived here rather than
// read off a baked flag (RageOverdrive no longer carries one - see docs/max-ascensions.md).
public class RageOverdriveUiWidget : SkillProgressUiWidget
{
    protected override bool TryGetProgress(Frame frame, EntityRef entity, out int current, out int max, out bool complete)
    {
        if (frame.Has<RageOverdrive>(entity) == false)
        {
            current = 0;
            max = 0;
            complete = false;
            return false;
        }

        RageOverdrive rage = frame.Get<RageOverdrive>(entity);
        current = rage.Stacks;
        max = rage.MaxStacks;
        complete = rage.Stacks >= rage.MaxStacks;
        return true;
    }
}

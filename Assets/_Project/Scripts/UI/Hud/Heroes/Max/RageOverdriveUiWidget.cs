using Quantum;

// RageOverdrive's SkillProgressUiWidget: Stacks/MaxStacks/Overdriven all live directly on the
// component (see RageOverdriveSkillAction), so no per-hero resolution is needed beyond the presence
// check.
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
        complete = rage.Overdriven;
        return true;
    }
}

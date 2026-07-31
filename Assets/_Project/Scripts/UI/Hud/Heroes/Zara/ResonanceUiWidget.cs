using Quantum;

// Resonance's SkillProgressUiWidget: unlike Adrenaline/Scrap (icon+text stack widgets), Resonance
// reads as a continuous build toward a pulse rather than a discrete count, so it gets the same
// slider gauge treatment as RageOverdrive/JuggernautCharge. Current/Max are FP, not Byte, so they're
// truncated to int for the shared int-based TryGetProgress signature - a fractional Resonance point
// is never meaningful on its own. "Complete" mirrors ResonanceUtility.FirePulse's own ">= Max" gate,
// even though Current wraps (carrying remainder) rather than resetting to 0 right after.
public class ResonanceUiWidget : SkillProgressUiWidget
{
    protected override bool TryGetProgress(Frame frame, EntityRef entity, out int current, out int max, out bool complete)
    {
        if (frame.Has<Resonance>(entity) == false)
        {
            current = 0;
            max = 0;
            complete = false;
            return false;
        }

        Resonance resonance = frame.Get<Resonance>(entity);
        current = resonance.Current.AsInt;
        max = resonance.Max.AsInt;
        complete = resonance.Current >= resonance.Max;
        return true;
    }
}

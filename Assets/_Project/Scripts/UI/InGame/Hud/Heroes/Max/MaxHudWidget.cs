using Photon.Deterministic;
using Quantum;
using UnityEngine;

// Max's Overdrive readout: Rage stacks and how long the activation itself has left. Shown only
// while Overdrive is running - RageOverdrive is granted at Begin and removed at End (see
// BerserkSkillData), including when Last Stand rank 1 parks the count on LastStandUpgrade instead,
// so its presence already IS the "an activation is running right now" check every Overdrive
// Ascension reads.
//
// Replaces the old RageOverdriveUiWidget.
public class MaxHudWidget : HeroHudWidget
{
    [SerializeField, Tooltip("Rage as a fill - Stacks against MaxStacks. Complete = at max Rage, the live condition Full Throttle/Ignition react to (RageOverdriveUtility.IsAtMaxRage).")]
    private Section rageGauge;

    [SerializeField, Tooltip("The same Rage as a discrete \"3/5\" count. Optional second view of rageGauge above - Rage moves one stack at a time off landed hits, which a bare fill is easy to lose track of.")]
    private Section rageStacks;

    [SerializeField, Tooltip("Seconds left before Overdrive ends on its own (SkillSlot.StateTimer against BerserkSkillData.Duration).")]
    private Section timer;

    protected override bool TryRefresh(Frame frame, EntityRef entity)
    {
        if (frame.TryGet<RageOverdrive>(entity, out var rage) == false)
            return false;

        int max = Mathf.Max(1, rage.MaxStacks);
        bool atMaxRage = rage.Stacks >= rage.MaxStacks;
        string value = $"{rage.Stacks}/{rage.MaxStacks}";

        rageGauge.Show(rage.Stacks / (float)max, value, atMaxRage);
        rageStacks.Show(rage.Stacks / (float)max, value, atMaxRage);

        UpdateTimer(frame, entity);
        return true;
    }

    private void UpdateTimer(Frame frame, EntityRef entity)
    {
        if (TryGetActiveSkill(frame, entity, out BerserkSkillData _, out FP remaining, out FP duration) == false)
        {
            timer.Hide();
            return;
        }

        timer.Show(duration > FP._0 ? (remaining / duration).AsFloat : 0f, FormatSeconds(remaining));
    }

    protected override void HideSections()
    {
        rageGauge.Hide();
        rageStacks.Hide();
        timer.Hide();
    }
}

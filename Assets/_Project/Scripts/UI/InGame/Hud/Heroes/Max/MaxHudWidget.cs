using Photon.Deterministic;
using Quantum;
using UnityEngine;

// Max's Overdrive readout: Rage stacks and how long the activation itself has left. Shown only
// while Overdrive is running - RageOverdrive is granted at Begin and removed at End (see
// BerserkSkillData), including when Last Stand rank 1 parks the count on LastStandUpgrade instead,
// so its presence already IS the "an activation is running right now" check every Overdrive
// Ascension reads.
//
// Rage is ONE section, not a bar plus a separate "3/5" row - the gauge's own valueText carries the
// exact count when it's wired, so a second stack row was two things to author and keep in sync for
// one number. Max Rage isn't a badge anymore either: the section glows in its own authored color
// instead (see HeroHudWidget.Section).
//
// Replaces the old RageOverdriveUiWidget.
public class MaxHudWidget : HeroHudWidget
{
    [SerializeField, Tooltip("Rage as a fill - Stacks against MaxStacks, with the exact \"3/5\" on its own valueText if one is wired. Glows at max Rage, the live condition Full Throttle/Ignition react to (RageOverdriveUtility.IsAtMaxRage).")]
    private Section rageGauge;

    [SerializeField, Tooltip("Seconds left before Overdrive ends on its own (SkillSlot.StateTimer against BerserkSkillData.Duration). Drains toward 0, so it deliberately never glows - a fresh activation being \"full\" isn't a payoff to call out.")]
    private Section timer;

    protected override bool TryRefresh(Frame frame, EntityRef entity)
    {
        if (frame.TryGet<RageOverdrive>(entity, out var rage) == false)
            return false;

        int max = Mathf.Max(1, rage.MaxStacks);
        float fill = Mathf.Clamp01(rage.Stacks / (float)max);
        bool atMaxRage = rage.Stacks >= rage.MaxStacks;
        string value = $"{rage.Stacks}/{rage.MaxStacks}";

        rageGauge.Show(fill, value, atMaxRage);

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
        timer.Hide();
    }
}

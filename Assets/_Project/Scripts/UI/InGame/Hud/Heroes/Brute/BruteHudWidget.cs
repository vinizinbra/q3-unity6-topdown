using Photon.Deterministic;
using Quantum;
using UnityEngine;

// Brute's Juggernaut readout: charge toward the next Discharge, Aftershock's "Building Pressure"
// stacks, and how long the channel itself has left. Shown only while Juggernaut is actually running
// - JuggernautCharge is added at Begin and removed at End (see JuggernautSkillData), which is also
// the only window in which any of these three numbers mean anything.
//
// Replaces the old JuggernautChargeUiWidget/JuggernautStackDamageUiWidget pair.
public class BruteHudWidget : HeroHudWidget
{
    [SerializeField, Tooltip("Ground covered toward the next Discharge - ChargePoints against the active JuggernautSkillData's own MaxCharge (which lives on the asset, not the component). Glows once Charged, i.e. the next enemy touched gets knocked back.")]
    private Section chargeGauge;

    [SerializeField, Tooltip("Aftershock's \"Building Pressure\" stacks - JuggernautCharge.UnitsHit clamped to AftershockUpgrade.MaxStacks, the same clamp its end-of-channel explosion applies (see JuggernautSkillData.TryEndExplosion). Hidden entirely unless that Ascension is equipped; glows at the clamp.")]
    private Section stacks;

    [SerializeField, Tooltip("Seconds left before Juggernaut ends on its own (SkillSlot.StateTimer against JuggernautSkillData.Duration).")]
    private Section timer;

    protected override bool TryRefresh(Frame frame, EntityRef entity)
    {
        if (frame.TryGet<JuggernautCharge>(entity, out var charge) == false)
            return false;

        // MaxCharge and Duration both live on the skill asset rather than the component, so the
        // active slot has to be resolved before either the gauge or the timer means anything.
        if (TryGetActiveSkill(frame, entity, out JuggernautSkillData skill, out FP remaining, out FP duration) == false)
            return false;

        int maxCharge = Mathf.Max(1, skill.MaxCharge);
        bool charged = charge.ChargePoints >= skill.MaxCharge;
        chargeGauge.Show(charge.ChargePoints / (float)maxCharge, $"{charge.ChargePoints}/{skill.MaxCharge}", charged);

        UpdateStacks(frame, entity, charge);

        timer.Show(duration > FP._0 ? (remaining / duration).AsFloat : 0f, FormatSeconds(remaining));
        return true;
    }

    // Gated on AftershockUpgrade rather than on JuggernautCharge alone: UnitsHit keeps counting
    // whether or not that Ascension is equipped, and showing a stack count that isn't feeding a real
    // damage bonus would be misleading (same gate the old JuggernautStackDamageUiWidget applied).
    private void UpdateStacks(Frame frame, EntityRef entity, JuggernautCharge charge)
    {
        if (frame.TryGet<AftershockUpgrade>(entity, out var aftershock) == false || aftershock.MaxStacks == 0)
        {
            stacks.Hide();
            return;
        }

        int current = Mathf.Min(charge.UnitsHit, aftershock.MaxStacks);
        stacks.Show(current / (float)aftershock.MaxStacks, $"{current}/{aftershock.MaxStacks}", current >= aftershock.MaxStacks);
    }

    protected override void HideSections()
    {
        chargeGauge.Hide();
        stacks.Hide();
        timer.Hide();
    }
}

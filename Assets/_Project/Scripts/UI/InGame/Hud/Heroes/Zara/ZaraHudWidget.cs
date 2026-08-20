using Photon.Deterministic;
using Quantum;
using UnityEngine;

// Zara's readout: Resonance building toward its next pulse, plus (once the Remix Ascension is
// taken) how many pulses are left before one of them also procs a random status effect. Resonance
// itself exists from the moment her base passive is applied and never goes away, so this widget is
// visible for Zara's whole run rather than only during a skill, unlike Brute's/Max's.
//
// Replaces the old ResonanceUiWidget/RemixUiWidget pair.
public class ZaraHudWidget : HeroHudWidget
{
    // Every third pulse - see ResonanceUtility.FirePulse/ZaraRemixUtility.
    private const int PulsesPerTrigger = 3;

    [SerializeField, Tooltip("Resonance toward the next pulse - Current against Max. Complete mirrors ResonanceUtility.FirePulse's own \">= Max\" gate, even though Current wraps (carrying the remainder) rather than resetting to 0.")]
    private Section resonanceGauge;

    [SerializeField, Tooltip("Remix - pulses fired since the last proc, out of 3. Hidden entirely unless the Ascension is taken (Resonance.RemixRank above 0); complete = the NEXT pulse is the one that procs.")]
    private Section remix;

    protected override bool TryRefresh(Frame frame, EntityRef entity)
    {
        if (frame.TryGet<Resonance>(entity, out var resonance) == false)
            return false;

        float fill = resonance.Max > FP._0 ? (resonance.Current / resonance.Max).AsFloat : 0f;
        resonanceGauge.Show(fill, $"{resonance.Current.AsInt}/{resonance.Max.AsInt}", resonance.Current >= resonance.Max);

        UpdateRemix(resonance);
        return true;
    }

    // Resonance is present for any Zara, ascension or not, so presence alone can't gate this the way
    // JuggernautCharge/RageOverdrive gate their own - RemixRank being 0 (RemixPassiveUpgradeData's
    // own "not taken" default) is what's checked instead.
    private void UpdateRemix(Resonance resonance)
    {
        if (resonance.RemixRank == 0)
        {
            remix.Hide();
            return;
        }

        int sinceProc = resonance.PulseCount % PulsesPerTrigger;
        remix.Show(sinceProc / (float)PulsesPerTrigger, $"{sinceProc}/{PulsesPerTrigger}", sinceProc == PulsesPerTrigger - 1);
    }

    protected override void HideSections()
    {
        resonanceGauge.Hide();
        remix.Hide();
    }
}

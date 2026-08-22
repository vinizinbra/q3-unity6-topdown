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

    [SerializeField, Tooltip("Resonance toward the next pulse - Current against Max. Glows on ResonanceUtility.FirePulse's own \">= Max\" gate, even though Current wraps (carrying the remainder) rather than resetting to 0.")]
    private Section resonanceGauge;

    [SerializeField, Tooltip("Remix - which pulse of the 3-pulse cycle fires next, so it counts 1/3 -> 2/3 -> 3/3 and wraps. Hidden entirely unless the Ascension is taken (Resonance.RemixRank above 0); glows at 3/3, i.e. the next pulse is the one that procs.")]
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

        // The sim increments PulseCount BEFORE testing it (ResonanceUtility.FirePulse's own
        // "PulseCount % 3 == 0"), so the proc pulse wraps the count back to 0 in the very same tick -
        // a raw remainder can therefore only ever read 0/3, 1/3 or 2/3, with the payoff landing on
        // 2/3. That's a gauge that fills two thirds and then lights up, and a 3/3 nobody can see.
        //
        // Counted as "which pulse of the 3 comes NEXT" instead: 1/3 immediately after a proc, 3/3 -
        // full and glowing - when the next pulse is the Remix one. Same number underneath, but the
        // bar is now full exactly when the payoff is one pulse away.
        int nextPulse = resonance.PulseCount % PulsesPerTrigger + 1;
        remix.Show(nextPulse / (float)PulsesPerTrigger, $"{nextPulse}/{PulsesPerTrigger}", nextPulse == PulsesPerTrigger);
    }

    protected override void HideSections()
    {
        resonanceGauge.Hide();
        remix.Hide();
    }
}

using Photon.Deterministic;
using Quantum;
using UnityEngine;
using UnityEngine.UI;

// Zara's readout: her Flow State (see Flow.qtn) as ONE fill bar going 0 -> full, which flips to an
// "active" colour once it lands.
//
// That is the whole mechanic, so it is the whole widget - Flow is two things (a fill and an on/off
// state) and this shows exactly those two. It replaced a 3-pip stage indicator when the underlying
// mechanic collapsed from a 3-stack ladder into a single bar; the pips were showing structure the
// simulation no longer had.
//
// Everything polls ZaraFlow directly - the simulation is authoritative and the UI never derives
// gameplay state. ZaraFlowChanged exists for one-shot audio/VFX stings, not for this.
public class ZaraHudWidget : HeroHudWidget
{
    [SerializeField, Tooltip("Image with Image Type = Filled. Runs 0 -> 1 as Flow builds, drains back down while she stands still, and snaps to 0 when a hit breaks it.")]
    private Image flowFill;

    [SerializeField, Tooltip("Fill colour while Flow is still building - the 'not there yet' state.")]
    private Color buildingColor = new Color(0.4f, 0.95f, 1f);

    [SerializeField, Tooltip("Fill colour once Flow is ACTIVE. The single most important thing this widget communicates, so it gets an unmistakable colour change rather than just a full bar.")]
    private Color activeColor = new Color(1f, 0.85f, 0.25f);

    [SerializeField, Tooltip("Fill colour while the bar is DRAINING because she has stood still past the grace window. The warning that Flow is actively being lost - without it, decay is invisible until it has already cost her.")]
    private Color decayingColor = new Color(1f, 0.35f, 0.35f);

    [SerializeField, Tooltip("Optional - shown only while Flow is ACTIVE (a glow, an icon, a label). Left unassigned, the fill colour carries the state on its own.")]
    private GameObject activeRoot;

    protected override bool TryRefresh(Frame frame, EntityRef entity)
    {
        if (frame.TryGet<ZaraFlow>(entity, out var flow) == false)
            return false;

        // "Decaying" is specifically past the grace window - NOT merely standing still, which during
        // grace costs nothing and must not read as a warning.
        bool decaying = flow.IsMoving == false
                        && flow.Progress > FP._0
                        && flow.StationaryTimer >= flow.StationaryGrace;

        if (flowFill != null)
        {
            flowFill.fillAmount = Mathf.Clamp01(flow.Progress.AsFloat);
            flowFill.color = decaying ? decayingColor : flow.IsActive ? activeColor : buildingColor;
        }

        if (activeRoot != null && activeRoot.activeSelf != flow.IsActive)
            activeRoot.SetActive(flow.IsActive);

        return true;
    }

    protected override void HideSections()
    {
        if (flowFill != null)
            flowFill.fillAmount = 0f;

        if (activeRoot != null)
            activeRoot.SetActive(false);
    }
}

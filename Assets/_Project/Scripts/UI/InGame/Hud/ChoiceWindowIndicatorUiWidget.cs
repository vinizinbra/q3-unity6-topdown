using Quantum;
using UnityEngine;

// Small per-player "has a Choice Window open" indicator for the party HUD strip (PartyHudWidget) -
// lets OTHER players (including remote slots) see who's still browsing a Cursed Rift/Store/
// Blacksmith, most relevant during a Breathing grace hold (see RunPhaseUtility.
// TickBreathingGraceHold/docs/run-phase.md), but stays accurate any time one of these is open.
// Always bound externally via Initialize, same as every other PartyHudWidget child - this slot can
// end up showing any match player, not just player 1.
public class ChoiceWindowIndicatorUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField, Tooltip("Toggled on while the bound player has a Cursed Rift/Store/Blacksmith Choice Window open.")]
    private GameObject indicatorRoot;

    [SerializeField] private EntityRef _entityRef;

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override void QUpdate(QuantumGame game)
    {
        if (indicatorRoot == null)
            return;

        var frame = game.Frames.Predicted;

        // Single source of truth for which POI components count as a "Choice Window" - a future POI
        // Choice Window (Store/Blacksmith's own successors, or a new one entirely) only needs
        // updating there, not here.
        bool hasWindowOpen = PoiInteractionLockUtility.HasChoiceWindowOpen(frame, _entityRef);

        if (indicatorRoot.activeSelf != hasWindowOpen)
            indicatorRoot.SetActive(hasWindowOpen);
    }
}

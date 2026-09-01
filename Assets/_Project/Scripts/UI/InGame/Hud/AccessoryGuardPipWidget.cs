using UnityEngine;

// One durability point of the Recoverable Accessory Guard (see docs/accessory-guard.md). Plain
// data-in widget with no Quantum awareness of its own, same idiom as PartyHistoryUpgradeWidget -
// CharacterUiWidget spawns one per point of MaxDurability and drives each one's state.
//
// The split matters: a pip is TWO things at once. The pip object itself is the SLOT - its mere
// existence says "this player has a durability point here at all", which tracks MaxDurability and is
// why these are spawned rather than hand-authored (Glass Core doubles that number mid-run). The
// available/spent visuals inside it say whether that particular point is currently usable, which
// tracks CurrentDurability and changes constantly as guards are spent and recovered.
//
// So the GameObject this sits on stays active for its whole life; only its children swap.
public class AccessoryGuardPipWidget : MonoBehaviour
{
    [SerializeField, Tooltip("The FILLED state - shown while this durability point is still available to block a hit. Usually the solid/lit pip graphic.")]
    private GameObject availableRoot;

    [SerializeField, Tooltip("Optional SPENT state - shown while this durability point has been used up. Leave unassigned if the empty frame is drawn by the pip's own backing rather than by a separate object.")]
    private GameObject spentRoot;

    public void SetAvailable(bool available)
    {
        if (availableRoot != null && availableRoot.activeSelf != available)
            availableRoot.SetActive(available);

        // Deliberately the exact inverse rather than a second independent flag - a pip is either
        // available or spent, never both and never neither.
        if (spentRoot != null && spentRoot.activeSelf == available)
            spentRoot.SetActive(available == false);
    }
}

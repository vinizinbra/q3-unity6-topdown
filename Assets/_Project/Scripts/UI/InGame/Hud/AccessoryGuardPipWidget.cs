using UnityEngine;
using UnityEngine.UI;

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

    [Header("Dropped (accessory lying in the level)")]
    [SerializeField, Tooltip("Pulses per second of the blink an AVAILABLE pip does while the accessory is off the hero (Airborne/Dropped) - those charges still exist, but they are on the floor and protect nothing until it is picked back up.")]
    private float droppedPulseRate = 2.5f;

    [SerializeField, Range(0f, 1f), Tooltip("Lowest alpha the dropped blink dips to.")]
    private float droppedMinAlpha = 0.1f;

    [SerializeField, Tooltip("Extra scale at the top of each dropped blink (0.2 = +20%), so it throbs as well as fades.")]
    private float droppedScalePulse = 0.2f;

    [SerializeField, Tooltip("Multiplied onto the pip's own colour while dropped. White keeps the art as is - the pip sprite is a coloured shield, so a hue tint only darkens it; the blink is what carries the state.")]
    private Color droppedTint = Color.white;

    private Graphic _availableGraphic;
    private Color _availableColor;
    private Vector3 _availableScale;
    private bool _availableCaptured;

    public void SetAvailable(bool available) => SetState(available, dropped: false);

    // `dropped`: the accessory is off the hero but recoverable. Available pips then blink instead of
    // sitting solid like charges that are ready to block - which is what a plain "2 of 3 lit" said
    // before, while the hero actually had no protection at all. Driven every frame by
    // CharacterUiWidget, so the pulse needs no Update of its own; unscaled time keeps it blinking over
    // a paused sim (level-up, chest).
    public void SetState(bool available, bool dropped)
    {
        if (availableRoot != null && availableRoot.activeSelf != available)
            availableRoot.SetActive(available);

        // Deliberately the exact inverse rather than a second independent flag - a pip is either
        // available or spent, never both and never neither.
        if (spentRoot != null && spentRoot.activeSelf == available)
            spentRoot.SetActive(available == false);

        ApplyDropped(available && dropped);
    }

    private void ApplyDropped(bool dropped)
    {
        if (_availableCaptured == false)
        {
            _availableCaptured = true;
            _availableGraphic = availableRoot != null ? availableRoot.GetComponent<Graphic>() : null;

            if (_availableGraphic != null)
            {
                _availableColor = _availableGraphic.color;
                _availableScale = _availableGraphic.rectTransform.localScale;
            }
        }

        if (_availableGraphic == null)
            return;

        if (dropped == false)
        {
            if (_availableGraphic.color != _availableColor)
                _availableGraphic.color = _availableColor;

            if (_availableGraphic.rectTransform.localScale != _availableScale)
                _availableGraphic.rectTransform.localScale = _availableScale;

            return;
        }

        float wave = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * droppedPulseRate * Mathf.PI * 2f);
        Color color = _availableColor * droppedTint;
        color.a = _availableColor.a * droppedTint.a * Mathf.Lerp(droppedMinAlpha, 1f, wave);
        _availableGraphic.color = color;
        _availableGraphic.rectTransform.localScale = _availableScale * (1f + droppedScalePulse * wave);
    }
}

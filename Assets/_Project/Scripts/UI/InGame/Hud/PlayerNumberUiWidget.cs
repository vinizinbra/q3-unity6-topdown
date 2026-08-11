using Quantum;
using QuantumUser.View.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One player's number ("1"/"2"/...) and hero-color accent for the party HUD - both resolved once in
// Initialize since neither changes after a player spawns (PlayerRef is fixed per session,
// CharacterData is fixed per hero pick). Number comes from PlayerRef._index via the entity's own
// CharView (same lookup PlayerPortraitUiWidget uses for the head sprite); color is
// CharacterData.RingColor, the same tint MovementRingView already uses for "this is you" ground
// markers, reused here as this player's HUD accent instead of authoring a second palette.
public class PlayerNumberUiWidget : MonoBehaviour
{
    [SerializeField] private TMP_Text numberText;
    [SerializeField, Tooltip("Optional - tinted with the hero's CharacterData.RingColor (Image, TMP_Text, or any other Graphic). Left unassigned, this feature is simply off.")]
    private Graphic colorAccent;

    public void Initialize(EntityRef entityRef)
    {
        CharView charView = EntityViewManager.Instance != null ? EntityViewManager.Instance.GetCharViewByEntityRef(entityRef) : null;

        if (numberText != null && charView != null)
            numberText.text = charView.PlayerRef._index.ToString();

        if (colorAccent == null || charView == null || charView.Game == null)
            return;

        Frame frame = charView.Game.Frames.Predicted;

        if (frame.TryGet<CharacterStats>(entityRef, out var stats) == false)
            return;

        CharacterData data = frame.FindAsset(stats.CharacterData);

        if (data != null)
            colorAccent.color = data.RingColor;
    }
}

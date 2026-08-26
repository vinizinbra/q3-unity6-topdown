using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One party slot. The local player is never shown in one of these - they get the main character
// preview instead - so every slot is a REMOTE party member, filled in order with no gaps (see
// PartyRoomWidget.RefreshRoster).
public class RoomWidget : MonoBehaviour
{
    // Slot content (an occupied-player visual vs an empty-slot placeholder), not the widget's own
    // GameObject - the widget itself stays active so the roster shows a fixed number of slots
    // (up to MaxPlayers) instead of collapsing/reflowing every time someone joins or leaves.
    public GameObject activeState;
    public GameObject inactiveState;

    public TMP_Text name;
    public TMP_Text characterName;
    public GameObject readyObject;
    public GameObject leaderObject;

    [Tooltip("Optional - shows this party member's chosen hero as a live animated preview, the same way the local player's own pick is shown. Each one costs its own camera and RenderTexture every frame, so leave it unassigned on slots that only need a name. MUST have Follow Local Selection turned OFF on the widget itself, or it will show YOUR character in every slot - and must NEVER be the local player's own main preview, which no slot owns.")]
    public CharacterPreviewWidget characterPreview;

    [Tooltip("Optional - the hero's portrait, taken straight off their view prefab's own rig rather than a separately authored sprite, so it always matches the character art. Their head where they have one, otherwise their whole body (Lux is drawn as a single piece). Hidden while the slot is empty.")]
    public Image characterIcon;

    [Tooltip("Optional - tinted with the hero's own CharacterData.RingColor, the same colour that marks this player's ground ring in the match, so a teammate reads as the same colour in the lobby and in game. The image's own alpha is preserved, so a translucent background stays translucent.")]
    public Image characterBackground;

    public void Setup(string playerName, bool isReady, string characterDisplayName = null, bool isLeader = false, string characterId = null)
    {
        bool occupied = !string.IsNullOrEmpty(playerName);
        if (activeState != null)
            activeState.SetActive(occupied);
        if (inactiveState != null)
            inactiveState.SetActive(!occupied);
        name.text = playerName;
        if (readyObject != null)
            readyObject.SetActive(occupied && isReady);
        if (characterName != null)
            characterName.text = occupied ? characterDisplayName : string.Empty;
        if (leaderObject != null)
            leaderObject.SetActive(occupied && isLeader);

        // Cleared rather than left on the last occupant: an empty slot must not keep showing the
        // hero of whoever used to be in it. Show/Clear both no-op when nothing actually changed.
        if (characterPreview != null)
        {
            if (occupied)
                characterPreview.ShowCharacterId(characterId);
            else
                characterPreview.Clear();
        }

        ApplyCharacterVisuals(occupied ? characterId : null);
    }

    // Head icon and signature colour, both resolved from the shared CharacterCatalog rather than
    // authored per slot - a slot shows whoever happens to be in it, so it can't hold per-hero art
    // of its own.
    private void ApplyCharacterVisuals(string characterId)
    {
        var catalog = PartyManager.Instance != null ? PartyManager.Instance.characterCatalog : null;
        bool resolved = catalog != null && string.IsNullOrEmpty(characterId) == false;

        if (characterIcon != null)
        {
            Sprite icon = resolved ? catalog.ResolveIconSprite(characterId) : null;
            characterIcon.sprite = icon;
            // A head is roughly square but a whole-body fallback is tall, so the same icon rect has
            // to hold both without squashing one of them.
            characterIcon.preserveAspect = true;
            // Disabled rather than left with a null sprite, which Unity draws as a white box.
            characterIcon.enabled = icon != null;
        }

        if (characterBackground == null)
            return;

        if (resolved && catalog.TryResolveRingColor(characterId, out Color ringColor))
        {
            // Alpha comes from however the background was authored, not from the hero's colour -
            // RingColor is tuned for an opaque ground ring and would otherwise override a
            // deliberately translucent panel.
            ringColor.a = characterBackground.color.a;
            characterBackground.color = ringColor;
        }
    }
}
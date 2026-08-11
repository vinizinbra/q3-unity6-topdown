using TMPro;
using UnityEngine;

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

    public void Setup(string playerName, bool isReady, string characterDisplayName = null, bool isLeader = false)
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
    }
}
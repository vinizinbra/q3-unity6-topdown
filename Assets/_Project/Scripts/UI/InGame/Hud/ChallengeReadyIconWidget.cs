using UnityEngine;

// One "is this Raider Ready" indicator slot inside InteractionPromptWidget's own Optional Team
// Challenge "challenge area" row (see docs). InteractionPromptWidget holds a fixed pool of these
// (sized to the game's max party size) - it shows/hides each slot's own GameObject based on the
// LIVE connected-player count, then toggles this slot's Ready/idle visual for whichever slot is
// shown, via SetReady.
public class ChallengeReadyIconWidget : MonoBehaviour
{
    [SerializeField, Tooltip("Shown while this Raider IS Ready.")]
    private GameObject readyVisual;

    [SerializeField, Tooltip("Shown while this Raider is NOT Ready yet.")]
    private GameObject idleVisual;

    public void SetReady(bool ready)
    {
        SetActive(readyVisual, ready);
        SetActive(idleVisual, ready == false);
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }
}

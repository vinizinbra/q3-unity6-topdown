using UnityEngine;
using UnityEngine.UI;

// "Skip Training" button on the Training Zone HUD (exists only in TrainingZoneScene's Canvas) - leaves
// the offline session back to the menu through the same LeaveMatch path as the in-match settings
// Disconnect button. See docs/training-zone.md.
public class TrainingZoneSkipWidget : MonoBehaviour
{
    [SerializeField] private Button button;

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        button.onClick.AddListener(Skip);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(Skip);
    }

    // LeaveMatch itself ignores a second click while its loading cover is already up.
    private void Skip()
    {
        if (MatchMakingConfig.Instance == null)
            return;

        MatchMakingConfig.Instance.LeaveMatch();
    }
}

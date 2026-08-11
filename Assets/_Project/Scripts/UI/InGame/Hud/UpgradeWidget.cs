using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One row in UpgradePopupWidget's hero/global/rift lists - plain data-in widget with no Quantum
// awareness of its own, same idiom as UpgradeCardWidget/PartyHistoryUpgradeWidget. Level is the
// upgrade's UpgradeHistoryEntry.Count (times picked) - hidden when 0.
public class UpgradeWidget : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text displayName;
    [SerializeField] private TMP_Text description;
    [SerializeField, Tooltip("Shows the pick count (e.g. \"Lv. 2\") - hidden when level is 0.")]
    private GameObject levelRoot;
    [SerializeField] private TMP_Text levelText;

    public void Setup(Sprite icon, string title, string description, int level)
    {
        if (this.icon != null)
            this.icon.sprite = icon;

        if (displayName != null)
            displayName.text = title;

        if (this.description != null)
            this.description.text = description;

        bool showLevel = level > 0;

        if (levelRoot != null)
            levelRoot.SetActive(showLevel);

        if (levelText != null && showLevel)
            levelText.text = level.ToString();
    }
}

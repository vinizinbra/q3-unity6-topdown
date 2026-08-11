using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One icon in PartyHistoryUpgradeContainer's grid - plain data-in widget with no Quantum awareness
// of its own, same idiom as UpgradeCardWidget. countText only shows once count is above 1, so a
// single pick of anything reads as a bare icon and a repeated pick (e.g. an uncapped Global Upgrade
// taken 3 times) reads as one icon with "3" on it instead of 3 duplicate icons.
public class PartyHistoryUpgradeWidget : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text countText;

    public void Setup(Sprite icon, int count)
    {
        if (iconImage != null)
            iconImage.sprite = icon;

        if (countText == null)
            return;

        bool shown = count > 1;
        countText.gameObject.SetActive(shown);

        if (shown)
            countText.text = count.ToString();
    }
}

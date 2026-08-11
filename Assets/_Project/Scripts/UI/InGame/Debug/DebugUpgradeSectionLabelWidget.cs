using TMPro;
using UnityEngine;

// Section header inside DebugUpgradeMenuWindow's Hero scrollview ("Dash"/"Hero Skill"/"Passive") -
// lives on its own prefab and owns its own text field, same pattern DebugUpgradeButtonWidget uses
// for its row, rather than DebugUpgradeMenuWindow reaching into a raw TMP_Text directly.
public class DebugUpgradeSectionLabelWidget : MonoBehaviour
{
    [SerializeField] private TMP_Text labelText;

    public void Setup(string text)
    {
        if (labelText != null)
            labelText.text = text;
    }
}

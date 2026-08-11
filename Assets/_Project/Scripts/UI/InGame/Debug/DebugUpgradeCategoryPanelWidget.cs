using UnityEngine;

// One scrollview tab inside DebugUpgradeMenuWindow (Hero/Global/Weapon Perk) - a single shared
// prefab DebugUpgradeMenuWindow.Awake instantiates 3 times, rather than 3 hand-duplicated scrollview
// hierarchies authored separately in the scene. Owns its own Content transform (where buttons/labels
// get added) the same way DebugUpgradeButtonWidget owns its row.
public class DebugUpgradeCategoryPanelWidget : MonoBehaviour
{
    [SerializeField] private Transform content;

    public Transform Content => content;
}

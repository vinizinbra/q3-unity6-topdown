using System;
using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.UI;

// In-game debug panel listing every upgrade the local player's hero can currently be granted -
// populated by DebugUpgradeMenuTrigger (View/Managers/, Quantum-aware) as soon as the local player
// is set up. Deliberately NOT a UiWindow subclass - WindowManager.ShowWindow<T>() hides every other
// window exclusively, which is wrong for a debug panel meant to stay visible alongside gameplay
// (e.g. the HUD).
//
// Starts closed - panelRoot (everything except toggleButton) is hidden at Awake, toggleButton flips
// it open/closed. Content still builds normally while closed (DebugUpgradeMenuTrigger.Rebuild has no
// dependency on visibility), so it's already populated the first time the panel opens.
//
// Five tabs (Hero/Global/Weapon Perk/Rift Mutation/Rift Mark Mutation), only one visible at a time -
// each is a DebugUpgradeCategoryPanelWidget instance, but there's only one hand-authored panelPrefab;
// Awake instantiates it 5 times into panelsParent rather than needing 5 duplicated scrollview
// hierarchies in the scene. heroTabButton/globalTabButton/weaponPerkTabButton/riftTabButton/
// riftMarkTabButton switch which instance is active (Hero is the default open tab). Hero's content is
// shared by all 3 per-hero pools, each under its own section header (AddLabel); Global/WeaponPerk/
// Rift/RiftMark are one category each, no label needed.
public class DebugUpgradeMenuWindow : MonoBehaviour
{
    // Everything except toggleButton itself - toggleButton has to live outside this so it stays
    // clickable while the panel is hidden. This class's own GameObject stays active at all times
    // regardless (see Toggle) so Awake always runs immediately at scene load and _heroPanel/etc. are
    // never null when DebugUpgradeMenuTrigger.Rebuild runs, even if the panel starts closed.
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button toggleButton;

    [SerializeField] private DebugUpgradeCategoryPanelWidget panelPrefab;
    [SerializeField] private Transform panelsParent;

    [SerializeField] private Button heroTabButton;
    [SerializeField] private Button globalTabButton;
    [SerializeField] private Button weaponPerkTabButton;
    [SerializeField] private Button riftTabButton;
    [SerializeField] private Button riftMarkTabButton;

    [SerializeField] private DebugUpgradeButtonWidget buttonPrefab;
    [SerializeField] private DebugUpgradeSectionLabelWidget labelPrefab;

    private DebugUpgradeCategoryPanelWidget _heroPanel;
    private DebugUpgradeCategoryPanelWidget _globalPanel;
    private DebugUpgradeCategoryPanelWidget _weaponPerkPanel;
    private DebugUpgradeCategoryPanelWidget _riftPanel;
    private DebugUpgradeCategoryPanelWidget _riftMarkPanel;

    public Transform HeroContent => _heroPanel.Content;
    public Transform GlobalContent => _globalPanel.Content;
    public Transform WeaponPerkContent => _weaponPerkPanel.Content;
    public Transform RiftContent => _riftPanel.Content;
    public Transform RiftMarkContent => _riftMarkPanel.Content;

    private void Awake()
    {
        _heroPanel = CreatePanel();
        _globalPanel = CreatePanel();
        _weaponPerkPanel = CreatePanel();
        _riftPanel = CreatePanel();
        _riftMarkPanel = CreatePanel();

        // panelPrefab/buttonPrefab/labelPrefab are live template objects sitting in the scene, not
        // Project-window .prefab assets - left active, each would otherwise render as one extra
        // stray copy alongside its real clones. Instantiate() copies the source's active state onto
        // the clone, so every clone (above and in AddLabel/AddButton) force-activates itself right
        // after spawning rather than inheriting "disabled" from an already-hidden template.
        panelPrefab.gameObject.SetActive(false);
        buttonPrefab.gameObject.SetActive(false);
        labelPrefab.gameObject.SetActive(false);

        heroTabButton.onClick.AddListener(() => ShowPanel(_heroPanel));
        globalTabButton.onClick.AddListener(() => ShowPanel(_globalPanel));
        weaponPerkTabButton.onClick.AddListener(() => ShowPanel(_weaponPerkPanel));

        // Not wired in the scene until the Rift tab button is manually cloned - null here shouldn't
        // take the rest of Awake() down with it (the toggle button/other 3 tabs still have to work).
        if (riftTabButton != null)
        {
            riftTabButton.onClick.AddListener(() => ShowPanel(_riftPanel));
        }
        else
        {
            LogHelper.Warn("DebugUpgradeMenu", "riftTabButton not assigned - Rift Mutation tab is unreachable until it's wired in the Inspector.");
        }

        // Not wired in the scene until the Rift Mark tab button is manually cloned - same
        // null-guarded fallback as riftTabButton above.
        if (riftMarkTabButton != null)
        {
            riftMarkTabButton.onClick.AddListener(() => ShowPanel(_riftMarkPanel));
        }
        else
        {
            LogHelper.Warn("DebugUpgradeMenu", "riftMarkTabButton not assigned - Rift Mark Mutation tab is unreachable until it's wired in the Inspector.");
        }

        ShowPanel(_heroPanel);

        toggleButton.onClick.AddListener(Toggle);
        panelRoot.SetActive(false);
    }

    public void Toggle()
    {
        panelRoot.SetActive(panelRoot.activeSelf == false);
    }

    private DebugUpgradeCategoryPanelWidget CreatePanel()
    {
        DebugUpgradeCategoryPanelWidget panel = Instantiate(panelPrefab, panelsParent);
        panel.gameObject.SetActive(true);
        return panel;
    }

    private void ShowPanel(DebugUpgradeCategoryPanelWidget panel)
    {
        _heroPanel.gameObject.SetActive(panel == _heroPanel);
        _globalPanel.gameObject.SetActive(panel == _globalPanel);
        _weaponPerkPanel.gameObject.SetActive(panel == _weaponPerkPanel);
        _riftPanel.gameObject.SetActive(panel == _riftPanel);
        _riftMarkPanel.gameObject.SetActive(panel == _riftMarkPanel);
    }

    public void Clear()
    {
        ClearContent(_heroPanel.Content);
        ClearContent(_globalPanel.Content);
        ClearContent(_weaponPerkPanel.Content);
        ClearContent(_riftPanel.Content);
        ClearContent(_riftMarkPanel.Content);
    }

    private static void ClearContent(Transform content)
    {
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
    }

    // Sub-group header inside HeroContent, e.g. "Dash"/"Hero Skill"/"Passive".
    public void AddLabel(Transform content, string text)
    {
        DebugUpgradeSectionLabelWidget label = Instantiate(labelPrefab, content);
        label.gameObject.SetActive(true);
        label.Setup(text);
    }

    public void AddButton(Transform content, string category, string displayName, Sprite icon, string description, bool granted, Action onActivate, Action onDeactivate,
        int currentStacks = 0, int maxStacks = 0, Func<int, string> getDescriptionForRank = null)
    {
        DebugUpgradeButtonWidget button = Instantiate(buttonPrefab, content);
        button.gameObject.SetActive(true);
        button.Setup(category, displayName, icon, description, granted, onActivate, onDeactivate, currentStacks, maxStacks, getDescriptionForRank);
    }
}

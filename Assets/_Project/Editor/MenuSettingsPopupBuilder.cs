using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the MenuSettingsPopup hierarchy into MenuScene, parented under its PopupManager (which
// discovers popups via GetComponentsInChildren at Awake, so parenting is the whole registration
// step), wires every serialized field, and hooks the main menu's existing "SettingsButton" onClick
// to MenuSettingsPopup.Open. Shares its UGUI helpers with InMatchSettingsPopupBuilder.
//
// Only the ACTIVE scene is searched, so it also works with MenuScene opened additively next to a
// gameplay scene. Re-running is safe: an existing popup is selected instead of duplicated.
public static class MenuSettingsPopupBuilder
{
    [MenuItem("Tools/RiftRaiders/UI/Create Menu Settings Popup")]
    internal static void Create()
    {
        var existing = FindInActiveScene<MenuSettingsPopup>();

        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            WireSettingsButton(existing);
            EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
            Debug.LogWarning("[MenuSettingsPopup] one already exists in this scene - selected it (and re-checked the SettingsButton wiring) instead of building a second one.", existing);
            return;
        }

        var manager = FindInActiveScene<PopupManager>();

        if (manager == null)
        {
            Debug.LogError("[MenuSettingsPopup] no PopupManager in the open scene - open MenuScene (as the active scene) and run this again.");
            return;
        }

        var rootGo = new GameObject("MenuSettingsPopup", typeof(RectTransform), typeof(CanvasGroup), typeof(MenuSettingsPopup));
        rootGo.transform.SetParent(manager.transform, false);
        Undo.RegisterCreatedObjectUndo(rootGo, "Create Menu Settings Popup");
        InMatchSettingsPopupBuilder.Stretch((RectTransform)rootGo.transform);

        TMP_FontAsset font = InMatchSettingsPopupBuilder.ResolveFont();

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(rootGo.transform, false);
        var panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(760f, 820f);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.13f, 0.97f);

        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(48, 48, 40, 40);
        layout.spacing = 24f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TMP_Text title = InMatchSettingsPopupBuilder.CreateText(panel.transform, "Title", "SETTINGS", 52f, font, FontStyles.Bold);
        InMatchSettingsPopupBuilder.SetHeight(title.gameObject, 70f);

        Slider sfx = InMatchSettingsPopupBuilder.CreateSliderRow(panel.transform, "Sfx", "SFX", font);
        Slider music = InMatchSettingsPopupBuilder.CreateSliderRow(panel.transform, "Music", "MUSIC", font);
        Slider voice = InMatchSettingsPopupBuilder.CreateSliderRow(panel.transform, "Voice", "VOICE", font);

        TMP_Dropdown region = CreateDropdownRow(panel.transform, "Region", "REGION", font);

        TMP_Text hint = InMatchSettingsPopupBuilder.CreateText(panel.transform, "RegionHint", "", 24f, font, FontStyles.Italic);
        hint.color = new Color(1f, 1f, 1f, 0.6f);
        InMatchSettingsPopupBuilder.SetHeight(hint.gameObject, 36f);

        Button close = InMatchSettingsPopupBuilder.CreateButton(panel.transform, "CloseButton", "CLOSE", font, new Color(0.3f, 0.32f, 0.4f, 1f));

        var popup = rootGo.GetComponent<MenuSettingsPopup>();
        var serialized = new SerializedObject(popup);
        serialized.FindProperty("canvasGroup").objectReferenceValue = rootGo.GetComponent<CanvasGroup>();
        serialized.FindProperty("closeButton").objectReferenceValue = close;
        serialized.FindProperty("sfxSlider").objectReferenceValue = sfx;
        serialized.FindProperty("musicSlider").objectReferenceValue = music;
        serialized.FindProperty("voiceSlider").objectReferenceValue = voice;
        serialized.FindProperty("regionDropdown").objectReferenceValue = region;
        serialized.FindProperty("regionHintText").objectReferenceValue = hint;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        WireSettingsButton(popup);

        // Popups start hidden - PopupManager activates every one in Awake and hides them all in
        // Start, so this is only about how the scene looks while editing.
        rootGo.SetActive(false);

        Selection.activeGameObject = rootGo;
        EditorSceneManager.MarkSceneDirty(rootGo.scene);

        Debug.Log("[MenuSettingsPopup] built and wired under PopupManager.", rootGo);
    }

    // MenuScene's TopBar "SettingsButton" was authored as a bare Image, so a Button is added when
    // missing. Idempotent: a listener already pointing at Open is not added twice.
    private static void WireSettingsButton(MenuSettingsPopup popup)
    {
        foreach (var root in popup.gameObject.scene.GetRootGameObjects())
        foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rect.name != "SettingsButton")
                continue;

            var button = rect.GetComponent<Button>();
            if (button == null)
            {
                button = Undo.AddComponent<Button>(rect.gameObject);
                button.targetGraphic = rect.GetComponent<Graphic>();
            }

            for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentTarget(i) == popup && button.onClick.GetPersistentMethodName(i) == nameof(MenuSettingsPopup.Open))
                    return;
            }

            Undo.RecordObject(button, "Wire Settings Button");
            UnityEventTools.AddPersistentListener(button.onClick, popup.Open);
            EditorUtility.SetDirty(button);
            Debug.Log("[MenuSettingsPopup] SettingsButton.onClick -> MenuSettingsPopup.Open.", button);
            return;
        }

        Debug.LogWarning("[MenuSettingsPopup] no 'SettingsButton' found - wire a button's onClick to MenuSettingsPopup.Open by hand.");
    }

    private static T FindInActiveScene<T>() where T : Component
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var found = root.GetComponentInChildren<T>(true);
            if (found != null)
                return found;
        }

        return null;
    }

    private static TMP_Dropdown CreateDropdownRow(Transform parent, string name, string label, TMP_FontAsset font)
    {
        var row = new GameObject(name + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        InMatchSettingsPopupBuilder.SetHeight(row, 64f);

        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 24f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        TMP_Text text = InMatchSettingsPopupBuilder.CreateText(row.transform, "Label", label, 34f, font, FontStyles.Normal);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.gameObject.AddComponent<LayoutElement>().preferredWidth = 180f;

        GameObject dropdownGo = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        dropdownGo.name = name + "Dropdown";
        dropdownGo.transform.SetParent(row.transform, false);
        dropdownGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var dropdown = dropdownGo.GetComponent<TMP_Dropdown>();
        dropdown.ClearOptions();

        // Default-control text is sized for a 160x30 control; scale it up and use the scene font.
        foreach (var tmp in dropdownGo.GetComponentsInChildren<TMP_Text>(true))
        {
            tmp.fontSize = 28f;
            if (font != null)
                tmp.font = font;
        }

        // Taller list items to match the bigger text, and room for the whole region list.
        var template = dropdown.template;
        template.sizeDelta = new Vector2(template.sizeDelta.x, 520f);
        var item = template.GetComponentInChildren<Toggle>(true);
        if (item != null)
        {
            var itemRect = (RectTransform)item.transform;
            itemRect.sizeDelta = new Vector2(itemRect.sizeDelta.x, 48f);
            var content = (RectTransform)itemRect.parent;
            content.sizeDelta = new Vector2(content.sizeDelta.x, 52f);
        }

        return dropdown;
    }
}

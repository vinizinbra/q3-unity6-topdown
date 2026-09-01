namespace Quantum.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEditor.IMGUI.Controls;
    using UnityEngine;

    // Attribute drawers take priority over type drawers, so this replaces Quantum's AssetRefDrawer
    // on [ExpandableAsset] fields only. It reuses AssetRefDrawer.DrawAsset for the object field
    // itself and adds a foldout that edits the referenced asset inline.
    [CustomPropertyDrawer(typeof(ExpandableAssetAttribute))]
    public class ExpandableAssetDrawer : PropertyDrawer
    {
        private const float BoxPadding = 4f;
        private const float CreateButtonWidth = 20f;
        private const float CreateNestedButtonWidth = 24f;
        private const float MenuButtonWidth = 20f;

        // Alpha-blended rather than opaque so nested expandables (box drawn inside a box) darken
        // with depth instead of all looking identical - a free visual cue for how deep you are.
        private static readonly Color ExpandedBackgroundColor = new Color(0.3f, 0.55f, 1f, 0.09f);

        // Bold label so an [ExpandableAsset] field reads as its own composition slot at a glance,
        // distinct from the plain tuning fields around it - built lazily since EditorStyles isn't
        // safe to touch outside OnGUI/layout callbacks.
        private static GUIStyle boldFoldoutStyle;
        private static GUIStyle BoldFoldoutStyle => boldFoldoutStyle ??= new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

        private static GUIStyle boldLabelStyle;
        private static GUIStyle BoldLabelStyle => boldLabelStyle ??= new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };

        private static readonly Dictionary<int, SerializedObject> NestedAssets = new Dictionary<int, SerializedObject>();

        // An effect that spawns a projectile can reference its way back to the asset it is nested
        // under; without this the drawer would recurse until the stack blew.
        private static readonly HashSet<int> AssetsBeingDrawn = new HashSet<int>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty guidProperty = property.FindPropertyRelativeOrThrow(AssetRefDrawer.RawValuePath);
            AssetObject asset = ResolveAsset(guidProperty);

            Rect headerRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            DrawHeader(headerRect, property, label, guidProperty, asset);

            if (IsExpanded(property, asset) == false)
                return;

            Rect bodyRect = new Rect(position.x, headerRect.yMax + EditorGUIUtility.standardVerticalSpacing,
                position.width, position.height - headerRect.height - EditorGUIUtility.standardVerticalSpacing);
            DrawNestedAsset(bodyRect, asset);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;

            SerializedProperty guidProperty = property.FindPropertyRelativeOrThrow(AssetRefDrawer.RawValuePath);
            AssetObject asset = ResolveAsset(guidProperty);

            if (IsExpanded(property, asset) == false)
                return height;

            return height + EditorGUIUtility.standardVerticalSpacing + GetNestedAssetHeight(asset);
        }

        private void DrawHeader(Rect rect, SerializedProperty property, GUIContent label,
            SerializedProperty guidProperty, AssetObject asset)
        {
            EditorGUI.BeginProperty(rect, label, guidProperty);

            Rect labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
            if (asset != null)
            {
                bool wasExpanded = property.isExpanded;
                property.isExpanded = EditorGUI.Foldout(labelRect, property.isExpanded, label, true, BoldFoldoutStyle);
                if (property.isExpanded && wasExpanded == false)
                    CollapseSiblingExpandables(property);
            }
            else
            {
                EditorGUI.LabelField(labelRect, label, BoldLabelStyle);
            }

            Rect valueRect = new Rect(labelRect.xMax, rect.y, rect.width - labelRect.width, rect.height);
            Type assetType = GetAssetType();

            EditorGUI.BeginChangeCheck();

            AssetObject selected;
            using (new EditorGUI.IndentLevelScope(-EditorGUI.indentLevel))
            {
                AssetGuid guid = (AssetGuid)guidProperty.longValue;
                if (guid.IsValid == false)
                {
                    float buttonsWidth = CreateButtonWidth + 5f + CreateNestedButtonWidth;
                    valueRect.width -= buttonsWidth + 5f;
                    selected = AssetRefDrawer.DrawAsset(valueRect, guid, assetType);
                    DrawCreateButton(new Rect(valueRect.xMax + 5f, rect.y, CreateButtonWidth, rect.height),
                        guidProperty, assetType);
                    DrawCreateNestedButton(new Rect(valueRect.xMax + 5f + CreateButtonWidth + 5f, rect.y,
                        CreateNestedButtonWidth, rect.height), guidProperty, assetType);
                }
                else if (asset != null)
                {
                    valueRect.width -= MenuButtonWidth + 5f;
                    selected = AssetRefDrawer.DrawAsset(valueRect, guid, assetType);
                    DrawContextMenuButton(new Rect(valueRect.xMax + 5f, rect.y, MenuButtonWidth, rect.height),
                        guidProperty, asset);
                }
                else
                {
                    // A valid guid that still resolves to nothing usually means its script was
                    // deleted - QuantumUnityDB can't construct the type, so it silently returns
                    // null and the normal ⋮ context menu above (which needs a resolved AssetObject)
                    // never has a chance to render. This is the only place left to clear it.
                    valueRect.width -= MenuButtonWidth + 5f;
                    selected = AssetRefDrawer.DrawAsset(valueRect, guid, assetType);
                    DrawBrokenReferenceButton(new Rect(valueRect.xMax + 5f, rect.y, MenuButtonWidth, rect.height),
                        guidProperty);
                }
            }

            if (EditorGUI.EndChangeCheck())
                guidProperty.longValue = selected != null ? selected.Guid.Value : 0L;

            EditorGUI.EndProperty();
        }

        // Only one field expanded per object at a time - keeps a chain of several [ExpandableAsset]
        // fields on the same asset from all unfolding at once and burying the inspector.
        private static void CollapseSiblingExpandables(SerializedProperty property)
        {
            foreach (SerializedProperty sibling in EnumerateExpandableProperties(property.serializedObject))
            {
                if (sibling.propertyPath != property.propertyPath)
                    sibling.isExpanded = false;
            }
        }

        private static IEnumerable<SerializedProperty> EnumerateExpandableProperties(SerializedObject serializedObject)
        {
            foreach (FieldInfo field in GetExpandableFields(serializedObject.targetObject.GetType()))
            {
                SerializedProperty property = serializedObject.FindProperty(field.Name);
                if (property == null)
                    continue;

                if (property.isArray && property.propertyType != SerializedPropertyType.String)
                {
                    for (int i = 0; i < property.arraySize; i++)
                        yield return property.GetArrayElementAtIndex(i);
                }
                else
                {
                    yield return property;
                }
            }
        }

        private static void DrawNestedAsset(Rect rect, AssetObject asset)
        {
            SerializedObject nested = GetNestedSerializedObject(asset);
            nested.UpdateIfRequiredOrScript();

            EditorGUI.DrawRect(rect, ExpandedBackgroundColor);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            Rect fieldRect = new Rect(rect.x + BoxPadding, rect.y + BoxPadding, rect.width - BoxPadding * 2f, 0f);

            AssetsBeingDrawn.Add(asset.GetInstanceID());
            EditorGUI.indentLevel++;
            try
            {
                foreach (SerializedProperty child in EditableChildren(nested))
                {
                    fieldRect.height = EditorGUI.GetPropertyHeight(child, true);
                    EditorGUI.PropertyField(fieldRect, child, true);
                    fieldRect.y += fieldRect.height + EditorGUIUtility.standardVerticalSpacing;
                }
            }
            finally
            {
                EditorGUI.indentLevel--;
                AssetsBeingDrawn.Remove(asset.GetInstanceID());
            }

            nested.ApplyModifiedProperties();
        }

        private static float GetNestedAssetHeight(AssetObject asset)
        {
            SerializedObject nested = GetNestedSerializedObject(asset);
            nested.UpdateIfRequiredOrScript();

            float height = BoxPadding * 2f;

            AssetsBeingDrawn.Add(asset.GetInstanceID());
            try
            {
                foreach (SerializedProperty child in EditableChildren(nested))
                    height += EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing;
            }
            finally
            {
                AssetsBeingDrawn.Remove(asset.GetInstanceID());
            }

            return height;
        }

        // Identifier is the asset's own path/guid plumbing, which is exactly the noise an inline
        // view is meant to hide; Quantum's own inspector shows it under "Quantum Unity DB".
        private static IEnumerable<SerializedProperty> EditableChildren(SerializedObject serializedObject)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyPath == QuantumEditorGUI.ScriptPropertyName)
                    continue;

                if (iterator.propertyPath == nameof(AssetObject.Identifier))
                    continue;

                yield return iterator.Copy();
            }
        }

        private static bool IsExpanded(SerializedProperty property, AssetObject asset)
        {
            return asset != null
                && property.isExpanded
                && AssetsBeingDrawn.Contains(asset.GetInstanceID()) == false;
        }

        private static AssetObject ResolveAsset(SerializedProperty guidProperty)
        {
            if (guidProperty.hasMultipleDifferentValues)
                return null;

            return ResolveAsset((AssetGuid)guidProperty.longValue);
        }

        private static AssetObject ResolveAsset(AssetGuid guid)
        {
            if (guid.IsValid == false || guid.IsDynamic)
                return null;

            try
            {
                return QuantumUnityDB.GetGlobalAssetEditorInstance(guid);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static SerializedObject GetNestedSerializedObject(AssetObject asset)
        {
            int instanceId = asset.GetInstanceID();

            if (NestedAssets.TryGetValue(instanceId, out SerializedObject cached) && cached.targetObject != null)
                return cached;

            cached = new SerializedObject(asset);
            NestedAssets[instanceId] = cached;
            return cached;
        }

        private Type GetAssetType()
        {
            Type fieldType = fieldInfo.FieldType.GetUnityLeafType();
            return fieldType.IsGenericType ? fieldType.GetGenericArguments()[0] : typeof(AssetObject);
        }

        private static void DrawCreateButton(Rect rect, SerializedProperty guidProperty, Type assetType)
        {
            if (GUI.Button(rect, new GUIContent("+", "Create as a new top-level asset file"), EditorStyles.miniButton) == false)
                return;

            SelectConcreteType(rect, assetType, chosen =>
                TextInputWizard.Show("Create Asset", "Name", chosen.Name, name => CreateAsset(rect, guidProperty, chosen, name)));
        }

        // For fields that will never be reused across parents - keeps the Project window from
        // filling up with one file per node in a long Weapon -> Projectile -> Movement chain.
        private static void DrawCreateNestedButton(Rect rect, SerializedProperty guidProperty, Type assetType)
        {
            if (GUI.Button(rect, new GUIContent("⊕", "Create nested inside this asset (shown as a child of it in the Project window)"), EditorStyles.miniButton) == false)
                return;

            SelectConcreteType(rect, assetType, chosen =>
                TextInputWizard.Show("Create Nested Asset", "Name", chosen.Name, name => CreateNestedAsset(guidProperty, chosen, name)));
        }

        private static void SelectConcreteType(Rect rect, Type assetType, Action<Type> onChosen)
        {
            List<Type> candidates = new List<Type>();
            if (assetType.IsAbstract == false)
                candidates.Add(assetType);
            candidates.AddRange(TypeCache.GetTypesDerivedFrom(assetType)
                .Where(x => x.IsAbstract == false && x.IsGenericTypeDefinition == false));

            if (candidates.Count == 1)
            {
                onChosen(candidates[0]);
            }
            else if (candidates.Count > 1)
            {
                new TypeSelectDropdown(candidates, onChosen).Show(rect);
            }
        }

        // AdvancedDropdown gets a search field for free, unlike EditorUtility.DisplayCustomMenu -
        // worth it once an abstract asset type has more than a handful of concrete subclasses to
        // scroll through.
        private class TypeSelectDropdown : AdvancedDropdown
        {
            private readonly List<Type> types;
            private readonly Action<Type> onChosen;

            public TypeSelectDropdown(List<Type> types, Action<Type> onChosen) : base(new AdvancedDropdownState())
            {
                this.types = types;
                this.onChosen = onChosen;
                minimumSize = new Vector2(minimumSize.x, 250f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                AdvancedDropdownItem root = new AdvancedDropdownItem("Select Type");
                foreach (Type type in types)
                    root.AddChild(new TypeDropdownItem(type));
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is TypeDropdownItem typeItem)
                    onChosen(typeItem.Type);
            }

            private class TypeDropdownItem : AdvancedDropdownItem
            {
                public readonly Type Type;

                public TypeDropdownItem(Type type) : base(type.FullName)
                {
                    Type = type;
                }
            }
        }

        // The owner is the asset the field is being authored on, which for an expanded ref is the
        // nested asset rather than the root one - so a chain grows outward folder by folder instead
        // of piling into one place. When the owner has no path of its own (a component on a scene
        // object, i.e. a View-side config asset) there's no natural home to inherit, so the user
        // picks one via ShowFolderPicker instead of guessing.
        private static void CreateAsset(Rect rect, SerializedProperty guidProperty, Type assetType, string assetName)
        {
            string ownerPath = AssetDatabase.GetAssetPath(guidProperty.serializedObject.targetObject);
            if (string.IsNullOrEmpty(ownerPath) == false)
            {
                CreateAssetInFolder(guidProperty, assetType, Path.GetDirectoryName(ownerPath).Replace('\\', '/'), assetName);
                return;
            }

            ShowFolderPicker(rect, folder => CreateAssetInFolder(guidProperty, assetType, folder, assetName));
        }

        // Mirrors CreateNestedAsset below: Refresh() + SetGuidValue (rather than writing straight
        // into the guidProperty captured when the button was clicked) so a freshly-created top-level
        // asset's guid is actually committed before it's assigned, and so the write survives even if
        // this callback lands on a later editor tick (e.g. after the type-select dropdown closes) by
        // which point the original SerializedProperty may already be stale.
        private static void CreateAssetInFolder(SerializedProperty guidProperty, Type assetType, string folder, string assetName)
        {
            AssetObject asset = ScriptableObject.CreateInstance(assetType) as AssetObject;
            asset.name = assetName;
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{assetName}.asset");

            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SetGuidValue(guidProperty, asset.Guid.Value);
        }

        // Lets the user pick an existing subfolder of DataRoot (e.g. ViewConfigs) or create a new
        // one, instead of silently guessing where a component-hosted asset with no owner path
        // should live.
        private const string DataRoot = "Assets/_Project/Data";
        private const string NewFolderOption = "New Folder...";

        private static void ShowFolderPicker(Rect rect, Action<string> onChosen)
        {
            EnsureFolderExists(DataRoot);

            List<string> optionNames = AssetDatabase.GetSubFolders(DataRoot)
                .Select(Path.GetFileName)
                .ToList();
            optionNames.Add(NewFolderOption);

            EditorUtility.DisplayCustomMenu(rect, optionNames.Select(n => new GUIContent(n)).ToArray(), -1,
                (_, menuOptions, chosen) =>
                {
                    if (menuOptions[chosen] == NewFolderOption)
                    {
                        TextInputWizard.Show("New Folder", "Folder Name", string.Empty, folderName =>
                        {
                            string path = $"{DataRoot}/{folderName}";
                            EnsureFolderExists(path);
                            onChosen(path);
                        });
                    }
                    else
                    {
                        onChosen($"{DataRoot}/{menuOptions[chosen]}");
                    }
                }, null);
        }

        private static void CreateNestedAsset(SerializedProperty guidProperty, Type assetType, string assetName)
        {
            UnityEngine.Object owner = guidProperty.serializedObject.targetObject;
            AssetObject asset = owner.CreateNestedScriptableObjectAsset(assetType, assetName) as AssetObject;

            SetGuidValue(guidProperty, asset.Guid.Value);
        }

        // AssetDatabase.Refresh() can reimport the owner and tear down the Inspector's cached
        // SerializedObject out from under an in-flight callback (e.g. a ScriptableWizard confirm),
        // so the guidProperty captured before the refresh may already be disposed by the time this
        // runs. Re-resolving a fresh SerializedObject by path avoids the disposed-property crash.
        private static void SetGuidValue(SerializedProperty guidProperty, long value)
        {
            UnityEngine.Object owner = guidProperty.serializedObject.targetObject;
            string propertyPath = guidProperty.propertyPath;

            SerializedObject serializedObject = new SerializedObject(owner);
            SerializedProperty refreshedProperty = serializedObject.FindProperty(propertyPath);
            refreshedProperty.longValue = value;
            serializedObject.ApplyModifiedProperties();
        }

        // Reuses ScriptableWizard for its built-in "Create"/"Cancel" buttons and validity gating
        // instead of hand-rolling a modal EditorWindow just for one text field. Shared by nested
        // asset naming and new-folder naming (ShowFolderPicker) - same dialog, different
        // title/label/callback per call site.
        private class TextInputWizard : ScriptableWizard
        {
            private string text = string.Empty;
            private string label = "Name";
            private Action<string> onConfirm;

            public static void Show(string title, string label, string defaultText, Action<string> onConfirm)
            {
                TextInputWizard wizard = DisplayWizard<TextInputWizard>(title, "Create");
                wizard.label = label;
                wizard.text = defaultText;
                wizard.onConfirm = onConfirm;
                wizard.OnWizardUpdate();
            }

            protected override bool DrawWizardGUI()
            {
                EditorGUI.BeginChangeCheck();
                text = EditorGUILayout.TextField(label, text);
                return EditorGUI.EndChangeCheck();
            }

            private void OnWizardUpdate()
            {
                isValid = string.IsNullOrWhiteSpace(text) == false;
            }

            private void OnWizardCreate()
            {
                onConfirm(text);
            }
        }

        // Only clears the dangling reference in this field - if it pointed at a nested sub-asset,
        // that sub-asset's serialized data can still be sitting in the owning file afterwards, since
        // Unity never hands out a live object for something with a missing script (AssetDatabase.
        // LoadAllAssetsAtPath just skips it as a real null - nothing to call RemoveObjectFromAsset
        // on). "Assets > Delete Missing Scripts In Asset" on the owning file cleans that part up.
        private static void DrawBrokenReferenceButton(Rect rect, SerializedProperty guidProperty)
        {
            GUIContent content = new GUIContent("✕", "Reference doesn't resolve to any asset (its script was likely deleted) - clear it");
            if (GUI.Button(rect, content, EditorStyles.miniButton) == false)
                return;

            if (EditorUtility.DisplayDialog("Clear Broken Reference",
                    "This reference doesn't resolve to any asset - its target's script was likely deleted.\n\n" +
                    "Clear it from this field? If it pointed at a nested sub-asset, also run " +
                    "Assets > Delete Missing Scripts In Asset on the owning file afterwards to remove " +
                    "the orphaned data.",
                    "Clear", "Cancel") == false)
                return;

            SetGuidValue(guidProperty, 0L);
        }

        private static void DrawContextMenuButton(Rect rect, SerializedProperty guidProperty, AssetObject asset)
        {
            if (GUI.Button(rect, new GUIContent("⋮", "Rename, extract, clear, or delete this asset"), EditorStyles.miniButton) == false)
                return;

            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Rename"), false,
                () => TextInputWizard.Show("Rename Asset", "Name", asset.name, name => RenameAsset(asset, name)));

            menu.AddItem(new GUIContent("Duplicate (Deep)"), false,
                () => DuplicateAssetDeep(guidProperty, asset));

            if (AssetDatabase.IsSubAsset(asset))
            {
                menu.AddItem(new GUIContent("Extract to Separate File"), false,
                    () => ExtractAsset(guidProperty, asset));
            }

            // Unassigns this field only - the asset itself (and any other field still pointing at
            // it) is untouched. The object field's own drag/picker "None" gesture is often the only
            // other way to do this and can be finicky for a nested sub-asset, so this is the
            // reliable path.
            menu.AddItem(new GUIContent("Clear Reference"), false, () => SetGuidValue(guidProperty, 0L));

            menu.AddItem(new GUIContent("Delete Asset"), false, () => DeleteAsset(guidProperty, asset));
            menu.DropDown(rect);
        }

        // Sub-assets have no path of their own to rename via AssetDatabase.RenameAsset, so their
        // display name is just the object name; top-level assets go through the proper API so the
        // .asset file name on disk stays in sync with what's shown in the Project window.
        private static void RenameAsset(AssetObject asset, string newName)
        {
            if (AssetDatabase.IsSubAsset(asset))
            {
                asset.name = newName;
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }
            else
            {
                string error = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(asset), newName);
                if (string.IsNullOrEmpty(error) == false)
                    LogHelper.Error("ExpandableAssetDrawer", $"Failed to rename asset: {error}");
            }

            AssetDatabase.Refresh();
        }

        private static void DeleteAsset(SerializedProperty guidProperty, AssetObject asset)
        {
            DeleteAssetObject(asset);
            SetGuidValue(guidProperty, 0L);
        }

        private static void DeleteAssetObject(AssetObject asset)
        {
            if (AssetDatabase.IsSubAsset(asset))
            {
                AssetDatabase.RemoveObjectFromAsset(asset);
                UnityEngine.Object.DestroyImmediate(asset, true);
            }
            else
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(asset));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Unity's Project window has no built-in Rename/Delete for sub-assets (the expandable
        // children shown under a nested asset's parent .asset file) - F2 and the native Delete
        // command are only wired to main assets. These MenuItems piggyback on the same "Assets/"
        // command set the Project window's right-click menu is built from, so they show up
        // alongside the native (grayed-out) Rename/Delete entries whenever a sub-asset is selected.
        [MenuItem("Assets/Rename Sub-Asset", false, 20)]
        private static void RenameSubAssetMenuItem()
        {
            AssetObject asset = (AssetObject)Selection.activeObject;
            TextInputWizard.Show("Rename Asset", "Name", asset.name, name => RenameAsset(asset, name));
        }

        [MenuItem("Assets/Rename Sub-Asset", true)]
        private static bool ValidateRenameSubAssetMenuItem()
        {
            return Selection.activeObject is AssetObject asset && AssetDatabase.IsSubAsset(asset);
        }

        [MenuItem("Assets/Delete Sub-Asset", false, 21)]
        private static void DeleteSubAssetMenuItem()
        {
            AssetObject asset = (AssetObject)Selection.activeObject;
            if (EditorUtility.DisplayDialog("Delete Sub-Asset", $"Delete '{asset.name}'? This cannot be undone.", "Delete", "Cancel"))
                DeleteAssetObject(asset);
        }

        [MenuItem("Assets/Delete Sub-Asset", true)]
        private static bool ValidateDeleteSubAssetMenuItem()
        {
            return Selection.activeObject is AssetObject asset && AssetDatabase.IsSubAsset(asset);
        }

        // Companion to DrawBrokenReferenceButton above: that only clears the dangling field
        // reference, not the orphaned nested sub-asset data left behind in the owning file (there's
        // no live object to remove once its script is missing). DeleteMissingNestedScriptableObjects
        // is Photon's own SDK utility for exactly this - it diffs the sub-assets Unity can still load
        // against the file's raw YAML and strips whatever's left over - it just wasn't wired to a
        // menu item anywhere in this project yet.
        [MenuItem("Assets/Delete Missing Scripts In Asset", false, 34)]
        private static void DeleteMissingScriptsInAssetMenuItem()
        {
            HashSet<string> paths = new HashSet<string>(Selection.objects
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => string.IsNullOrEmpty(path) == false));

            int totalRemoved = paths.Sum(AssetDatabaseExt.DeleteMissingNestedScriptableObjects);

            EditorUtility.DisplayDialog("Delete Missing Scripts In Asset",
                totalRemoved > 0
                    ? $"Removed {totalRemoved} orphaned sub-asset(s) with missing scripts."
                    : "No orphaned sub-assets with missing scripts found in the selected asset(s).",
                "OK");
        }

        [MenuItem("Assets/Delete Missing Scripts In Asset", true)]
        private static bool ValidateDeleteMissingScriptsInAssetMenuItem()
        {
            return Selection.objects.Any(x => string.IsNullOrEmpty(AssetDatabase.GetAssetPath(x)) == false);
        }

        // Covers the same aliasing problem as the drawer's own "Duplicate (Deep)" context menu item,
        // but for a top-level .asset file selected directly in the Project window rather than a
        // specific [ExpandableAsset] field.
        [MenuItem("Assets/Duplicate Quantum Asset (Deep)", false, 22)]
        private static void DuplicateAssetDeepMenuItem()
        {
            AssetObject asset = (AssetObject)Selection.activeObject;
            Dictionary<AssetObject, AssetObject> clones = new Dictionary<AssetObject, AssetObject>();
            AssetObject clone = AssetDatabase.IsSubAsset(asset)
                ? DeepDuplicateNested(asset, clones)
                : DeepDuplicateTopLevel(asset, clones);

            if (clone == null)
                return;

            Selection.activeObject = clone;
            EditorGUIUtility.PingObject(clone);
        }

        [MenuItem("Assets/Duplicate Quantum Asset (Deep)", true)]
        private static bool ValidateDuplicateAssetDeepMenuItem()
        {
            return Selection.activeObject is AssetObject;
        }

        // Guid recomputes on extraction since the asset stops being a sub-asset representation,
        // so this only leaves other AssetRef fields pointing at it intact if nothing else referenced it.
        private static void ExtractAsset(SerializedProperty guidProperty, AssetObject asset)
        {
            string ownerFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(asset)).Replace('\\', '/');
            string newPath = AssetDatabase.GenerateUniqueAssetPath($"{ownerFolder}/{asset.name}.asset");

            AssetDatabase.RemoveObjectFromAsset(asset);
            AssetDatabase.CreateAsset(asset, newPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SetGuidValue(guidProperty, asset.Guid.Value);
        }

        // Plain Unity Duplicate only copies the selected .asset file. Nested sub-assets ride along
        // in that copy, but every [ExpandableAsset] field - nested or a separate top-level file - still
        // holds the raw guid it was copied with, so the duplicate keeps aliasing the original's
        // children instead of owning independent ones. This walks the whole [ExpandableAsset] tree
        // and gives the duplicate its own copy of everything reachable from it.
        private static void DuplicateAssetDeep(SerializedProperty guidProperty, AssetObject asset)
        {
            Dictionary<AssetObject, AssetObject> clones = new Dictionary<AssetObject, AssetObject>();
            AssetObject clone = AssetDatabase.IsSubAsset(asset)
                ? DeepDuplicateNested(asset, clones)
                : DeepDuplicateTopLevel(asset, clones);

            if (clone == null)
                return;

            SetGuidValue(guidProperty, clone.Guid.Value);
        }

        // clones is shared across the whole operation (not just this file's own siblings) so a
        // reference cycle - e.g. an effect that points back to the asset it was reached from -
        // resolves to the clone already in flight instead of recursing forever.
        private static AssetObject DeepDuplicateTopLevel(AssetObject source, Dictionary<AssetObject, AssetObject> clones)
        {
            if (clones.TryGetValue(source, out AssetObject existing))
                return existing;

            string sourcePath = AssetDatabase.GetAssetPath(source);
            string folder = Path.GetDirectoryName(sourcePath).Replace('\\', '/');
            string newPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{source.name}.asset");

            AssetObject mainClone = UnityEngine.Object.Instantiate(source);
            mainClone.name = source.name;
            AssetDatabase.CreateAsset(mainClone, newPath);
            clones[source] = mainClone;

            Dictionary<AssetObject, string> nestedNames = new Dictionary<AssetObject, string>();
            foreach (UnityEngine.Object nestedObj in AssetDatabase.LoadAllAssetRepresentationsAtPath(sourcePath))
            {
                if (nestedObj is AssetObject nestedSource == false)
                    continue;

                AssetObject nestedClone = UnityEngine.Object.Instantiate(nestedSource);
                nestedClone.name = nestedSource.name;
                AssetDatabase.AddObjectToAsset(nestedClone, mainClone);
                clones[nestedSource] = nestedClone;
                nestedNames[nestedSource] = nestedSource.name;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Refresh reimports the new file to assign it fresh AssetGuids, which can invalidate the
            // native instances created above - reload every clone from disk before touching them again.
            mainClone = AssetDatabase.LoadAssetAtPath<AssetObject>(newPath);
            clones[source] = mainClone;

            foreach (UnityEngine.Object nestedObj in AssetDatabase.LoadAllAssetRepresentationsAtPath(newPath))
            {
                if (nestedObj is AssetObject reloadedNested == false)
                    continue;

                AssetObject originalNested = nestedNames.FirstOrDefault(kv => kv.Value == reloadedNested.name).Key;
                if (originalNested != null)
                    clones[originalNested] = reloadedNested;
            }

            FixUpFields(mainClone, clones);
            foreach (AssetObject originalNested in nestedNames.Keys)
                FixUpFields(clones[originalNested], clones);

            AssetDatabase.SaveAssets();
            return mainClone;
        }

        private static AssetObject DeepDuplicateNested(AssetObject source, Dictionary<AssetObject, AssetObject> clones)
        {
            if (clones.TryGetValue(source, out AssetObject existing))
                return existing;

            UnityEngine.Object owner = source.FindNestedObjectParent();
            string ownerPath = AssetDatabase.GetAssetPath(owner);

            // The clone lands in the same file as source, so it needs a name distinct from every
            // existing sibling (including source itself) to be unambiguously found again after the
            // reimport below invalidates the native instances created here.
            string tempName = $"__DeepDuplicateTemp_{GUID.Generate()}";
            AssetObject clone = UnityEngine.Object.Instantiate(source);
            clone.name = tempName;
            AssetDatabase.AddObjectToAsset(clone, owner);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            clone = AssetDatabase.LoadAllAssetRepresentationsAtPath(ownerPath)
                .OfType<AssetObject>()
                .FirstOrDefault(x => x.name == tempName);
            if (clone == null)
                return null;

            clone.name = source.name;
            EditorUtility.SetDirty(clone);
            clones[source] = clone;

            FixUpFields(clone, clones);

            AssetDatabase.SaveAssets();
            return clone;
        }

        // Re-fetches its own SerializedObject per field (rather than one held across the whole loop)
        // because fixing up one field can recursively duplicate another asset and trigger an
        // AssetDatabase.Refresh(), which can tear down any SerializedObject/SerializedProperty
        // created before that call - see the SetGuidValue comment for the same gotcha.
        private static void FixUpFields(AssetObject newNode, Dictionary<AssetObject, AssetObject> clones)
        {
            foreach (FieldInfo field in GetExpandableFields(newNode.GetType()))
            {
                int arraySize = GetArraySizeOrSingle(newNode, field.Name);
                if (arraySize < 0)
                {
                    FixUpGuidField(newNode, field.Name, -1, clones);
                    continue;
                }

                for (int i = 0; i < arraySize; i++)
                    FixUpGuidField(newNode, field.Name, i, clones);
            }
        }

        private static int GetArraySizeOrSingle(AssetObject newNode, string fieldName)
        {
            SerializedObject serializedObject = new SerializedObject(newNode);
            SerializedProperty property = serializedObject.FindProperty(fieldName);
            if (property == null)
                return -1;

            return property.isArray && property.propertyType != SerializedPropertyType.String ? property.arraySize : -1;
        }

        private static SerializedProperty FindFieldProperty(SerializedObject serializedObject, string fieldName, int arrayIndex)
        {
            SerializedProperty property = serializedObject.FindProperty(fieldName);
            if (property == null)
                return null;

            return arrayIndex >= 0 ? property.GetArrayElementAtIndex(arrayIndex) : property;
        }

        private static void FixUpGuidField(AssetObject newNode, string fieldName, int arrayIndex, Dictionary<AssetObject, AssetObject> clones)
        {
            SerializedObject serializedObject = new SerializedObject(newNode);
            SerializedProperty property = FindFieldProperty(serializedObject, fieldName, arrayIndex);
            if (property == null)
                return;

            SerializedProperty guidProperty = property.FindPropertyRelativeOrThrow(AssetRefDrawer.RawValuePath);
            AssetObject oldChild = ResolveAsset((AssetGuid)guidProperty.longValue);
            if (oldChild == null)
                return;

            AssetObject newChild = AssetDatabase.IsSubAsset(oldChild)
                ? DeepDuplicateNested(oldChild, clones)
                : DeepDuplicateTopLevel(oldChild, clones);

            if (newChild == null)
                return;

            // The recursive duplicate call above may have refreshed the AssetDatabase, so re-resolve
            // before writing rather than reusing the property fetched at the top of this method.
            serializedObject = new SerializedObject(newNode);
            property = FindFieldProperty(serializedObject, fieldName, arrayIndex);
            guidProperty = property.FindPropertyRelativeOrThrow(AssetRefDrawer.RawValuePath);
            guidProperty.longValue = newChild.Guid.Value;
            serializedObject.ApplyModifiedProperties();
        }

        private static IEnumerable<FieldInfo> GetExpandableFields(Type type)
        {
            return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => field.GetCustomAttribute<ExpandableAssetAttribute>() != null);
        }

        // AssetDatabase.CreateFolder only creates one new segment at a time, so walk the path
        // piece by piece the first time this fires.
        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string[] segments = folderPath.Split('/');
            string current = segments[0];

            for (int i = 1; i < segments.Length; i++)
            {
                string next = $"{current}/{segments[i]}";
                if (AssetDatabase.IsValidFolder(next) == false)
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }
    }
}

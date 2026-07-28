namespace Quantum.Editor
{
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    // Draws AssetRef<EntityPrototype> fields (CharacterData.Prototype, ProjectileDataAsset.Prototype,
    // SpawnEntitySkillAction.Prototype, etc.) as the normal AssetRefDrawer field, followed by a
    // thumbnail of the SOURCE PREFAB rather than the auto-generated .qprototype companion asset -
    // the .qprototype has no custom preview of its own (QuantumEntityPrototypeAssetObjectImporter
    // defines none), so without this the field only ever shows a generic icon.
    //
    // The .qprototype file's raw text content is literally the source prefab's GUID (see
    // QuantumEntityPrototypeAssetObjectImporter.OnImportAsset) - the same trick its own importer
    // Inspector uses to resolve its read-only "Source Prefab" field.
    //
    // Registered on the closed generic AssetRef<EntityPrototype> rather than the open AssetRef<>,
    // so Unity picks this over Quantum's own AssetRefDrawer for this field type specifically.
    [CustomPropertyDrawer(typeof(AssetRef<EntityPrototype>))]
    public class EntityPrototypeAssetRefDrawer : PropertyDrawer
    {
        private const float PreviewSize = 64f;
        private const float Spacing = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect fieldRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            var valueProperty = property.FindPropertyRelative("Id.Value");
            var guid = (AssetGuid)valueProperty.longValue;

            Rect labeledRect = EditorGUI.PrefixLabel(fieldRect, label);

            EditorGUI.BeginChangeCheck();
            Quantum.AssetObject selected;
            using (new EditorGUI.IndentLevelScope(-EditorGUI.indentLevel))
            {
                selected = AssetRefDrawer.DrawAsset(labeledRect, guid, typeof(EntityPrototype));
            }
            if (EditorGUI.EndChangeCheck())
            {
                valueProperty.longValue = selected != null ? selected.Guid.Value : 0L;
            }

            GameObject prefab = ResolveSourcePrefab(guid);
            if (prefab != null)
            {
                Texture2D preview = AssetPreview.GetAssetPreview(prefab);
                if (preview == null)
                    preview = AssetPreview.GetMiniThumbnail(prefab);

                if (preview != null)
                {
                    Rect previewRect = new Rect(position.x, fieldRect.yMax + Spacing, PreviewSize, PreviewSize);
                    GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
                }
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var valueProperty = property.FindPropertyRelative("Id.Value");
            var guid = (AssetGuid)valueProperty.longValue;

            float height = EditorGUIUtility.singleLineHeight;

            if (ResolveSourcePrefab(guid) != null)
                height += PreviewSize + Spacing;

            return height;
        }

        private static GameObject ResolveSourcePrefab(AssetGuid guid)
        {
            if (!guid.IsValid)
                return null;

            if (QuantumUnityDB.GetGlobalAssetEditorInstance(guid) is not EntityPrototype asset)
                return null;

            string prototypePath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(prototypePath) || !prototypePath.EndsWith(QuantumEntityPrototypeAssetObjectImporter.ExtensionWithDot))
                return null;

            string prefabGuid = File.ReadAllText(prototypePath);
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
            return string.IsNullOrEmpty(prefabPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }
    }
}

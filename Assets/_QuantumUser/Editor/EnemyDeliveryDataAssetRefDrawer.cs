namespace Quantum.Editor
{
    using UnityEditor;
    using UnityEngine;

    // Draws AssetRef<EnemyDeliveryData> fields (EnemyActionData.Delivery, BossDataAsset.Stagger.
    // OnBreakForcedAction, etc.) as the normal AssetRefDrawer picker, plus a foldout that
    // inline-edits the referenced delivery asset's own fields (e.g. KneelDeliveryData.Duration)
    // right here - without this, tuning a delivery means leaving this asset's Inspector entirely
    // to find and select the separate delivery asset in the Project window. Same precedent as
    // EntityPrototypeAssetRefDrawer, registered on the closed generic AssetRef<EnemyDeliveryData>
    // rather than the open AssetRef<>, so Unity picks this over Quantum's own AssetRefDrawer for
    // this field type specifically.
    [CustomPropertyDrawer(typeof(AssetRef<EnemyDeliveryData>))]
    public class EnemyDeliveryDataAssetRefDrawer : PropertyDrawer
    {
        private const float FoldoutWidth = 12f;
        private const float InlineIndent = 14f;
        private const float InlineTopSpacing = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect fieldRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EnemyDeliveryData deliveryAsset = ResolveDeliveryAsset(property);

            Rect foldoutRect = new Rect(fieldRect.x, fieldRect.y, FoldoutWidth, fieldRect.height);
            Rect pickerRect = new Rect(fieldRect.x + FoldoutWidth, fieldRect.y, fieldRect.width - FoldoutWidth, fieldRect.height);

            property.isExpanded = deliveryAsset != null && EditorGUI.Foldout(foldoutRect, property.isExpanded, GUIContent.none);

            var valueProperty = property.FindPropertyRelative("Id.Value");
            var guid = (AssetGuid)valueProperty.longValue;
            Rect labeledRect = EditorGUI.PrefixLabel(pickerRect, label);

            EditorGUI.BeginChangeCheck();
            Quantum.AssetObject selected;
            using (new EditorGUI.IndentLevelScope(-EditorGUI.indentLevel))
            {
                selected = AssetRefDrawer.DrawAsset(labeledRect, guid, typeof(EnemyDeliveryData));
            }
            if (EditorGUI.EndChangeCheck())
            {
                valueProperty.longValue = selected != null ? selected.Guid.Value : 0L;
            }

            if (deliveryAsset != null && property.isExpanded)
            {
                DrawInlineFields(position, fieldRect.yMax + InlineTopSpacing, deliveryAsset);
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            EnemyDeliveryData deliveryAsset = ResolveDeliveryAsset(property);

            if (deliveryAsset != null && property.isExpanded)
            {
                height += InlineTopSpacing;
                ForEachInlineProperty(deliveryAsset, iterator => height += EditorGUI.GetPropertyHeight(iterator, true) + EditorGUIUtility.standardVerticalSpacing);
            }

            return height;
        }

        private static EnemyDeliveryData ResolveDeliveryAsset(SerializedProperty property)
        {
            var valueProperty = property.FindPropertyRelative("Id.Value");
            var guid = (AssetGuid)valueProperty.longValue;

            return guid.IsValid ? QuantumUnityDB.GetGlobalAssetEditorInstance(guid) as EnemyDeliveryData : null;
        }

        private static void DrawInlineFields(Rect position, float y, EnemyDeliveryData deliveryAsset)
        {
            var serializedAsset = new SerializedObject(deliveryAsset);
            serializedAsset.UpdateIfRequiredOrScript();

            EditorGUI.indentLevel++;

            ForEachInlineProperty(deliveryAsset, iterator =>
            {
                float height = EditorGUI.GetPropertyHeight(iterator, true);
                Rect fieldRect = new Rect(position.x + InlineIndent, y, position.width - InlineIndent, height);
                EditorGUI.PropertyField(fieldRect, iterator, true);
                y += height + EditorGUIUtility.standardVerticalSpacing;
            }, serializedAsset);

            EditorGUI.indentLevel--;
            serializedAsset.ApplyModifiedProperties();
        }

        // Shared by GetPropertyHeight (measuring) and DrawInlineFields (drawing) so both walk the
        // exact same field set in the exact same order - a mismatch there is what makes a nested
        // drawer's content silently overflow or leave a dead gap in its reserved rect. Skips
        // m_Script, same as EntityPrototypeAssetRefDrawer's own precedent has no need to (it draws
        // a thumbnail, not the full inspector) but every other embedded-inspector idiom in Unity
        // does - nothing else about "which script this ScriptableObject is" belongs inline here.
        private static void ForEachInlineProperty(EnemyDeliveryData deliveryAsset, System.Action<SerializedProperty> visit, SerializedObject existing = null)
        {
            var serializedAsset = existing ?? new SerializedObject(deliveryAsset);
            var iterator = serializedAsset.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyPath == "m_Script")
                    continue;

                visit(iterator);
            }
        }
    }
}

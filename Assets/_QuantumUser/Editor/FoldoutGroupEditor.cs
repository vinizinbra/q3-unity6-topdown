namespace Quantum.Editor
{
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;

    // Base Editor for any AssetObject that tags fields with [FoldoutGroup("Name")] (see
    // FoldoutGroupAttribute) - turns each contiguous run sharing a group name into a real
    // collapsible boxed section. Unity's own [Header] only draws a bold label above a field and
    // has no way to hide the fields that follow it, and Quantum's broad QuantumAssetObjectEditor
    // ([CustomEditor(typeof(AssetObject), true)]) always wins over any third-party inspector
    // extension (NaughtyAttributes' [Foldout]/[BoxGroup] included) for AssetObject types, so an
    // exact-type Editor is the only way to get a real foldout here. A subclass needs nothing but
    // the CustomEditor attribute:
    //
    //   [CustomEditor(typeof(EnemyActionData))]
    //   public class EnemyActionDataEditor : FoldoutGroupEditor { }
    //
    // Foldout open/closed state is keyed by group name only (not per-asset), so opening "Base" on
    // one EnemyActionData keeps it open when selecting another - matches the existing single
    // shared expand state ExpandableAssetDrawer already uses for its own foldouts.
    public abstract class FoldoutGroupEditor : Editor
    {
        private static readonly Dictionary<string, bool> GroupExpanded = new Dictionary<string, bool>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            string openGroup = null;
            bool groupVisible = true;

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.propertyPath == "m_Script")
                    continue;

                FieldInfo field = target.GetType().GetField(property.name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                FoldoutGroupAttribute group = field?.GetCustomAttribute<FoldoutGroupAttribute>();
                string groupName = group?.Name;

                if (groupName != openGroup)
                {
                    if (openGroup != null)
                        EditorGUILayout.EndVertical();

                    openGroup = groupName;
                    if (openGroup != null)
                    {
                        bool expanded = GroupExpanded.TryGetValue(openGroup, out bool value) ? value : true;
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        expanded = EditorGUILayout.Foldout(expanded, openGroup, true, EditorStyles.foldoutHeader);
                        GroupExpanded[openGroup] = expanded;
                        groupVisible = expanded;
                    }
                }

                if (openGroup != null && groupVisible == false)
                    continue;

                EditorGUILayout.PropertyField(property, true);
            }

            if (openGroup != null)
                EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }
    }
}

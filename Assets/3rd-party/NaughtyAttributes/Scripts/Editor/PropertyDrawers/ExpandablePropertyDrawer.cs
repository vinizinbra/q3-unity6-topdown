using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace NaughtyAttributes.Editor
{
    [CustomPropertyDrawer(typeof(ExpandableAttribute))]
    public class ExpandablePropertyDrawer : PropertyDrawerBase
    {
        private const float CreateButtonWidth = 20f;
        private const string DataRoot = "Assets/_Project/Data";
        protected override float GetPropertyHeight_Internal(SerializedProperty property, GUIContent label)
        {
            if (property.objectReferenceValue == null)
            {
                return GetPropertyHeight(property);
            }

            System.Type propertyType = PropertyUtility.GetPropertyType(property);
            if (typeof(ScriptableObject).IsAssignableFrom(propertyType))
            {
                ScriptableObject scriptableObject = property.objectReferenceValue as ScriptableObject;
                if (scriptableObject == null)
                {
                    return GetPropertyHeight(property);
                }

                if (property.isExpanded)
                {
                    using (SerializedObject serializedObject = new SerializedObject(scriptableObject))
                    {
                        float totalHeight = EditorGUIUtility.singleLineHeight;

                        using (var iterator = serializedObject.GetIterator())
                        {
                            if (iterator.NextVisible(true))
                            {
                                do
                                {
                                    SerializedProperty childProperty = serializedObject.FindProperty(iterator.name);
                                    if (childProperty.name.Equals("m_Script", System.StringComparison.Ordinal))
                                    {
                                        continue;
                                    }

                                    bool visible = PropertyUtility.IsVisible(childProperty);
                                    if (!visible)
                                    {
                                        continue;
                                    }

                                    float height = GetPropertyHeight(childProperty);
                                    totalHeight += height;
                                }
                                while (iterator.NextVisible(false));
                            }
                        }

                        totalHeight += EditorGUIUtility.standardVerticalSpacing;
                        return totalHeight;
                    }
                }
                else
                {
                    return GetPropertyHeight(property);
                }
            }
            else
            {
                return GetPropertyHeight(property) + GetHelpBoxHeight();
            }
        }

        protected override void OnGUI_Internal(Rect rect, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(rect, label, property);

            if (property.objectReferenceValue == null)
            {
                DrawFieldWithCreateButton(rect, property, label);
            }
            else
            {
                System.Type propertyType = PropertyUtility.GetPropertyType(property);
                if (typeof(ScriptableObject).IsAssignableFrom(propertyType))
                {
                    ScriptableObject scriptableObject = property.objectReferenceValue as ScriptableObject;
                    if (scriptableObject == null)
                    {
                        EditorGUI.PropertyField(rect, property, label, false);
                    }
                    else
                    {
                        // Draw a foldout
                        Rect foldoutRect = new Rect()
                        {
                            x = rect.x,
                            y = rect.y,
                            width = EditorGUIUtility.labelWidth,
                            height = EditorGUIUtility.singleLineHeight
                        };

                        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, toggleOnLabelClick: true);

                        // Draw the scriptable object field
                        Rect propertyRect = new Rect()
                        {
                            x = rect.x,
                            y = rect.y,
                            width = rect.width,
                            height = EditorGUIUtility.singleLineHeight
                        };

                        EditorGUI.PropertyField(propertyRect, property, label, false);

                        // Draw the child properties
                        if (property.isExpanded)
                        {
                            DrawChildProperties(rect, property);
                        }
                    }
                }
                else
                {
                    string message = $"{typeof(ExpandableAttribute).Name} can only be used on scriptable objects";
                    DrawDefaultPropertyAndHelpBox(rect, property, message, MessageType.Warning);
                }
            }

            property.serializedObject.ApplyModifiedProperties();
            EditorGUI.EndProperty();
        }

        private void DrawChildProperties(Rect rect, SerializedProperty property)
        {
            ScriptableObject scriptableObject = property.objectReferenceValue as ScriptableObject;
            if (scriptableObject == null)
            {
                return;
            }

            Rect boxRect = new Rect()
            {
                x = 0.0f,
                y = rect.y + EditorGUIUtility.singleLineHeight,
                width = rect.width * 2.0f,
                height = rect.height - EditorGUIUtility.singleLineHeight
            };

            GUI.Box(boxRect, GUIContent.none);

            using (new EditorGUI.IndentLevelScope())
            {
                SerializedObject serializedObject = new SerializedObject(scriptableObject);
                serializedObject.Update();

                using (var iterator = serializedObject.GetIterator())
                {
                    float yOffset = EditorGUIUtility.singleLineHeight;

                    if (iterator.NextVisible(true))
                    {
                        do
                        {
                            SerializedProperty childProperty = serializedObject.FindProperty(iterator.name);
                            if (childProperty.name.Equals("m_Script", System.StringComparison.Ordinal))
                            {
                                continue;
                            }

                            bool visible = PropertyUtility.IsVisible(childProperty);
                            if (!visible)
                            {
                                continue;
                            }

                            float childHeight = GetPropertyHeight(childProperty);
                            Rect childRect = new Rect()
                            {
                                x = rect.x,
                                y = rect.y + yOffset,
                                width = rect.width,
                                height = childHeight
                            };

                            NaughtyEditorGUI.PropertyField(childRect, childProperty, true);

                            yOffset += childHeight;
                        }
                        while (iterator.NextVisible(false));
                    }
                }

                serializedObject.ApplyModifiedProperties();
            }
        }

        // Empty [Expandable] ScriptableObject fields get a "+" button next to them so a missing
        // config can be created inline instead of needing a manually-created asset dragged in.
        // Can't use PropertyUtility.GetPropertyType here - it resolves the *referenced object's*
        // runtime type, which NREs when the reference is null. fieldInfo.FieldType (the drawn
        // field's declared type, available via the base PropertyDrawer) works for a null value too.
        private void DrawFieldWithCreateButton(Rect rect, SerializedProperty property, GUIContent label)
        {
            Type propertyType = GetDeclaredFieldType();
            bool canCreate = propertyType != null && typeof(ScriptableObject).IsAssignableFrom(propertyType);

            Rect fieldRect = rect;
            if (canCreate)
            {
                fieldRect.width -= CreateButtonWidth + 2f;
            }

            EditorGUI.PropertyField(fieldRect, property, label, false);

            if (canCreate)
            {
                Rect buttonRect = new Rect(fieldRect.xMax + 2f, rect.y, CreateButtonWidth, EditorGUIUtility.singleLineHeight);
                if (GUI.Button(buttonRect, new GUIContent("+", "Create a new asset for this field")))
                {
                    SelectConcreteType(buttonRect, propertyType, chosen => CreateAsset(property, chosen));
                }
            }
        }

        private Type GetDeclaredFieldType()
        {
            Type type = fieldInfo?.FieldType;
            if (type == null)
            {
                return null;
            }

            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            {
                return type.GetGenericArguments().FirstOrDefault();
            }

            return type;
        }

        // propertyType itself if concrete, otherwise a menu of its non-abstract derived types -
        // same shape as ExpandableAssetDrawer's type picker for AssetRef fields.
        private static void SelectConcreteType(Rect rect, Type propertyType, Action<Type> onChosen)
        {
            var candidates = new System.Collections.Generic.List<Type>();
            if (propertyType.IsAbstract == false)
            {
                candidates.Add(propertyType);
            }
            candidates.AddRange(TypeCache.GetTypesDerivedFrom(propertyType)
                .Where(t => t.IsAbstract == false && t.IsGenericTypeDefinition == false));

            if (candidates.Count == 1)
            {
                onChosen(candidates[0]);
            }
            else if (candidates.Count > 1)
            {
                EditorUtility.DisplayCustomMenu(rect, candidates.Select(t => new GUIContent(t.FullName)).ToArray(), -1,
                    (_, menuOptions, chosen) => onChosen(candidates[chosen]), null);
            }
        }

        // Defaults the native save panel to Assets/_Project/Data (creating it if this is the
        // first asset ever created this way) - the user can still browse/create other folders
        // from the panel itself.
        private static void CreateAsset(SerializedProperty property, Type type)
        {
            EnsureFolderExists(DataRoot);

            string path = EditorUtility.SaveFilePanelInProject("Create Asset", type.Name, "asset",
                "Choose where to save the new asset.", DataRoot);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            ScriptableObject asset = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            property.objectReferenceValue = asset;
            property.serializedObject.ApplyModifiedProperties();
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] segments = folderPath.Split('/');
            string current = segments[0];

            for (int i = 1; i < segments.Length; i++)
            {
                string next = $"{current}/{segments[i]}";
                if (AssetDatabase.IsValidFolder(next) == false)
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }
                current = next;
            }
        }
    }
}

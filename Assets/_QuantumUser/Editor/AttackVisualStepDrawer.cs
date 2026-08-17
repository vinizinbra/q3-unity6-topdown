namespace Quantum.Editor
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    // Quantum's own [CustomEditor(typeof(AssetObject), true)] (QuantumAssetObjectEditor, see
    // Assets/Photon/Quantum/Editor/QuantumUnityEditor.cs:299) draws every AssetObject - including
    // EnemyActionData - via plain DrawDefaultInspector(). Unity resolves CustomEditor by picking the
    // most specific type match, so that editor always wins over NaughtyAttributes' own
    // NaughtyInspector (registered on the much more generic UnityEngine.Object) for any
    // AssetObject-derived asset - meaning [ShowIf]/[Foldout] on AttackVisualStep's fields never
    // activate there, since NaughtyInspector's OnInspectorGUI is simply never called.
    //
    // PropertyDrawers are a different, lower-level hook that DrawDefaultInspector's per-field
    // EditorGUILayout.PropertyField calls still respect regardless of which top-level Editor is
    // active, so this reimplements the same conditional-field/foldout behavior directly instead of
    // depending on NaughtyAttributes' Editor-level logic for this type. See TelegraphDataEditor
    // (same folder) for the analogous problem on TelegraphData - now its own top-level AssetObject
    // rather than a nested field, so that one needs a full CustomEditor instead of a PropertyDrawer.
    [CustomPropertyDrawer(typeof(AttackVisualStep))]
    public class AttackVisualStepDrawer : PropertyDrawer
    {
        // Keyed by property.propertyPath, which already differs per field (AnticipationStep vs
        // BeginStep vs OnGoingStep vs EndStep all have distinct paths) - one shared static
        // dictionary per foldout level is enough, no per-instance state needed.
        private static readonly Dictionary<string, bool> StepFoldouts = new Dictionary<string, bool>();
        private static readonly Dictionary<string, bool> AnimationFoldouts = new Dictionary<string, bool>();
        private static readonly Dictionary<string, bool> ParticleFoldouts = new Dictionary<string, bool>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            Rect rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            // Outer foldout - this is what was missing: without it, every AttackVisualStep field
            // (AnticipationStep/BeginStep/OnGoingStep/EndStep) drew its "Body Animation"/
            // "Particle" foldouts with no label showing which step they belonged to, so all four
            // steps looked like one repeated, indistinguishable stack.
            string stepKey = property.propertyPath;
            bool stepExpanded = GetFoldout(StepFoldouts, stepKey);
            stepExpanded = EditorGUI.Foldout(rect, stepExpanded, label, true, EditorStyles.foldoutHeader);
            StepFoldouts[stepKey] = stepExpanded;
            rect.y += lineHeight;

            if (stepExpanded == true)
            {
                EditorGUI.indentLevel++;

                SerializedProperty animationType = property.FindPropertyRelative("AnimationType");
                SerializedProperty duration = property.FindPropertyRelative("Duration");
                SerializedProperty bodySprite = property.FindPropertyRelative("BodySprite");
                SerializedProperty particlePrefab = property.FindPropertyRelative("ParticlePrefab");

                EditorGUI.PropertyField(rect, bodySprite);
                rect.y += lineHeight;

                string animationKey = property.propertyPath + ".animation";
                bool animationExpanded = GetFoldout(AnimationFoldouts, animationKey);
                animationExpanded = EditorGUI.Foldout(rect, animationExpanded, "Body Animation", true);
                AnimationFoldouts[animationKey] = animationExpanded;
                rect.y += lineHeight;

                if (animationExpanded == true)
                {
                    EditorGUI.indentLevel++;

                    EditorGUI.PropertyField(rect, animationType);
                    rect.y += lineHeight;

                    EditorGUI.PropertyField(rect, duration);
                    rect.y += lineHeight;

                    string paramsFieldName = GetParamsFieldName((AttackAnimationType)animationType.enumValueIndex);
                    if (paramsFieldName != null)
                    {
                        SerializedProperty paramsProp = property.FindPropertyRelative(paramsFieldName);
                        float paramsHeight = EditorGUI.GetPropertyHeight(paramsProp, true);
                        Rect paramsRect = new Rect(rect.x, rect.y, rect.width, paramsHeight);
                        EditorGUI.PropertyField(paramsRect, paramsProp, true);
                        rect.y += paramsHeight + EditorGUIUtility.standardVerticalSpacing;
                    }

                    if (UsesRotation((AttackAnimationType)animationType.enumValueIndex) == true)
                    {
                        SerializedProperty centerPivot = property.FindPropertyRelative("CenterPivot");
                        EditorGUI.PropertyField(rect, centerPivot);
                        rect.y += lineHeight;

                        if (centerPivot.boolValue == true)
                        {
                            EditorGUI.PropertyField(rect, property.FindPropertyRelative("PivotHeightOverride"));
                            rect.y += lineHeight;
                        }
                    }

                    EditorGUI.indentLevel--;
                }

                string particleKey = property.propertyPath + ".particle";
                bool particleExpanded = GetFoldout(ParticleFoldouts, particleKey);
                particleExpanded = EditorGUI.Foldout(rect, particleExpanded, "Particle", true);
                ParticleFoldouts[particleKey] = particleExpanded;
                rect.y += lineHeight;

                if (particleExpanded == true)
                {
                    EditorGUI.indentLevel++;

                    EditorGUI.PropertyField(rect, particlePrefab);
                    rect.y += lineHeight;

                    if (particlePrefab.objectReferenceValue != null)
                    {
                        SerializedProperty anchor = property.FindPropertyRelative("Anchor");
                        SerializedProperty offset = property.FindPropertyRelative("Offset");
                        SerializedProperty parented = property.FindPropertyRelative("Parented");
                        SerializedProperty alignToEnemyDirection = property.FindPropertyRelative("AlignToEnemyDirection");
                        SerializedProperty rotationOffset = property.FindPropertyRelative("RotationOffset");
                        SerializedProperty scale = property.FindPropertyRelative("Scale");
                        SerializedProperty overrideSortingOrder = property.FindPropertyRelative("OverrideSortingOrder");

                        EditorGUI.PropertyField(rect, anchor);
                        rect.y += lineHeight;

                        EditorGUI.PropertyField(rect, offset);
                        rect.y += lineHeight;

                        EditorGUI.PropertyField(rect, parented);
                        rect.y += lineHeight;

                        EditorGUI.PropertyField(rect, alignToEnemyDirection);
                        rect.y += lineHeight;

                        EditorGUI.PropertyField(rect, rotationOffset);
                        rect.y += lineHeight;

                        EditorGUI.PropertyField(rect, scale);
                        rect.y += lineHeight;

                        EditorGUI.PropertyField(rect, overrideSortingOrder);
                        rect.y += lineHeight;

                        if (overrideSortingOrder.boolValue == true)
                        {
                            EditorGUI.PropertyField(rect, property.FindPropertyRelative("SortingOrder"));
                        }
                    }

                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            float height = lineHeight; // outer step foldout header

            string stepKey = property.propertyPath;
            if (GetFoldout(StepFoldouts, stepKey) == false)
                return height;

            height += lineHeight; // BodySprite

            height += lineHeight; // "Body Animation" foldout header

            string animationKey = property.propertyPath + ".animation";
            if (GetFoldout(AnimationFoldouts, animationKey) == true)
            {
                height += lineHeight * 2; // AnimationType + Duration

                SerializedProperty animationType = property.FindPropertyRelative("AnimationType");
                var type = (AttackAnimationType)animationType.enumValueIndex;
                string paramsFieldName = GetParamsFieldName(type);
                if (paramsFieldName != null)
                {
                    SerializedProperty paramsProp = property.FindPropertyRelative(paramsFieldName);
                    height += EditorGUI.GetPropertyHeight(paramsProp, true) + EditorGUIUtility.standardVerticalSpacing;
                }

                if (UsesRotation(type) == true)
                {
                    height += lineHeight; // CenterPivot

                    if (property.FindPropertyRelative("CenterPivot").boolValue == true)
                        height += lineHeight; // PivotHeightOverride
                }
            }

            height += lineHeight; // "Particle" foldout header

            string particleKey = property.propertyPath + ".particle";
            if (GetFoldout(ParticleFoldouts, particleKey) == true)
            {
                height += lineHeight; // ParticlePrefab

                SerializedProperty particlePrefab = property.FindPropertyRelative("ParticlePrefab");
                if (particlePrefab.objectReferenceValue != null)
                {
                    height += lineHeight * 7; // Anchor, Offset, Parented, AlignToEnemyDirection, RotationOffset, Scale, OverrideSortingOrder

                    if (property.FindPropertyRelative("OverrideSortingOrder").boolValue == true)
                        height += lineHeight; // SortingOrder
                }
            }

            return height;
        }

        private static bool GetFoldout(Dictionary<string, bool> foldouts, string key)
        {
            return foldouts.TryGetValue(key, out bool expanded) == false || expanded;
        }

        // Types whose case in EnemyBlobAnimationView.UpdatePose sets rockTarget (root's own
        // rotation) - CenterPivot/PivotHeightOverride only mean anything for these; Pulse/Crouch/
        // Inflate/Lunge/Slam/Chomp/PunchScale never rotate root at all.
        private static bool UsesRotation(AttackAnimationType type)
        {
            switch (type)
            {
                case AttackAnimationType.Shake:
                case AttackAnimationType.SwingBack:
                case AttackAnimationType.Snap:
                case AttackAnimationType.Spin:
                case AttackAnimationType.ArmSwingBack:
                case AttackAnimationType.ArmSnap:
                case AttackAnimationType.ArmPunch:
                    return true;
                default:
                    return false;
            }
        }

        private static string GetParamsFieldName(AttackAnimationType type)
        {
            switch (type)
            {
                case AttackAnimationType.Shake: return "Shake";
                case AttackAnimationType.SwingBack: return "SwingBack";
                case AttackAnimationType.Pulse: return "Pulse";
                case AttackAnimationType.Crouch: return "Crouch";
                case AttackAnimationType.Inflate: return "Inflate";
                case AttackAnimationType.Lunge: return "Lunge";
                case AttackAnimationType.Slam: return "Slam";
                case AttackAnimationType.Snap: return "Snap";
                case AttackAnimationType.Chomp: return "Chomp";
                case AttackAnimationType.Spin: return "Spin";
                case AttackAnimationType.ArmSwingBack: return "ArmSwingBack";
                case AttackAnimationType.ArmSnap: return "ArmSnap";
                case AttackAnimationType.PunchScale: return "PunchScale";
                case AttackAnimationType.ArmPunch: return "ArmPunch";
                default: return null;
            }
        }
    }
}

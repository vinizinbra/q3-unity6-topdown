namespace Quantum.Editor
{
    using UnityEditor;

    // TelegraphData is a top-level AssetObject (see TelegraphData.cs) rather than a field nested
    // inside another asset, so a PropertyDrawer (the trick AttackVisualStepDrawer, same folder,
    // uses for its own conditional-field problem) doesn't apply here - opening a TelegraphData
    // asset directly invokes whatever CustomEditor matches its type, not a PropertyDrawer. Quantum's
    // own [CustomEditor(typeof(AssetObject), true)] would otherwise win (via plain
    // DrawDefaultInspector(), showing every field unconditionally) - this one is more specific
    // (exact type match beats the base-type match), so it wins instead, letting Shape-based
    // conditional display (RadiusMultiplier for Circle/Cone, the box fields for ChargeLane/Rectangle) work.
    [CustomEditor(typeof(TelegraphData))]
    public class TelegraphDataEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty shape = serializedObject.FindProperty("Shape");

            EditorGUILayout.PropertyField(shape);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("StartPhase"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("EndPhase"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("TelegraphPrefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SnapToGround"));

            TelegraphShape shapeValue = (TelegraphShape)shape.enumValueIndex;

            if (shapeValue == TelegraphShape.ChargeLane || shapeValue == TelegraphShape.Rectangle)
            {
                // Rectangle reuses ChargeLane's exact box math (see EnemyAttackVisualsView.
                // ComputeTelegraphPose) - same fields drive both, so both show the same block here.
                SerializedProperty lineLength = serializedObject.FindProperty("LineLength");
                EditorGUILayout.PropertyField(lineLength);

                if ((TelegraphLineLength)lineLength.enumValueIndex == TelegraphLineLength.FixedDistance)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("FixedDistanceValue"));
                }

                EditorGUILayout.PropertyField(serializedObject.FindProperty("FromOffset"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ToOffset"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Width"));
            }
            else if (shapeValue == TelegraphShape.Circle || shapeValue == TelegraphShape.Cone)
            {
                // Cone reuses Circle's exact RadiusMultiplier-based positioning math (see
                // EnemyAttackVisualsView.ComputeTelegraphPose) - same field drives both.
                EditorGUILayout.PropertyField(serializedObject.FindProperty("RadiusMultiplier"));
            }
            else
            {
                EditorGUILayout.HelpBox($"TelegraphShape.{shapeValue} has no rendering implementation yet (see TelegraphShape's own comment) - this asset won't spawn anything until EnemyAttackVisualsView.SpawnTelegraph supports it.", MessageType.Info);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("GrowthDuration"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("FadeInDuration"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("FadeOutDuration"));

            serializedObject.ApplyModifiedProperties();
        }
    }
}

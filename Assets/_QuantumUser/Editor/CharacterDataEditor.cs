namespace Quantum.Editor
{
    using UnityEditor;
    using UnityEngine;

    // Dash/Hero skill (CharacterData.DashSkill/HeroSkill) are the fields most often reshuffled while
    // tuning a hero - swapping in a different SkillData. This button does that in one click instead
    // of dragging the AssetRef to None in the default inspector. DashSkillUpgrades also gets a "Clear
    // Upgrades" button for the same reason its hand-authored pool sometimes needs wiping to start
    // over - HeroSkill has no equivalent list to clear (see LevelUpUtility.
    // AddHeroSkillUpgradeCandidates - its pool is HeroSkill's own Actions, edited on that asset
    // directly, not here).
    [CustomEditor(typeof(CharacterData))]
    public class CharacterDataEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Skill Upgrade Tools", EditorStyles.boldLabel);

            DrawSkillClearButtons("Dash Skill", "DashSkill", "DashSkillUpgrades");
            DrawSkillClearButtons("Hero Skill", "HeroSkill", null);
        }

        private void DrawSkillClearButtons(string label, string skillPropertyName, string upgradesPropertyName)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));

            if (GUILayout.Button("Clear Skill") &&
                EditorUtility.DisplayDialog("Clear Skill", $"Clear {label} on \"{target.name}\"?", "Clear", "Cancel"))
            {
                serializedObject.Update();
                serializedObject.FindProperty(skillPropertyName).FindPropertyRelative("Id.Value").longValue = 0L;
                serializedObject.ApplyModifiedProperties();
            }

            if (upgradesPropertyName != null && GUILayout.Button("Clear Upgrades") &&
                EditorUtility.DisplayDialog("Clear Upgrades", $"Clear all {label} upgrades on \"{target.name}\"?", "Clear", "Cancel"))
            {
                serializedObject.Update();
                serializedObject.FindProperty(upgradesPropertyName).ClearArray();
                serializedObject.ApplyModifiedProperties();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}

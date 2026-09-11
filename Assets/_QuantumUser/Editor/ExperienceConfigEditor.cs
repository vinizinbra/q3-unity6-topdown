namespace Quantum.Editor
{
    using Photon.Deterministic;
    using UnityEditor;
    using UnityEngine;

    // Live "what does this curve actually cost" preview underneath the default inspector, so
    // DifficultyMultiplier/curve tweaks can be judged by the resulting cumulative XP per level
    // instead of squinting at the CurveField. Mirrors ExperienceUtility.GetRequiredExperience's
    // exact math (curve * DifficultyMultiplier * co-op XpRequirement multiplier), reading the
    // co-op row straight from BalanceConfig so this table can never drift from the real formula.
    [CustomEditor(typeof(ExperienceConfig))]
    public class ExperienceConfigEditor : Editor
    {
        private static readonly int[] PreviewLevels =
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
            25, 30, 35, 40, 45, 50,
        };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var config = (ExperienceConfig)target;
            BalanceConfig balance = FindBalanceConfig();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Simulated Cumulative XP Required", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Level", GUILayout.Width(40));
            EditorGUILayout.LabelField("Base", GUILayout.Width(65));
            EditorGUILayout.LabelField("Δ", GUILayout.Width(55));

            if (balance != null)
            {
                EditorGUILayout.LabelField("1P", GUILayout.Width(55));
                EditorGUILayout.LabelField("2P", GUILayout.Width(55));
                EditorGUILayout.LabelField("3P", GUILayout.Width(55));
                EditorGUILayout.LabelField("4P", GUILayout.Width(55));
            }

            EditorGUILayout.EndHorizontal();

            foreach (int level in PreviewLevels)
            {
                if (level > config.MaxLevel)
                    break;

                FP baseValue = config.RequiredExperience.Evaluate(level) * config.DifficultyMultiplier;
                FP baseValuePrev = config.RequiredExperience.Evaluate(level - 1) * config.DifficultyMultiplier;
                FP delta = baseValue - baseValuePrev;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(level.ToString(), GUILayout.Width(40));
                EditorGUILayout.LabelField(baseValue.AsFloat.ToString("0"), GUILayout.Width(65));
                EditorGUILayout.LabelField("+" + delta.AsFloat.ToString("0"), GUILayout.Width(55));

                if (balance != null)
                {
                    EditorGUILayout.LabelField((baseValue * balance.GetCoopGlobal(CoopGlobalKey.XpRequirement, 1)).AsFloat.ToString("0"), GUILayout.Width(55));
                    EditorGUILayout.LabelField((baseValue * balance.GetCoopGlobal(CoopGlobalKey.XpRequirement, 2)).AsFloat.ToString("0"), GUILayout.Width(55));
                    EditorGUILayout.LabelField((baseValue * balance.GetCoopGlobal(CoopGlobalKey.XpRequirement, 3)).AsFloat.ToString("0"), GUILayout.Width(55));
                    EditorGUILayout.LabelField((baseValue * balance.GetCoopGlobal(CoopGlobalKey.XpRequirement, 4)).AsFloat.ToString("0"), GUILayout.Width(55));
                }

                EditorGUILayout.EndHorizontal();
            }

            if (balance == null)
            {
                EditorGUILayout.HelpBox("No BalanceConfig asset found - showing Base (curve x DifficultyMultiplier) only, co-op XpRequirement scaling not shown.", MessageType.Info);
            }
        }

        private static BalanceConfig FindBalanceConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:BalanceConfig");

            if (guids.Length == 0)
                return null;

            return AssetDatabase.LoadAssetAtPath<BalanceConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}

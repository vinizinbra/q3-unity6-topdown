namespace QuantumUser.Editor
{
    using UnityEditor;
    using UnityEngine;

    // Moves every hero's base-skill asset into a Skills/<Hero>/BaseSkill/ subfolder, giving all 6
    // heroes the same layout (Lux/Max/Pixie/Zara's used to sit inside HeroSkillUpgrades/ alongside
    // unrelated upgrade assets; Brute's/Kai's used to sit directly at Skills/<Hero>/ root). No renaming -
    // AssetDatabase.MoveAsset preserves each .meta's GUID so every existing AssetRef (which resolves by
    // Quantum's own Guid, not by path) keeps working.
    public static class HeroBaseSkillRelocator
    {
        private static readonly (string From, string To)[] Moves =
        {
            ("Assets/_QuantumUser/Resources/Skills/Brute/BruteSkill.asset", "Assets/_QuantumUser/Resources/Skills/Brute/BaseSkill/BruteSkill.asset"),
            ("Assets/_QuantumUser/Resources/Skills/Kai/KaiVortexSkill.asset", "Assets/_QuantumUser/Resources/Skills/Kai/BaseSkill/KaiVortexSkill.asset"),
            ("Assets/_QuantumUser/Resources/Skills/Lux/HeroSkillUpgrades/LuxSkill.asset", "Assets/_QuantumUser/Resources/Skills/Lux/BaseSkill/LuxSkill.asset"),
            ("Assets/_QuantumUser/Resources/Skills/Max/HeroSkillUpgrades/MaxHeroSkill.asset", "Assets/_QuantumUser/Resources/Skills/Max/BaseSkill/MaxHeroSkill.asset"),
            ("Assets/_QuantumUser/Resources/Skills/Pixie/HeroSkillUpgrades/BunnyBombSkill.asset", "Assets/_QuantumUser/Resources/Skills/Pixie/BaseSkill/BunnyBombSkill.asset"),
            ("Assets/_QuantumUser/Resources/Skills/Zara/HeroSkillUpgrades/ZaraSkillData.asset", "Assets/_QuantumUser/Resources/Skills/Zara/BaseSkill/ZaraSkillData.asset"),
        };

        [MenuItem("Tools/RiftRaiders/Move Hero Base Skills Into BaseSkill Subfolder")]
        internal static void Move()
        {
            foreach (var (from, to) in Moves)
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(from) == null)
                {
                    Debug.LogWarning($"[HeroBaseSkillRelocator] Skipping - not found at {from}.");
                    continue;
                }

                CreateFolderRecursive(to.Substring(0, to.LastIndexOf('/')));

                string error = AssetDatabase.MoveAsset(from, to);

                if (string.IsNullOrEmpty(error))
                {
                    Debug.Log($"[HeroBaseSkillRelocator] Moved {from} -> {to}.");
                }
                else
                {
                    Debug.LogError($"[HeroBaseSkillRelocator] Failed to move {from} -> {to}: {error}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";

                if (AssetDatabase.IsValidFolder(next) == false)
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}

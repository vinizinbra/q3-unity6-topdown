namespace QuantumUser.Editor
{
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors ReviveConfig.asset with tuned defaults - see docs/revive.md. Mirrors
    // BreathingPoiContentGenerator's own folder-creation/update-in-place shape; re-running this is
    // safe, an existing asset at the expected path is updated rather than duplicated. Every number
    // below is a decisive placeholder pending a real balance pass, same convention every other
    // content generator in this codebase already follows. Deliberately does NOT touch
    // RuntimeConfig/QuantumMenuConfig, hero EntityPrototypes, or any UI prefab wiring, for the same
    // "no safe way to locate this" reason every other generator here has.
    public static class ReviveContentGenerator
    {
        private const string ReviveFolderPath = "Assets/_QuantumUser/Resources/Revive";
        private const string ReviveConfigPath = ReviveFolderPath + "/ReviveConfig.asset";

        [MenuItem("Tools/RiftRaiders/Generate Revive Content")]
        internal static void Generate()
        {
            CreateFolderRecursive(ReviveFolderPath);

            GenerateReviveConfig();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LogHelper.Log("ReviveContentGenerator",
                $"ReviveConfig ({ReviveConfigPath}) authored. Still needed by hand: " +
                "(1) assign RuntimeConfig's ReviveConfig field (QuantumMenuConfig.asset), same place " +
                "CursedRiftConfig etc. are already assigned; " +
                "(2) add a PlayerLifeState component to every hero EntityPrototype (Pixie/Brute/Zara/" +
                "Kai/Max/MainChar/Lux under Assets/_QuantumUser/Entities/Characters/) - defaults all-" +
                "zero (Alive), nothing else to author on it; " +
                "(3) add a ReviveInteractionPromptView component to each hero's own View prefab, wiring " +
                "InteractionPromptWidgetManager the same way PoiView already is; " +
                "(4) build a Slider on the HUD prompt prefab for InteractionPromptWidget's new " +
                "progressFillSlider field (the title is plain REVIVE text, no color override; the " +
                "bleed-out countdown itself reuses the existing descriptionText, no new Text " +
                "element needed); " +
                "(5) wire SkillCooldownUiWidget's HeroSkill-slot contextInteractionIcon/interactPromptRoot " +
                "(same pre-existing gap docs/breathing-poi.md already tracks); " +
                "(6) build a SelfReviveWidget prefab (titleText/chargesText/selfReviveButton/" +
                "bleedOutTimerText) per local player slot in the HUD scene (localSlotIndex 0 and 1 for " +
                "couch co-op) - a dedicated small HUD widget, not ChooseWindow, shown to an " +
                "incapacitated LOCAL player with a single press/confirm SELF REVIVE button " +
                "(SelfReviveCommand), separate from the teammate hold-to-revive flow entirely; " +
                "(7) nothing writes the new self_revive_charges PlayerPref yet - same accepted gap " +
                "weapon_talent_level/reroll_quantity already have; seed PlayerTalents.SelfReviveCharges " +
                "by hand in the Inspector for testing; " +
                "(8) the Downed/KO collapse pose (BlobAnimationView) and weapon-hide (WeaponViewController) " +
                "need no new Editor wiring - both reuse each hero's already-authored rig/weapon socket " +
                "references and only need PlayerLifeState present on the prototype (step 2) - but tune " +
                "the new downedFallDuration/downedRiseDuration/downedToppleDegrees/downedSquash/" +
                "downedGroundOffsetY/downedGroundOffsetZ fields per hero once real sprite art exists, " +
                "same as every other BlobAnimationView tuning group.");
        }

        private static void GenerateReviveConfig()
        {
            var config = LoadOrCreate<ReviveConfig>(ReviveConfigPath, out bool isNew);

            config.DownedReviveDuration = (FP._2 + FP._0_50);
            config.DownedBleedOutDuration = 20;
            config.ReviveMoveSpeedMultiplier = FP.FromString("0.30");
            config.ReviveProgressDecayRate = FP._0_50;
            config.ReviveHealthPercent = FP.FromString("0.40");
            config.ReviveInvulnerabilityDuration = 2;
            config.ReviveInteractionRange = 3;

            FinalizeAsset(config, ReviveConfigPath, isNew);
        }

        private static T LoadOrCreate<T>(string path, out bool isNew) where T : AssetObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            isNew = existing == null;
            return isNew ? ScriptableObject.CreateInstance<T>() : existing;
        }

        private static void FinalizeAsset(AssetObject asset, string path, bool isNew)
        {
            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath) == true)
                return;

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

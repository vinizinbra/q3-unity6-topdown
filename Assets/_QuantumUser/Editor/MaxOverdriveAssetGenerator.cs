namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors 4 Hero Skill Upgrades for Berserk/Overdrive (Too Angry to Die/Vendetta Rush/Seeing Red/
    // Uncontrolled Fury), then wires them into MaxHeroSkill.asset's own Actions list - NOT
    // CharacterData.HeroSkillUpgrades, which doesn't exist (see CLAUDE.md's Level-Up Upgrades
    // section: the Hero Skill slice of the Skill Upgrade pool is pulled straight from HeroSkill's
    // own Actions). Each is authored with Activated = false so LevelUpUtility treats it as a pick
    // candidate rather than always-on baseline behavior - AddUpgrade ignores Activated once granted
    // (see SkillActionData's own comment), same convention the existing 3 Overdrive actions
    // (RageOverdriveSkillAction/OverdriveDamageSkillAction/OverdriveInstantReloadSkillAction, already
    // sub-assets of MaxHeroSkill.asset) were presumably meant to follow. Unlike those 3, this
    // generator authors standalone .asset files - mirrors MaxVendettaAssetGenerator.cs/
    // MaxFireMasteryAssetGenerator.cs's own create-or-update-in-place behavior; re-running this is
    // safe for the same reasons those are. See docs/max-vendetta-fire-mastery.md's "Addition: 4
    // Berserk/Overdrive Hero Skill Upgrades" note.
    public static class MaxOverdriveAssetGenerator
    {
        private const string OverdriveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Max/M_HeroSkill/M_HeroSkillUpgrades/Overdrive";
        private const string HeroSkillPath = "Assets/_QuantumUser/Resources/Skills/Max/M_HeroSkill/MaxHeroSkill.asset";

        [MenuItem("Tools/RiftRaiders/Max/Generate Overdrive Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(OverdriveUpgradesFolderPath);

            TooAngryToDieSkillAction tooAngryToDie = CreateOrUpdate<TooAngryToDieSkillAction>($"{OverdriveUpgradesFolderPath}/TooAngryToDie.asset", asset =>
            {
                asset.DisplayName = "Too Angry to Die";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Lethal damage during Overdrive leaves you at 1 Health and immediately ends Overdrive.";
                asset.Activated = false;
            });

            VendettaRushSkillAction vendettaRush = CreateOrUpdate<VendettaRushSkillAction>($"{OverdriveUpgradesFolderPath}/VendettaRush.asset", asset =>
            {
                asset.DisplayName = "Vendetta Rush";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Killing your Vendetta target extends the current Overdrive by {0}s.";
                asset.ExtensionSeconds = 2;
                asset.Activated = false;
            });

            SeeingRedSkillAction seeingRed = CreateOrUpdate<SeeingRedSkillAction>($"{OverdriveUpgradesFolderPath}/SeeingRed.asset", asset =>
            {
                asset.DisplayName = "Seeing Red";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Activating Overdrive releases a shockwave in a {0}m radius, dealing {1} damage and igniting nearby enemies.";
                asset.Radius = 4;
                asset.Damage = 20;
                asset.BurnDuration = 3;
                asset.BurnIntensity = FP._0_10;
                asset.MaxTargets = 8;
                asset.Activated = false;
            });

            UncontrolledFurySkillAction uncontrolledFury = CreateOrUpdate<UncontrolledFurySkillAction>($"{OverdriveUpgradesFolderPath}/UncontrolledFury.asset", asset =>
            {
                asset.DisplayName = "Uncontrolled Fury";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Each kill during Overdrive extends it by {0}s, up to {1}s per activation.";
                asset.PerKillExtension = FP._0_20;
                asset.MaxExtension = 3;
                asset.Activated = false;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireHeroSkill(new List<SkillActionData> { tooAngryToDie, vendettaRush, seeingRed, uncontrolledFury });

            LogHelper.Log("MaxOverdriveAssetGenerator", "4 Overdrive Hero Skill Upgrades authored and appended to MaxHeroSkill.Actions " +
                      "(Activated = false, so they only run once granted via a level-up pick, not for every Berserk activation).");
        }

        private static T CreateOrUpdate<T>(string path, System.Action<T> configure) where T : AssetObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            bool isNew = existing == null;
            T asset = isNew ? (T)ScriptableObject.CreateInstance(typeof(T)) : existing;

            configure(asset);

            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        private static void WireHeroSkill(List<SkillActionData> overdriveUpgrades)
        {
            var heroSkill = AssetDatabase.LoadAssetAtPath<SkillData>(HeroSkillPath);

            if (heroSkill == null)
            {
                LogHelper.Error("MaxOverdriveAssetGenerator", $"No SkillData asset at {HeroSkillPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            foreach (var upgrade in overdriveUpgrades)
            {
                bool alreadyPresent = heroSkill.Actions.Any(existing => existing.Id.Value == upgrade.Guid.Value);

                if (alreadyPresent == true)
                    continue;

                heroSkill.Actions.Add(new AssetRef<SkillActionData>(upgrade.Guid));
            }

            EditorUtility.SetDirty(heroSkill);
            AssetDatabase.SaveAssets();
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

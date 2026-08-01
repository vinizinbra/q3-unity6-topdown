namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Max's Adrenaline Rush passive + its 4 Passive Ascensions + 2 of his 3 Dash Ascensions
    // (Reloading Slide, Adrenaline Injection - Blazing Trail still needs a fire-trail
    // EntityPrototype authored by hand first, see the log this prints), then wires all of it into
    // MaxCharacterData.asset. Mirrors LuxScrapAssetGenerator.cs/KaiVoidFieldAssetGenerator.cs
    // exactly (same folder-creation/update-in-place/rebuild-the-list-from-scratch behavior);
    // re-running this is safe for the same reasons those are.
    public static class MaxAdrenalineAssetGenerator
    {
        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Passives/Max";
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Max/PassiveSkillUpgrades";
        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Max/DashSkillUpgrades";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/MaxCharacterData.asset";

        [MenuItem("Tools/RiftRaiders/Max/Generate Adrenaline Rush Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(PassiveUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);

            // PassiveData (unlike PassiveUpgradeData) derives AssetObject directly, not UpgradeData -
            // a hero's single base Passive is Inspector-assigned (CharacterData.Passive), never
            // offered as a level-up card, so it has no DisplayName/Rarity/Description to set here.
            AdrenalineRushPassiveData passive = CreateOrUpdate<AdrenalineRushPassiveData>($"{PassivesFolderPath}/AdrenalineRushPassiveData.asset", asset =>
            {
                asset.MaxStacks = 20;
                asset.GainPerHit = 1;
                asset.FireRatePerStack = FP.FromString("0.01");
                asset.MoveSpeedPerStack = FP.FromString("0.01");
                asset.DecayDelay = 3;
                asset.DecayInterval = FP._0_50;
            });

            HotBloodedPassiveUpgradeData hotBlooded = CreateOrUpdate<HotBloodedPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/HotBlooded.asset", asset =>
            {
                asset.DisplayName = "Hot Blooded";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Adrenaline builds faster.";
                asset.GainPerHitBonus = 1;
            });

            BattleHighPassiveUpgradeData battleHigh = CreateOrUpdate<BattleHighPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/BattleHigh.asset", asset =>
            {
                asset.DisplayName = "Battle High";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "At maximum Adrenaline, gain increased Weapon Damage.";
                asset.WeaponDamageBonusAtMax = FP.FromString("0.25");
            });

            TooAngryToDiePassiveUpgradeData tooAngryToDie = CreateOrUpdate<TooAngryToDiePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/TooAngryToDie.asset", asset =>
            {
                asset.DisplayName = "Too Angry to Die";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Taking damage at maximum Adrenaline grants temporary Damage Reduction.";
                asset.DamageReductionAtMax = FP.FromString("0.25");
                asset.DamageReductionDuration = 3;
            });

            NoTimeToBreathePassiveUpgradeData noTimeToBreathe = CreateOrUpdate<NoTimeToBreathePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/NoTimeToBreathe.asset", asset =>
            {
                asset.DisplayName = "No Time to Breathe";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Adrenaline decays slower, and does not decay while enemies are within weapon range.";
                asset.DecayIntervalBonus = FP._0_50;
            });

            ReloadingSlideSkillAction reloadingSlide = CreateOrUpdate<ReloadingSlideSkillAction>($"{DashUpgradesFolderPath}/ReloadingSlideSkillAction.asset", asset =>
            {
                asset.DisplayName = "Reloading Slide";
                asset.Rarity = UpgradeRarity.Rare;
                asset.RestoreFraction = FP.FromString("0.25");
            });

            AdrenalineInjectionSkillAction adrenalineInjection = CreateOrUpdate<AdrenalineInjectionSkillAction>($"{DashUpgradesFolderPath}/AdrenalineInjectionSkillAction.asset", asset =>
            {
                asset.DisplayName = "Adrenaline Injection";
                asset.Rarity = UpgradeRarity.Rare;
                asset.StacksOnDash = 2;
                asset.StacksPerEnemyHit = 1;
                asset.HitRadius = 2;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive, new List<PassiveUpgradeData> { hotBlooded, battleHigh, tooAngryToDie, noTimeToBreathe },
                new List<SkillActionData> { reloadingSlide, adrenalineInjection });

            LogHelper.Log("MaxAdrenalineAssetGenerator", "Passive + 4 ascensions + 2 dash ascensions authored and wired into MaxCharacterData. " +
                      "Blazing Trail (the 3rd dash ascension) still needs a fire-trail EntityPrototype authored by hand first " +
                      "(an AreaDamage-carrying prototype with a BurnEffectData asset in its Effects list - BurnEffectData already " +
                      "applies Burn independent of DamageSource, no new code needed) - once that exists, add a SpawnEntitySkillAction " +
                      "asset pointing Prototype at it (Phase=OnGoing, small Spacing so it drops a segment every few units of dash " +
                      "travel) and append it to MaxCharacterData.DashSkillUpgrades the same way this script just did for the other two.");
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

        private static void WireCharacterData(AdrenalineRushPassiveData passive, List<PassiveUpgradeData> passiveUpgrades, List<SkillActionData> dashUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("MaxAdrenalineAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            if (characterData.Passive.IsValid == true && characterData.Passive.Id.Value != passive.Guid.Value)
            {
                LogHelper.Warn("MaxAdrenalineAssetGenerator", $"MaxCharacterData.Passive was already set to {characterData.Passive} - overwriting with AdrenalineRushPassiveData.");
            }

            characterData.Passive = new AssetRef<PassiveData>(passive.Guid);
            characterData.PassiveUpgrades = passiveUpgrades.Select(a => new AssetRef<PassiveUpgradeData>(a.Guid)).ToList();

            foreach (var upgrade in dashUpgrades)
            {
                bool alreadyPresent = characterData.DashSkillUpgrades.Any(existing => existing.Id.Value == upgrade.Guid.Value);

                if (alreadyPresent == true)
                    continue;

                characterData.DashSkillUpgrades.Add(new AssetRef<SkillActionData>(upgrade.Guid));
            }

            EditorUtility.SetDirty(characterData);
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

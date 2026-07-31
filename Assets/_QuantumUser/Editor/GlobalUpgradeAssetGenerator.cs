namespace QuantumUser.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using UnityEditor;
    using UnityEngine;

    // Authors one .asset instance per implemented GlobalUpgradeData class (see
    // docs/global-upgrades.md), tuned to the numbers from the original design list, plus wiring all
    // of them into the existing LevelUpConfig.asset's GlobalUpgrades list - the two pieces of
    // Editor authoring that doc says are still missing. Mirrors WeaponPerkAssetGenerator.cs exactly
    // (same folder-creation/update-in-place/rebuild-the-list-from-scratch behavior); re-running this
    // is safe for the same reasons that one is.
    //
    // Every Description below is a template ({0} filled in from the asset's own live Multiplier/
    // Chance/Charges/RegenAmount via DescriptionArgs - see GlobalUpgradeData.View.cs), not a plain
    // string - so retuning a Configure value here (or later, by hand, in the Inspector) can't drift
    // out of sync with the sentence describing it, same convention as WeaponPerkData/
    // SkillActionData's own Description fields.
    public static class GlobalUpgradeAssetGenerator
    {
        private const string FolderPath = "Assets/_QuantumUser/Resources/LevelUp/GlobalUpgrade";
        private const string ConfigAssetPath = "Assets/_QuantumUser/Resources/LevelUpConfig.asset";

        private class UpgradeSpec
        {
            public Type Type;
            public string FileName;
            public string DisplayName;
            public UpgradeRarity Rarity;
            public string Description;
            public Action<GlobalUpgradeData> Configure;
        }

        private static readonly List<UpgradeSpec> Specs = new()
        {
            // -- Weapon -- (each intentionally overlaps a Weapon Perk of the same name, stacking as
            // an independent second source - see docs/global-upgrades.md "Design notes" #1)
            new UpgradeSpec
            {
                Type = typeof(WeaponDamageUpgradeData), FileName = "WeaponDamage",
                DisplayName = "Weapon Damage", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Weapon Damage",
                Configure = p => Multiplier(p, "1.1")
            },
            new UpgradeSpec
            {
                Type = typeof(FireRateUpgradeData), FileName = "FireRate",
                DisplayName = "Fire Rate", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Fire Rate",
                Configure = p => Multiplier(p, "1.1")
            },
            new UpgradeSpec
            {
                Type = typeof(ReloadSpeedUpgradeData), FileName = "ReloadSpeed",
                DisplayName = "Reload Speed", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Reload Speed",
                Configure = p => Multiplier(p, "1.15")
            },
            new UpgradeSpec
            {
                Type = typeof(MagazineSizeUpgradeData), FileName = "MagazineSize",
                DisplayName = "Magazine Size", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Magazine",
                Configure = p => ((MagazineSizeUpgradeData)p).Multiplier = FP.FromString("1.2")
            },
            new UpgradeSpec
            {
                Type = typeof(CriticalChanceUpgradeData), FileName = "CriticalChance",
                DisplayName = "Critical Chance", Rarity = UpgradeRarity.Rare,
                Description = "+{0}% Crit Chance",
                Configure = p => ((CriticalChanceUpgradeData)p).Chance = FP.FromString("0.05")
            },
            new UpgradeSpec
            {
                Type = typeof(CriticalDamageUpgradeData), FileName = "CriticalDamage",
                DisplayName = "Critical Damage", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Crit Damage",
                Configure = p => Multiplier(p, "1.2")
            },
            new UpgradeSpec
            {
                Type = typeof(WeaponRangeUpgradeData), FileName = "WeaponRange",
                DisplayName = "Weapon Range", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Weapon Range",
                Configure = p => ((WeaponRangeUpgradeData)p).Multiplier = FP.FromString("1.15")
            },
            new UpgradeSpec
            {
                Type = typeof(ProjectileSpeedUpgradeData), FileName = "ProjectileSpeed",
                DisplayName = "Projectile Speed", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Projectile Speed",
                Configure = p => Multiplier(p, "1.2")
            },

            // -- Hero --
            new UpgradeSpec
            {
                Type = typeof(MaxHealthUpgradeData), FileName = "MaxHealth",
                DisplayName = "Max Health", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Max HP",
                Configure = p => Multiplier(p, "1.15")
            },
            new UpgradeSpec
            {
                Type = typeof(ShieldUpgradeData), FileName = "Shield",
                DisplayName = "Shield", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Shield",
                Configure = p => Multiplier(p, "1.1")
            },
            new UpgradeSpec
            {
                Type = typeof(MoveSpeedUpgradeData), FileName = "MoveSpeed",
                DisplayName = "Movement Speed", Rarity = UpgradeRarity.Rare,
                Description = "+{0}% Move Speed",
                Configure = p => Multiplier(p, "1.1")
            },
            new UpgradeSpec
            {
                Type = typeof(HealthRegenUpgradeData), FileName = "HealthRegen",
                DisplayName = "Health Regeneration", Rarity = UpgradeRarity.Rare,
                Description = "+{0} HP/sec",
                Configure = p => ((HealthRegenUpgradeData)p).RegenAmount = FP._1
            },
            new UpgradeSpec
            {
                Type = typeof(HealingReceivedUpgradeData), FileName = "HealingReceived",
                DisplayName = "Healing Received", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Healing",
                Configure = p => Multiplier(p, "1.2")
            },
            new UpgradeSpec
            {
                Type = typeof(PickupRadiusUpgradeData), FileName = "PickupRadius",
                DisplayName = "Pickup Radius", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Pickup Radius",
                Configure = p => Multiplier(p, "1.2")
            },

            // -- Dash --
            new UpgradeSpec
            {
                Type = typeof(DashCooldownUpgradeData), FileName = "DashCooldown",
                DisplayName = "Dash Cooldown", Rarity = UpgradeRarity.Rare,
                Description = "-{0}% Dash Cooldown",
                Configure = p => Multiplier(p, "1.15")
            },
            new UpgradeSpec
            {
                Type = typeof(DashChargeUpgradeData), FileName = "DashCharge",
                DisplayName = "Dash Charge", Rarity = UpgradeRarity.Rare,
                Description = "+{0} charge",
                Configure = p => ((DashChargeUpgradeData)p).Charges = 1
            },

            // -- Hero Skill --
            new UpgradeSpec
            {
                Type = typeof(SkillDamageUpgradeData), FileName = "SkillDamage",
                DisplayName = "Skill Damage", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Skill Damage",
                Configure = p => Multiplier(p, "1.2")
            },
            new UpgradeSpec
            {
                Type = typeof(SkillCooldownUpgradeData), FileName = "SkillCooldown",
                DisplayName = "Skill Cooldown", Rarity = UpgradeRarity.Rare,
                Description = "-{0}% Cooldown",
                Configure = p => Multiplier(p, "1.15")
            },
            new UpgradeSpec
            {
                Type = typeof(SkillDurationUpgradeData), FileName = "SkillDuration",
                DisplayName = "Skill Duration", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Duration",
                Configure = p => Multiplier(p, "1.2")
            },
            new UpgradeSpec
            {
                Type = typeof(SkillAreaUpgradeData), FileName = "SkillArea",
                DisplayName = "Skill Area", Rarity = UpgradeRarity.Common,
                Description = "+{0}% Radius",
                Configure = p => Multiplier(p, "1.2")
            },

            // -- Economy --
            new UpgradeSpec
            {
                Type = typeof(ExperienceGainUpgradeData), FileName = "ExperienceGain",
                DisplayName = "Experience Gain", Rarity = UpgradeRarity.Common,
                Description = "+{0}% XP",
                Configure = p => Multiplier(p, "1.15")
            },
        };

        // Every CharacterStatMultiplierUpgradeData subtype shares the same Multiplier field, so
        // Configure doesn't need to cast down to each concrete leaf type individually.
        private static void Multiplier(GlobalUpgradeData upgrade, string value)
        {
            ((CharacterStatMultiplierUpgradeData)upgrade).Multiplier = FP.FromString(value);
        }

        [MenuItem("Tools/RiftRaiders/Generate Global Upgrade Assets")]
        internal static void Generate()
        {
            if (AssetDatabase.IsValidFolder(FolderPath) == false)
            {
                CreateFolderRecursive(FolderPath);
            }

            int created = 0;
            int updated = 0;

            foreach (var spec in Specs)
            {
                string path = $"{FolderPath}/{spec.FileName}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<GlobalUpgradeData>(path);
                bool isNew = existing == null;

                GlobalUpgradeData asset = isNew
                    ? (GlobalUpgradeData)ScriptableObject.CreateInstance(spec.Type)
                    : existing;

                asset.DisplayName = spec.DisplayName;
                asset.Rarity = spec.Rarity;
                asset.Description = spec.Description;
                spec.Configure(asset);

                if (isNew)
                {
                    AssetDatabase.CreateAsset(asset, path);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(asset);
                    updated++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            var config = AssetDatabase.LoadAssetAtPath<LevelUpConfig>(ConfigAssetPath);

            if (config == null)
            {
                Debug.LogError($"[GlobalUpgradeAssetGenerator] No LevelUpConfig asset at {ConfigAssetPath} - upgrade assets were created/updated, but GlobalUpgrades wasn't wired.");
                return;
            }

            config.GlobalUpgrades = Specs
                .Select(spec => AssetDatabase.LoadAssetAtPath<GlobalUpgradeData>($"{FolderPath}/{spec.FileName}.asset"))
                .Where(asset => asset != null)
                .Select(asset => new AssetRef<GlobalUpgradeData>(asset.Guid))
                .ToList();

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log($"[GlobalUpgradeAssetGenerator] {created} created, {updated} updated, {config.GlobalUpgrades.Count} wired into {ConfigAssetPath}.");
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

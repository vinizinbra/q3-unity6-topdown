namespace QuantumUser.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
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
        private const string ConfigAssetPath = "Assets/_QuantumUser/Resources/Configs/LevelUpConfig.asset";

        private class UpgradeSpec
        {
            public Type Type;
            public string FileName;
            public string DisplayName;
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
                DisplayName = "Weapon Damage",
                Description = "+{0}% Weapon Damage",
                Configure = p => Multiplier(p, "1.1")
            },
            new UpgradeSpec
            {
                Type = typeof(FireRateUpgradeData), FileName = "FireRate",
                DisplayName = "Fire Rate",
                Description = "+{0}% Fire Rate",
                Configure = p => Multiplier(p, "1.1")
            },
            new UpgradeSpec
            {
                Type = typeof(ReloadSpeedUpgradeData), FileName = "ReloadSpeed",
                DisplayName = "Reload Speed",
                Description = "+{0}% Reload Speed",
                Configure = p => Multiplier(p, "1.15")
            },
            new UpgradeSpec
            {
                Type = typeof(MagazineSizeUpgradeData), FileName = "MagazineSize",
                DisplayName = "Magazine Size",
                Description = "+{0}% Magazine",
                Configure = p => ((MagazineSizeUpgradeData)p).Multiplier = FP.FromString("1.2")
            },
            new UpgradeSpec
            {
                Type = typeof(CriticalChanceUpgradeData), FileName = "CriticalChance",
                DisplayName = "Critical Chance",
                Description = "+{0}% Crit Chance",
                Configure = p => ((CriticalChanceUpgradeData)p).Chance = FP.FromString("0.05")
            },
            new UpgradeSpec
            {
                Type = typeof(CriticalDamageUpgradeData), FileName = "CriticalDamage",
                DisplayName = "Critical Damage",
                Description = "+{0}% Crit Damage",
                Configure = p => Multiplier(p, "1.2")
            },
            new UpgradeSpec
            {
                Type = typeof(WeaponRangeUpgradeData), FileName = "WeaponRange",
                DisplayName = "Weapon Range",
                Description = "+{0}% Weapon Range",
                Configure = p => ((WeaponRangeUpgradeData)p).Multiplier = FP.FromString("1.15")
            },
            new UpgradeSpec
            {
                Type = typeof(ProjectileSpeedUpgradeData), FileName = "ProjectileSpeed",
                DisplayName = "Projectile Speed",
                Description = "+{0}% Projectile Speed",
                Configure = p => Multiplier(p, "1.2")
            },

            // -- Hero --
            new UpgradeSpec
            {
                Type = typeof(MaxHealthUpgradeData), FileName = "MaxHealth",
                DisplayName = "Max Health",
                Description = "+{0}% Max HP",
                Configure = p => Multiplier(p, "1.15")
            },
            new UpgradeSpec
            {
                // Replaced the old flat "+10 Shield" pick - see docs/global-upgrades.md. 0.9
                // compounds per pick (never additive), so stacking it all run approaches but never
                // reaches immunity.
                Type = typeof(ToughnessUpgradeData), FileName = "Toughness",
                DisplayName = "Toughness",
                Description = "-{0}% Damage Taken",
                Configure = p => Multiplier(p, "0.9")
            },
            new UpgradeSpec
            {
                Type = typeof(MoveSpeedUpgradeData), FileName = "MoveSpeed",
                DisplayName = "Movement Speed",
                Description = "+{0}% Move Speed",
                Configure = p => Multiplier(p, "1.1")
            },
            new UpgradeSpec
            {
                Type = typeof(HealthRegenUpgradeData), FileName = "HealthRegen",
                DisplayName = "Health Regeneration",
                Description = "+{0} HP/sec",
                Configure = p => ((HealthRegenUpgradeData)p).RegenAmount = FP._1
            },
            new UpgradeSpec
            {
                Type = typeof(HealingReceivedUpgradeData), FileName = "HealingReceived",
                DisplayName = "Healing Received",
                Description = "+{0}% Healing",
                Configure = p => Multiplier(p, "1.2")
            },
            new UpgradeSpec
            {
                Type = typeof(PickupRadiusUpgradeData), FileName = "PickupRadius",
                DisplayName = "Pickup Radius",
                Description = "+{0}% Pickup Radius",
                Configure = p => Multiplier(p, "1.2")
            },

            // -- Dash --
            new UpgradeSpec
            {
                Type = typeof(DashCooldownUpgradeData), FileName = "DashCooldown",
                DisplayName = "Dash Cooldown",
                Description = "-{0}% Dash Cooldown",
                Configure = p => Multiplier(p, "1.15")
            },
            new UpgradeSpec
            {
                Type = typeof(DashChargeUpgradeData), FileName = "DashCharge",
                DisplayName = "Dash Charge",
                Description = "+{0} charge",
                Configure = p => ((DashChargeUpgradeData)p).Charges = 1
            },

            // -- Hero Skill --
            new UpgradeSpec
            {
                Type = typeof(SkillDamageUpgradeData), FileName = "SkillDamage",
                DisplayName = "Skill Damage",
                Description = "+{0}% Skill Damage",
                Configure = p => Multiplier(p, "1.2")
            },
            new UpgradeSpec
            {
                Type = typeof(SkillCooldownUpgradeData), FileName = "SkillCooldown",
                DisplayName = "Skill Cooldown",
                Description = "-{0}% Cooldown",
                Configure = p => Multiplier(p, "1.15")
            },
            new UpgradeSpec
            {
                Type = typeof(SkillDurationUpgradeData), FileName = "SkillDuration",
                DisplayName = "Skill Duration",
                Description = "+{0}% Duration",
                Configure = p => Multiplier(p, "1.2")
            },
            new UpgradeSpec
            {
                Type = typeof(SkillAreaUpgradeData), FileName = "SkillArea",
                DisplayName = "Skill Area",
                Description = "+{0}% Radius",
                Configure = p => Multiplier(p, "1.2")
            },
            new UpgradeSpec
            {
                Type = typeof(HeroSkillChargeUpgradeData), FileName = "HeroSkillCharge",
                DisplayName = "Hero Skill Charge",
                Description = "+{0} charge",
                Configure = p => ((HeroSkillChargeUpgradeData)p).Charges = 1
            },

            // -- Economy --
            new UpgradeSpec
            {
                Type = typeof(ExperienceGainUpgradeData), FileName = "ExperienceGain",
                DisplayName = "Experience Gain",
                // Experience is one SHARED run-wide total, so this benefits the whole team - but it
                // only applies to orbs this player personally walks into (CurrencyOrbSystem scales
                // by the finder's own multiplier).
                Description = "+{0}% XP for the team",
                Configure = p => Multiplier(p, "1.10")
            },
            new UpgradeSpec
            {
                Type = typeof(CoinGainUpgradeData), FileName = "CoinGain",
                DisplayName = "Coin Gain",
                // Deliberately worded as personal: Coins are per-player wallets, so unlike XP above
                // this only ever raises the picker's own income (CoinUtility.GrantAll scales each
                // player by their OWN multiplier).
                Description = "+{0}% Coins for you",
                Configure = p => Multiplier(p, "1.20")
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
                LogHelper.Error("GlobalUpgradeAssetGenerator", $"No LevelUpConfig asset at {ConfigAssetPath} - upgrade assets were created/updated, but GlobalUpgrades wasn't wired.");
                return;
            }

            config.GlobalUpgrades = Specs
                .Select(spec => AssetDatabase.LoadAssetAtPath<GlobalUpgradeData>($"{FolderPath}/{spec.FileName}.asset"))
                .Where(asset => asset != null)
                .Select(asset => new AssetRef<GlobalUpgradeData>(asset.Guid))
                .ToList();

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            LogHelper.Log("GlobalUpgradeAssetGenerator", $"{created} created, {updated} updated, {config.GlobalUpgrades.Count} wired into {ConfigAssetPath}.");
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

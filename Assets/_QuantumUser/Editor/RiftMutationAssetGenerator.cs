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

    // Authors one .asset instance per implemented RiftMutationData class (see
    // docs/rift-mutations.md), plus wiring them into the existing LevelUpConfig.asset's two Rift
    // Mutation lists - RiftMutations (the 14 "core" specs below) and RiftMarkMutations (the 11
    // "Rift Mark content pool" specs, tagged MutationSpec.RiftMarkPool = true). Mirrors
    // GlobalUpgradeAssetGenerator.cs/WeaponPerkAssetGenerator.cs exactly (same folder-creation/
    // update-in-place/rebuild-the-list-from-scratch behavior); re-running this is safe for the same
    // reasons those are.
    //
    // Every Description below is a template ({0}/{1} filled in from the asset's own live values via
    // DescriptionArgs - see RiftMutationData.View.cs), not a plain string - so retuning a Configure
    // value here (or later, by hand, in the Inspector) can't drift out of sync with the sentence
    // describing it, same convention as every other Upgrade/Perk Description field.
    public static class RiftMutationAssetGenerator
    {
        private const string FolderPath = "Assets/_QuantumUser/Resources/LevelUp/RiftMutation";
        private const string ConfigAssetPath = "Assets/_QuantumUser/Resources/LevelUpConfig.asset";

        private class MutationSpec
        {
            public Type Type;
            public string FileName;
            public string DisplayName;
            public UpgradeRarity Rarity;
            public string Description;
            public Action<RiftMutationData> Configure;

            // False (default) = wired into LevelUpConfig.RiftMutations (the 14 "core" mutations).
            // True = wired into LevelUpConfig.RiftMarkMutations (the 11 "Rift Mark content pool"
            // mutations below) - see Generate()'s config-wiring block.
            public bool RiftMarkPool;
        }

        private static readonly List<MutationSpec> Specs = new()
        {
            new MutationSpec
            {
                Type = typeof(GlassCoreMutationData), FileName = "GlassCore",
                DisplayName = "Glass Core", Rarity = UpgradeRarity.Legendary,
                Description = "Shield ×{0:0.#}, Max Health becomes {1:0}",
                Configure = p =>
                {
                    var mutation = (GlassCoreMutationData)p;
                    mutation.ShieldMultiplier = 2;
                    mutation.TargetMaxHealth = FP._1;
                }
            },
            new MutationSpec
            {
                Type = typeof(LastBastionMutationData), FileName = "LastBastion",
                DisplayName = "Last Bastion", Rarity = UpgradeRarity.Legendary,
                Description = "+{0:0}% Max Health, Shield removed",
                Configure = p => ((LastBastionMutationData)p).HealthMultiplier = 2
            },
            new MutationSpec
            {
                Type = typeof(HeavyArsenalMutationData), FileName = "HeavyArsenal",
                DisplayName = "Heavy Arsenal", Rarity = UpgradeRarity.Epic,
                Description = "{0:+0;-0}% Weapon Damage, {1:+0;-0}% Fire Rate",
                Configure = p =>
                {
                    var mutation = (HeavyArsenalMutationData)p;
                    mutation.DamageMultiplier = FP.FromString("1.75");
                    mutation.FireRateMultiplier = FP.FromString("0.65");
                }
            },
            new MutationSpec
            {
                Type = typeof(BulletStormMutationData), FileName = "BulletStorm",
                DisplayName = "Bullet Storm", Rarity = UpgradeRarity.Epic,
                Description = "{0:+0;-0}% Fire Rate, {1:+0;-0}% Weapon Damage",
                Configure = p =>
                {
                    var mutation = (BulletStormMutationData)p;
                    mutation.FireRateMultiplier = FP.FromString("1.6");
                    mutation.DamageMultiplier = FP.FromString("0.7");
                }
            },
            new MutationSpec
            {
                Type = typeof(OneInTheChamberMutationData), FileName = "OneInTheChamber",
                DisplayName = "One in the Chamber", Rarity = UpgradeRarity.Legendary,
                Description = "Magazine becomes 1 round, +{0}% damage on it",
                Configure = p => ((OneInTheChamberMutationData)p).FinalRoundDamageBonus = 4
            },
            new MutationSpec
            {
                Type = typeof(UltimateCommitmentMutationData), FileName = "UltimateCommitment",
                DisplayName = "Ultimate Commitment", Rarity = UpgradeRarity.Epic,
                Description = "{0:+0;-0}% Skill Damage, {1:+0;-0}% Skill Cooldown",
                Configure = p =>
                {
                    var mutation = (UltimateCommitmentMutationData)p;
                    mutation.SkillDamageMultiplier = 2;
                    mutation.SkillCooldownRateMultiplier = FP._0_50;
                }
            },
            new MutationSpec
            {
                Type = typeof(FocusedPowerMutationData), FileName = "FocusedPower",
                DisplayName = "Focused Power", Rarity = UpgradeRarity.Epic,
                Description = "{0:+0;-0}% Skill Area, {1:+0;-0}% Skill Damage",
                Configure = p =>
                {
                    var mutation = (FocusedPowerMutationData)p;
                    mutation.SkillAreaMultiplier = FP._0_50;
                    mutation.SkillDamageMultiplier = 2;
                }
            },
            new MutationSpec
            {
                Type = typeof(InfiniteMomentumMutationData), FileName = "InfiniteMomentum",
                DisplayName = "Infinite Momentum", Rarity = UpgradeRarity.Epic,
                Description = "{0:+0;-0}% Dash Cooldown, Dash now costs {1:0.#} Shield",
                Configure = p =>
                {
                    var mutation = (InfiniteMomentumMutationData)p;
                    mutation.DashCooldownRateMultiplier = 2;
                    mutation.ShieldCost = 10;
                }
            },
            new MutationSpec
            {
                Type = typeof(AllOrNothingMutationData), FileName = "AllOrNothing",
                DisplayName = "All or Nothing", Rarity = UpgradeRarity.Epic,
                Description = "Your next level-up (and every one after) offers a single, higher-rarity choice instead of 3",
                Configure = p => { }
            },
            new MutationSpec
            {
                Type = typeof(GreedMutationData), FileName = "Greed",
                DisplayName = "Greed", Rarity = UpgradeRarity.Legendary,
                Description = "+{0:0}% Rift Shards, enemies gain +{1}% Max Health",
                Configure = p =>
                {
                    var mutation = (GreedMutationData)p;
                    mutation.RiftShardMultiplier = 2;
                    mutation.EnemyHealthBonus = FP._0_50;
                }
            },
            new MutationSpec
            {
                Type = typeof(CloseQuartersMutationData), FileName = "CloseQuarters",
                DisplayName = "Close Quarters", Rarity = UpgradeRarity.Rare,
                Description = "{0:+0;-0}% Damage up close, {1:+0;-0}% Damage at range",
                Configure = p =>
                {
                    var mutation = (CloseQuartersMutationData)p;
                    mutation.NearMultiplier = FP.FromString("1.5");
                    mutation.FarMultiplier = FP.FromString("0.7");
                }
            },
            new MutationSpec
            {
                Type = typeof(LongshotMutationData), FileName = "Longshot",
                DisplayName = "Longshot", Rarity = UpgradeRarity.Rare,
                Description = "{0:+0;-0}% Damage at range, {1:+0;-0}% Damage up close",
                Configure = p =>
                {
                    var mutation = (LongshotMutationData)p;
                    mutation.FarMultiplier = FP.FromString("1.5");
                    mutation.NearMultiplier = FP.FromString("0.7");
                }
            },
            new MutationSpec
            {
                Type = typeof(CriticalFocusMutationData), FileName = "CriticalFocus",
                DisplayName = "Critical Focus", Rarity = UpgradeRarity.Epic,
                Description = "Critical hits reduce Hero Skill and Dash cooldown by {0:0.#}s",
                Configure = p => ((CriticalFocusMutationData)p).CooldownReduction = FP._0_50
            },
            new MutationSpec
            {
                Type = typeof(ShieldBreakerMutationData), FileName = "ShieldBreaker",
                DisplayName = "Shield Breaker", Rarity = UpgradeRarity.Rare,
                Description = "Breaking your Shield grants an immediately-usable Dash charge",
                Configure = p => { }
            },

            // -- Rift Mark content pool (see docs/rift-mutations.md) - wired into LevelUpConfig.
            // RiftMarkMutations, not RiftMutations, via RiftMarkPool = true below. Every threshold/
            // cooldown/radius below is a shared, global ElementalReactionConfig value, not baked
            // per-asset (unlike the mutations above), so every Configure here is a no-op and every
            // Description is a plain string with no {0}/{1} template args - same shape All or
            // Nothing/Shield Breaker already use for a mutation with nothing live to template in.
            new MutationSpec
            {
                Type = typeof(CriticalFractureMutationData), FileName = "CriticalFracture",
                DisplayName = "Critical Fracture", Rarity = UpgradeRarity.Rare,
                Description = "Critical hits apply Rift Mark",
                Configure = p => { },
                RiftMarkPool = true
            },
            new MutationSpec
            {
                Type = typeof(SkillFractureMutationData), FileName = "SkillFracture",
                DisplayName = "Skill Fracture", Rarity = UpgradeRarity.Rare,
                Description = "Hero Skill hits apply Rift Mark",
                Configure = p => { },
                RiftMarkPool = true
            },
            new MutationSpec
            {
                Type = typeof(RiftDashMutationData), FileName = "RiftDash",
                DisplayName = "Rift Dash", Rarity = UpgradeRarity.Rare,
                Description = "Dashing through an enemy applies Rift Mark",
                Configure = p => { },
                RiftMarkPool = true
            },
            new MutationSpec
            {
                Type = typeof(HeavyFractureMutationData), FileName = "HeavyFracture",
                DisplayName = "Heavy Fracture", Rarity = UpgradeRarity.Rare,
                Description = "Large hits apply Rift Mark",
                Configure = p => { },
                RiftMarkPool = true
            },
            new MutationSpec
            {
                Type = typeof(CloseFractureMutationData), FileName = "CloseFracture",
                DisplayName = "Close Fracture", Rarity = UpgradeRarity.Rare,
                Description = "Hits against nearby enemies periodically apply Rift Mark",
                Configure = p => { },
                RiftMarkPool = true
            },
            new MutationSpec
            {
                Type = typeof(LongFractureMutationData), FileName = "LongFracture",
                DisplayName = "Long Fracture", Rarity = UpgradeRarity.Rare,
                Description = "Hits against distant enemies periodically apply Rift Mark",
                Configure = p => { },
                RiftMarkPool = true
            },
            new MutationSpec
            {
                Type = typeof(ExecutionFractureMutationData), FileName = "ExecutionFracture",
                DisplayName = "Execution Fracture", Rarity = UpgradeRarity.Rare,
                Description = "Hitting enemies below 25% health applies Rift Mark",
                Configure = p => { },
                RiftMarkPool = true
            },
            new MutationSpec
            {
                Type = typeof(FirstContactMutationData), FileName = "FirstContact",
                DisplayName = "First Contact", Rarity = UpgradeRarity.Rare,
                Description = "The first hit against a full-health enemy applies Rift Mark",
                Configure = p => { },
                RiftMarkPool = true
            },
            new MutationSpec
            {
                Type = typeof(LastStandMutationData), FileName = "LastStand",
                DisplayName = "Last Stand", Rarity = UpgradeRarity.Epic,
                Description = "Taking a large hit marks nearby enemies",
                Configure = p => { },
                RiftMarkPool = true
            },
            new MutationSpec
            {
                Type = typeof(FracturedPresenceMutationData), FileName = "FracturedPresence",
                DisplayName = "Fractured Presence", Rarity = UpgradeRarity.Rare,
                Description = "Enemies that remain near you become Rift-marked",
                Configure = p => { },
                RiftMarkPool = true
            },
            new MutationSpec
            {
                Type = typeof(OverflowingRiftMutationData), FileName = "OverflowingRift",
                DisplayName = "Overflowing Rift", Rarity = UpgradeRarity.Epic,
                Description = "Marking an already-maxed target releases a small Rift pulse instead",
                Configure = p => { },
                RiftMarkPool = true
            },
        };

        [MenuItem("Tools/RiftRaiders/Generate Rift Mutation Assets")]
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
                var existing = AssetDatabase.LoadAssetAtPath<RiftMutationData>(path);
                bool isNew = existing == null;

                RiftMutationData asset = isNew
                    ? (RiftMutationData)ScriptableObject.CreateInstance(spec.Type)
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
                LogHelper.Error("RiftMutationAssetGenerator", $"No LevelUpConfig asset at {ConfigAssetPath} - mutation assets were created/updated, but RiftMutations wasn't wired.");
                return;
            }

            config.RiftMutations = Specs
                .Where(spec => spec.RiftMarkPool == false)
                .Select(spec => AssetDatabase.LoadAssetAtPath<RiftMutationData>($"{FolderPath}/{spec.FileName}.asset"))
                .Where(asset => asset != null)
                .Select(asset => new AssetRef<RiftMutationData>(asset.Guid))
                .ToList();

            config.RiftMarkMutations = Specs
                .Where(spec => spec.RiftMarkPool)
                .Select(spec => AssetDatabase.LoadAssetAtPath<RiftMutationData>($"{FolderPath}/{spec.FileName}.asset"))
                .Where(asset => asset != null)
                .Select(asset => new AssetRef<RiftMutationData>(asset.Guid))
                .ToList();

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            LogHelper.Log("RiftMutationAssetGenerator", $"{created} created, {updated} updated, {config.RiftMutations.Count} Rift Mutation + {config.RiftMarkMutations.Count} Rift Mark Mutation wired into {ConfigAssetPath}.");
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

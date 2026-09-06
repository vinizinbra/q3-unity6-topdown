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
    // docs/rift-mutations.md), plus wiring them into the existing LevelUpConfig.asset's
    // RiftMutations list (the specs below). Mirrors
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
        private const string ConfigAssetPath = "Assets/_QuantumUser/Resources/Configs/LevelUpConfig.asset";

        private class MutationSpec
        {
            public Type Type;
            public string FileName;
            public string DisplayName;
            public UpgradeRarity Rarity;
            public string Description;
            public Action<RiftMutationData> Configure;

            // Player (default) = affects only its picker. Run = changes shared simulation state and
            // is applied exactly once per run - see MutationScope/RunMutations.qtn.
            public MutationScope Scope = MutationScope.Player;

            // FileNames of mutations this one can never be owned alongside. Resolved into real
            // AssetRefs in a SECOND pass, after every asset exists and AssetDatabase.Refresh has
            // stamped their Guids - a first-pass lookup would find nothing on a clean generate.
            // The check itself is symmetric (RiftMutationUtility.IsBlocked), so each exclusive pair
            // only needs listing on ONE of its two sides.
            public string[] IncompatibleWithFileNames;
        }

        private static readonly List<MutationSpec> Specs = new()
        {
            new MutationSpec
            {
                Type = typeof(GlassCoreMutationData), FileName = "GlassCore",
                DisplayName = "Glass Core", Rarity = UpgradeRarity.Legendary,
                Description = "Accessory durability x{0:0.#}, but {1:+0;-0}% Max Health",
                Configure = p =>
                {
                    var mutation = (GlassCoreMutationData)p;
                    mutation.DurabilityMultiplier = 2;
                    mutation.HealthMultiplier = FP._0_50;
                }
            },
            new MutationSpec
            {
                Type = typeof(LastBastionMutationData), FileName = "LastBastion",
                DisplayName = "Last Bastion", Rarity = UpgradeRarity.Legendary,
                Description = "+{0:0}% Max Health, but you no longer have an Accessory",
                Configure = p => ((LastBastionMutationData)p).HealthMultiplier = 2
            },
            new MutationSpec
            {
                Type = typeof(HeavyArsenalMutationData), FileName = "HeavyArsenal",
                DisplayName = "Heavy Arsenal", Rarity = UpgradeRarity.Epic,
                Description = "{0:+0;-0}% Weapon Damage, {1:+0;-0}% Fire Rate. Shots knock back and stagger.",
                Configure = p =>
                {
                    var mutation = (HeavyArsenalMutationData)p;
                    mutation.DamageMultiplier = FP.FromString("1.6");
                    mutation.FireRateMultiplier = FP.FromString("0.7");
                    mutation.KnockbackMultiplier = FP.FromString("1.5");
                    mutation.StaggerChance = FP.FromString("0.15");
                    mutation.StaggerDuration = FP.FromString("0.5");
                }
            },
            new MutationSpec
            {
                Type = typeof(BulletStormMutationData), FileName = "BulletStorm",
                DisplayName = "Bullet Storm", Rarity = UpgradeRarity.Epic,
                Description = "{0:+0;-0}% Fire Rate, {1:+0;-0}% Magazine Size, {2:+0;-0}% Weapon Damage, {3:+0;-0}% Reload Speed",
                Configure = p =>
                {
                    var mutation = (BulletStormMutationData)p;
                    mutation.FireRateMultiplier = FP.FromString("1.5");
                    mutation.MagazineSizeBonus = FP.FromString("0.5");
                    mutation.DamageMultiplier = FP.FromString("0.7");
                    mutation.ReloadSpeedMultiplier = FP.FromString("0.75");
                }
            },
            new MutationSpec
            {
                Type = typeof(OneInTheChamberMutationData), FileName = "OneInTheChamber",
                DisplayName = "One in the Chamber", Rarity = UpgradeRarity.Legendary,
                Description = "Magazine Size becomes {0:0}, but that round deals {1:+0;-0}% Weapon Damage",
                Configure = p =>
                {
                    var mutation = (OneInTheChamberMutationData)p;
                    mutation.MagazineSize = 1;
                    mutation.DamageMultiplier = 5;
                }
            },
            new MutationSpec
            {
                Type = typeof(CloseQuartersMutationData), FileName = "CloseQuarters",
                DisplayName = "Close Quarters", Rarity = UpgradeRarity.Rare,
                Description = "{0:+0;-0}% Damage up close, {1:+0;-0}% at range. Close kills grant {2:+0;-0}% Move Speed for {3:0.#}s.",
                Configure = p =>
                {
                    var mutation = (CloseQuartersMutationData)p;
                    mutation.NearMultiplier = FP.FromString("1.5");
                    mutation.FarMultiplier = FP.FromString("0.7");
                    mutation.KillMoveSpeedBonus = FP.FromString("0.2");
                    mutation.KillMoveSpeedDuration = 2;
                }
            },
            new MutationSpec
            {
                Type = typeof(LongshotMutationData), FileName = "Longshot",
                DisplayName = "Longshot", Rarity = UpgradeRarity.Rare,
                Description = "Up to {0:+0;-0}% Damage at range and +{1:0} Pierce on distant shots, {2:+0;-0}% up close",
                Configure = p =>
                {
                    var mutation = (LongshotMutationData)p;
                    mutation.FarMultiplier = FP.FromString("1.5");
                    mutation.NearMultiplier = FP.FromString("0.75");
                    mutation.LongRangePierceBonus = 1;
                }
            },
            new MutationSpec
            {
                Type = typeof(UltimateCommitmentMutationData), FileName = "UltimateCommitment",
                DisplayName = "Ultimate Commitment", Rarity = UpgradeRarity.Epic,
                Description = "{0:+0;-0}% Hero Skill Damage, {1:+0;-0}% Hero Skill Cooldown",
                Configure = p =>
                {
                    var mutation = (UltimateCommitmentMutationData)p;
                    mutation.SkillDamageMultiplier = 2;
                    // A RATE (StatUtility.GetSkillCooldown divides by it), so halving the rate is
                    // what doubles the actual cooldown duration the brief asks for.
                    mutation.SkillCooldownRateMultiplier = FP._0_50;
                }
            },
            new MutationSpec
            {
                Type = typeof(FocusedPowerMutationData), FileName = "FocusedPower",
                DisplayName = "Focused Power", Rarity = UpgradeRarity.Epic,
                Description = "{0:+0;-0}% Skill Area, but Skill Damage rises to {1:+0;-0}% at the center of the effect",
                Configure = p =>
                {
                    var mutation = (FocusedPowerMutationData)p;
                    mutation.SkillAreaMultiplier = FP._0_50;
                    mutation.CenterDamageBonus = FP.FromString("1.5");
                }
            },
            new MutationSpec
            {
                Type = typeof(InfiniteMomentumMutationData), FileName = "InfiniteMomentum",
                DisplayName = "Infinite Momentum", Rarity = UpgradeRarity.Epic,
                Description = "While Dash is on cooldown, keep Dashing for {0:0.#}% of your Max Health each time",
                Configure = p => ((InfiniteMomentumMutationData)p).HealthCostFraction = FP.FromString("0.05")
            },
            new MutationSpec
            {
                Type = typeof(CriticalFocusMutationData), FileName = "CriticalFocus",
                DisplayName = "Critical Focus", Rarity = UpgradeRarity.Epic,
                Description = "Every {0:0} Critical Hits, reduce Hero Skill and Dash cooldowns by {1:0.#}s",
                Configure = p =>
                {
                    var mutation = (CriticalFocusMutationData)p;
                    mutation.CritsRequired = 3;
                    mutation.CooldownReduction = FP._1;
                }
            },
            new MutationSpec
            {
                Type = typeof(AdrenalineKickMutationData), FileName = "AdrenalineKick",
                DisplayName = "Adrenaline Kick", Rarity = UpgradeRarity.Epic,
                Description = "When your Accessory blocks a hit, reset Dash and cut {0:0}% off your remaining Hero Skill cooldown",
                Configure = p => ((AdrenalineKickMutationData)p).SkillCooldownFraction = FP._0_50
            },
            new MutationSpec
            {
                Type = typeof(MoneyTalksMutationData), FileName = "MoneyTalks",
                DisplayName = "Money Talks", Rarity = UpgradeRarity.Epic,
                Description = "+{0:0.#}% Damage per {1:0} Coins you are carrying, up to +{2:0}%",
                Configure = p =>
                {
                    var mutation = (MoneyTalksMutationData)p;
                    mutation.DamagePerHundredCoins = FP.FromString("0.05");
                    mutation.MaxDamageBonus = FP.FromString("0.40");
                }
            },
            new MutationSpec
            {
                Type = typeof(SparePartsMutationData), FileName = "SpareParts",
                DisplayName = "Spare Parts", Rarity = UpgradeRarity.Epic,
                Description = "Once per run, a destroyed Accessory instantly returns with {0:0} durability",
                Configure = p =>
                {
                    var mutation = (SparePartsMutationData)p;
                    mutation.Charges = 1;
                    mutation.RestoreDurability = 2;
                }
            },
            new MutationSpec
            {
                Type = typeof(DangerPayMutationData), FileName = "DangerPay",
                DisplayName = "Danger Pay", Rarity = UpgradeRarity.Epic,
                Description = "Below {0:0}% Health: +{1:0}% Damage and +{2:0}% Move Speed",
                Configure = p =>
                {
                    var mutation = (DangerPayMutationData)p;
                    mutation.HealthThreshold = FP.FromString("0.40");
                    mutation.DamageBonus = FP.FromString("0.35");
                    mutation.MoveSpeedBonus = FP.FromString("0.20");
                }
            },
            new MutationSpec
            {
                Type = typeof(OverkillMutationData), FileName = "Overkill",
                DisplayName = "Overkill", Rarity = UpgradeRarity.Epic,
                Description = "Damage beyond a killed enemy's health explodes for {0:0}% of the excess",
                Configure = p =>
                {
                    var mutation = (OverkillMutationData)p;
                    mutation.OverkillConversion = FP._0_50;
                    mutation.ExplosionRadius = 3;
                }
            },
            new MutationSpec
            {
                Type = typeof(ScavengerRushMutationData), FileName = "ScavengerRush",
                DisplayName = "Scavenger Rush", Rarity = UpgradeRarity.Rare,
                Description = "Collect {0:0} pickups within {1:0.#}s: +{2:0}% Move Speed and +{3:0}% Fire Rate for {4:0.#}s",
                Configure = p =>
                {
                    var mutation = (ScavengerRushMutationData)p;
                    mutation.RequiredPickups = 5;
                    mutation.CollectionWindow = 3;
                    mutation.BuffDuration = 4;
                    mutation.MoveSpeedBonus = FP.FromString("0.30");
                    mutation.FireRateBonus = FP.FromString("0.30");
                }
            },
            new MutationSpec
            {
                Type = typeof(BloodMoneyMutationData), FileName = "BloodMoney",
                DisplayName = "Blood Money", Rarity = UpgradeRarity.Legendary,
                Description = "{0:+0;-0}% Coins, but lose {1:0}% of your Coins whenever you take Health damage",
                Configure = p =>
                {
                    var mutation = (BloodMoneyMutationData)p;
                    mutation.CoinDropMultiplier = FP.FromString("1.50");
                    mutation.CoinLossPercentOnHpDamage = FP.FromString("0.10");
                }
            },
            new MutationSpec
            {
                Type = typeof(NoSafetyNetMutationData), FileName = "NoSafetyNet",
                DisplayName = "No Safety Net", Rarity = UpgradeRarity.Legendary,
                Description = "+{0:0}% Damage while your Accessory is not on your head",
                Configure = p => ((NoSafetyNetMutationData)p).DamageBonus = FP.FromString("0.75")
            },
            new MutationSpec
            {
                Type = typeof(SecondWindMutationData), FileName = "SecondWind",
                DisplayName = "Second Wind", Rarity = UpgradeRarity.Epic,
                Description = "Recovering your Accessory heals {0:0}% of Max Health",
                Configure = p => ((SecondWindMutationData)p).HealPercentMaxHp = FP.FromString("0.05")
            },
            new MutationSpec
            {
                Type = typeof(DeadWeightMutationData), FileName = "DeadWeight",
                DisplayName = "Dead Weight", Rarity = UpgradeRarity.Legendary,
                Description = "+{0:0}% Weapon Damage, but only {1:0} Dash charge and {2:+0;-0}% Dash Cooldown",
                Configure = p =>
                {
                    var mutation = (DeadWeightMutationData)p;
                    mutation.WeaponDamageBonus = FP._0_50;
                    mutation.DashChargeHardCap = 1;
                    mutation.DashCooldownMultiplier = FP.FromString("1.50");
                }
            },
            new MutationSpec
            {
                Type = typeof(PressureCookerMutationData), FileName = "PressureCooker",
                DisplayName = "Pressure Cooker", Rarity = UpgradeRarity.Epic,
                Description = "+{0:0}% Damage per second without taking damage, up to +{1:0}%",
                Configure = p =>
                {
                    var mutation = (PressureCookerMutationData)p;
                    mutation.DamagePerSecond = FP.FromString("0.03");
                    mutation.MaxDamageBonus = FP.FromString("0.30");
                }
            },
            new MutationSpec
            {
                Type = typeof(GreedMutationData), FileName = "Greed",
                DisplayName = "Greed", Rarity = UpgradeRarity.Legendary,
                Description = "The whole team gains {0:+0;-0}% Rift Shards, but every enemy gains {1:+0;-0}% Max Health",
                Scope = MutationScope.Run,
                Configure = p =>
                {
                    var mutation = (GreedMutationData)p;
                    mutation.RiftShardGainBonus = FP._1;
                    mutation.EnemyHealthBonus = FP._0_50;
                }
            },
            new MutationSpec
            {
                Type = typeof(OverpopulationMutationData), FileName = "Overpopulation",
                DisplayName = "Overpopulation", Rarity = UpgradeRarity.Epic,
                Description = "{0:+0;-0}% enemies, but they have {1:+0;-0}% Max Health",
                Scope = MutationScope.Run,
                Configure = p =>
                {
                    var mutation = (OverpopulationMutationData)p;
                    mutation.SpawnDensityBonus = FP.FromString("0.4");
                    mutation.EnemyHealthBonus = FP.FromString("-0.25");
                }
            },
            new MutationSpec
            {
                Type = typeof(EliteTerritoryMutationData), FileName = "EliteTerritory",
                DisplayName = "Elite Territory", Rarity = UpgradeRarity.Legendary,
                Description = "{0:+0;-0}% enemies, but Elites appear {1:0.#}x as often",
                Scope = MutationScope.Run,
                Configure = p =>
                {
                    var mutation = (EliteTerritoryMutationData)p;
                    mutation.SpawnDensityBonus = FP.FromString("-0.3");
                    mutation.EliteWeightMultiplier = FP.FromString("2.5");
                }
            },
            new MutationSpec
            {
                Type = typeof(BloodTitheMutationData), FileName = "BloodTithe",
                DisplayName = "Blood Tithe", Rarity = UpgradeRarity.Epic,
                Description = "The whole team gains {0:+0;-0}% Rift Shards, but enemies deal {1:+0;-0}% Damage",
                Scope = MutationScope.Run,
                Configure = p =>
                {
                    var mutation = (BloodTitheMutationData)p;
                    mutation.RiftShardGainBonus = FP.FromString("0.75");
                    mutation.EnemyDamageBonus = FP.FromString("0.25");
                }
            },
            new MutationSpec
            {
                Type = typeof(EscalationMutationData), FileName = "Escalation",
                DisplayName = "Escalation", Rarity = UpgradeRarity.Epic,
                Description = "Enemies spawn faster and faster as each Survival phase goes on, up to {0:0.##}x by its end",
                Scope = MutationScope.Run,
                Configure = p => ((EscalationMutationData)p).EndOfPhaseDensityBonus = FP.FromString("0.75")
            },
        };

        [MenuItem("Tools/RiftRaiders/Progression/Generate Rift Mutation Assets")]
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
                asset.Scope = spec.Scope;
                asset.IncompatibleWith.Clear(); // rebuilt from scratch in the second pass below
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

            WireIncompatibilities();

            var config = AssetDatabase.LoadAssetAtPath<LevelUpConfig>(ConfigAssetPath);

            if (config == null)
            {
                LogHelper.Error("RiftMutationAssetGenerator", $"No LevelUpConfig asset at {ConfigAssetPath} - mutation assets were created/updated, but RiftMutations wasn't wired.");
                return;
            }

            config.RiftMutations = Specs
                .Select(spec => AssetDatabase.LoadAssetAtPath<RiftMutationData>($"{FolderPath}/{spec.FileName}.asset"))
                .Where(asset => asset != null)
                .Select(asset => new AssetRef<RiftMutationData>(asset.Guid))
                .ToList();

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            LogHelper.Log("RiftMutationAssetGenerator", $"{created} created, {updated} updated, {config.RiftMutations.Count} Rift Mutation wired into {ConfigAssetPath}.");
        }

        // Second pass, deliberately after Refresh: a freshly-created asset has no Guid stamped until
        // QuantumAssetObjectPostprocessor has run, so resolving these inline with the first pass
        // would silently write empty AssetRefs on a clean generate.
        private static void WireIncompatibilities()
        {
            foreach (var spec in Specs)
            {
                if (spec.IncompatibleWithFileNames == null || spec.IncompatibleWithFileNames.Length == 0)
                    continue;

                var asset = AssetDatabase.LoadAssetAtPath<RiftMutationData>($"{FolderPath}/{spec.FileName}.asset");

                if (asset == null)
                    continue;

                foreach (string otherFileName in spec.IncompatibleWithFileNames)
                {
                    var other = AssetDatabase.LoadAssetAtPath<RiftMutationData>($"{FolderPath}/{otherFileName}.asset");

                    if (other == null)
                    {
                        LogHelper.Error("RiftMutationAssetGenerator", $"{spec.FileName} lists '{otherFileName}' as incompatible, but no such asset exists - that exclusion will not apply.");
                        continue;
                    }

                    asset.IncompatibleWith.Add(new AssetRef<RiftMutationData>(other.Guid));
                }

                EditorUtility.SetDirty(asset);
            }

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

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

    // Authors one .asset instance per WeaponPerkData class in the roster (see
    // docs/weapon-perks.md), tuned to the original design table's numbers, plus wiring all of them
    // into the existing WeaponPerkPoolData.asset stub - the two pieces of Editor authoring that doc
    // says are still missing. Re-running this is safe: existing assets at the expected path are
    // updated in place (DisplayName/Rarity/Description/tuned fields overwritten, not duplicated),
    // and the pool's Perks list is rebuilt from scratch each time rather than appended to.
    //
    // Description below is a TEMPLATE, not the final player-facing text - each class's own
    // DescriptionArgs override fills in {0}/{1}/... from its live tuned fields at read time (see
    // WeaponPerkData.GetDescription/GetFormattedDescription, same DescriptionUtility.Format
    // machinery SkillActionData already uses), so retuning a field's value in the Inspector later
    // can never leave this text describing the wrong number - there's nothing to hand-edit back in
    // sync. The literal numbers baked into Configure below are what these templates will actually
    // render on first generation.
    public static class WeaponPerkAssetGenerator
    {
        private const string FolderPath = "Assets/_QuantumUser/Resources/Weapon/WeaponPerk";
        private const string PoolAssetPath = FolderPath + "/WeaponPerkPoolData.asset";

        private class PerkSpec
        {
            public Type Type;
            public string FileName;
            public string DisplayName;
            public UpgradeRarity Rarity;
            public string Description;
            public Action<WeaponPerkData> Configure;
        }

        private static readonly List<PerkSpec> Specs = new()
        {
            // -- Common --
            new PerkSpec
            {
                Type = typeof(HeavyCaliberWeaponPerkData), FileName = "HeavyCaliber",
                DisplayName = "Heavy Caliber", Rarity = UpgradeRarity.Common,
                Description = "{0:+0;-0}% Damage, {1:+0;-0}% Fire Rate",
                Configure = p =>
                {
                    var d = (HeavyCaliberWeaponPerkData)p;
                    d.DamageMultiplier = FP.FromString("1.2");
                    d.FireRateMultiplier = FP.FromString("0.9");
                }
            },
            new PerkSpec
            {
                Type = typeof(FireRateWeaponPerkData), FileName = "RapidMechanism",
                DisplayName = "Rapid Mechanism", Rarity = UpgradeRarity.Common,
                Description = "{0:+0;-0}% Fire Rate",
                Configure = p => ((FireRateWeaponPerkData)p).Multiplier = FP.FromString("1.15")
            },
            new PerkSpec
            {
                Type = typeof(MagazineMultiplierWeaponPerkData), FileName = "ExtendedMagazine",
                DisplayName = "Extended Magazine", Rarity = UpgradeRarity.Common,
                Description = "{0:+0;-0}% Magazine",
                Configure = p => ((MagazineMultiplierWeaponPerkData)p).Multiplier = FP.FromString("1.25")
            },
            new PerkSpec
            {
                Type = typeof(ReloadSpeedWeaponPerkData), FileName = "FastLoader",
                DisplayName = "Fast Loader", Rarity = UpgradeRarity.Common,
                Description = "{0:+0;-0}% Reload Speed",
                Configure = p => ((ReloadSpeedWeaponPerkData)p).Multiplier = FP.FromString("1.2")
            },
            new PerkSpec
            {
                Type = typeof(RangeMultiplierWeaponPerkData), FileName = "LongBarrel",
                DisplayName = "Long Barrel", Rarity = UpgradeRarity.Common,
                Description = "{0:+0;-0}% Weapon Range",
                Configure = p => ((RangeMultiplierWeaponPerkData)p).Multiplier = FP.FromString("1.2")
            },
            new PerkSpec
            {
                Type = typeof(CriticalChanceWeaponPerkData), FileName = "PrecisionBarrel",
                DisplayName = "Precision Barrel", Rarity = UpgradeRarity.Common,
                Description = "{0:+0;-0}% Crit Chance",
                Configure = p => ((CriticalChanceWeaponPerkData)p).Chance = FP.FromString("0.08")
            },
            new PerkSpec
            {
                Type = typeof(CriticalDamageWeaponPerkData), FileName = "HollowPoint",
                DisplayName = "Hollow Point", Rarity = UpgradeRarity.Common,
                Description = "{0:+0;-0}% Crit Damage",
                Configure = p => ((CriticalDamageWeaponPerkData)p).Bonus = FP.FromString("0.25")
            },

            // -- Rare --
            new PerkSpec
            {
                Type = typeof(PiercingRoundsWeaponPerkData), FileName = "PiercingRounds",
                DisplayName = "Piercing Rounds", Rarity = UpgradeRarity.Rare,
                Description = "{0:+0;-0} Pierce",
                Configure = p => ((PiercingRoundsWeaponPerkData)p).BonusPierce = 1
            },
            new PerkSpec
            {
                Type = typeof(RicochetWeaponPerkData), FileName = "Ricochet",
                DisplayName = "Ricochet", Rarity = UpgradeRarity.Rare,
                Description = "+{0:0} Bounce",
                Configure = p => ((RicochetWeaponPerkData)p).BonusBounces = 1
            },
            new PerkSpec
            {
                Type = typeof(DoubleTapWeaponPerkData), FileName = "DoubleTap",
                DisplayName = "Double Tap", Rarity = UpgradeRarity.Rare,
                Description = "{0:0}% chance to fire an extra projectile",
                Configure = p => ((DoubleTapWeaponPerkData)p).Chance = FP.FromString("0.15")
            },
            new PerkSpec
            {
                Type = typeof(OpeningBurstWeaponPerkData), FileName = "OpeningBurst",
                DisplayName = "Opening Burst", Rarity = UpgradeRarity.Rare,
                Description = "First {0:0}% of magazine: {1:+0;-0}% Fire Rate",
                Configure = p =>
                {
                    var d = (OpeningBurstWeaponPerkData)p;
                    d.Threshold = FP.FromString("0.2");
                    d.FireRateBonus = FP.FromString("0.25");
                }
            },
            new PerkSpec
            {
                Type = typeof(ExecutionRoundsWeaponPerkData), FileName = "ExecutionRounds",
                DisplayName = "Execution Rounds", Rarity = UpgradeRarity.Rare,
                Description = "Last {0:0}% of magazine: {1:+0;-0}% Damage",
                Configure = p =>
                {
                    var d = (ExecutionRoundsWeaponPerkData)p;
                    d.Threshold = FP.FromString("0.2");
                    d.DamageBonus = FP.FromString("0.3");
                }
            },
            new PerkSpec
            {
                Type = typeof(FinalRoundWeaponPerkData), FileName = "FinalRound",
                DisplayName = "Final Round", Rarity = UpgradeRarity.Rare,
                Description = "Last bullet deals {0:+0;-0}% Damage",
                Configure = p => ((FinalRoundWeaponPerkData)p).DamageBonus = FP._1
            },
            new PerkSpec
            {
                Type = typeof(KillerInstinctWeaponPerkData), FileName = "KillerInstinct",
                DisplayName = "Killer Instinct", Rarity = UpgradeRarity.Rare,
                Description = "{0:+0;-0}% Fire Rate for {1:0}s after kill",
                Configure = p =>
                {
                    var d = (KillerInstinctWeaponPerkData)p;
                    d.FireRateBonus = FP.FromString("0.15");
                    d.Duration = 2;
                }
            },
            new PerkSpec
            {
                Type = typeof(RelentlessFireWeaponPerkData), FileName = "RelentlessFire",
                DisplayName = "Relentless Fire", Rarity = UpgradeRarity.Rare,
                Description = "Consecutive hits increase damage by {0:0}% per stack (max {1:0} stacks)",
                Configure = p =>
                {
                    var d = (RelentlessFireWeaponPerkData)p;
                    d.MaxStacks = 5;
                    d.DamageBonusPerStack = FP.FromString("0.02");
                    d.DecayGrace = 1;
                }
            },
            new PerkSpec
            {
                Type = typeof(ExplosiveSequenceWeaponPerkData), FileName = "ExplosiveSequence",
                DisplayName = "Explosive Sequence", Rarity = UpgradeRarity.Rare,
                Description = "Every {0:0}th shot explodes for {1:0}% damage in a {2:0}m radius",
                Configure = p =>
                {
                    var d = (ExplosiveSequenceWeaponPerkData)p;
                    d.Interval = 5;
                    d.Radius = 3;
                    d.DamageMultiplier = FP._1;
                }
            },
            new PerkSpec
            {
                Type = typeof(CriticalReboundWeaponPerkData), FileName = "CriticalRebound",
                DisplayName = "Critical Rebound", Rarity = UpgradeRarity.Rare,
                Description = "Crits fire a secondary projectile at {0:0}% damage within {1:0}m",
                Configure = p =>
                {
                    var d = (CriticalReboundWeaponPerkData)p;
                    d.Radius = 8;
                    d.DamageMultiplier = FP._0_50;
                }
            },
            new PerkSpec
            {
                Type = typeof(SplitShotWeaponPerkData), FileName = "SplitShot",
                DisplayName = "Split Shot", Rarity = UpgradeRarity.Rare,
                Description = "Projectile splits into {0:0} shots at {1:0}% damage after impact",
                Configure = p =>
                {
                    var d = (SplitShotWeaponPerkData)p;
                    d.Count = 2;
                    d.DamageMultiplier = FP._0_50;
                }
            },
            new PerkSpec
            {
                Type = typeof(EmptyChamberWeaponPerkData), FileName = "EmptyChamber",
                DisplayName = "Empty Chamber", Rarity = UpgradeRarity.Rare,
                Description = "Empty magazine releases a shockwave that knocks back enemies within {0:0}m",
                Configure = p =>
                {
                    var d = (EmptyChamberWeaponPerkData)p;
                    d.Radius = 4;
                    d.Knockback = 10;
                }
            },
            new PerkSpec
            {
                Type = typeof(EscalatingRoundsWeaponPerkData), FileName = "EscalatingRounds",
                DisplayName = "Escalating Rounds", Rarity = UpgradeRarity.Rare,
                Description = "Damage increases up to {0:+0;-0}% through the magazine",
                Configure = p => ((EscalatingRoundsWeaponPerkData)p).MaxDamageBonus = FP.FromString("0.3")
            },
            new PerkSpec
            {
                Type = typeof(SuppressiveCycleWeaponPerkData), FileName = "SuppressiveCycle",
                DisplayName = "Suppressive Cycle", Rarity = UpgradeRarity.Rare,
                Description = "Fire Rate increases by {0:0}% per stack while continuously firing (max {1:0} stacks)",
                Configure = p =>
                {
                    var d = (SuppressiveCycleWeaponPerkData)p;
                    d.MaxStacks = 5;
                    d.FireRateBonusPerStack = FP.FromString("0.03");
                    d.DecayGrace = 1;
                }
            },
            new PerkSpec
            {
                Type = typeof(PredatorMagazineWeaponPerkData), FileName = "PredatorMagazine",
                DisplayName = "Predator Magazine", Rarity = UpgradeRarity.Rare,
                Description = "Restore {0:0}% magazine on kill",
                Configure = p => ((PredatorMagazineWeaponPerkData)p).RestoreFraction = FP.FromString("0.1")
            },
            new PerkSpec
            {
                Type = typeof(EmergencyReloadWeaponPerkData), FileName = "EmergencyReload",
                DisplayName = "Emergency Reload", Rarity = UpgradeRarity.Rare,
                Description = "Gain {0:+0;-0}% Move Speed and {1:+0;-0}% Damage Reduction while reloading",
                Configure = p =>
                {
                    var d = (EmergencyReloadWeaponPerkData)p;
                    d.MoveSpeedBonus = FP.FromString("0.2");
                    d.DamageReduction = FP.FromString("0.2");
                }
            },

            // -- Epic --
            new PerkSpec
            {
                Type = typeof(OverchargeCycleWeaponPerkData), FileName = "OverchargeCycle",
                DisplayName = "Overcharge Cycle", Rarity = UpgradeRarity.Epic,
                Description = "Continuous fire builds {0:0}% Damage and {1:0}% Fire Rate per stack (max {2:0} stacks)",
                Configure = p =>
                {
                    var d = (OverchargeCycleWeaponPerkData)p;
                    d.MaxStacks = 8;
                    d.DamageBonusPerStack = FP.FromString("0.03");
                    d.FireRateBonusPerStack = FP.FromString("0.03");
                    d.DecayGrace = 1;
                }
            },
            new PerkSpec
            {
                Type = typeof(EchoChamberWeaponPerkData), FileName = "EchoChamber",
                DisplayName = "Echo Chamber", Rarity = UpgradeRarity.Epic,
                Description = "First 3 shots of every magazine repeat after {0:0.0}s",
                Configure = p => ((EchoChamberWeaponPerkData)p).Delay = FP._0_25
            },
            new PerkSpec
            {
                Type = typeof(BottomlessMomentumWeaponPerkData), FileName = "BottomlessMomentum",
                DisplayName = "Bottomless Momentum", Rarity = UpgradeRarity.Epic,
                Description = "{0:0}% chance for crits to restore {1:0} ammo",
                Configure = p =>
                {
                    var d = (BottomlessMomentumWeaponPerkData)p;
                    d.Chance = FP._0_50;
                    d.Amount = 1;
                }
            },
            new PerkSpec
            {
                Type = typeof(CataclysmRoundWeaponPerkData), FileName = "CataclysmRound",
                DisplayName = "Cataclysm Round", Rarity = UpgradeRarity.Epic,
                Description = "Final shot becomes a massive explosive projectile ({0:0}% damage, {1:0}m radius)",
                Configure = p =>
                {
                    var d = (CataclysmRoundWeaponPerkData)p;
                    d.Radius = 5;
                    d.DamageMultiplier = 2;
                }
            },
            new PerkSpec
            {
                Type = typeof(CombatRebootWeaponPerkData), FileName = "CombatReboot",
                DisplayName = "Combat Reboot", Rarity = UpgradeRarity.Epic,
                Description = "Emptying the magazine reduces Hero Skill cooldown by {0:0}s",
                Configure = p => ((CombatRebootWeaponPerkData)p).CooldownReduction = 2
            },

            // -- Legendary --
            new PerkSpec
            {
                Type = typeof(InfiniteEchoWeaponPerkData), FileName = "InfiniteEcho",
                DisplayName = "Infinite Echo", Rarity = UpgradeRarity.Legendary,
                Description = "Every projectile repeats once after {0:0.0}s",
                Configure = p => ((InfiniteEchoWeaponPerkData)p).Delay = FP._0_25
            },
            new PerkSpec
            {
                Type = typeof(QuantumRoundsWeaponPerkData), FileName = "QuantumRounds",
                DisplayName = "Quantum Rounds", Rarity = UpgradeRarity.Legendary,
                Description = "Hits damage an additional nearby enemy for {0:0}% damage within {1:0}m",
                Configure = p =>
                {
                    var d = (QuantumRoundsWeaponPerkData)p;
                    d.Radius = 6;
                    d.DamageMultiplier = FP._1;
                }
            },
        };

        [MenuItem("Tools/RiftRaiders/Generate Weapon Perk Assets")]
        internal static void Generate()
        {
            if (AssetDatabase.IsValidFolder(FolderPath) == false)
            {
                LogHelper.Error("WeaponPerkAssetGenerator", $"{FolderPath} doesn't exist - create it (or the WeaponPerkPoolData.asset stub it should already hold) before running this.");
                return;
            }

            int created = 0;
            int updated = 0;

            foreach (var spec in Specs)
            {
                string path = $"{FolderPath}/{spec.FileName}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<WeaponPerkData>(path);
                bool isNew = existing == null;

                WeaponPerkData asset = isNew
                    ? (WeaponPerkData)ScriptableObject.CreateInstance(spec.Type)
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

            var pool = AssetDatabase.LoadAssetAtPath<WeaponPerkPoolData>(PoolAssetPath);

            if (pool == null)
            {
                LogHelper.Error("WeaponPerkAssetGenerator", $"No WeaponPerkPoolData asset at {PoolAssetPath} - perk assets were created/updated, but the pool wasn't wired.");
                return;
            }

            pool.Perks = Specs
                .Select(spec => AssetDatabase.LoadAssetAtPath<WeaponPerkData>($"{FolderPath}/{spec.FileName}.asset"))
                .Where(asset => asset != null)
                .Select(asset => new AssetRef<WeaponPerkData>(asset.Guid))
                .ToList();

            EditorUtility.SetDirty(pool);
            AssetDatabase.SaveAssets();

            LogHelper.Log("WeaponPerkAssetGenerator", $"{created} created, {updated} updated, {pool.Perks.Count} wired into {PoolAssetPath}.");
        }
    }
}

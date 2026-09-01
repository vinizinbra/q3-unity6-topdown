namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Replaces MaxAdrenalineAssetGenerator/MaxOverdriveAssetGenerator/MaxFireMasteryAssetGenerator/
    // MaxVendettaAssetGenerator (four generators, two with confirmed-stale path constants -
    // "M_HeroSkill" vs the real on-disk "Max_HeroSkill") with one consolidated generator authoring
    // all 10 of Max's Ascension lines - see docs/max-ascensions.md. Same full-replace-every-list-
    // every-run pattern as Pixie/Brute's own consolidations: every run fully replaces
    // MaxCharacterData.PassiveUpgrades (4), .DashSkillUpgrades (2), and MaxHeroSkill.Actions (4),
    // rather than only appending, so the pool can never drift out of sync with what's actually live.
    // Also sweeps MaxHeroSkill.asset's own dead embedded sub-objects (every baseline/orphaned
    // standalone action from before this refactor - MarkExplosiveDeath/BurnOnHit's own Max-private
    // instances included, since both are un-wired here for good, not deleted - the generic classes
    // still serve Pixie/other heroes elsewhere).
    //
    // Per-rank tuned values ARE explicitly set here on every run, even though every ranked ascension
    // class already carries a matching C# field-initializer default - that default only applies to a
    // BRAND NEW object, not one that already existed before a field's TYPE changed shape (a plain FP
    // becoming FP[]) - see PixieAscensionAssetGenerator's own comment for the exact corrupted-array
    // failure mode this avoids.
    public static class MaxAscensionAssetGenerator
    {
        private const string HeroSkillFolderPath = "Assets/_QuantumUser/Resources/Skills/Max/Max_HeroSkill";
        private const string HeroSkillPath = HeroSkillFolderPath + "/MaxHeroSkill.asset";
        private const string OverdriveUpgradesFolderPath = HeroSkillFolderPath + "/Max_HeroSkillUpgrades/Overdrive";

        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Skills/Max/Max_PassiveSkill";
        private const string PassiveSkillUpgradesFolderPath = PassivesFolderPath + "/Max_PassiveSkillUpgrades";
        private const string VendettaUpgradesFolderPath = PassiveSkillUpgradesFolderPath + "/Vendetta";
        private const string FireMasteryUpgradesFolderPath = PassiveSkillUpgradesFolderPath + "/FireMastery";

        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Max/Max_DashSkillUpgrades";

        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/MaxCharacterData.asset";

        // Ignition rank 2's Burning Ground spawn - the same prototype the old baseline
        // SpawnEntitySkillAction (now un-wired from MaxHeroSkill.Actions) used to point at, and the
        // same one the old "Max Burning Ground" Dash variant used before it was dropped entirely.
        private const string BurningGroundPrototypePath = "Assets/_QuantumUser/Resources/Skills/Max/Prefabs/MaxBurningGroundEntityPrototype.qprototype";

        [MenuItem("Tools/RiftRaiders/Max/Generate Ascension Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(OverdriveUpgradesFolderPath);
            CreateFolderRecursive(VendettaUpgradesFolderPath);
            CreateFolderRecursive(FireMasteryUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);

            // PassiveData (unlike PassiveUpgradeData) derives AssetObject directly, not UpgradeData -
            // a hero's single base Passive is Inspector-assigned (CharacterData.Passive), never
            // offered as a level-up card, so it has no DisplayName/Rarity/Description to set here.
            VendettaPassiveData passive = CreateOrUpdate<VendettaPassiveData>($"{PassivesFolderPath}/VendettaPassiveData.asset", asset =>
            {
                asset.Description = "Enemies that damage you get marked - killing one heals you.";

                asset.BaseHealMultiplier = FP._0_50;
                asset.BaseMarkDuration = 8;
                asset.BaseDamageBonus = FP.FromString("0.15");
                asset.BaseMinHealFraction = FP.FromString("0.01");
            });

            LastStandSkillAction lastStand = CreateOrUpdate<LastStandSkillAction>($"{OverdriveUpgradesFolderPath}/LastStandSkillAction.asset", asset =>
            {
                asset.DisplayName = "Last Stand";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Rage stops being fragile - it survives Overdrive ending, damage, and death.";
                asset.RankDescriptions = new[]
                {
                    "Rage carries over between Overdrives instead of resetting.",
                    "Rage carries over between Overdrives, and damage taken during Overdrive costs only half of it.",
                    "Rage carries over and resists damage. Lethal damage during Overdrive leaves you at 1 Health and grants 2s immunity.",
                };
                asset.RageLossFraction = new[] { FP._1, FP._0_50, FP._0_50 };
                asset.CheatDeathImmunityDuration = 2;
            });

            FullThrottleSkillAction fullThrottle = CreateOrUpdate<FullThrottleSkillAction>($"{OverdriveUpgradesFolderPath}/FullThrottleSkillAction.asset", asset =>
            {
                asset.DisplayName = "Full Throttle";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "At max Rage, gain bonus Weapon Damage and faster reloads.";
                asset.RankDescriptions = new[]
                {
                    "At max Rage, +20% Weapon Damage.",
                    "At max Rage, +30% Weapon Damage and +50% Reload Speed.",
                    "At max Rage, +40% Weapon Damage and instant reloads.",
                };
                asset.WeaponDamageBonus = new[] { FP.FromString("0.20"), FP.FromString("0.30"), FP.FromString("0.40") };
                asset.ReloadSpeedBonus = new[] { FP._0, FP._0_50, FP._0_50 };
            });

            UncontrolledFurySkillAction uncontrolledFury = CreateOrUpdate<UncontrolledFurySkillAction>($"{OverdriveUpgradesFolderPath}/UncontrolledFurySkillAction.asset", asset =>
            {
                asset.DisplayName = "Uncontrolled Fury";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Kills during Overdrive extend it.";
                asset.RankDescriptions = new[]
                {
                    "Every 3rd kill during Overdrive extends it by 1s, up to +3s.",
                    "Every 2nd kill during Overdrive extends it by 1s, up to +5s.",
                    "Every 2nd kill during Overdrive extends it by 1s, up to +7s. Vendetta kills add an uncapped +2s.",
                };
                asset.PerKillExtension = new FP[] { 1, 1, 1 };
                asset.KillsPerExtension = new byte[] { 3, 2, 2 };
                asset.MaxExtension = new FP[] { 3, 5, 7 };
                asset.VendettaKillExtension = new FP[] { 0, 0, 2 };
            });

            IgnitionSkillAction ignition = CreateOrUpdate<IgnitionSkillAction>($"{OverdriveUpgradesFolderPath}/IgnitionSkillAction.asset", asset =>
            {
                asset.DisplayName = "Ignition";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "At max Rage, every weapon hit Burns.";
                asset.RankDescriptions = new[]
                {
                    "At max Rage, weapon hits guarantee Burn.",
                    "At max Rage, weapon hits Burn, and Burning kills leave a Burning Ground patch.",
                    "At max Rage, weapon hits Burn and Burning kills leave Burning Ground. Reaching max Rage bursts a Burn pulse.",
                };
                asset.BurnOnHitStacks = new byte[] { 1, 1, 1 };
                asset.HasBurningGround = new[] { false, true, true };
                asset.HasInferno = new[] { false, false, true };
                asset.InfernoRadius = new FP[] { 0, 0, 4 };
                asset.InfernoBurnDuration = new FP[] { 0, 0, 4 };
                asset.InfernoBurnIntensity = new FP[] { 0, 0, 5 };
                asset.BurningGroundPrototype = AssetDatabase.LoadAssetAtPath<EntityPrototype>(BurningGroundPrototypePath);
                asset.BurningGroundDuration = 4;
                asset.BurningGroundRadius = 2;
                asset.BurningGroundDamage = 4;
                asset.BurningGroundTickInterval = FP._0_50;

                if (asset.BurningGroundPrototype == null)
                {
                    LogHelper.Warn("MaxAscensionAssetGenerator", $"No EntityPrototype at {BurningGroundPrototypePath} - Ignition rank 2 won't spawn anything until this is assigned.");
                }
            });

            BloodDebtPassiveUpgradeData bloodDebt = CreateOrUpdate<BloodDebtPassiveUpgradeData>($"{VendettaUpgradesFolderPath}/BloodDebt.asset", asset =>
            {
                asset.DisplayName = "Blood Debt";
                asset.MaxRank = 3;
                asset.Description = "Vendetta marks last longer, feed your Rage, and heal you.";
                asset.RankDescriptions = new[]
                {
                    "Vendetta marks last 12s.",
                    "Vendetta marks last 12s. Shield damage also marks attackers, and each Vendetta kill grants +2 Rage.",
                    "Marks last 12s, Shield damage marks attackers, kills grant +2 Rage. Consuming a mark heals 60% of its damage.",
                };
                asset.MarkDuration = new FP[] { 12, 12, 12 };
                asset.RageOnVendettaKill = 2;
                asset.HealMultiplierAtMaxRank = FP.FromString("0.60");
                asset.MaxHealFractionPerKill = FP.FromString("0.15");
            });

            WildfirePassiveUpgradeData wildfire = CreateOrUpdate<WildfirePassiveUpgradeData>($"{FireMasteryUpgradesFolderPath}/Wildfire.asset", asset =>
            {
                asset.DisplayName = "Wildfire";
                asset.MaxRank = 3;
                asset.Description = "Killing a Burning enemy spreads the fire to nearby enemies.";
                asset.RankDescriptions = new[]
                {
                    "Killing a Burning enemy spreads Burn to 2 enemies within 4m.",
                    "Killing a Burning enemy spreads a stronger Burn to 5 enemies within 6m.",
                    "Killing a Burning enemy spreads 75% of its remaining Burn to 5 enemies within 6m.",
                };
                asset.Radius = new FP[] { 4, 6, 6 };
                asset.BurnDuration = new FP[] { 3, 4, 4 };
                asset.BurnIntensity = new[] { FP._0_10, FP.FromString("0.18"), FP.FromString("0.18") };
                asset.MaxTargets = new[] { 2, 5, 5 };
                asset.RetainedFractionAtMaxRank = FP.FromString("0.75");
            });

            FlashpointPassiveUpgradeData flashpoint = CreateOrUpdate<FlashpointPassiveUpgradeData>($"{FireMasteryUpgradesFolderPath}/Flashpoint.asset", asset =>
            {
                asset.DisplayName = "Flashpoint";
                asset.MaxRank = 3;
                asset.Description = "Burning enemies take extra crits, explode on crit, and can be executed.";
                asset.RankDescriptions = new[]
                {
                    "+10% Critical Chance against Burning enemies.",
                    "+10% Critical Chance against Burning enemies. Crits on them explode for 50% damage in 3m, up to 5 targets.",
                    "+10% Crit Chance vs Burning enemies, whose crits explode. Execute Burning enemies below 15% Health; Elites take +25%.",
                };
                asset.CriticalChanceBonusVsBurning = FP._0_10;
                asset.ExplosionRadius = 3;
                asset.ExplosionDamageCoefficient = FP._0_50;
                asset.ExplosionProcCooldown = 2;
                asset.ExplosionMaxTargets = 5;
                asset.NormalHealthThreshold = FP.FromString("0.15");
                asset.SpecialistHealthThreshold = FP.FromString("0.08");
                asset.EliteBossDamageThreshold = FP.FromString("0.15");
                asset.EliteBossDamageBonus = FP._0_25;
            });

            RunAndGunSkillAction runAndGun = CreateOrUpdate<RunAndGunSkillAction>($"{DashUpgradesFolderPath}/RunAndGunSkillAction.asset", asset =>
            {
                asset.DisplayName = "Run & Gun";
                asset.MaxRank = 3;
                asset.Description = "Dashing reloads and grants a burst of Fire Rate.";
                asset.RankDescriptions = new[]
                {
                    "Dashing restores 50% of your magazine and grants +20% Fire Rate for 2s.",
                    "Dashing fully reloads and grants +30% Fire Rate and +15% Weapon Damage for 2s.",
                    "Dashing fully reloads and grants +40% Fire Rate, +15% Weapon Damage and unlimited ammo for 2s.",
                };
                asset.AmmoRestoreFraction = new[] { FP._0_50, FP._1, FP._1 };
                asset.FireRateBonus = new[] { FP._0_20, FP.FromString("0.30"), FP.FromString("0.40") };
                asset.HasteDuration = 2;
                asset.WeaponDamageBonusDuration = 2;
                asset.WeaponDamageBonus = FP.FromString("0.15");
                asset.NoAmmoConsumptionDuration = 2;
            });

            VendettaStrikeSkillAction vendettaStrike = CreateOrUpdate<VendettaStrikeSkillAction>($"{DashUpgradesFolderPath}/VendettaStrikeSkillAction.asset", asset =>
            {
                asset.DisplayName = "Vendetta Strike";
                asset.MaxRank = 3;
                asset.Description = "Dashing through an enemy Burns it.";
                asset.RankDescriptions = new[]
                {
                    "Dashing through an enemy guarantees Burn.",
                    "Dashing through an enemy Burns it and marks it for Vendetta, even if it has never hit you.",
                    "Dashing through an enemy Burns and marks it, and cuts 2s off Overdrive's cooldown or extends it by 1s.",
                };
                asset.Radius = FP._1_50;
                asset.BurnDuration = 3;
                asset.BurnIntensity = FP._0_10;
                asset.CooldownReduction = 2;
                asset.OverdriveDurationBonus = 1;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireHeroSkill(new List<SkillActionData> { lastStand, fullThrottle, uncontrolledFury, ignition });

            WireCharacterData(passive,
                new List<PassiveUpgradeData> { bloodDebt, wildfire, flashpoint },
                new List<SkillActionData> { runAndGun, vendettaStrike });

            LogHelper.Log("MaxAscensionAssetGenerator", "Vendetta base passive + 9 Max Ascension lines authored and wired (4 Overdrive into " +
                      "MaxHeroSkill.Actions, 3 Passive + 2 Dash into MaxCharacterData) - every list fully replaced, not appended; every " +
                      "per-rank value is re-set explicitly on every run. MaxHeroSkill.asset's own dead embedded sub-objects were swept.");
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

        private static void WireCharacterData(VendettaPassiveData passive, List<PassiveUpgradeData> passiveUpgrades, List<SkillActionData> dashUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("MaxAscensionAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            characterData.Passive = new AssetRef<PassiveData>(passive.Guid);
            characterData.PassiveUpgrades = passiveUpgrades.Select(a => new AssetRef<PassiveUpgradeData>(a.Guid)).ToList();
            characterData.DashSkillUpgrades = dashUpgrades.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();

            EditorUtility.SetDirty(characterData);
            AssetDatabase.SaveAssets();
        }

        // Wires the 4 Overdrive Ascensions into MaxHeroSkill.Actions - CheckActions stays false
        // either way, since these execute via SkillSlot.Upgrades once picked, same mechanism Pixie's
        // ClusterBombSkillAction/Brute's MomentumSkillAction already use; Actions here is purely the
        // draft-eligibility source list LevelUpUtility.AddHeroSkillUpgradeCandidates reads
        // (Activated == false -> offerable). Also sweeps and removes every stray sub-object embedded
        // directly in the asset file that ISN'T the main BerserkSkillData asset - both the two
        // orphaned missing-script leftovers AND every live-class baseline action (MarkExplosiveDeath/
        // BurnOnHit/the old Burning Ground spawn/every deleted standalone Overdrive pick) - none of
        // them are referenced by the new Actions list above, so all of them are dead weight now. Same
        // pattern Pixie/Brute's own Hero Skill asset cleanup already established.
        private static void WireHeroSkill(List<SkillActionData> overdriveLines)
        {
            var mainAsset = AssetDatabase.LoadAssetAtPath<BerserkSkillData>(HeroSkillPath);

            if (mainAsset == null)
            {
                LogHelper.Error("MaxAscensionAssetGenerator", $"No BerserkSkillData asset at {HeroSkillPath} - Overdrive lines were created, but Actions was not wired.");
                return;
            }

            mainAsset.Actions = overdriveLines.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();
            EditorUtility.SetDirty(mainAsset);
            AssetDatabase.SaveAssets();

            var allObjects = AssetDatabase.LoadAllAssetsAtPath(HeroSkillPath);

            foreach (var obj in allObjects)
            {
                if (obj == null || obj == mainAsset)
                    continue;

                AssetDatabase.RemoveObjectFromAsset(obj);
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

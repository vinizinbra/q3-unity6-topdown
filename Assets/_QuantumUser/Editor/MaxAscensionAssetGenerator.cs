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
                asset.Description = "Your Rage stops being fragile - it survives between Overdrives, then survives being hit, then survives death itself.";
                asset.RankDescriptions = new[]
                {
                    "Rage now persists after Overdrive ends - your next Overdrive starts where the last one left off.",
                    "Rage persists between Overdrives, and taking damage during Overdrive now only costs half of it instead of all of it.",
                    "Rage persists and resists damage - and Too Angry to Die: lethal damage during Overdrive instead leaves you at 1 Health, spends your Rage, ends Overdrive, and grants 2s of invulnerability.",
                };
                asset.RageLossFraction = new[] { FP._1, FP._0_50, FP._0_50 };
                asset.CheatDeathImmunityDuration = 2;
            });

            FullThrottleSkillAction fullThrottle = CreateOrUpdate<FullThrottleSkillAction>($"{OverdriveUpgradesFolderPath}/FullThrottleSkillAction.asset", asset =>
            {
                asset.DisplayName = "Full Throttle";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "At max Rage during Overdrive, gain bonus Weapon Damage - eventually Reload Speed and instant reloads too.";
                asset.RankDescriptions = new[]
                {
                    "At max Rage: +20% Weapon Damage.",
                    "At max Rage: +30% Weapon Damage, +50% Reload Speed.",
                    "At max Rage: +40% Weapon Damage, instant reloads.",
                };
                asset.WeaponDamageBonus = new[] { FP.FromString("0.20"), FP.FromString("0.30"), FP.FromString("0.40") };
                asset.ReloadSpeedBonus = new[] { FP._0, FP._0_50, FP._0_50 };
            });

            UncontrolledFurySkillAction uncontrolledFury = CreateOrUpdate<UncontrolledFurySkillAction>($"{OverdriveUpgradesFolderPath}/UncontrolledFurySkillAction.asset", asset =>
            {
                asset.DisplayName = "Uncontrolled Fury";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Kills during Overdrive extend the activation - killing your Vendetta target extends it further still.";
                asset.RankDescriptions = new[]
                {
                    "Every 3rd kill during Overdrive extends it by 1s (up to +3s per activation).",
                    "Every 2nd kill during Overdrive extends it by 1s (up to +5s per activation).",
                    "Every 2nd kill during Overdrive extends it by 1s (up to +7s per activation) - killing your Vendetta target grants an additional, uncapped +2s.",
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
                asset.Description = "At max Rage during Overdrive, every hit guarantees Burn - eventually leaving a trail of fire and igniting the battlefield outright.";
                asset.RankDescriptions = new[]
                {
                    "At max Rage, weapon hits guarantee Burn.",
                    "At max Rage, weapon hits guarantee Burn - and Burning enemies you kill leave a Burning Ground patch behind.",
                    "At max Rage, weapon hits guarantee Burn and Burning kills leave Burning Ground - the first time you reach max Rage each activation, Inferno also detonates a radial Burn pulse around you.",
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
                asset.Description = "Vendetta marks last longer, feed your Rage, and eventually heal you when consumed.";
                asset.RankDescriptions = new[]
                {
                    "Vendetta marks last 12s.",
                    "Vendetta marks last 12s, Shield damage now also marks your attacker, and every Vendetta kill grants +2 Rage.",
                    "Vendetta marks last 12s, Shield damage counts, Vendetta kills grant +2 Rage, and consuming a mark heals you for 60% of the damage it dealt (capped at 15% of your max Health).",
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
                asset.Description = "Killing any Burning enemy spreads the fire to nearby enemies - eventually propagating the dying enemy's own live Burn instead of a flat amount.";
                asset.RankDescriptions = new[]
                {
                    "Killing a Burning enemy spreads Burn to 2 nearby enemies within 4m.",
                    "Killing a Burning enemy spreads a stronger Burn to 5 nearby enemies within 6m.",
                    "Killing a Burning enemy spreads Burn to 5 nearby enemies within 6m, propagating 75% of its own remaining Burn instead of a flat amount - a fire this strong keeps jumping.",
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
                asset.Description = "Burning enemies become far more dangerous to be near - bonus Critical Chance, crits that detonate, and eventually outright execution.";
                asset.RankDescriptions = new[]
                {
                    "Hot Target: +10% Critical Chance against Burning enemies.",
                    "Hot Target, plus Flashpoint: critical hits against Burning enemies detonate a fiery explosion (3m radius, 50% damage, capped at 5 targets).",
                    "Hot Target, Flashpoint, plus Cremation: Burning enemies below a Health threshold are executed outright (15% for Filler/Normal, 8% for Specialist/Heavy). Elite and Boss can't be executed - instead you deal +25% damage to them while they're Burning and below 15% Health.",
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
                asset.Description = "Dashing restores ammo and grants a brief Fire Rate window - eventually adding bonus Weapon Damage and a window of unlimited ammo.";
                asset.RankDescriptions = new[]
                {
                    "Dashing restores 50% of your magazine and grants +20% Fire Rate for 2s.",
                    "Dashing fully reloads your magazine, grants +30% Fire Rate for 2s, and +15% Weapon Damage for 2s.",
                    "Dashing fully reloads your magazine, grants +40% Fire Rate for 2s, +15% Weapon Damage for 2s, and 2s of unlimited ammo.",
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
                asset.Description = "Dashing through an enemy guarantees Burn - eventually also marking it for Vendetta and rewarding Overdrive.";
                asset.RankDescriptions = new[]
                {
                    "Dashing through an enemy guarantees Burn.",
                    "Also marks the enemy for Vendetta - refreshing the mark if it already had one, or creating a fresh one even if it's never hit you.",
                    "Also reduces your Hero Skill cooldown by 2s if Overdrive is dormant, or extends the current Overdrive by 1s if already active.",
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

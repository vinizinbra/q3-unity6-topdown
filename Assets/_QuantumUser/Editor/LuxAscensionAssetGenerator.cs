namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Lux's Scrap Collector base passive and her 9 Ascension lines (Weapon Systems/Overclock/
    // Fortification/Overload Core on the Sentry Hero Skill; Scavenger/Rapid Recycling/Field
    // Modifications as Passives; Emergency Repair/Relocation Protocol as Dash Ascensions), then wires
    // all of it into LuxCharacterData.asset and LuxSkill.asset.
    //
    // Replaces LuxScrapAssetGenerator.cs, which authored a now-deleted roster (Efficient Salvage,
    // Enhancement, Portable Cover, an unfinished Decoy Beacon) and only ever APPENDED to
    // DashSkillUpgrades - the exact append-vs-replace drift bug every other hero's own refactor
    // already had to fix. This one fully replaces every list it touches.
    //
    // Per-rank tuned values ARE explicitly set here on every run, even though every ranked ascension
    // class already carries a matching C# field-initializer default - that default only applies to a
    // BRAND NEW object (ScriptableObject.CreateInstance), not one that already existed before a
    // field's TYPE changed shape (a plain FP becoming FP[]). Explicitly setting every array here every
    // run is what makes CreateOrUpdate idempotent and correct for pre-existing assets too.
    public static class LuxAscensionAssetGenerator
    {
        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Skills/Lux/Lux_PassiveSkill";
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Lux/Lux_PassiveSkill/Lux_PassiveSkillUpgrades";
        private const string HeroSkillFolderPath = "Assets/_QuantumUser/Resources/Skills/Lux/Lux_HeroSkill";
        private const string HeroSkillUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Lux/Lux_HeroSkill/Lux_HeroSkillUpgrades";
        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Lux/Lux_DashSkillUpgrades";
        private const string SharedEffectsFolderPath = "Assets/_QuantumUser/Resources/Skills/Lux/Lux_SharedEffects";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/LuxCharacterData.asset";
        private const string HeroSkillPath = "Assets/_QuantumUser/Resources/Skills/Lux/Lux_HeroSkill/LuxSkill.asset";

        // Starting-point weapon data per barrel slot. These are EXISTING weapons reused as decisive
        // placeholders so the whole line is playable immediately - a real pass should author dedicated
        // sentry variants (a Minigun that fires in bursts, a Rocket with real AoE, a piercing Laser).
        // Every one of them is ordinary WeaponDataAsset, which is the point: "periodic burst",
        // "periodic AoE rocket" and "piercing laser" are authored weapon data, not turret code.
        private const string BaselineCannonPath = "Assets/_QuantumUser/Resources/Weapon/Sentry.asset";
        private const string MinigunPath = "Assets/_QuantumUser/Resources/Weapon/SMG.asset";
        private const string RocketPath = "Assets/_QuantumUser/Resources/Weapon/GrenadeLauncher.asset";
        private const string LaserPath = "Assets/_QuantumUser/Resources/Weapon/BeamGun.asset";

        [MenuItem("Tools/RiftRaiders/Lux/Generate Ascension Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(PassiveUpgradesFolderPath);
            CreateFolderRecursive(HeroSkillUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);
            CreateFolderRecursive(SharedEffectsFolderPath);

            // Not wired into RuntimeConfig here - RuntimeConfig's asset refs live on
            // QuantumMenuConfig.asset (a scene/menu config, not a plain AssetObject this generator can
            // safely locate) - see the log below for the manual step.
            CreateOrUpdate<ScrapConfig>($"{PassivesFolderPath}/ScrapConfig.asset", asset =>
            {
                asset.PickupRadius = 2;
                asset.OrbLifetime = 30;
                asset.MinSpawnOffset = FP._0_50;
                asset.MaxSpawnOffset = FP._1_50;
            });

            // PassiveData (unlike PassiveUpgradeData) derives AssetObject directly, not UpgradeData -
            // a hero's single base Passive is Inspector-assigned (CharacterData.Passive), never offered
            // as a level-up card, so it has no DisplayName/Description to set here.
            ScrapCollectorPassiveData passive = CreateOrUpdate<ScrapCollectorPassiveData>($"{PassivesFolderPath}/ScrapCollectorPassiveData.asset", asset =>
            {
                asset.DropChance = FP._0_25;
                asset.StacksRequired = 10;

                // Together with GrantFreeCast's own no-op-if-already-pending (max 1 stored Fabrication
                // Charge), this cap is what makes a Sentry -> kill -> Scrap -> Sentry runaway
                // structurally impossible.
                asset.MaxActiveSentries = 2;
            });

            // Fire Support's ally buff - the SAME generic AllyBuffEffectData class Zara's Support Beat
            // uses. Its Damage Reduction therefore lands in the one shared aura-DR slot, which is what
            // makes "multiple Sentries don't stack" and "Guardian + Fire Support don't compound" true
            // by construction rather than by a per-source check.
            var fireSupportBuff = CreateOrUpdate<AllyBuffEffectData>($"{SharedEffectsFolderPath}/LuxFireSupportBuff.asset", a =>
            {
                a.Duration = FP._2;
                a.FireRateBonus = FP.FromString("0.15");
                a.DamageReductionAmount = FP._0_10;
            });

            // --- 4 Sentry (Hero Skill) lines ---

            SentryWeaponSystemsSkillAction weaponSystems = CreateOrUpdate<SentryWeaponSystemsSkillAction>($"{HeroSkillUpgradesFolderPath}/SentryWeaponSystemsSkillAction.asset", asset =>
            {
                asset.DisplayName = "Weapon Systems";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Bolts additional weapon systems onto your Sentry, one per rank.";
                asset.RankDescriptions = new[]
                {
                    "Minigun: your Sentry gains a rapid secondary gun alongside its Cannon.",
                    "Rocket Pod: your Sentry fires its Cannon, a rapid Minigun, and periodic explosive rockets.",
                    "Full Arsenal: your Sentry fires Cannon, Minigun, Rockets and a piercing Laser all at once.",
                };
                asset.MinigunWeapon = LoadWeapon(MinigunPath);
                asset.MinigunOffset = new FPVector3(FP._0_50, FP._0_50, 0);
                asset.RocketWeapon = LoadWeapon(RocketPath);
                asset.RocketOffset = new FPVector3(-FP._0_50, FP._0_50, 0);
                asset.LaserWeapon = LoadWeapon(LaserPath);
                asset.LaserOffset = new FPVector3(0, FP._1, 0);
            });

            SentryOverclockSkillAction overclock = CreateOrUpdate<SentryOverclockSkillAction>($"{HeroSkillUpgradesFolderPath}/SentryOverclockSkillAction.asset", asset =>
            {
                asset.DisplayName = "Overclock";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Your Sentry fires faster and lives longer - and goes into overdrive as it burns out.";
                asset.RankDescriptions = new[]
                {
                    "Sentry Fire Rate +25%.",
                    "Sentry Fire Rate +40%, and your Sentry lasts 2s longer.",
                    "Redline: Sentry Fire Rate +50%, and during the last 3s of its life it gains a further +100% Fire Rate.",
                };
                asset.FireRateMultiplier = new[] { FP._1_25, FP.FromString("1.40"), FP._1_50 };
                asset.DurationBonus = new FP[] { 0, 2, 2 };
                asset.RedlineThreshold = new FP[] { 0, 0, 3 };
                asset.RedlineFireRateMultiplier = FP._2;
            });

            SentryFortificationSkillAction fortification = CreateOrUpdate<SentryFortificationSkillAction>($"{HeroSkillUpgradesFolderPath}/SentryFortificationSkillAction.asset", asset =>
            {
                asset.DisplayName = "Fortification";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Your Sentry becomes a position worth holding - longer reach, then covering fire.";
                asset.RankDescriptions = new[]
                {
                    "Extended Range: Sentry attack range +2.",
                    "Covering Fire: Sentry attack range +2, and allies standing near your Sentry are shielded from one hit. Once per ally per Sentry.",
                    "Fire Support: Sentry attack range +2. Allies near your Sentry are shielded from one hit (once per ally per Sentry) and gain +15% Fire Rate and 10% Damage Reduction.",
                };
                asset.RangeBonus = new FP[] { 2, 2, 2 };

                // Replaced Shield Battery's flat Shield-per-second. Short duration because the aura
                // re-applies it every tick an ally stands in range - this is how long it survives after
                // they step OUT, not how long they hold it. One per ally per turret, capped by the
                // sentry's own AreaAllyBudget, so another denial means another deployment.
                asset.GuardDuration = new FP[] { 0, 1, 1 };
                asset.GuardsPerAlly = new byte[] { 0, 1, 1 };
                asset.AuraRangeRatio = FP._0_50;
                asset.FireSupportEffect = new[]
                {
                    default(AssetRef<HitEffectData>),
                    default(AssetRef<HitEffectData>),
                    new AssetRef<HitEffectData>(fireSupportBuff.Guid),
                };
            });

            SentryOverloadCoreSkillAction overloadCore = CreateOrUpdate<SentryOverloadCoreSkillAction>($"{HeroSkillUpgradesFolderPath}/SentryOverloadCoreSkillAction.asset", asset =>
            {
                asset.DisplayName = "Overload Core";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Losing a Sentry stops being a loss - it detonates.";
                asset.RankDescriptions = new[]
                {
                    "When your Sentry expires or is destroyed, it explodes for 100% of Sentry Skill Damage.",
                    "When your Sentry expires or is destroyed, it explodes for 175% Sentry Skill Damage over 30% more ground, knocking enemies back hard.",
                    "Critical Meltdown: when your Sentry expires or is destroyed, it explodes for 250% Sentry Skill Damage, leaving enemies Exposed and taking +20% damage for 3s.",
                };
                asset.DamagePercent = new[] { FP._1, FP.FromString("1.75"), FP.FromString("2.50") };
                asset.BaseRadius = FP._4;
                asset.RadiusMultiplier = new[] { FP._1, FP.FromString("1.30"), FP.FromString("1.30") };
                asset.KnockbackForce = new FP[] { 0, 12, 12 };
                asset.ExposedDamageTakenBonus = new[] { FP._0, FP._0, FP._0_20 };
                asset.ExposedDuration = FP._3;
            });

            // --- 3 Scrap (Passive) lines ---

            ScavengerPassiveUpgradeData scavenger = CreateOrUpdate<ScavengerPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Scavenger.asset", asset =>
            {
                asset.DisplayName = "Scavenger";
                asset.MaxRank = 3;
                asset.Description = "More enemies drop Scrap, and more often.";
                asset.RankDescriptions = new[]
                {
                    "Filler enemies can drop Scrap (10% chance).",
                    "Filler enemies can drop Scrap (10% chance), and all Scrap drop chances increase by about 25%.",
                    "Jackpot: Filler enemies can drop Scrap and every drop chance is raised. Specialist, Heavy and Elite always drop at least 1 Scrap; Bosses drop 3.",
                };
                asset.DropChance = new[] { FP._0_25, FP.FromString("0.31"), FP.FromString("0.31") };
                asset.FillerDropChance = new[] { FP._0_10, FP.FromString("0.13"), FP.FromString("0.13") };
                asset.GuaranteedDropTier = EnemyTier.Specialist;
                asset.GuaranteedDropCount = new byte[] { 0, 0, 1 };
                asset.BossGuaranteedScrap = 3;
            });

            RapidRecyclingPassiveUpgradeData rapidRecycling = CreateOrUpdate<RapidRecyclingPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/RapidRecycling.asset", asset =>
            {
                asset.DisplayName = "Rapid Recycling";
                asset.MaxRank = 3;
                asset.Description = "Collected Scrap shortens your Sentry's cooldown.";
                asset.RankDescriptions = new[]
                {
                    "Each Scrap collected removes 0.5s from your Sentry's remaining cooldown.",
                    "Each Scrap collected removes 1s from your Sentry's remaining cooldown.",
                    "Instant Assembly: each Scrap removes 1s from your Sentry's cooldown, and earning a Fabrication Charge removes a further 3s.",
                };
                asset.CooldownReductionPerPickup = new[] { FP._0_50, FP._1, FP._1 };
                asset.CooldownReductionOnCharge = new FP[] { 0, 0, 3 };
            });

            FieldModificationsPassiveUpgradeData fieldModifications = CreateOrUpdate<FieldModificationsPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/FieldModifications.asset", asset =>
            {
                asset.DisplayName = "Field Modifications";
                asset.MaxRank = 3;
                asset.Description = "Scrap collected while a Sentry is deployed upgrades that Sentry, for as long as it lives.";
                asset.RankDescriptions = new[]
                {
                    "Scrap collected while your Sentry is active grants it +4% Damage, up to 5 stacks.",
                    "Scrap collected while your Sentry is active grants it +4% Damage and +3% Fire Rate, up to 5 stacks.",
                    "MK II: Scrap collected while your Sentry is active grants +4% Damage and +3% Fire Rate, up to 5 stacks. At 5 stacks its Cannon becomes a Twin Cannon.",
                };
                asset.DamagePerStack = new[] { FP.FromString("0.04"), FP.FromString("0.04"), FP.FromString("0.04") };
                asset.FireRatePerStack = new[] { FP._0, FP.FromString("0.03"), FP.FromString("0.03") };
                asset.MaxStacks = new byte[] { 5, 5, 5 };

                // Placeholder - point this at a real Twin Cannon WeaponDataAsset (2 projectiles at
                // ~70% damage each) once one is authored. Reusing the baseline Cannon here means MK II
                // currently changes nothing but the visual/event until that exists, which is a safe
                // no-op rather than a broken reference.
                asset.MkIIWeapon = LoadWeapon(BaselineCannonPath);
            });

            // --- 2 Dash lines ---

            EmergencyRepairSkillAction emergencyRepair = CreateOrUpdate<EmergencyRepairSkillAction>($"{DashUpgradesFolderPath}/EmergencyRepairSkillAction.asset", asset =>
            {
                asset.DisplayName = "Emergency Repair";
                asset.MaxRank = 3;
                asset.Phase = SkillActionPhase.Begin | SkillActionPhase.End;
                asset.Description = "Ending a dash next to your Sentry services it.";
                asset.RankDescriptions = new[]
                {
                    "Ending a dash within 6m of your Sentry repairs 30% of its Max Health.",
                    "Ending a dash within 6m of your Sentry repairs 30% of its Max Health and extends its lifetime by 2s (up to 4s per Sentry).",
                    "Emergency Overclock: ending a dash within 6m of your Sentry repairs 30% of its Max Health, extends its lifetime by 2s, and grants it +50% Fire Rate for 2s.",
                };
                asset.Range = FP._6;
                asset.RepairFraction = new[] { FP.FromString("0.30"), FP.FromString("0.30"), FP.FromString("0.30") };
                asset.LifetimeExtension = new FP[] { 0, 2, 2 };

                // Per-SENTRY, not per-Lux - each new machine gets a fresh allowance, but no single one
                // can be kept alive indefinitely by a dash-cooldown build.
                asset.MaxLifetimeExtensionPerSentry = FP._4;
                asset.TempFireRateMultiplier = new[] { FP._1, FP._1, FP._1_50 };
                asset.TempFireRateDuration = FP._2;
            });

            RelocationProtocolSkillAction relocationProtocol = CreateOrUpdate<RelocationProtocolSkillAction>($"{DashUpgradesFolderPath}/RelocationProtocolSkillAction.asset", asset =>
            {
                asset.DisplayName = "Relocation Protocol";
                asset.MaxRank = 3;
                asset.Phase = SkillActionPhase.Begin | SkillActionPhase.End;
                asset.Description = "Dash while standing at your Sentry and it comes with you - fully intact.";
                asset.RankDescriptions = new[]
                {
                    "Reposition: dashing from within 4m of your Sentry moves it to your dash destination, keeping its Health, lifetime, upgrades and modifications.",
                    "Rapid Setup: dashing from within 4m of your Sentry moves it to your dash destination, then grants it +25% Fire Rate for 2s and 1s of extra lifetime.",
                    "Hot Drop: dashing from within 4m of your Sentry moves it to your dash destination, where it immediately fires a volley and a knockback pulse at everything nearby.",
                };
                asset.PickupRange = FP._4;
                asset.TempFireRateMultiplier = new[] { FP._1, FP._1_25, FP._1_25 };
                asset.TempFireRateDuration = FP._2;
                asset.LifetimeExtension = new FP[] { 0, 1, 1 };

                // Deliberately the SAME per-sentry ceiling Emergency Repair authors - the two lines
                // draw on one shared allowance, so holding both can't double it.
                asset.MaxLifetimeExtensionPerSentry = FP._4;
                asset.HotDropDamagePercent = new[] { FP._0, FP._0, FP.FromString("0.80") };
                asset.HotDropRadius = FP._4;
                asset.HotDropKnockbackForce = 8;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            ConfigureBaselineSentry();
            WireHeroSkill(new List<SkillActionData> { weaponSystems, overclock, fortification, overloadCore });

            WireCharacterData(passive,
                new List<PassiveUpgradeData> { scavenger, rapidRecycling, fieldModifications },
                new List<SkillActionData> { emergencyRepair, relocationProtocol });

            LogHelper.Log("LuxAscensionAssetGenerator", "Scrap Collector passive + 9 Lux Ascension lines authored and wired (4 Sentry lines into " +
                      "LuxSkill.Actions, 3 Passive + 2 Dash into LuxCharacterData) - every list fully replaced, not appended. " +
                      "Still needed by hand: (1) assign ScrapConfig.asset and a ScrapOrb EntityPrototype to RuntimeConfig's " +
                      "ScrapConfig/ScrapOrbPrototype fields (QuantumMenuConfig.asset); (2) author dedicated sentry weapon data - " +
                      "Minigun/Rocket Pod/Laser currently reuse SMG/GrenadeLauncher/BeamGun as decisive placeholders, and MK II's " +
                      "Twin Cannon reuses the baseline Cannon (so MK II is a visual no-op until a real 2-projectile weapon exists); " +
                      "(3) delete the stale EfficientSalvage.asset/Enhacement.asset/PortableCoverSkillAction.asset files - those " +
                      "lines no longer exist.");
        }

        private static AssetRef<WeaponDataAsset> LoadWeapon(string path)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDataAsset>(path);

            if (weapon == null)
            {
                LogHelper.Warn("LuxAscensionAssetGenerator", $"No WeaponDataAsset at {path} - that Sentry barrel slot will be left unarmed until it's assigned by hand.");
                return default;
            }

            return new AssetRef<WeaponDataAsset>(weapon.Guid);
        }

        // The BASELINE sentry - one Cannon, short range, short life, nothing else. Every removed
        // baseline capability (Rocket/Minigun/Laser/Shield Battery/Fire Support/Overclock/Extended
        // Range/Overload Core) is an Ascension now; this is the "simple machine" the whole arc starts
        // from. Reconfigured on every run so a pre-existing asset from before the refactor is repaired
        // rather than left carrying old values.
        private static void ConfigureBaselineSentry()
        {
            SpawnSentrySkillAction spawn = null;

            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(HeroSkillPath))
            {
                if (obj is SpawnSentrySkillAction found)
                {
                    spawn = found;
                    break;
                }
            }

            if (spawn == null)
            {
                LogHelper.Error("LuxAscensionAssetGenerator", $"No embedded SpawnSentrySkillAction found in {HeroSkillPath} - the baseline sentry was NOT reconfigured (it may still carry pre-refactor values).");
                return;
            }

            spawn.Duration = 10;
            spawn.Range = 3;
            spawn.SkillDamage = 20;
            spawn.BaselineWeapon = LoadWeapon(BaselineCannonPath);
            spawn.BaselineWeaponOffset = new FPVector3(0, FP._0_50, 0);

            EditorUtility.SetDirty(spawn);

            var heroSkill = AssetDatabase.LoadAssetAtPath<SkillData>(HeroSkillPath);

            if (heroSkill != null)
            {
                // 20s baseline cooldown per the brief - a Fabrication Charge is what bypasses it.
                heroSkill.Cooldown = 20;
                EditorUtility.SetDirty(heroSkill);
            }

            AssetDatabase.SaveAssets();
        }

        private static void WireHeroSkill(List<SkillActionData> actions)
        {
            var heroSkill = AssetDatabase.LoadAssetAtPath<SkillData>(HeroSkillPath);

            if (heroSkill == null)
            {
                LogHelper.Error("LuxAscensionAssetGenerator", $"No SkillData asset at {HeroSkillPath} - the 4 Sentry Ascensions were created, but LuxSkill.Actions was not wired.");
                return;
            }

            // The embedded SpawnSentrySkillAction is the skill's own BASELINE action and must stay in
            // the list - it's what actually deploys the sentry. Preserved by GUID, then the 4
            // Ascensions replace everything else, so a stale pre-refactor entry can't survive.
            var preserved = heroSkill.Actions
                .Where(existing => existing.IsValid && AssetDatabase.LoadAllAssetsAtPath(HeroSkillPath)
                    .Any(obj => obj is SpawnSentrySkillAction spawn && spawn.Guid.Value == existing.Id.Value))
                .ToList();

            heroSkill.Actions = preserved
                .Concat(actions.Select(a => new AssetRef<SkillActionData>(a.Guid)))
                .ToList();

            if (preserved.Count == 0)
            {
                LogHelper.Warn("LuxAscensionAssetGenerator", "LuxSkill.Actions had no SpawnSentrySkillAction entry to preserve - the Hero Skill will deploy nothing until one is re-added by hand.");
            }

            EditorUtility.SetDirty(heroSkill);
            AssetDatabase.SaveAssets();
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

        // Fully REPLACES both lists rather than appending - the old generator only appended to
        // DashSkillUpgrades, which is exactly how a deleted line's stale GUID survives every
        // regeneration.
        private static void WireCharacterData(ScrapCollectorPassiveData passive, List<PassiveUpgradeData> passiveUpgrades, List<SkillActionData> dashUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("LuxAscensionAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            characterData.Passive = new AssetRef<PassiveData>(passive.Guid);
            characterData.PassiveUpgrades = passiveUpgrades.Select(a => new AssetRef<PassiveUpgradeData>(a.Guid)).ToList();
            characterData.DashSkillUpgrades = dashUpgrades.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();

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

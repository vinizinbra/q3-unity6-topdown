namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Kai's Void Field base passive and his 9 Ascension lines (Singularity/Compression/
    // Vortex Collapse/Void Shards on the Vortex Hero Skill; Event Horizon/Undertow/First Strike as
    // Passives; Mirror Step/Phantom Strike as Dash Ascensions - Warp Wake was removed entirely in the
    // balance pass - see docs/kai-ascensions.md),
    // then wires all of it into KaiCharacterData.asset and KaiVortexSkill.asset. Replaces the old
    // KaiVoidFieldAssetGenerator.cs/KaiVoidwalkerMasteryAssetGenerator.cs pair - same "one generator
    // fully replaces every list it touches end to end" fix Brute/Max/Pixie's own refactors already
    // applied for the identical append-vs-replace drift bug.
    //
    // Singularity/Compression/Vortex Collapse/Void Shards are SkillActionData living on
    // KaiVortexSkill.Actions (Activated = false), NOT PassiveUpgradeData - same "Hero Skill Ascension"
    // shape every other hero's own refactor already established, so they correctly show as "Hero
    // Skill" (not a generic "Passive Upgrade") in both the level-up UI and the debug menu.
    //
    // Per-rank tuned values ARE explicitly set here on every run, even though every ranked ascension
    // class already carries a matching C# field-initializer default - that default only applies to a
    // BRAND NEW object (ScriptableObject.CreateInstance), not one that already existed before a
    // field's TYPE changed shape (a plain FP becoming FP[]). Explicitly setting every array here every
    // run is what makes CreateOrUpdate idempotent and correct for pre-existing assets, not just newly
    // created ones.
    public static class KaiAscensionAssetGenerator
    {
        private const string PassiveFolderPath = "Assets/_QuantumUser/Resources/Skills/Kai/Kai_PassiveUpgrades";
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Kai/Kai_PassiveUpgrades/Kai_PassiveSkillUpgrades";
        private const string HeroSkillUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Kai/Kai_HeroSkill/Kai_HeroSkillUpgrades";
        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Kai/Kai_DashSkillUpgrades";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/KaiCharacterData.asset";
        private const string VortexSkillPath = "Assets/_QuantumUser/Resources/Skills/Kai/Kai_HeroSkill/KaiVortexSkill.asset";
        private const string GenericDamageX1Path = "Assets/_QuantumUser/Resources/Enemy/_GenericActions/GenericDamageX1.asset";

        [MenuItem("Tools/RiftRaiders/Kai/Generate Ascension Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassiveFolderPath);
            CreateFolderRecursive(PassiveUpgradesFolderPath);
            CreateFolderRecursive(HeroSkillUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);

            VoidFieldPassiveData passive = CreateOrUpdate<VoidFieldPassiveData>($"{PassiveFolderPath}/VoidFieldPassiveData.asset", asset =>
            {
                asset.Radius = FP.FromString("2.5");
                asset.SpeedMultiplier = FP.FromString("0.60");
            });

            // Shared generic "deal context.Damage to context.Target" effect (DamageMultiplier=1),
            // already used elsewhere in this codebase - AreaDamage's own pulse only actually deals
            // damage through a valid entry in its Effects array (HitEffectUtility.ApplyToTarget skips
            // any AssetRef<HitEffectData> that IsValid == false), so Compression/Warp Wake's own
            // AreaDamage grants below need this wired in, not just a Damage number.
            var genericDamageX1 = AssetDatabase.LoadAssetAtPath<DamageEffectData>(GenericDamageX1Path);
            AssetRef<HitEffectData> genericDamageEffect = genericDamageX1 != null
                ? new AssetRef<HitEffectData>(genericDamageX1.Guid)
                : default;

            if (genericDamageX1 == null)
            {
                LogHelper.Error("KaiAscensionAssetGenerator", $"No DamageEffectData asset at {GenericDamageX1Path} - Compression/Warp Wake's AreaDamage pulses will deal 0 damage until DamageEffect is assigned by hand.");
            }

            SingularitySkillAction singularity = CreateOrUpdate<SingularitySkillAction>($"{HeroSkillUpgradesFolderPath}/SingularitySkillAction.asset", asset =>
            {
                asset.DisplayName = "Singularity";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Vortex grows larger, pulls harder, and interrupts enemies' telegraphed attacks.";
                asset.RankDescriptions = new[]
                {
                    "Vortex grows 30% larger and interrupts anticipated attacks from enemies up to Normal tier.",
                    "Vortex grows larger and pulls harder, interrupting anticipated attacks from enemies up to Heavy tier.",
                    "Vortex becomes a Singularity, periodically crushing enemies toward its core and interrupting anticipated attacks from enemies up to Elite tier.",
                };
                asset.PullRadiusMultiplier = new[] { FP.FromString("1.30"), FP.FromString("1.50"), FP.FromString("1.75") };
                asset.PullForceMultiplier = new[] { FP._1, FP.FromString("1.30"), FP._1_50 };
                asset.MaxEligibleTierIndex = new[] { (byte)EnemyTier.Normal, (byte)EnemyTier.Heavy, (byte)EnemyTier.Elite };
                asset.HasGravityPulse = new[] { false, false, true };
                asset.GravityPulseForceMultiplier = 3;

                // ~3 pulses/second. Safe at this cadence because re-interrupt pacing is now the generic
                // per-tier hard-CC immunity window (EnemyTierResistanceConfig.InterruptImmunityDuration),
                // not a per-vortex tracker - so a protected enemy can't be perma-locked by the pulse rate.
                asset.GravityPulseInterval = FP._1 / 3;
            });

            CompressionSkillAction compression = CreateOrUpdate<CompressionSkillAction>($"{HeroSkillUpgradesFolderPath}/CompressionSkillAction.asset", asset =>
            {
                asset.DisplayName = "Compression";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Vortex damages trapped enemies, scaling up with how many are caught at once.";
                asset.RankDescriptions = new[]
                {
                    "Vortex damages trapped enemies every 0.5s.",
                    "Vortex damages trapped enemies every 0.5s, dealing 8% more per additional enemy trapped, up to +56%.",
                    "Vortex damages trapped enemies every 0.5s, scaling with how many are trapped. Every third pulse creates a powerful implosion at its core.",
                };
                asset.PulseDamagePercent = FP._0_20;
                asset.PulseTickInterval = FP._0_50;
                asset.DamageEffect = genericDamageEffect;
                asset.CrowdPerEnemyBonus = new[] { FP._0, FP.FromString("0.08"), FP.FromString("0.08") };
                asset.CrowdMaxCount = new byte[] { 0, 8, 8 };
                asset.ImplosionDamagePercent = new[] { FP._0, FP._0, FP.FromString("0.75") };
                asset.ImplosionEveryNthPulse = new byte[] { 0, 0, 3 };
                asset.ImplosionRadiusFraction = FP._0_50;
            });

            VortexCollapseSkillAction vortexCollapse = CreateOrUpdate<VortexCollapseSkillAction>($"{HeroSkillUpgradesFolderPath}/VortexCollapseSkillAction.asset", asset =>
            {
                asset.DisplayName = "Vortex Collapse";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "When Vortex ends, it collapses in a heavy blast around its center.";
                asset.RankDescriptions = new[]
                {
                    "When Vortex ends, it collapses and deals heavy damage around its center.",
                    "When Vortex ends, it collapses for 200% Skill Damage in a larger blast.",
                    "Before collapsing, Vortex violently pulls enemies into its core, then detonates for massive damage.",
                };
                asset.DamagePercent = new[] { FP._1_50, FP._2, FP.FromString("2.50") };
                asset.RadiusMultiplier = new[] { FP._1, FP.FromString("1.25"), FP._1_50 };
                asset.PreExplosionPullForce = new FP[] { FP._0, FP._0, 12 };
            });

            VoidShardsSkillAction voidShards = CreateOrUpdate<VoidShardsSkillAction>($"{HeroSkillUpgradesFolderPath}/VoidShardsSkillAction.asset", asset =>
            {
                asset.DisplayName = "Void Shards";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Vortex periodically fires homing Void Shards at nearby enemies.";
                asset.RankDescriptions = new[]
                {
                    "Vortex periodically fires a homing Void Shard at a nearby enemy.",
                    "Vortex fires homing Void Shards more often, travelling farther, hitting harder and piercing 2 enemies.",
                    "Vortex launches two powerful Void Shards with every volley, each piercing through 3 enemies.",
                };
                asset.DamagePercent = new[] { FP.FromString("0.30"), FP.FromString("0.40"), FP.FromString("0.45") };
                asset.TickInterval = new[] { FP._1, FP.FromString("0.75"), FP.FromString("0.75") };
                asset.SearchRadiusMultiplier = new[] { FP._3, FP._5, FP._5 };
                asset.ShardCount = new byte[] { 1, 1, 2 };
                asset.PierceCount = new byte[] { 1, 2, 3 };
            });

            EventHorizonPassiveUpgradeData eventHorizon = CreateOrUpdate<EventHorizonPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/EventHorizon.asset", asset =>
            {
                asset.DisplayName = "Event Horizon";
                asset.MaxRank = 3;
                asset.Description = "Grows Kai's Void Field, slowing enemy projectiles inside it and eventually the enemies themselves.";
                asset.RankDescriptions = new[]
                {
                    "Void Field reaches 4m and slows enemy projectiles inside it by 35%.",
                    "Void Field reaches 5m and slows enemy projectiles by 50%.",
                    "Void Field slows enemy projectiles by 60%, and enemies inside it attack 20% slower.",
                };
                // Absolute per-rank totals, not bonuses. Deliberately far milder than the pre-rebalance
                // values (which reached -80% projectile speed / -40% attack speed and suppressed enemy
                // ranged pressure almost entirely).
                asset.Radius = new FP[] { 4, 5, 5 };
                asset.ProjectileSpeedMultiplier = new[] { FP.FromString("0.65"), FP._0_50, FP.FromString("0.40") };
                asset.EnemyTimeDilationMultiplier = new[] { FP._0, FP._0, FP.FromString("0.80") };
            });

            UndertowPassiveUpgradeData undertow = CreateOrUpdate<UndertowPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Undertow.asset", asset =>
            {
                asset.DisplayName = "Undertow";
                asset.MaxRank = 3;
                asset.Description = "Weapon hits pull enemies together, eventually Binding them for bonus damage.";
                asset.RankDescriptions = new[]
                {
                    "Weapon hits pull enemies toward their nearest ally.",
                    "Weapon hits pull enemies toward their nearest ally with much greater force.",
                    "Weapon hits pull enemies toward their nearest ally, Binding them for 2s and making them take 20% more damage from you.",
                };
                asset.PullForce = new[] { FP._3, FP._6, FP._6 };
                asset.PullDuration = new[] { FP.FromString("0.2"), FP.FromString("0.2"), FP.FromString("0.2") };
                asset.HeavyTierMultiplier = new[] { FP._1, FP._1_50, FP._1_50 };
                asset.BoundDuration = new[] { FP._0, FP._0, FP._2 };
                asset.BoundDamageBonus = new[] { FP._0, FP._0, FP.FromString("0.20") };
            });

            FirstStrikePassiveUpgradeData firstStrike = CreateOrUpdate<FirstStrikePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/FirstStrike.asset", asset =>
            {
                asset.DisplayName = "First Strike";
                asset.MaxRank = 3;
                asset.Description = "Your first hit against an enemy deals bonus damage.";
                asset.RankDescriptions = new[]
                {
                    "Your first hit against an enemy deals 40% bonus damage.",
                    "Your first hit against an enemy deals 70% bonus damage.",
                    "Your first hit against an enemy deals 100% bonus damage. Killing that target empowers your next first hit by a further 25%.",
                };
                asset.DamageMultiplierBonus = new[] { FP.FromString("0.40"), FP.FromString("0.70"), FP._1 };
                asset.KillEmpowerBonus = new[] { FP._0, FP._0, FP._0_25 };
            });

            MirrorStepSkillAction mirrorStep = CreateOrUpdate<MirrorStepSkillAction>($"{DashUpgradesFolderPath}/MirrorStepSkillAction.asset", asset =>
            {
                asset.DisplayName = "Mirror Step";
                asset.MaxRank = 3;
                asset.Description = "Dashing reflects nearby enemy projectiles back toward their attackers.";
                asset.RankDescriptions = new[]
                {
                    "Dashing reflects nearby enemy projectiles back toward their attackers.",
                    "Dashing reflects enemy projectiles from farther away and increases their reflected damage.",
                    "Dashing reflects nearby enemy projectiles back at their attackers. Each reflect cuts 0.5s off Vortex's cooldown, up to 2s per dash.",
                };
                asset.Radius = new[] { FP._3, FP.FromString("4.50"), FP.FromString("4.50") };
                asset.ReflectedDamageMultiplier = new[] { FP._1, FP._1_50, FP._1_50 };
                asset.CooldownReductionPerReflect = new[] { FP._0, FP._0, FP._0_50 };
                asset.MaxCooldownReductionPerDash = FP._2;
            });

            PhantomStrikeSkillAction phantomStrike = CreateOrUpdate<PhantomStrikeSkillAction>($"{DashUpgradesFolderPath}/PhantomStrikeSkillAction.asset", asset =>
            {
                asset.DisplayName = "Phantom Strike";
                asset.MaxRank = 3;
                asset.Description = "After Dashing, your next weapon hit deals bonus damage and pierces extra enemies.";
                asset.RankDescriptions = new[]
                {
                    "After Dashing, your next weapon hit deals 50% bonus damage and pierces one additional enemy.",
                    "After Dashing, your next weapon hit deals 75% bonus damage and pierces two additional enemies.",
                    "After Dashing, your next weapon hit deals double damage and gains massive Pierce.",
                };
                asset.DamageMultiplierBonus = new[] { FP.FromString("0.50"), FP.FromString("0.75"), FP._1 };
                asset.PierceBonus = new[] { 1, 2, 99 };
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive,
                new List<PassiveUpgradeData> { eventHorizon, undertow, firstStrike },
                new List<SkillActionData> { mirrorStep, phantomStrike });

            WireVortexActions(new List<SkillActionData> { singularity, compression, vortexCollapse, voidShards });

            LogHelper.Log("KaiAscensionAssetGenerator", "Void Field passive + 9 Ascension lines authored and wired (3 Passive Upgrades " +
                      "into KaiCharacterData.PassiveUpgrades, Mirror Step/Phantom Strike/Warp Wake into KaiCharacterData.DashSkillUpgrades, " +
                      "Singularity/Compression/Vortex Collapse/Void Shards into KaiVortexSkill.Actions as Hero Skill Ascensions - every list " +
                      "fully replaced, not appended; every per-rank value is re-set explicitly on every run).");
        }

        // Resolves the existing KaiVortexEntityPrototype (Kai's own Hero Skill vortex prefab) as Warp
        // Wake's default spawn prototype - found by GUID via the live KaiVortexSkill asset's own
        // embedded SpawnVortexEffectData rather than a hardcoded path, since it's an in-file
        // sub-object, not its own .asset. Returns default (unassigned) if not found - Warp Wake simply
        // won't spawn anything until a Prototype is assigned by hand in that case, same as any other
        // unauthored AssetRef in this codebase.
        private static AssetRef<EntityPrototype> ResolveKaiVortexPrototype()
        {
            var subObjects = AssetDatabase.LoadAllAssetsAtPath(VortexSkillPath);

            foreach (var obj in subObjects)
            {
                if (obj is SpawnVortexEffectData spawnVortex)
                {
                    return spawnVortex.Prototype;
                }
            }

            return default;
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

        private static void WireCharacterData(VoidFieldPassiveData passive, List<PassiveUpgradeData> passiveUpgrades, List<SkillActionData> dashUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("KaiAscensionAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            characterData.Passive = new AssetRef<PassiveData>(passive.Guid);
            characterData.PassiveUpgrades = passiveUpgrades.Select(a => new AssetRef<PassiveUpgradeData>(a.Guid)).ToList();
            characterData.DashSkillUpgrades = dashUpgrades.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();

            EditorUtility.SetDirty(characterData);
            AssetDatabase.SaveAssets();
        }

        // Wires the 4 Hero Skill Ascensions into KaiVortexSkill.Actions - CheckActions stays false
        // either way (same "the CheckActions bug" reasoning docs/brute-ascensions.md already
        // documents for Juggernaut), since these execute via SkillSlot.Upgrades once picked. Also
        // sweeps and removes any stray sub-object embedded directly in the asset file that ISN'T the
        // main KaiVortexSkill asset (or its own embedded ProjectileData/DirectHitData/
        // SpawnVortexEffectData, which this generator doesn't touch) - a safety net against the 6
        // dead pre-refactor sub-actions (Damage Pulse/Vortex Collapse/Random Blast/Bigger Vortex/
        // Homing Shard/Crowd Damage) this asset used to carry embedded, before this refactor moved
        // their live equivalents out to their own standalone .asset files.
        private static void WireVortexActions(List<SkillActionData> actions)
        {
            var mainAsset = AssetDatabase.LoadAssetAtPath<ProjectileSkillData>(VortexSkillPath);

            if (mainAsset == null)
            {
                LogHelper.Error("KaiAscensionAssetGenerator", $"No ProjectileSkillData asset at {VortexSkillPath} - Singularity/Compression/Vortex Collapse/Void Shards were created, but Actions was not wired.");
                return;
            }

            mainAsset.Actions = actions.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();
            EditorUtility.SetDirty(mainAsset);
            AssetDatabase.SaveAssets();

            var allObjects = AssetDatabase.LoadAllAssetsAtPath(VortexSkillPath);

            foreach (var obj in allObjects)
            {
                if (obj == null || obj == mainAsset)
                    continue;

                // Kai's own thrown-projectile chain lives embedded in this same file (ProjectileData/
                // DirectHitData/SpawnVortexEffectData) - not part of the dead-actions roster, must
                // survive the sweep.
                if (obj is ProjectileDataAsset || obj is DirectHitData || obj is SpawnVortexEffectData)
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

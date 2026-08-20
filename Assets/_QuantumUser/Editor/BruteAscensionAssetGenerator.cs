namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Brute's Protector base passive and his 9 Ascension lines (Momentum/Bone Breaker/
    // Aftershock/Concussive Impact/Iron Presence/Guardian/Groundbreaker/Iron Shoulder/Bodyguard - see
    // docs/brute-ascensions.md), then wires all of it into BruteCharacterData.asset and
    // BruteBaseSkill-Juggernaut.asset. Replaces the old BruteProtectorAssetGenerator.cs/
    // BruteKnockbackMasteryAssetGenerator.cs pair - same "one generator fully replaces every list it
    // touches end to end" fix the Pixie Ascension refactor already applied for the identical
    // append-vs-replace drift bug (see PixieAscensionAssetGenerator's own comment).
    //
    // Momentum/Bone Breaker/Aftershock/Concussive Impact are SkillActionData living on
    // JuggernautSkillData.Actions (Activated = false), NOT PassiveUpgradeData - same "Hero Skill
    // Ascension" shape Pixie's ClusterBombSkillAction/BirthdayCakeSkillAction already use. They were
    // originally built as PassiveUpgradeData, but that made them show up labeled as a generic "Passive
    // Upgrade" in both the level-up UI and the debug menu, indistinguishable from Iron Presence/
    // Guardian (which genuinely are hero-wide passives) - misleading for something this specifically
    // tied to Juggernaut. Converting them to Hero Skill Ascensions fixes the label everywhere for
    // free, with zero UI code changes, since GameplayUiController.KindText/DebugUpgradeMenuTrigger
    // already resolve "Hero Skill" vs "Passive Upgrade" purely from which list/Actions-array an
    // option was drafted from.
    //
    // The 4 surviving pre-refactor assets (Guardian/IronPresence/IronShoulderSkillAction/
    // BodyguardSkillAction, plus the unchanged ProtectorPassiveData base passive) are verified against
    // BruteCharacterData.asset's own live GUIDs and CreateOrUpdate'd at their EXACT existing paths, so
    // they keep their GUID/wiring rather than forking a duplicate - the old BruteProtectorAssetGenerator's
    // own path constants had drifted out of sync with where these actually live on disk (see
    // docs/brute-ascensions.md's own history note), which this generator corrects.
    //
    // Per-rank tuned values (DamagePercent/RadiusBonus/etc.) ARE explicitly set here on every run, even
    // though every ranked ascension class already carries a matching C# field-initializer default -
    // that default only applies to a BRAND NEW object (ScriptableObject.CreateInstance), not one
    // that already existed before a field's TYPE changed shape (a plain FP becoming FP[]) - see
    // PixieAscensionAssetGenerator's own comment for the exact corrupted-array failure mode this
    // avoids. Explicitly setting every array here every run is what makes CreateOrUpdate idempotent
    // and correct for pre-existing assets, not just newly-created ones.
    public static class BruteAscensionAssetGenerator
    {
        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Skills/Brute/Brute_PassiveSkill";
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Brute/Brute_PassiveSkill/Brute_PassiveSkillUpgrades";
        private const string HeroSkillUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Brute/Brute_HeroSkill/Brute_HeroSkillUpgrades";
        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Brute/Brute_DashSkillUpgrades";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/BruteCharacterData.asset";
        private const string JuggernautSkillPath = "Assets/_QuantumUser/Resources/Skills/Brute/Brute_HeroSkill/BruteBaseSkill-Juggernaut.asset";

        [MenuItem("Tools/RiftRaiders/Brute/Generate Ascension Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(PassiveUpgradesFolderPath);
            CreateFolderRecursive(HeroSkillUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);

            ProtectorPassiveData passive = CreateOrUpdate<ProtectorPassiveData>($"{PassivesFolderPath}/ProtectorPassiveData.asset", asset =>
            {
                asset.Radius = 6;
                asset.IntimidateDamageMultiplier = FP.FromString("0.75");
            });

            MomentumSkillAction momentum = CreateOrUpdate<MomentumSkillAction>($"{HeroSkillUpgradesFolderPath}/MomentumSkillAction.asset", asset =>
            {
                asset.DisplayName = "Momentum";
                asset.Activated = false;
                asset.MaxRank = 3;
                // Static fallback for surfaces that call the plain, rank-unaware GetDescription() -
                // e.g. UpgradePopupWidget's Tab-hold history list. GetDescription(int rank) (below,
                // built from the per-rank arrays) is what every rank-aware surface actually shows.
                asset.Description = "Juggernaut builds Charge faster while you're running, and rewards staying Charged with extra Move Speed.";
                asset.RankDescriptions = new[]
                {
                    "Momentum builds 25% faster while running during Juggernaut, +10% Move Speed while Charged, and a discharge only resets Momentum to 30% instead of fully draining it.",
                    "Momentum builds 40% faster while running during Juggernaut, +20% Move Speed while Charged, and a discharge only resets Momentum to 60% instead of fully draining it.",
                    "Momentum builds 40% faster while running during Juggernaut, +30% Move Speed while Charged, and discharging no longer resets Momentum at all.",
                };
                asset.GenerationMultiplier = new[] { FP.FromString("1.25"), FP.FromString("1.40"), FP.FromString("1.40") };
                asset.ChargedMoveSpeedBonus = new[] { FP._0_10, FP._0_20, FP.FromString("0.30") };
                asset.DischargeRetentionFraction = new[] { FP.FromString("0.30"), FP.FromString("0.60"), FP._1 };
            });

            BoneBreakerSkillAction boneBreaker = CreateOrUpdate<BoneBreakerSkillAction>($"{HeroSkillUpgradesFolderPath}/BoneBreakerSkillAction.asset", asset =>
            {
                asset.DisplayName = "Bone Breaker";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Discharge deals significantly more damage, especially against Specialist and Heavy enemies.";
                asset.RankDescriptions = new[]
                {
                    "Discharge deals +30% damage.",
                    "Discharge deals +60% damage.",
                    "Discharge deals +100% damage. Specialist and Heavy enemies take an additional +30% Discharge damage.",
                };
                asset.DamageMultiplierBonus = new[] { FP.FromString("0.30"), FP.FromString("0.60"), FP._1 };
                asset.TierDamageBonus = new[] { FP._0, FP._0, FP.FromString("0.30") };
            });

            AftershockSkillAction aftershock = CreateOrUpdate<AftershockSkillAction>($"{HeroSkillUpgradesFolderPath}/AftershockSkillAction.asset", asset =>
            {
                asset.DisplayName = "Aftershock";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Route through a crowd, then cash it in - Juggernaut's closing shockwave grows with every enemy you struck during the cast.";
                asset.RankDescriptions = new[]
                {
                    "Juggernaut's closing shockwave deals +15% damage per enemy struck during the cast, up to 5 stacks.",
                    "Each stack also widens the shockwave by 5%.",
                    "Earthquake: at 5 stacks, a second shockwave lands half a second after the first.",
                };
                asset.StackDamagePercent = new[] { FP.FromString("0.15"), FP.FromString("0.15"), FP.FromString("0.15") };
                asset.StackRadiusPercent = new[] { FP._0, FP.FromString("0.05"), FP.FromString("0.05") };
                asset.MaxStacks = new byte[] { 5, 5, 5 };
                asset.EarthquakeStackThreshold = new byte[] { 0, 0, 5 };
                asset.EarthquakeDamagePercent = FP.FromString("0.60");
                asset.EarthquakeRadiusMultiplier = FP._1;
                asset.EarthquakeDelay = FP._0_50;
            });

            ConcussiveImpactSkillAction concussiveImpact = CreateOrUpdate<ConcussiveImpactSkillAction>($"{HeroSkillUpgradesFolderPath}/ConcussiveImpactSkillAction.asset", asset =>
            {
                asset.DisplayName = "Concussive Impact";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Enemies launched by Discharge take real damage when they land and hit harder walls - culminating in an impact shockwave and bonus damage against Stunned enemies.";
                asset.RankDescriptions = new[]
                {
                    "Launched enemies take 30% Juggernaut damage and are Stunned 0.75s on landing.",
                    "Launched enemies take 50% Juggernaut damage and are Stunned 1s on landing, and launch force is increased by +25%.",
                    "Launched enemies take 75% Juggernaut damage and are Stunned 1.25s on landing, +25% launch force, and create a 2.5m impact shockwave dealing 40% Juggernaut damage that Stuns nearby enemies. You deal +40% damage to Stunned enemies.",
                };
                asset.LandingDamagePercent = new[] { FP.FromString("0.30"), FP._0_50, FP.FromString("0.75") };
                asset.LandingStunDuration = new[] { FP.FromString("0.75"), FP._1, FP.FromString("1.25") };
                asset.KnockbackForceBonus = new[] { FP._0, FP.FromString("0.25"), FP.FromString("0.25") };
                asset.ShockwaveRadius = new[] { FP._0, FP._0, FP.FromString("2.5") };
                asset.ShockwaveDamagePercent = new[] { FP._0, FP._0, FP.FromString("0.40") };
                asset.ShockwaveStunDuration = new[] { FP._0, FP._0, FP._1 };
                asset.StunDamageBonus = FP.FromString("0.40");
            });

            IronPresencePassiveUpgradeData ironPresence = CreateOrUpdate<IronPresencePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/IronPresence.asset", asset =>
            {
                asset.DisplayName = "Iron Presence";
                asset.Description = "Intimidated enemies in your Protector Aura move slower and take more knockback - eventually, you deal bonus damage to them too.";
                asset.RankDescriptions = new[]
                {
                    "Intimidated enemies in your aura move 15% slower and take +25% knockback force.",
                    "Intimidated enemies in your aura move 15% slower, take +25% knockback force, and you deal +20% damage to them.",
                    "Intimidated enemies in your aura move 25% slower, take +50% knockback force, and you deal +35% damage to them.",
                };
                asset.MaxRank = 3;
                asset.SlowMultiplier = new[] { FP.FromString("0.85"), FP.FromString("0.85"), FP.FromString("0.75") };
                asset.KnockbackTakenMultiplier = new[] { FP.FromString("1.25"), FP.FromString("1.25"), FP._1_50 };
                asset.FearlessBonusVsIntimidated = new[] { FP._0, FP.FromString("0.20"), FP.FromString("0.35") };
            });

            GuardianPassiveUpgradeData guardian = CreateOrUpdate<GuardianPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Guardian.asset", asset =>
            {
                asset.DisplayName = "Guardian";
                asset.Description = "Grows the Protector Aura and shelters allies inside it - eventually reacting to protect them further the instant they're hit.";
                asset.RankDescriptions = new[]
                {
                    "Aura radius +2m; allies inside gain 10% Damage Reduction.",
                    "Aura radius +3m; allies inside gain 15% Damage Reduction and 30% Knockback Resistance.",
                    "The same 15% baseline - and when an ally in the aura takes an enemy hit, they gain a further +20% Damage Reduction for 2s (5s cooldown per ally).",
                };
                asset.MaxRank = 3;
                asset.RadiusBonus = new[] { FP._2, FP._3, FP._3 };

                // Deliberately FLAT from rank 2 onward - rank 3's value is the reactive spike, not a
                // bigger always-on number. Combined with the shared aura-DR slot (two Brutes never
                // stack additively), this is what keeps a co-op DR stack from reaching near-immunity.
                asset.AllyDamageReductionAmount = new[] { FP.FromString("0.10"), FP.FromString("0.15"), FP.FromString("0.15") };
                asset.AllyKnockbackTakenMultiplier = new[] { FP._1, FP.FromString("0.70"), FP.FromString("0.70") };
                asset.ReactiveDamageReductionAmount = FP._0_20;
                asset.ReactiveDamageReductionDuration = FP._2;
                asset.ReactiveCooldownPerAlly = FP._5;
            });

            GroundbreakerPassiveUpgradeData groundbreaker = CreateOrUpdate<GroundbreakerPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Groundbreaker.asset", asset =>
            {
                asset.DisplayName = "Groundbreaker";
                asset.Description = "Dropping from higher ground turns Brute's own weight into a weapon - scattering enemies, slamming them into walls, and cracking them open.";
                asset.RankDescriptions = new[]
                {
                    "Heavy Landing: landing from higher ground creates a shockwave that knocks nearby enemies away.",
                    "Crash Landing: Heavy Landing knocks enemies farther and hits harder. Enemies slammed into walls are Stunned.",
                    "Seismic Impact: Heavy Landing has increased area and knockback. Enemies slammed into walls become Exposed and take increased damage.",
                };
                asset.MaxRank = 3;

                // Double MovementDataAsset.MaxLedgeHeight (1) - the tallest ledge Brute can auto-mantle
                // - so ordinary traversal can never reach it. See GroundbreakerPassiveUpgradeData.
                asset.MinimumFallHeight = 2;
                asset.AllowFallLandings = true;
                asset.AllowJumpLandings = true;
                asset.AllowLaunchedLandings = true;

                asset.ImpactRadius = new[] { FP._3, FP._3, FP.FromString("4.5") };
                asset.KnockbackForce = new FP[] { 10, 14, FP.FromString("16.5") };
                asset.KnockbackUpwardForce = FP._2;
                asset.ImpactDamagePercent = new[] { FP.FromString("0.20"), FP._0_50, FP.FromString("0.75") };
                asset.MaxAffectedTier = EnemyTier.Boss;

                asset.WallStunDuration = FP._1;
                asset.WallCheckDistance = FP._2;

                asset.VulnerabilityDamageTakenModifier = FP._0_25;
                asset.VulnerabilityDuration = FP._3;
            });

            IronShoulderSkillAction ironShoulder = CreateOrUpdate<IronShoulderSkillAction>($"{DashUpgradesFolderPath}/IronShoulderSkillAction.asset", asset =>
            {
                asset.DisplayName = "Iron Shoulder";
                asset.MaxRank = 3;
                asset.Description = "Dash becomes an empowered shoulder charge - enemies hit take strong knockback and are Stunned if pushed into a wall.";
                asset.RankDescriptions = new[]
                {
                    "Dash becomes an empowered shoulder charge - enemies hit take strong knockback and are Stunned if pushed into a wall.",
                    "Dash becomes an empowered shoulder charge dealing 60% Juggernaut damage on collision (+50% if slammed into a wall) and Stunning enemies pushed into a wall.",
                    "Dash becomes an empowered shoulder charge dealing 60% Juggernaut damage on collision (+50% if slammed into a wall) and Stunning enemies pushed into a wall - a wall-slam also creates a 3m impact shockwave dealing 80% Juggernaut damage that Stuns nearby enemies.",
                };
                asset.KnockbackTier = KnockbackTier.Strong;
                asset.WallCheckDistance = 2;
                asset.StunDuration = 1;
                asset.DamagePercent = new[] { FP._0, FP.FromString("0.60"), FP.FromString("0.60") };
                asset.WallSlamDamageBonus = new[] { FP._0, FP._0_50, FP._0_50 };
                asset.ShockwaveRadius = new[] { FP._0, FP._0, FP._3 };
                asset.ShockwaveDamagePercent = new[] { FP._0, FP._0, FP.FromString("0.80") };
            });

            BodyguardSkillAction bodyguard = CreateOrUpdate<BodyguardSkillAction>($"{DashUpgradesFolderPath}/BodyguardSkillAction.asset", asset =>
            {
                asset.DisplayName = "Bodyguard";
                asset.MaxRank = 3;
                asset.Description = "On Dash complete, restore Shield to nearby allies.";
                asset.RankDescriptions = new[]
                {
                    "On Dash complete, restore 10 Shield to allies within 6m.",
                    "On Dash complete, restore 15 Shield to allies within 8m.",
                    "On Dash complete, restore 20 Shield to allies within 8m and grant them +20% Damage Reduction for 2s.",
                };
                asset.Radius = new[] { FP._6, FP._8, FP._8 };

                // FLAT, not a percentage of the ally's own Max Shield - a percentage restore scales with
                // the recipient and let a dash-cooldown build pump unbounded Shield into a tanky ally.
                asset.ShieldRestore = new FP[] { 10, 15, 20 };
                asset.CooldownPerAlly = FP.FromString("4.5");
                asset.SelfEffectMultiplier = FP._0_50;
                asset.DamageReductionAmount = FP._0_20;
                asset.DamageReductionDuration = FP._2;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive,
                new List<PassiveUpgradeData> { ironPresence, guardian, groundbreaker },
                new List<SkillActionData> { ironShoulder, bodyguard });

            WireJuggernautActions(new List<SkillActionData> { momentum, boneBreaker, aftershock, concussiveImpact });

            LogHelper.Log("BruteAscensionAssetGenerator", "Protector passive + 9 Ascension lines authored and wired (3 Passive Upgrades " +
                      "into BruteCharacterData.PassiveUpgrades, Iron Shoulder/Bodyguard into BruteCharacterData.DashSkillUpgrades, Momentum/Bone " +
                      "Breaker/Aftershock/Concussive Impact into BruteBaseSkill-Juggernaut.Actions as Hero Skill Ascensions - every list fully " +
                      "replaced, not appended; every per-rank value is re-set explicitly on every run). Unstoppable was " +
                      "removed and replaced by Groundbreaker - delete the stale Unstoppable.asset by hand.");
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

        private static void WireCharacterData(ProtectorPassiveData passive, List<PassiveUpgradeData> passiveUpgrades, List<SkillActionData> dashUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("BruteAscensionAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            characterData.Passive = new AssetRef<PassiveData>(passive.Guid);
            characterData.PassiveUpgrades = passiveUpgrades.Select(a => new AssetRef<PassiveUpgradeData>(a.Guid)).ToList();
            characterData.DashSkillUpgrades = dashUpgrades.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();

            EditorUtility.SetDirty(characterData);
            AssetDatabase.SaveAssets();
        }

        // Wires the 4 Hero Skill Ascensions into JuggernautSkillData.Actions - CheckActions stays
        // false either way (see docs/brute-ascensions.md's own "CheckActions bug" section), since these
        // execute via SkillSlot.Upgrades once picked, same mechanism Pixie's ClusterBombSkillAction/
        // BirthdayCakeSkillAction already use; Actions here is purely the draft-eligibility source list
        // LevelUpUtility.AddHeroSkillUpgradeCandidates reads (Activated == false -> offerable). Also
        // sweeps and removes any stray sub-object embedded directly in the asset file that ISN'T the
        // main JuggernautSkillData asset - a safety net against the exact leftover-embedded-orphan bug
        // this whole Ascension pool already had before this refactor (see the class comment).
        private static void WireJuggernautActions(List<SkillActionData> actions)
        {
            var mainAsset = AssetDatabase.LoadAssetAtPath<JuggernautSkillData>(JuggernautSkillPath);

            if (mainAsset == null)
            {
                LogHelper.Error("BruteAscensionAssetGenerator", $"No JuggernautSkillData asset at {JuggernautSkillPath} - Momentum/Bone Breaker/Aftershock/Concussive Impact were created, but Actions was not wired.");
                return;
            }

            mainAsset.Actions = actions.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();
            EditorUtility.SetDirty(mainAsset);
            AssetDatabase.SaveAssets();

            var allObjects = AssetDatabase.LoadAllAssetsAtPath(JuggernautSkillPath);

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

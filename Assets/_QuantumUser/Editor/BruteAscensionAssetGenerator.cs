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
                asset.Description = "An aura that intimidates nearby enemies, weakening their damage.";

                asset.Radius = 6;
                asset.IntimidateDamageMultiplier = FP.FromString("0.75");
            });

            MomentumSkillAction momentum = CreateOrUpdate<MomentumSkillAction>($"{HeroSkillUpgradesFolderPath}/MomentumSkillAction.asset", asset =>
            {
                asset.DisplayName = "Momentum";
                asset.Activated = false;
                asset.MaxRank = 3;
                // Static fallback for surfaces that call the plain, rank-unaware GetDescription() -
                // e.g. HeroInfoPopupWidget's Tab-hold history list. GetDescription(int rank) (below,
                // built from the per-rank arrays) is what every rank-aware surface actually shows.
                asset.Description = "Juggernaut rewards staying on the move - Charge builds faster, and a discharge no longer drains it all.";
                asset.RankDescriptions = new[]
                {
                    "Momentum builds <color=#FD3971>25%</color> faster during Juggernaut. Gain <color=#FD3971>+10%</color> Move Speed while Charged. Discharge leaves <color=#FD3971>30%</color> Charge.",
                    "Momentum builds <color=#FD3971>40%</color> faster. Gain <color=#FD3971>+20%</color> Move Speed while Charged. Discharge leaves <color=#FD3971>60%</color> Charge.",
                    "Gain <color=#FD3971>+30%</color> Move Speed while Charged. Discharge costs no Charge, and Juggernaut won't end while fully Charged.",
                };
                asset.GenerationMultiplier = new[] { FP.FromString("1.25"), FP.FromString("1.40"), FP.FromString("1.40") };
                asset.ChargedMoveSpeedBonus = new[] { FP._0_10, FP._0_20, FP.FromString("0.30") };
                asset.DischargeRetentionFraction = new[] { FP.FromString("0.30"), FP.FromString("0.60"), FP._1 };
                asset.HoldUntilDischarge = new[] { false, false, true };
            });

            BoneBreakerSkillAction boneBreaker = CreateOrUpdate<BoneBreakerSkillAction>($"{HeroSkillUpgradesFolderPath}/BoneBreakerSkillAction.asset", asset =>
            {
                asset.DisplayName = "Bone Breaker";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Juggernaut's discharge stops being pure knockback and starts killing, especially tougher enemies.";
                asset.RankDescriptions = new[]
                {
                    "Discharge deals <color=#FD3971>+30%</color> Damage.",
                    "Discharge deals <color=#FD3971>+60%</color> Damage.",
                    "Discharge deals <color=#FD3971>+100%</color> Damage and <color=#FD3971>+30%</color> additional Damage to Specialist and Heavy enemies.",
                };
                asset.DamageMultiplierBonus = new[] { FP.FromString("0.30"), FP.FromString("0.60"), FP._1 };
                asset.TierDamageBonus = new[] { FP._0, FP._0, FP.FromString("0.30") };
            });

            AftershockSkillAction aftershock = CreateOrUpdate<AftershockSkillAction>($"{HeroSkillUpgradesFolderPath}/AftershockSkillAction.asset", asset =>
            {
                asset.DisplayName = "Aftershock";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "Juggernaut's closing shockwave grows with every enemy you strike during the channel.";
                asset.RankDescriptions = new[]
                {
                    "Each enemy hit by Discharge makes the closing shockwave <color=#FD3971>15%</color> stronger, up to <color=#FD3971>5</color> enemies.",
                    "Each enemy hit also makes the shockwave <color=#FD3971>5%</color> larger, up to <color=#FD3971>5</color> enemies.",
                    "At <color=#FD3971>5</color> enemies hit, the shockwave repeats <color=#FD3971>0.5s</color> later for <color=#FD3971>60%</color> of its Damage.",
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
                asset.Description = "Enemies your discharge launches no longer land safely - they take damage, get Stunned, and crack the ground.";
                asset.RankDescriptions = new[]
                {
                    "Launched enemies take <color=#FD3971>30%</color> Juggernaut Skill Damage and are Stunned for <color=#FD3971>0.75s</color> when they land.",
                    "<color=#FD3971>+25%</color> Knockback. Landing Damage increases to <color=#FD3971>50%</color> and Stun increases to <color=#FD3971>1s</color>.",
                    "Landing Damage rises to <color=#FD3971>75%</color>, Stun to <color=#FD3971>1.25s</color>. Creates a <color=#FD3971>2.5m</color> shockwave (<color=#FD3971>40%</color> Damage, <color=#FD3971>1s</color> Stun). <color=#FD3971>+40%</color> Damage to Stunned.",
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
                asset.Description = "Enemies intimidated by your Protector Aura are worn down before they reach you.";
                asset.RankDescriptions = new[]
                {
                    "Intimidated enemies take <color=#FD3971>+25%</color> Knockback.",
                    "Brute also deals <color=#FD3971>+20%</color> Damage to Intimidated enemies.",
                    "Knockback bonus increases to <color=#FD3971>+50%</color> and Damage bonus to <color=#FD3971>+35%</color>.",
                };
                asset.MaxRank = 3;
                asset.KnockbackTakenMultiplier = new[] { FP.FromString("1.25"), FP.FromString("1.25"), FP._1_50 };
                asset.FearlessBonusVsIntimidated = new[] { FP._0, FP.FromString("0.20"), FP.FromString("0.35") };
            });

            GuardianPassiveUpgradeData guardian = CreateOrUpdate<GuardianPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Guardian.asset", asset =>
            {
                asset.DisplayName = "Guardian";
                asset.Description = "Your Protector Aura becomes a safe zone - allies inside take less damage and get covered when hit.";
                asset.RankDescriptions = new[]
                {
                    "Protector radius increases by <color=#FD3971>2m</color>. Allies inside gain <color=#FD3971>10%</color> Damage Reduction.",
                    "Radius increases by <color=#FD3971>3m</color>. Allies gain <color=#FD3971>15%</color> Damage Reduction and <color=#FD3971>+30%</color> Knockback Resistance.",
                    "When an ally inside Protector is hit, they gain an additional <color=#FD3971>20%</color> Damage Reduction for <color=#FD3971>2s</color>.",
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
                asset.Description = "Landing from a height turns Brute's weight into a weapon - throwing, slamming, and cracking enemies open.";
                asset.RankDescriptions = new[]
                {
                    "Jumping down from a height creates a <color=#FD3971>3m</color> landing shockwave that damages and knocks enemies away.",
                    "Heavy Landing deals more Damage and Knockback. Enemies knocked into walls are Stunned for <color=#FD3971>1s</color>.",
                    "Also triggers on a same-height jump. Radius <color=#FD3971>4.5m</color>, Damage <color=#FD3971>75%</color>. Wall-Stunned enemies become Exposed (<color=#FD3971>+25%</color> Damage, <color=#FD3971>3s</color>).",
                };
                asset.MaxRank = 3;

                // Double MovementDataAsset.MaxLedgeHeight (1) - the tallest ledge Brute can auto-mantle
                // - so ordinary traversal can never reach it. See GroundbreakerPassiveUpgradeData.
                asset.MinimumFallHeight = 2;
                asset.AllowFallLandings = true;
                asset.AllowJumpLandings = true;
                asset.AllowLaunchedLandings = true;

                // Rank 3 only - see GroundbreakerPassiveUpgradeData/Groundbreaker.qtn. 0.5 world units
                // either side of takeoff height, and only for a genuine (manual-input) jump.
                asset.SameHeightTolerance = FP._0_50;

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
                asset.Description = "Dash becomes a shoulder charge that bowls enemies over and punishes anyone slammed into a wall.";
                asset.RankDescriptions = new[]
                {
                    "Dashing into enemies knocks them back. Enemies slammed into walls are Stunned.",
                    "Dash collisions deal <color=#FD3971>60%</color> Juggernaut Skill Damage. Wall slams deal <color=#FD3971>+50%</color> additional Damage.",
                    "Wall slams create a <color=#FD3971>3m</color> shockwave dealing <color=#FD3971>80%</color> Juggernaut Skill Damage and Stunning nearby enemies.",
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
                asset.Description = "Dash to hand out protection - a guard that blocks the next hit and pays you back in Shield.";
                asset.RankDescriptions = new[]
                {
                    "After Dashing, Brute and allies within <color=#FD3971>3m</color> gain Free Hit Guard for <color=#FD3971>2.5s</color>. The next damaging hit is completely blocked.",
                    "Radius increases to <color=#FD3971>6m</color> and duration to <color=#FD3971>3.5s</color>. Whenever a Guard blocks a hit, Brute gains <color=#FD3971>10</color> Temporary Shield.",
                    "A block also grants Brute <color=#FD3971>15</color> Shield and releases a <color=#FD3971>3m</color> knockback shockwave around whoever it saved.",
                };

                // Grows every rank rather than plateauing at rank 2 (hand-tuned in the Inspector and
                // brought back here so a regeneration can't stomp it). A tight rank-1 radius makes
                // guarding a teammate a deliberate act of aiming the dash at them.
                asset.Radius = new[] { FP._3, FP._6, FP._8 };
                asset.GuardDuration = new[] { FP.FromString("2.5"), FP.FromString("3.5"), FP.FromString("3.5") };

                // Brute's own payoff is EARNED on a guard actually blocking, rather than handed to him
                // for dashing the way the old SelfEffectMultiplier self-restore was. He guards himself
                // too, so ranks 2-3 close a real loop: guard, eat a hit with it, get Shield back. Same
                // pool Juggernaut charges, so it's a second route to keeping his own Accessory on
                // (any Shield at all = the accessory never pops).
                asset.ShieldReward = new FP[] { FP._0, 10, 15 };
                asset.CooldownPerAlly = FP.FromString("4.5");
                asset.ShockwaveRadius = FP._3;
                asset.ShockwaveForce = FP._4;
            });

            // Hero Mastery (see docs/hero-mastery.md) - Shotgun (Weapon Family) + Neutral (Element),
            // drafted through the same Passive Upgrade pool as Iron Presence/Guardian/Groundbreaker
            // above, not a separate system. Authored in HeroMasteryAssetGenerator (shared with the
            // other 5 heroes' own Mastery pairs and with that file's own "regenerate all 12 Mastery
            // assets" menu item) rather than inline here, so the tuned values live in exactly one place.
            var (shotgunMastery, neutralMastery) = HeroMasteryAssetGenerator.CreateBruteMastery();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive,
                new List<PassiveUpgradeData> { ironPresence, guardian, groundbreaker, shotgunMastery, neutralMastery },
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

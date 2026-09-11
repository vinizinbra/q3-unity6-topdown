namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Pixie's Chain Reaction base passive and her 9 Ascension lines (Cluster Bomb/Direct
    // Hit/Birthday Cake/Pocket Bombs/Unstable Mixture/Unstable Targeting/Explosive Rounds/Backblast/
    // Hot Fuse - see docs/pixie-ascensions.md), then wires all of it into PixieCharacterData.asset and
    // PixieBaseSkill.asset. Replaces the old PixieChainReactionAssetGenerator.cs/
    // PixieDemolitionMasteryAssetGenerator.cs pair - those two split the same PassiveUpgrades list
    // across a replace-owner and an append-only generator, which is exactly how it drifted out of
    // sync with what was actually live (see docs/pixie-ascensions.md's own history note). One
    // generator now fully replaces every list it touches (PassiveUpgrades, PixieBaseSkill.Actions,
    // DashSkillUpgrades) end to end, so re-running it always converges back to the intended roster
    // instead of compounding drift.
    //
    // Per-rank tuned values (Chance/DamagePercent/etc.) ARE explicitly set here on every run, even
    // though every ranked ascension class already carries a matching C# field-initializer default
    // (see e.g. UnstableMixturePassiveUpgradeData.BonusDamageMultiplier) - that default only applies
    // to a BRAND NEW object (ScriptableObject.CreateInstance). For an asset that already existed
    // before a field's TYPE changed shape (a plain FP becoming FP[], as every one of these did during
    // the rank rework), Unity's deserializer doesn't re-run the C# initializer - it just carries over
    // whatever it can from the old serialized data, which for a scalar-to-array migration can silently
    // produce a corrupted single-element array (confirmed - DirectHit.asset's own
    // DamageMultiplierBonus deserialized to a 1-element array holding RawValue 0, not the 3 real
    // values). Explicitly setting every array here every run is what makes CreateOrUpdate idempotent
    // and correct for pre-existing assets, not just newly-created ones. Asset references (Pocket
    // Bombs' MiniBombPrototype/Explosion, Backblast's BombPrototype/Explosion, Cluster Bomb's own
    // Projectile) still need hand-assigning in the Editor - flagged in this script's own log.
    //
    // All 3 folders (base passive, passive upgrades, hero skill/dash upgrades) live under one shared
    // Skills/Pixie root now, matching Brute/Lux/Max's own convention - the base passive and its
    // upgrades used to sit under a separate Resources/Passives/Pixie tree instead, which is exactly
    // the kind of drift that let a stale ChainReactionPassiveData/UnstableMixture duplicate accumulate
    // at the Skills/Pixie location too (found and removed moving these). Also found and fixed the same
    // class of leftover-orphan bug the Brute Ascension refactor already caught: PixieBaseSkill.asset
    // (a multi-object file with several sub-actions embedded directly in it) still had 6 dead
    // sub-objects from the earlier removal pass (BombRadiusUpSkillAction/BombInstantDetonateSkillAction/
    // FireworksSkillAction and their own nested ProjectileData/MovementData sub-assets) physically
    // sitting in the file, unreferenced by PixieBaseSkill.Actions - WireBaseSkill now sweeps and
    // removes them via AssetDatabase.RemoveObjectFromAsset on every run, same fix
    // BruteAscensionAssetGenerator.ClearJuggernautActions already applies for Brute.
    public static class PixieAscensionAssetGenerator
    {
        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Skills/Pixie/Pixie_PassiveSkill";
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Pixie/Pixie_PassiveSkill/Pixie_PassiveSkillUpgrades";
        private const string HeroSkillUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Pixie/Pixie_HeroSkillUpgrades";
        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Pixie/Pixie_DashSkillUpgrades";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/PixieCharacterData.asset";
        private const string BaseSkillPath = "Assets/_QuantumUser/Resources/Skills/Pixie/Pixie_HeroSkillUpgrades/PixieBaseSkill.asset";

        [MenuItem("Tools/RiftRaiders/Pixie/Generate Ascension Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(PassiveUpgradesFolderPath);
            CreateFolderRecursive(HeroSkillUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);

            // PassiveData (unlike PassiveUpgradeData) derives AssetObject directly, not UpgradeData -
            // a hero's single base Passive is Inspector-assigned (CharacterData.Passive), never
            // offered as a level-up card, so it has no DisplayName/Rarity/Description to set here.
            ChainReactionPassiveData passive = CreateOrUpdate<ChainReactionPassiveData>($"{PassivesFolderPath}/ChainReactionPassiveData.asset", asset =>
            {
                asset.Description = "Your explosions mark weak enemies - a marked enemy blows up on death.";

                asset.MarkChance = FP._0_50;
            });

            // MOVED from the Passive pool into the Hero Skill pool per the balance brief - it reads as
            // "how Bunny Bomb behaves", and the level-up UI labels a line by the pool it's drafted
            // from. Activated = false, same "Hero Skill Ascension" shape Cluster Bomb/Birthday Cake use.
            DirectHitSkillAction directHit = CreateOrUpdate<DirectHitSkillAction>($"{HeroSkillUpgradesFolderPath}/DirectHitSkillAction.asset", asset =>
            {
                asset.DisplayName = "Direct Hit";
                asset.Activated = false;
                asset.Description = "Enemies caught near the center of the blast take bonus damage.";
                asset.RankDescriptions = new[]
                {
                    "Enemies near the center of Pixie's explosions take <color=#FD3971>+30%</color> Damage.",
                    "The high-damage center becomes larger and the bonus increases to <color=#FD3971>+50%</color>.",
                    "Bonus increases to <color=#FD3971>+75%</color>. Center hits also strongly Knockback enemies.",
                };
                asset.MaxRank = 3;
                asset.InnerRadiusFraction = new[] { FP.FromString("0.35"), FP.FromString("0.45"), FP.FromString("0.45") };
                asset.DamageMultiplierBonus = new[] { FP.FromString("0.30"), FP.FromString("0.50"), FP.FromString("0.75") };
                asset.KnockbackForce = 8;
                asset.KnockbackUpwardForce = 2;
                asset.KnockbackEliteMultiplier = FP.FromString("0.4");
            });

            PocketBombsPassiveUpgradeData pocketBombs = CreateOrUpdate<PocketBombsPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/PocketBombs.asset", asset =>
            {
                asset.DisplayName = "Pocket Bombs";
                asset.Description = "Your explosions can drop a stationary Mini Bomb.";
                asset.RankDescriptions = new[]
                {
                    "Pixie's explosions have a <color=#FD3971>15%</color> chance to leave a Mini Bomb dealing <color=#FD3971>35%</color> Bunny Bomb Damage.",
                    "Chance increases to <color=#FD3971>25%</color> and Damage to <color=#FD3971>45%</color>.",
                    "Chance increases to <color=#FD3971>35%</color> and Damage to <color=#FD3971>55%</color>.",
                };
                asset.MaxRank = 3;
                asset.Chance = new[] { FP.FromString("0.15"), FP.FromString("0.25"), FP.FromString("0.35") };
                asset.DamagePercent = new[] { FP.FromString("0.35"), FP.FromString("0.45"), FP.FromString("0.55") };
                asset.Fuse = FP.FromString("0.4");
            });

            UnstableMixturePassiveUpgradeData unstableMixture = CreateOrUpdate<UnstableMixturePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/UnstableMixture.asset", asset =>
            {
                asset.DisplayName = "Unstable Mixture";
                asset.Description = "Pixie's explosion kills empower her next explosion.";
                asset.RankDescriptions = new[]
                {
                    "Explosion kills bank <color=#FD3971>1</color> charge. Pixie's next explosion consumes the stored charge for <color=#FD3971>+30%</color> Damage and <color=#FD3971>+15%</color> Radius.",
                    "Pixie can store up to <color=#FD3971>2</color> charges.",
                    "Using maximum charges also creates a delayed secondary explosion.",
                };
                asset.MaxRank = 3;
                asset.DamageBonusPerStack = FP.FromString("0.30");
                asset.RadiusBonusPerStack = FP.FromString("0.15");
                asset.MaxStacks = new byte[] { 1, 2, 2 };
                asset.SecondaryDamagePercent = FP._0_50;
                asset.SecondaryRadiusMultiplier = FP.FromString("0.75");
                asset.SecondaryDelay = FP._0_50;
            });

            ExplosiveRoundsPassiveUpgradeData explosiveRounds = CreateOrUpdate<ExplosiveRoundsPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/ExplosiveRounds.asset", asset =>
            {
                asset.DisplayName = "Explosive Rounds";
                asset.Description = "Weapon hits can trigger a small explosion.";
                asset.RankDescriptions = new[]
                {
                    "Weapon hits have a <color=#FD3971>15%</color> chance to trigger a small explosion dealing <color=#FD3971>+20%</color> Damage in a <color=#FD3971>2m</color> radius.",
                    "Chance increases to <color=#FD3971>22%</color>, Damage to <color=#FD3971>+30%</color>, and radius to <color=#FD3971>2.4m</color>.",
                    "Chance increases to <color=#FD3971>30%</color> and Damage to <color=#FD3971>+40%</color>.",
                };
                asset.MaxRank = 3;
                asset.ProcChance = new[] { FP.FromString("0.15"), FP.FromString("0.22"), FP.FromString("0.30") };
                asset.Radius = new[] { FP._2, FP.FromString("2.4"), FP.FromString("2.4") };
                asset.DamageMultiplier = new[] { FP.FromString("0.20"), FP.FromString("0.30"), FP.FromString("0.40") };

                // Shipped OFF - the per-shot chance above is the primary lever. Turn this on from data
                // only if a very high-Fire-Rate weapon still procs too often in playtesting.
                asset.ProcCooldown = FP._0;
            });

            ClusterBombSkillAction clusterBomb = CreateOrUpdate<ClusterBombSkillAction>($"{HeroSkillUpgradesFolderPath}/ClusterBombSkillAction.asset", asset =>
            {
                asset.DisplayName = "Cluster Bomb";
                asset.Activated = false;
                asset.MaxRank = 3;
                // Static fallback for surfaces that call the plain, rank-unaware GetDescription() -
                // e.g. HeroInfoPopupWidget's Tab-hold history list. GetDescription(int rank) (below,
                // built from the per-rank arrays) is what every rank-aware surface actually shows.
                asset.Description = "Bunny Bomb scatters bomblets when it explodes.";
                asset.RankDescriptions = new[]
                {
                    "A thrown Bunny Bomb releases <color=#FD3971>2</color> bomblets when it explodes, each dealing <color=#FD3971>40%</color> Damage.",
                    "Releases <color=#FD3971>3</color> bomblets dealing <color=#FD3971>45%</color> Damage each.",
                    "Releases <color=#FD3971>4</color> bomblets dealing <color=#FD3971>50%</color> Damage each.",
                };
                asset.Count = new byte[] { 2, 3, 4 };
                asset.DamagePercent = new[] { FP.FromString("0.40"), FP.FromString("0.45"), FP.FromString("0.50") };
            });

            BirthdayCakeSkillAction birthdayCake = CreateOrUpdate<BirthdayCakeSkillAction>($"{HeroSkillUpgradesFolderPath}/BirthdayCakeSkillAction.asset", asset =>
            {
                asset.DisplayName = "Birthday Cake";
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "A landed Bunny Bomb becomes a Decoy before it blows.";
                asset.RankDescriptions = new[]
                {
                    "After landing, Bunny Bomb becomes a Decoy for <color=#FD3971>1s</color> before exploding.",
                    "Decoy duration increases to <color=#FD3971>1.5s</color> and explosion radius increases by <color=#FD3971>25%</color>.",
                    "The explosion also deals <color=#FD3971>+30%</color> Damage.",
                };
                asset.TauntDuration = new[] { FP._1, FP.FromString("1.5"), FP.FromString("1.5") };
                asset.TauntRadiusMultiplier = new[] { FP._1, FP.FromString("1.25"), FP.FromString("1.25") };
                asset.BonusDamageMultiplier = FP.FromString("0.30");
            });

            BackblastSkillAction backblast = CreateOrUpdate<BackblastSkillAction>($"{DashUpgradesFolderPath}/BackblastSkillAction.asset", asset =>
            {
                asset.DisplayName = "Backblast";
                asset.MaxRank = 3;
                asset.Description = "Dashing drops a fused bomb behind you.";
                asset.RankDescriptions = new[]
                {
                    "Dashing leaves a bomb at the starting point dealing <color=#FD3971>50%</color> Bunny Bomb Damage.",
                    "Also leaves a bomb at the Dash destination.",
                    "Dash bombs deal <color=#FD3971>75%</color> Bunny Bomb Damage and guarantee Chain Reaction marks on enemies hit.",
                };
                asset.Fuse = FP._1;
                asset.DamagePercent = new[] { FP.FromString("0.50"), FP.FromString("0.50"), FP.FromString("0.75") };
            });

            HotFuseSkillAction hotFuse = CreateOrUpdate<HotFuseSkillAction>($"{DashUpgradesFolderPath}/HotFuseSkillAction.asset", asset =>
            {
                asset.DisplayName = "Hot Fuse";
                asset.MaxRank = 3;
                asset.Description = "Dashing empowers your next Bunny Bomb.";
                asset.RankDescriptions = new[]
                {
                    "After Dashing, the next Bunny Bomb thrown within <color=#FD3971>3s</color> deals <color=#FD3971>+30%</color> Damage.",
                    "The empowered Bunny Bomb also gains <color=#FD3971>+30%</color> Radius.",
                    "Damage bonus increases to <color=#FD3971>+60%</color>. A direct hit detonates the empowered Bunny Bomb instantly.",
                };
                asset.Window = 3;
                asset.DamageMultiplier = new[] { FP.FromString("1.30"), FP.FromString("1.30"), FP.FromString("1.60") };
                asset.RadiusMultiplier = new[] { FP._1, FP.FromString("1.30"), FP.FromString("1.30") };
            });

            BlastJumpSkillAction blastJump = CreateOrUpdate<BlastJumpSkillAction>($"{DashUpgradesFolderPath}/BlastJumpSkillAction.asset", asset =>
            {
                asset.DisplayName = "Blast Jump";
                asset.MaxRank = 3;
                asset.Description = "Dashing supercharges your next Bunny Bomb throw.";
                asset.RankDescriptions = new[]
                {
                    "After Dashing, the next Bunny Bomb thrown within <color=#FD3971>2s</color> travels <color=#FD3971>25%</color> faster and has <color=#FD3971>+25%</color> explosion radius.",
                    "Dashing also reduces Bunny Bomb's remaining cooldown by <color=#FD3971>1s</color>.",
                    "Dashing through your own planted Bunny Bomb detonates it for <color=#FD3971>+50%</color> Damage.",
                };
                asset.Window = 2;
                asset.ProjectileSpeedMultiplier = new[] { FP._1_25, FP._1_25, FP._1_25 };
                asset.RadiusMultiplier = new[] { FP._1_25, FP._1_25, FP._1_25 };
                asset.CooldownReduction = new FP[] { 0, 1, 1 };
                asset.TriggerRadius = 3;
                asset.DetonationDamageBonus = FP._0_50;
            });

            // Hero Mastery (see docs/hero-mastery.md) - Grenade Launcher (Weapon Family) + Fire
            // (Element), drafted through the same Passive Upgrade pool as the lines above. Authored in
            // HeroMasteryAssetGenerator (shared across all 6 heroes) rather than inline here.
            var (grenadeLauncherMastery, fireMastery) = HeroMasteryAssetGenerator.CreatePixieMastery();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive,
                new List<PassiveUpgradeData> { pocketBombs, unstableMixture, explosiveRounds, grenadeLauncherMastery, fireMastery },
                new List<SkillActionData> { backblast, hotFuse, blastJump });

            WireBaseSkill(new List<SkillActionData> { clusterBomb, birthdayCake, directHit });

            LogHelper.Log("PixieAscensionAssetGenerator", "Chain Reaction passive + 9 Ascension lines authored and wired (3 Passive Upgrades " +
                      "into PixieCharacterData.PassiveUpgrades, Cluster Bomb/Birthday Cake/Direct Hit into PixieBaseSkill.Actions, Backblast/Hot Fuse/Blast Jump into " +
                      "PixieCharacterData.DashSkillUpgrades - every list fully replaced, not appended; every per-rank value is re-set explicitly on " +
                      "every run, so a pre-existing asset from before the rank rework - Direct Hit/Unstable Mixture/Explosive Rounds/Backblast - " +
                      "gets its arrays repaired too, not just newly-created ones). Direct Hit MOVED from the Passive pool into PixieBaseSkill.Actions, and Unstable Targeting was removed entirely - delete their stale .asset files by hand. Three asset references still need assigning by hand: " +
                      "PocketBombs.MiniBombPrototype/Explosion and Backblast.BombPrototype/Explosion (a minimal stationary EntityPrototype - " +
                      "Transform3D only, no PhysicsCollider3D/movement data, see ExplodeOnDestroy.qtn's own comment - and an AreaHitData asset with " +
                      "a small BlastRadius each; DashBomb.prefab/its own AreaHitData, see docs/explode-on-destroy.md, is an existing reference " +
                      "prototype Backblast can point straight at), and ClusterBombSkillAction.Projectile (the bomblet ProjectileDataAsset - the old " +
                      "embedded HeroSkill sub-object had this wired already, but this is a brand new standalone asset with no reference yet). " +
                      "Any leftover dead sub-objects still embedded in PixieBaseSkill.asset from before the old baseline actions were removed " +
                      "(Bomb Radius Up/Instant Detonate/Fireworks and their own nested Projectile/MovementData) were swept and removed this run.");
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

        private static void WireCharacterData(ChainReactionPassiveData passive, List<PassiveUpgradeData> passiveUpgrades, List<SkillActionData> dashUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("PixieAscensionAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            characterData.Passive = new AssetRef<PassiveData>(passive.Guid);
            characterData.PassiveUpgrades = passiveUpgrades.Select(a => new AssetRef<PassiveUpgradeData>(a.Guid)).ToList();
            characterData.DashSkillUpgrades = dashUpgrades.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();

            EditorUtility.SetDirty(characterData);
            AssetDatabase.SaveAssets();
        }

        // Named sub-objects still physically embedded in PixieBaseSkill.asset from before the old
        // baseline actions (Bomb Radius Up/Instant Detonate/Fireworks) were removed - dereferencing
        // them from Actions alone (below) doesn't purge them from the file itself, since they're
        // embedded sub-assets, not separate files. Removed by exact name so BunnyBomb's own legitimate
        // embedded data (BunnyBombProjectileDataAsset/BunnyBombAreaHitData/DamageEffectData/etc.) is
        // never touched - same targeted fix BruteAscensionAssetGenerator.ClearJuggernautActions applies
        // for Brute (there every sub-object could be removed since none of Juggernaut's own baseline
        // data lives as a sub-asset; here it can't be that blunt).
        private static readonly string[] DeadEmbeddedSubAssetNames =
        {
            "BombRadiusUpSkillAction",
            "BombInstantDetonateSkillAction",
            "FireworksSkillAction",
            "FireworksProjectileDataAsset",
            "FireworksHomingProjectileMovementData",
            "MiniBombProjectileDataAsset",
        };

        private static void WireBaseSkill(List<SkillActionData> actions)
        {
            var baseSkill = AssetDatabase.LoadAssetAtPath<ProjectileSkillData>(BaseSkillPath);

            if (baseSkill == null)
            {
                LogHelper.Error("PixieAscensionAssetGenerator", $"No ProjectileSkillData asset at {BaseSkillPath} - Cluster Bomb/Birthday Cake were created, but PixieBaseSkill.Actions was not wired.");
                return;
            }

            // Fully replaces the list - clears the old baseline actions (Bomb Radius Up/Instant
            // Detonate/Fireworks, all removed) and the dangling cross-hero GUID that used to sit here
            // (see docs/pixie-ascensions.md's own history note) in the same pass.
            baseSkill.Actions = actions.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();

            EditorUtility.SetDirty(baseSkill);
            AssetDatabase.SaveAssets();

            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(BaseSkillPath))
            {
                if (obj != null && System.Array.IndexOf(DeadEmbeddedSubAssetNames, obj.name) >= 0)
                {
                    AssetDatabase.RemoveObjectFromAsset(obj);
                }
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

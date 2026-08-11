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
                asset.MarkChance = FP._0_50;
            });

            DirectHitPassiveUpgradeData directHit = CreateOrUpdate<DirectHitPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/DirectHit.asset", asset =>
            {
                asset.DisplayName = "Direct Hit";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Rewards accurate Bunny Bomb placement - enemies inside the inner blast zone take bonus damage. At rank 3, they're also knocked back hard.";
                asset.RankDescriptions = new[]
                {
                    "Enemies inside the Direct Hit area take +30% explosion damage.",
                    "Enemies inside the Direct Hit area take +50% explosion damage.",
                    "Enemies inside the Direct Hit area take +75% explosion damage and receive strong knockback.",
                };
                asset.MaxRank = 3;
                asset.InnerRadiusFraction = FP.FromString("0.35");
                asset.DamageMultiplierBonus = new[] { FP.FromString("0.30"), FP.FromString("0.50"), FP.FromString("0.75") };
                asset.KnockbackForce = 8;
                asset.KnockbackUpwardForce = 2;
                asset.KnockbackEliteMultiplier = FP.FromString("0.4");
            });

            PocketBombsPassiveUpgradeData pocketBombs = CreateOrUpdate<PocketBombsPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/PocketBombs.asset", asset =>
            {
                asset.DisplayName = "Pocket Bombs";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Qualifying Pixie explosions have a chance to drop a stationary Mini Bomb, dealing a percentage of Bunny Bomb damage.";
                asset.RankDescriptions = new[]
                {
                    "Qualifying explosions have a 15% chance to spawn a stationary Mini Bomb dealing 35% of Bunny Bomb damage.",
                    "Qualifying explosions have a 25% chance to spawn a stationary Mini Bomb dealing 45% of Bunny Bomb damage.",
                    "Qualifying explosions have a 35% chance to spawn a stationary Mini Bomb dealing 55% of Bunny Bomb damage.",
                };
                asset.MaxRank = 3;
                asset.Chance = new[] { FP.FromString("0.15"), FP.FromString("0.25"), FP.FromString("0.35") };
                asset.DamagePercent = new[] { FP.FromString("0.35"), FP.FromString("0.45"), FP.FromString("0.55") };
                asset.Fuse = FP.FromString("0.4");
            });

            UnstableMixturePassiveUpgradeData unstableMixture = CreateOrUpdate<UnstableMixturePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/UnstableMixture.asset", asset =>
            {
                asset.DisplayName = "Unstable Mixture";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Marked-enemy death explosions deal more damage and cover more area. Specialist and Heavy enemies create especially large death explosions.";
                asset.RankDescriptions = new[]
                {
                    "Marked-enemy death explosions gain +30% damage and +15% radius. Specialist and Heavy enemies create death explosions with an additional +50% radius.",
                    "Marked-enemy death explosions gain +60% damage and +30% radius. Specialist and Heavy enemies create death explosions with an additional +50% radius.",
                    "Marked-enemy death explosions gain +90% damage and +40% radius. Specialist and Heavy enemies create death explosions with an additional +50% radius.",
                };
                asset.MaxRank = 3;
                asset.BonusDamageMultiplier = new[] { FP.FromString("1.30"), FP.FromString("1.60"), FP.FromString("1.90") };
                asset.BonusRadiusMultiplier = new[] { FP.FromString("1.15"), FP.FromString("1.30"), FP.FromString("1.40") };
                asset.TierRadiusMultiplier = FP.FromString("1.5");
            });

            UnstableTargetingPassiveUpgradeData unstableTargeting = CreateOrUpdate<UnstableTargetingPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/UnstableTargeting.asset", asset =>
            {
                asset.DisplayName = "Unstable Targeting";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Deal bonus damage against any enemy currently marked to explode on death.";
                asset.RankDescriptions = new[]
                {
                    "Deal +20% damage against enemies marked to explode on death.",
                    "Deal +35% damage against enemies marked to explode on death.",
                    "Deal +50% damage against enemies marked to explode on death.",
                };
                asset.MaxRank = 3;
                asset.DamageMultiplier = new[] { FP.FromString("1.20"), FP.FromString("1.35"), FP.FromString("1.50") };
            });

            ExplosiveRoundsPassiveUpgradeData explosiveRounds = CreateOrUpdate<ExplosiveRoundsPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/ExplosiveRounds.asset", asset =>
            {
                asset.DisplayName = "Explosive Rounds";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Weapon hits also create a small explosion - a full qualifying Pixie explosion in its own right.";
                asset.RankDescriptions = new[]
                {
                    "Weapon hits create a small explosion dealing 20% of the triggering weapon hit's damage.",
                    "Weapon hits create a small explosion dealing 30% of the triggering weapon hit's damage.",
                    "Weapon hits create a small explosion dealing 40% of the triggering weapon hit's damage.",
                };
                asset.MaxRank = 3;
                asset.Radius = new[] { FP._2, FP.FromString("2.4"), FP.FromString("2.4") };
                asset.DamageMultiplier = new[] { FP.FromString("0.20"), FP.FromString("0.30"), FP.FromString("0.40") };
            });

            ClusterBombSkillAction clusterBomb = CreateOrUpdate<ClusterBombSkillAction>($"{HeroSkillUpgradesFolderPath}/ClusterBombSkillAction.asset", asset =>
            {
                asset.DisplayName = "Cluster Bomb";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Activated = false;
                asset.MaxRank = 3;
                // Static fallback for surfaces that call the plain, rank-unaware GetDescription() -
                // e.g. UpgradePopupWidget's Tab-hold history list. GetDescription(int rank) (below,
                // built from the per-rank arrays) is what every rank-aware surface actually shows.
                asset.Description = "Bunny Bomb explosions scatter bomblets, each dealing a percentage of Bunny Bomb damage.";
                asset.RankDescriptions = new[]
                {
                    "After Bunny Bomb explodes, spawn 2 Mini Bombs, each dealing 40% of Bunny Bomb damage.",
                    "After Bunny Bomb explodes, spawn 3 Mini Bombs, each dealing 45% of Bunny Bomb damage.",
                    "After Bunny Bomb explodes, spawn 4 Mini Bombs, each dealing 50% of Bunny Bomb damage.",
                };
                asset.Count = new byte[] { 2, 3, 4 };
                asset.DamagePercent = new[] { FP.FromString("0.40"), FP.FromString("0.45"), FP.FromString("0.50") };
            });

            BirthdayCakeSkillAction birthdayCake = CreateOrUpdate<BirthdayCakeSkillAction>($"{HeroSkillUpgradesFolderPath}/BirthdayCakeSkillAction.asset", asset =>
            {
                asset.DisplayName = "Birthday Cake";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Activated = false;
                asset.MaxRank = 3;
                asset.Description = "A landed Bunny Bomb taunts nearby enemies before detonating.";
                asset.RankDescriptions = new[]
                {
                    "After landing, Bunny Bomb becomes a decoy, taunting nearby enemies for 1s before detonating.",
                    "After landing, Bunny Bomb becomes a decoy, taunting nearby enemies for 1.5s before detonating with a bigger blast.",
                    "After landing, Bunny Bomb becomes a decoy, taunting nearby enemies for 1.5s before detonating with a bigger blast. Taunted enemies take +30% Bunny Bomb damage.",
                };
                asset.TauntDuration = new[] { FP._1, FP.FromString("1.5"), FP.FromString("1.5") };
                asset.TauntRadiusMultiplier = new[] { FP._1, FP.FromString("1.25"), FP.FromString("1.25") };
                asset.BonusDamageMultiplier = FP.FromString("0.30");
            });

            BackblastSkillAction backblast = CreateOrUpdate<BackblastSkillAction>($"{DashUpgradesFolderPath}/BackblastSkillAction.asset", asset =>
            {
                asset.DisplayName = "Backblast";
                asset.Rarity = UpgradeRarity.Rare;
                asset.MaxRank = 3;
                asset.Description = "When Pixie dashes, she drops a bomb that explodes after a short fuse for a percentage of Bunny Bomb damage.";
                asset.RankDescriptions = new[]
                {
                    "When Pixie dashes, she drops a bomb at the dash starting position - it explodes after a short fuse for 50% of Bunny Bomb damage.",
                    "Drop a bomb at both the start and end of the dash - each explodes after a short fuse for 50% of Bunny Bomb damage.",
                    "Drop a bomb at both the start and end of the dash - each explodes after a short fuse for 75% of Bunny Bomb damage, and enemies hit are marked for Chain Reaction.",
                };
                asset.Fuse = FP._1;
                asset.DamagePercent = new[] { FP.FromString("0.50"), FP.FromString("0.50"), FP.FromString("0.75") };
            });

            HotFuseSkillAction hotFuse = CreateOrUpdate<HotFuseSkillAction>($"{DashUpgradesFolderPath}/HotFuseSkillAction.asset", asset =>
            {
                asset.DisplayName = "Hot Fuse";
                asset.Rarity = UpgradeRarity.Rare;
                asset.MaxRank = 3;
                asset.Description = "Dashing empowers your next Bunny Bomb throw.";
                asset.RankDescriptions = new[]
                {
                    "After dashing, Pixie's next Bunny Bomb within 3s gains +30% damage.",
                    "After dashing, Pixie's next Bunny Bomb within 3s gains +30% damage and +30% explosion radius.",
                    "After dashing, Pixie's next Bunny Bomb within 3s gains +60% damage and +30% explosion radius, and detonates instantly if it directly hits an enemy.",
                };
                asset.Window = 3;
                asset.DamageMultiplier = new[] { FP.FromString("1.30"), FP.FromString("1.30"), FP.FromString("1.60") };
                asset.RadiusMultiplier = new[] { FP._1, FP.FromString("1.30"), FP.FromString("1.30") };
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive,
                new List<PassiveUpgradeData> { directHit, pocketBombs, unstableMixture, unstableTargeting, explosiveRounds },
                new List<SkillActionData> { backblast, hotFuse });

            WireBaseSkill(new List<SkillActionData> { clusterBomb, birthdayCake });

            LogHelper.Log("PixieAscensionAssetGenerator", "Chain Reaction passive + 9 Ascension lines authored and wired (5 Passive Upgrades " +
                      "into PixieCharacterData.PassiveUpgrades, Cluster Bomb/Birthday Cake into PixieBaseSkill.Actions, Backblast/Hot Fuse into " +
                      "PixieCharacterData.DashSkillUpgrades - every list fully replaced, not appended; every per-rank value is re-set explicitly on " +
                      "every run, so a pre-existing asset from before the rank rework - Direct Hit/Unstable Mixture/Explosive Rounds/Backblast - " +
                      "gets its arrays repaired too, not just newly-created ones). Three asset references still need assigning by hand: " +
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

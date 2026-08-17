namespace QuantumUser.Editor
{
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors the Cursed Rift content that a script can actually produce - the 3 initial
    // SacrificeDefinition instances (Blood/Coin/Rift Shard Offering), SacrificePoolData.asset
    // (wired to all 3), and CursedRiftConfig.asset (wired to the pool) - see
    // docs/breathing-poi.md. Mirrors RiftShardAssetGenerator's own folder-creation/update-in-place
    // shape; re-running this is safe, an existing asset at the expected path is updated rather
    // than duplicated. Every number below is a decisive placeholder pending a real balance pass,
    // same convention every other content generator in this codebase already follows.
    //
    // Deliberately does NOT touch SurvivalConfig.Phases[] - Breathing Break timing is now just
    // SurvivalPhase entries with Kind=Breathing interleaved among SurvivalDirectorContentGenerator's
    // own combat phases (see docs/run-phase.md), and that generator fully REPLACES Phases[] each
    // run, so programmatically inserting entries here would silently get wiped out the next time
    // it runs. Authoring which phases are Breathing Breaks is a manual Inspector step (see the
    // logged follow-up list below) - also does NOT touch RuntimeConfig/QuantumMenuConfig,
    // hand-placed HealingShrine/CursedRift EntityPrototypes, or any UI prefab wiring, for the same
    // "no safe way to locate this" reason RiftShardAssetGenerator's own equivalent gap explains.
    public static class BreathingPoiContentGenerator
    {
        private const string SacrificeFolderPath = "Assets/_QuantumUser/Resources/Sacrifice";
        private const string SacrificePoolPath = SacrificeFolderPath + "/SacrificePool.asset";
        private const string BloodOfferingPath = SacrificeFolderPath + "/BloodOffering.asset";
        private const string CoinOfferingPath = SacrificeFolderPath + "/CoinOffering.asset";
        private const string RiftShardOfferingPath = SacrificeFolderPath + "/RiftShardOffering.asset";
        private const string CursedRiftConfigPath = SacrificeFolderPath + "/CursedRiftConfig.asset";

        [MenuItem("Tools/RiftRaiders/Generate Breathing POI Content")]
        internal static void Generate()
        {
            CreateFolderRecursive(SacrificeFolderPath);

            BloodOfferingSacrificeData blood = GenerateBloodOffering();
            CoinOfferingSacrificeData coin = GenerateCoinOffering();
            RiftShardOfferingSacrificeData shard = GenerateRiftShardOffering();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            GenerateSacrificePool(blood, coin, shard);
            GenerateCursedRiftConfig();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LogHelper.Log("BreathingPoiContentGenerator",
                $"3 SacrificeDefinition instances, SacrificePoolData ({SacrificePoolPath}) and CursedRiftConfig ({CursedRiftConfigPath}) authored. " +
                "Still needed by hand: " +
                "(1) on SurvivalConfig.asset (Tools/RiftRaiders/Generate Survival Director Content), interleave a few Phases[] entries " +
                "with Kind=Breathing (e.g. Duration=30) among the existing combat phases - that IS the whole Combat<->Breathing " +
                "timeline now, see docs/run-phase.md; " +
                "(2) assign RuntimeConfig's CursedRiftConfig field (QuantumMenuConfig.asset), same place LevelUpConfig etc. are already assigned; " +
                "(3) hand-place a HealingShrine EntityPrototype (HealingShrine + Interactable components, Interactable.Kind = " +
                "HealingShrine - press-to-heal via the same Base-Skill redirect Cursed Rift uses, not a walk-in auto-heal) and a " +
                "CursedRift EntityPrototype (CursedRift + Interactable components, Interactable.Kind = CursedRift) in the level; " +
                "(4) on GameplayUiController.choiceWindows[0] (the SAME instance the Level-Up screen already uses - Cursed Rift reuses " +
                "it directly, no second window), wire a subtitleText (TMP_Text) - secondaryButton needs no new work, it's the same " +
                "already-wired button Choose-Weapon's Keep Current uses, now also doubling as Cursed Rift's Cancel - and on its own " +
                "cardPrefab wire a valuePreviewText (TMP_Text) and buttonLabelText (TMP_Text, can point at the card's own existing " +
                "baked button label) - none of these 3 fields exist on the scene instance yet; also wire BreathingCountdownWidget on the scene HUD prefab; " +
                "(5) wire SkillCooldownUiWidget's new contextInteractionIcon/interactPromptRoot fields on the HeroSkill-slot instance; " +
                "(6) build out PoiView's Inactive/Active/Expired child visuals on each POI's own View prefab (already wired on " +
                "HealingShrine.prefab, PoiView referenced by CursedShrine.prefab too), and set up InteractionPromptWidgetManager " +
                "on the HUD scene (widgetPrefab/widgetParent + an InteractionPromptWidget prefab under the Canvas) for the world-space prompt; " +
                "(7) author real Icon sprites for the 3 SacrificeDefinition assets (Blood/Coin/RiftShard Offering) - left unassigned here.");
        }

        private static BloodOfferingSacrificeData GenerateBloodOffering()
        {
            var data = LoadOrCreate<BloodOfferingSacrificeData>(BloodOfferingPath, out bool isNew);

            data.DisplayName = "Blood Offering";
            data.TopLabel = "BLOOD";
            data.Description = "Lose 20% of your Max Health.";
            data.ButtonLabel = "SACRIFICE";
            data.Weight = 100;
            data.HealthPercent = FP._0_20;
            data.MinimumMaxHealth = 1;

            FinalizeAsset(data, BloodOfferingPath, isNew);
            return data;
        }

        private static CoinOfferingSacrificeData GenerateCoinOffering()
        {
            var data = LoadOrCreate<CoinOfferingSacrificeData>(CoinOfferingPath, out bool isNew);

            data.DisplayName = "Coin Offering";
            data.TopLabel = "WEALTH";
            data.Description = "Pay 50% of your Coins.";
            data.ButtonLabel = "PAY";
            data.Weight = 100;
            data.CoinPercent = FP._0_50;

            FinalizeAsset(data, CoinOfferingPath, isNew);
            return data;
        }

        private static RiftShardOfferingSacrificeData GenerateRiftShardOffering()
        {
            var data = LoadOrCreate<RiftShardOfferingSacrificeData>(RiftShardOfferingPath, out bool isNew);

            data.DisplayName = "Rift Shard Offering";
            data.TopLabel = "RIFT";
            data.Description = "Offer 3 Rift Shards.";
            data.ButtonLabel = "PAY";
            data.Weight = 100;
            data.ShardCost = 3;

            FinalizeAsset(data, RiftShardOfferingPath, isNew);
            return data;
        }

        private static void GenerateSacrificePool(BloodOfferingSacrificeData blood, CoinOfferingSacrificeData coin, RiftShardOfferingSacrificeData shard)
        {
            var pool = LoadOrCreate<SacrificePoolData>(SacrificePoolPath, out bool isNew);

            pool.Sacrifices = new System.Collections.Generic.List<AssetRef<SacrificeDefinition>>
            {
                new AssetRef<SacrificeDefinition>(blood.Guid),
                new AssetRef<SacrificeDefinition>(coin.Guid),
                new AssetRef<SacrificeDefinition>(shard.Guid)
            };

            FinalizeAsset(pool, SacrificePoolPath, isNew);
        }

        private static void GenerateCursedRiftConfig()
        {
            var pool = AssetDatabase.LoadAssetAtPath<SacrificePoolData>(SacrificePoolPath);
            var config = LoadOrCreate<CursedRiftConfig>(CursedRiftConfigPath, out bool isNew);

            config.SacrificePool = pool != null ? new AssetRef<SacrificePoolData>(pool.Guid) : default;
            config.SacrificeChoiceCount = 3;
            config.MutationChoiceCount = 3;

            FinalizeAsset(config, CursedRiftConfigPath, isNew);
        }

        private static T LoadOrCreate<T>(string path, out bool isNew) where T : AssetObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            isNew = existing == null;
            return isNew ? ScriptableObject.CreateInstance<T>() : existing;
        }

        private static void FinalizeAsset(AssetObject asset, string path, bool isNew)
        {
            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath) == true)
                return;

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

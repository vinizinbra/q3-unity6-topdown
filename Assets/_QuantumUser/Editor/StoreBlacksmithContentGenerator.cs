namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors the Store/Blacksmith content a script can actually produce - 5 initial FoodOfferData
    // instances, FoodOfferPoolData.asset (wired to all 5), StoreConfig.asset, and
    // BlacksmithConfig.asset - see docs/store-blacksmith.md. Mirrors BreathingPoiContentGenerator's
    // own folder-creation/update-in-place shape; re-running this is safe, an existing asset at the
    // expected path is updated rather than duplicated. Every number below is a decisive placeholder
    // pending a real balance pass, same convention every other content generator in this codebase
    // already follows.
    //
    // Deliberately does NOT touch StoreConfig.WeaponPool/BlacksmithConfig.PerkPool (no safe way to
    // locate the right WeaponChoicePoolData/WeaponPerkPoolData assets to point at - same "no safe
    // way to locate this" reasoning BreathingPoiContentGenerator's own gap explains for
    // RuntimeConfig), RuntimeConfig itself, hand-placed Store/Blacksmith EntityPrototypes, the new
    // ChunkType.Blacksmith's own chunk prefab, or any UI prefab wiring (purchase-row fields on the
    // card prefabs, ChooseWindow's two-row Store layout) - all logged as manual follow-up below.
    public static class StoreBlacksmithContentGenerator
    {
        private const string StoreFolderPath = "Assets/_QuantumUser/Resources/Store";
        private const string HealPath = StoreFolderPath + "/FieldRations.asset";
        private const string BurgerPath = StoreFolderPath + "/Burger.asset";
        private const string ShieldPath = StoreFolderPath + "/ShieldCell.asset";
        private const string MoveSpeedPath = StoreFolderPath + "/EnergyDrink.asset";
        private const string DamagePath = StoreFolderPath + "/CombatStims.asset";
        private const string FoodPoolPath = StoreFolderPath + "/FoodOfferPool.asset";
        private const string StoreConfigPath = StoreFolderPath + "/StoreConfig.asset";
        private const string BlacksmithConfigPath = StoreFolderPath + "/BlacksmithConfig.asset";

        // Existing hand-authored icon sheet (multi-sprite) already has an "RR_Burger" sub-sprite -
        // unlike every other FoodOfferData here (left with no Icon, see the log message below),
        // Burger's is wired directly since a matching sprite already exists for exactly this.
        private const string IconSheetPath = "Assets/_Project/Art/Sprites/UI/RiftRaidersIcon1.png";

        [MenuItem("Tools/RiftRaiders/Generate Store & Blacksmith Content")]
        internal static void Generate()
        {
            CreateFolderRecursive(StoreFolderPath);

            HealFoodOfferData heal = GenerateHeal();
            HealFoodOfferData burger = GenerateBurger();
            RestoreShieldFoodOfferData shield = GenerateShield();
            TempMoveSpeedFoodOfferData moveSpeed = GenerateMoveSpeed();
            TempDamageFoodOfferData damage = GenerateDamage();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            GenerateFoodPool(heal, burger, shield, moveSpeed, damage);
            GenerateStoreConfig();
            GenerateBlacksmithConfig();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LogHelper.Log("StoreBlacksmithContentGenerator",
                $"5 FoodOfferData instances, FoodOfferPoolData ({FoodPoolPath}), StoreConfig ({StoreConfigPath}) and BlacksmithConfig " +
                $"({BlacksmithConfigPath}) authored. Still needed by hand: " +
                "(1) assign StoreConfig.WeaponPool and BlacksmithConfig.PerkPool - point both at the same WeaponChoicePoolData/" +
                "WeaponPerkPoolData assets LevelUpConfig already uses (LevelUpConfig.WeaponChoicePool/WeaponPerkPool), or curated ones; " +
                "(2) assign RuntimeConfig's StoreConfig/BlacksmithConfig fields (QuantumMenuConfig.asset), same place LevelUpConfig etc. are already assigned; " +
                "(3) hand-place a Store EntityPrototype (Store + StoreInventory + Interactable components, Interactable.Kind = Store) on " +
                "MarketChunk.prefab (ChunkType.Merchant, already exists, currently unwired) plus a StoreChunkSpawnConfig assigned to its own " +
                "Chunk.SpawnConfig, and a Blacksmith EntityPrototype (Blacksmith + Interactable components, Interactable.Kind = Blacksmith) on a " +
                "new BlacksmithChunk.prefab (ChunkType.Blacksmith) plus its own ChunkSpawnConfig - register both chunks in TestChunkLevel.asset's " +
                "own ChunkPool; " +
                "(4) on UpgradeCardWidget's and WeaponCardWidget's own card prefab, wire the new purchaseRoot/priceText/currencyIcon/" +
                "currencySprites/soldOutOverlay fields (Purchase row) - none exist in the scene yet; " +
                "(5) on ChooseWindow's own prefab, split the food row (cards[]) and weapon row (weaponCards[]) into two visible sections " +
                "(food/utility above, weapons below - currently the same overlapping rect, toggled mutually exclusive) and add a \"CLOSE\" " +
                "label variant for secondaryButton; " +
                "(6) append a Blacksmith sprite to MinimapWidget.chunkTypeSprites[] (positionally indexed, append-only - Merchant's slot " +
                "already exists); " +
                "(7) author real Icon sprites for the other 4 FoodOfferData assets (Field Rations/Shield Cell/Energy Drink/Combat Stims) - " +
                "left unassigned here (Burger's own Icon IS wired, from the existing RiftRaidersIcon1.png sheet's RR_Burger sub-sprite).");
        }

        private static HealFoodOfferData GenerateHeal()
        {
            var data = LoadOrCreate<HealFoodOfferData>(HealPath, out bool isNew);

            data.DisplayName = "Field Rations";
            data.TopLabel = "HEAL";
            data.Description = "Restore 50% of your Max Health.";
            data.ButtonLabel = "BUY";
            data.Weight = 100;
            data.Price = 40;
            data.HealPercent = FP._0_50;

            FinalizeAsset(data, HealPath, isNew);
            return data;
        }

        private static HealFoodOfferData GenerateBurger()
        {
            var data = LoadOrCreate<HealFoodOfferData>(BurgerPath, out bool isNew);

            data.DisplayName = "Burger";
            data.TopLabel = "HEAL";
            data.Description = "Restore 20% of your Max Health.";
            data.ButtonLabel = "BUY";
            data.Weight = 100;
            data.Price = 20;
            data.HealPercent = FP._0_20;
            data.Icon = LoadNamedSprite(IconSheetPath, "RR_Burger");

            FinalizeAsset(data, BurgerPath, isNew);
            return data;
        }

        private static RestoreShieldFoodOfferData GenerateShield()
        {
            var data = LoadOrCreate<RestoreShieldFoodOfferData>(ShieldPath, out bool isNew);

            data.DisplayName = "Shield Cell";
            data.TopLabel = "SHIELD";
            data.Description = "Restore 50% of your Max Shield.";
            data.ButtonLabel = "BUY";
            data.Weight = 100;
            data.Price = 40;
            data.ShieldPercent = FP._0_50;

            FinalizeAsset(data, ShieldPath, isNew);
            return data;
        }

        private static TempMoveSpeedFoodOfferData GenerateMoveSpeed()
        {
            var data = LoadOrCreate<TempMoveSpeedFoodOfferData>(MoveSpeedPath, out bool isNew);

            data.DisplayName = "Energy Drink";
            data.TopLabel = "SPEED";
            data.Description = "+50% Move Speed for 20 seconds.";
            data.ButtonLabel = "BUY";
            data.Weight = 100;
            data.Price = 30;
            data.Duration = 20;
            data.SpeedMultiplier = FP._1_50;

            FinalizeAsset(data, MoveSpeedPath, isNew);
            return data;
        }

        private static TempDamageFoodOfferData GenerateDamage()
        {
            var data = LoadOrCreate<TempDamageFoodOfferData>(DamagePath, out bool isNew);

            data.DisplayName = "Combat Stims";
            data.TopLabel = "DAMAGE";
            data.Description = "+50% Weapon Damage for 20 seconds.";
            data.ButtonLabel = "BUY";
            data.Weight = 100;
            data.Price = 50;
            data.Duration = 20;
            data.DamageBonus = FP._0_50;

            FinalizeAsset(data, DamagePath, isNew);
            return data;
        }

        private static void GenerateFoodPool(HealFoodOfferData heal, HealFoodOfferData burger, RestoreShieldFoodOfferData shield,
            TempMoveSpeedFoodOfferData moveSpeed, TempDamageFoodOfferData damage)
        {
            var pool = LoadOrCreate<FoodOfferPoolData>(FoodPoolPath, out bool isNew);

            pool.Foods = new List<AssetRef<FoodOfferData>>
            {
                new AssetRef<FoodOfferData>(heal.Guid),
                new AssetRef<FoodOfferData>(burger.Guid),
                new AssetRef<FoodOfferData>(shield.Guid),
                new AssetRef<FoodOfferData>(moveSpeed.Guid),
                new AssetRef<FoodOfferData>(damage.Guid)
            };

            FinalizeAsset(pool, FoodPoolPath, isNew);
        }

        private static void GenerateStoreConfig()
        {
            var pool = AssetDatabase.LoadAssetAtPath<FoodOfferPoolData>(FoodPoolPath);
            var config = LoadOrCreate<StoreConfig>(StoreConfigPath, out bool isNew);

            config.FoodPool = pool != null ? new AssetRef<FoodOfferPoolData>(pool.Guid) : default;
            config.FoodOfferCount = 2;
            config.MaxWeaponOfferSlots = 3;
            config.WeaponOfferBasePrice = 100;
            config.WeaponOfferPricePerPerk = 25;

            // Break Index -> Weapon Level / starting perk count - see StoreConfig.
            // ResolveBreakWeaponConfig/docs/store-blacksmith.md's "Break Progression" section.
            config.BreakWeaponConfig = new[]
            {
                new StoreBreakWeaponConfig { WeaponLevel = 0, StartingPerkRolls = new[] { FP._0_20 } },
                new StoreBreakWeaponConfig { WeaponLevel = 1, StartingPerkRolls = new[] { FP.FromString("0.45"), FP._0_20 } },
                new StoreBreakWeaponConfig { WeaponLevel = 2, StartingPerkRolls = new[] { FP.FromString("0.65"), FP.FromString("0.40"), FP._0_20 } },
                new StoreBreakWeaponConfig { WeaponLevel = 3, StartingPerkRolls = new[] { FP.FromString("0.80"), FP.FromString("0.60"), FP.FromString("0.40"), FP._0_20 } },
            };

            // Weapon Talent Level -> starting perk rarity - see StoreConfig.ResolveTalentRarityTuning.
            config.TalentRarityTuning = new[]
            {
                new WeaponTalentRarityTuning { CommonWeight = 90, RareWeight = 10, EpicWeight = 0, LegendaryWeight = 0 },
                new WeaponTalentRarityTuning { CommonWeight = 75, RareWeight = 25, EpicWeight = 0, LegendaryWeight = 0 },
                new WeaponTalentRarityTuning { CommonWeight = 55, RareWeight = 35, EpicWeight = 10, LegendaryWeight = 0 },
                new WeaponTalentRarityTuning { CommonWeight = 35, RareWeight = 45, EpicWeight = 18, LegendaryWeight = 2 },
            };

            FinalizeAsset(config, StoreConfigPath, isNew);
        }

        private static void GenerateBlacksmithConfig()
        {
            var config = LoadOrCreate<BlacksmithConfig>(BlacksmithConfigPath, out bool isNew);

            config.PerkChoiceCount = 3;
            config.CommonPerkPrice = 50;
            config.RarePerkPrice = 100;
            config.EpicPerkPrice = 175;
            config.LegendaryPerkPrice = 300;
            config.BreakTuning = new[]
            {
                new BlacksmithBreakTuning { CommonWeight = 85, RareWeight = 15, EpicWeight = 0, LegendaryWeight = 0 },
                new BlacksmithBreakTuning { CommonWeight = 70, RareWeight = 28, EpicWeight = 2, LegendaryWeight = 0 },
                new BlacksmithBreakTuning { CommonWeight = 50, RareWeight = 45, EpicWeight = 5, LegendaryWeight = 0 },
                new BlacksmithBreakTuning { CommonWeight = 30, RareWeight = 60, EpicWeight = 10, LegendaryWeight = 0 },
            };

            FinalizeAsset(config, BlacksmithConfigPath, isNew);
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

        // Multi-sprite sheets (spriteMode: Multiple, see RiftRaidersIcon1.png's own import
        // settings) expose their sub-sprites only via LoadAllAssetRepresentationsAtPath, not
        // LoadAssetAtPath<Sprite> - matched by name (the sheet's own per-sprite "name" field,
        // e.g. "RR_Burger"), same lookup shape SpriteConfigSO.TryGetSprite uses at runtime.
        private static Sprite LoadNamedSprite(string texturePath, string spriteName)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(texturePath))
            {
                if (asset is Sprite sprite && sprite.name == spriteName)
                    return sprite;
            }

            LogHelper.Warn("StoreBlacksmithContentGenerator", $"No sprite named '{spriteName}' found in {texturePath}");
            return null;
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

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the Exploration (Rift Jumpers v1.0) PermanentUpgradeData assets from the table below and rewires
/// GameDB.PermanentUpgrades: keeps the Power entries, replaces every Exploration one. Idempotent - re-run after
/// editing the table. Obsolete Exploration assets are deleted. The table is the source of truth for the TEXT
/// and cost of each line; what a level does in a match is TalentsConfig + the simulation (docs/talents.md,
/// "Meta progression").
/// </summary>
public static class ExplorationUpgradesGenerator
{
    private const string Folder = "Assets/_Project/Data/PermanentUpgrades/";
    private const string GameDBPath = "Assets/_Project/Resources/GameDB.asset";

    private static readonly string[] ObsoleteAssets =
    {
        "UnlockRift", "RiftEvents", "RiftStones", "Traversal",
        "StartingWeaponChest", "StartingHeroChest", "StartingGlobalChest"
    };

    private struct Line
    {
        public PermanentUpgradeId id;
        public string asset, name, summary, kind, iconFrom;
        public string levelZero; // CURRENT card text at level 0 (empty = the tab's default)
        public int group, baseCost, costStep;

        // Unlock requirement (see PermanentUpgradeData): another line at a level, and/or N Exploration levels owned.
        public PermanentUpgradeId requires;
        public int requiresLevel, requiresExplorationLevels;
        public Color accent;
        public string[] levels; // "Title|Description" per level
    }

    private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

    // One sprite per line, named Upgrade_<asset> (sliced from this atlas). Accents above are each icon's main colour.
    private const string IconAtlas = "Assets/_Project/Art/Icons/ExplorationUpgradesAtlas_11.png";

    private static readonly Color Equipment = C(0.992f, 0.769f, 0.008f);
    private static readonly Color World = C(0f, 0.882f, 0.992f);
    private static readonly Color Economy = C(0.992f, 0.38f, 0.004f);
    private static readonly Color Survival = C(0.933f, 0.145f, 0.988f);

    private static Line[] Lines() => new[]
    {
        new Line { id = PermanentUpgradeId.Store, asset = "Store", name = "Store", group = 0, kind = "EQUIPMENT UPGRADE", accent = C(0.992f, 0.765f, 0.02f), iconFrom = "Store",
            summary = "A fixed Store per run selling weapons, healing consumables and defensive accessories, bought with Gold.",
            levels = new[]
            {
                "Unlock Store|Unlocks the Store on the map, letting you buy weapons, healing and accessories.",
                "Store Restock|The Store restocks every break, offering new things to buy.",
                "Expanded Arsenal|Adds one extra weapon to the options available in the Store."
            } },
        new Line { id = PermanentUpgradeId.Gunsmith, requires = PermanentUpgradeId.Store, requiresLevel = 1, asset = "Gunsmith", name = "Gunsmith", group = 0, kind = "EQUIPMENT UPGRADE", accent = C(0f, 0.914f, 0.992f), iconFrom = "Gunsmith",
            summary = "One Gunsmith per run. Buy 1 Weapon Perk out of 3 options, applied to the weapon you choose.",
            levels = new[]
            {
                "Unlock Gunsmith|Unlocks the Gunsmith and lets you buy a Weapon Perk during the run.",
                "Gunsmith Restock|Offers renew every break and allow a new perk purchase.",
                "Master Craftsman|Higher-rarity perks appear more often at the Gunsmith.",
                "Gunsmith Reroll|Reroll the 3 perk options once per break, without increasing the purchases allowed."
            } },
        new Line { id = PermanentUpgradeId.Weapons, requires = PermanentUpgradeId.Store, requiresLevel = 1, asset = "Weapons", name = "Weapons", group = 0, kind = "EQUIPMENT UPGRADE", accent = C(0.984f, 0.067f, 0.616f), iconFrom = "StartingWeaponChest",
            summary = "Every weapon you equip during a run is stronger, and new weapons roll better perks.",
            levels = new[]
            {
                "Weapon Level +1|Every weapon you equip this run is 1 level higher, starting weapon included. New weapons roll slightly rarer perks.",
                "Weapon Level +2|Every weapon you equip this run is 2 levels higher. New weapons roll rarer perks.",
                "Weapon Level +3|Every weapon you equip this run is 3 levels higher. New weapons roll rarer perks.",
                "Weapon Level +4|Every weapon you equip this run is 4 levels higher. New weapons roll much rarer perks.",
                "Weapon Level +5|Every weapon you equip this run is 5 levels higher. New weapons roll the rarest perks."
            } },

        new Line { id = PermanentUpgradeId.Challenges, requiresExplorationLevels = 3, asset = "Challenges", name = "Challenges", group = 1, kind = "EXPLORATION UPGRADE", accent = C(1f, 0.416f, 0f), iconFrom = "Challenges",
            summary = "Unlocks optional activities on the map that reward exploration, execution and combat.",
            levels = new[]
            {
                "Traversal Challenge|Unlocks movement and platform challenges that reward you on completion.",
                "Team Challenge|Unlocks combat challenges where the team fights enemies to earn a reward."
            } },
        new Line { id = PermanentUpgradeId.HealingShrine, requiresExplorationLevels = 3, asset = "HealingShrine", name = "Healing Shrine", group = 1, kind = "EXPLORATION UPGRADE", accent = C(0.271f, 0.925f, 0.216f), iconFrom = "Traversal",
            summary = "A healing point found while exploring. Usable again after a cooldown.",
            levels = new[]
            {
                "Unlock Healing Shrine|Unlocks the Healing Shrine, restoring 25% of max HP per use.",
                "Improved Healing|Shrine healing rises from 25% to 35% of max HP.",
                "Faster Recovery|Shrine cooldown is 25% shorter, so it can be used more often."
            } },
        new Line { id = PermanentUpgradeId.SacrificeRift, requiresExplorationLevels = 5, asset = "SacrificeRift", name = "Sacrifice Rift", group = 1, kind = "EXPLORATION UPGRADE", accent = C(0.388f, 0.008f, 0.973f), iconFrom = "UnlockRift",
            summary = "A risk-and-reward POI: accept a sacrifice in exchange for a benefit.",
            levels = new[]
            {
                "Unlock Sacrifice Rift|Unlocks the option to accept a sacrifice and receive a reward.",
                "Sacrifice Choice|Shows 2 sacrifice options instead of 1, so you can pick the one that suits your build."
            } },
        new Line { id = PermanentUpgradeId.RiftAnchor, asset = "RiftAnchor", name = "Rift Anchor / Base", group = 1, kind = "EXPLORATION UPGRADE", accent = C(0.012f, 0.906f, 0.988f), iconFrom = "StartingGlobalChest",
            summary = "Your landing spot at the start of every run. Upgrade it to find gear waiting for you and to fix your Accessory during breaks.",
            levelZero = "Nothing to see here...",
            levels = new[]
            {
                "Starting Weapon Chest|A Weapon Chest waits next to the ship when the run starts. Open it to pick a new weapon before the fight begins.",
                "Recover 1 Accessory|Once per break, interact with the ship to restore 1 Accessory Guard block. The ship glows when it's ready again.",
                "Starter Perk|Every weapon offered by the starting Weapon Chest comes with at least 1 perk."
            } },

        new Line { id = PermanentUpgradeId.StartingGold, asset = "StartingGold", name = "Starting Gold", group = 2, kind = "ECONOMY UPGRADE", accent = C(0.992f, 0.765f, 0.008f), iconFrom = "CoinGain",
            summary = "Start every run with Gold to buy gear earlier, without relying only on combat income.",
            levels = new[]
            {
                "Starting Gold I|Start every run with 100 Gold.",
                "Starting Gold II|Starting Gold rises to 200.",
                "Starting Gold III|Starting Gold rises to 300.",
                "Starting Gold IV|Starting Gold rises to 400.",
                "Starting Gold V|Starting Gold rises to 500."
            } },
        new Line { id = PermanentUpgradeId.Rerolls, asset = "Rerolls", name = "Rerolls", group = 2, kind = "ECONOMY UPGRADE", accent = C(0.6f, 0.027f, 0.984f), iconFrom = "RiftEvents",
            summary = "More chances to find upgrades that fit your build. Independent from the Gunsmith's own reroll.",
            levels = new[]
            {
                "Extra Reroll I|Get 1 additional reroll to refresh progression options during the run.",
                "Extra Reroll II|Rises to 2 additional rerolls per run."
            } },

        new Line { id = PermanentUpgradeId.Revive, requiresExplorationLevels = 3, asset = "Revive", name = "Revive", group = 3, kind = "SURVIVAL UPGRADE", accent = C(0.988f, 0.039f, 0.306f), iconFrom = "MaxHealth",
            summary = "Second chances after a death. Recovery bonuses apply to Self-Revive only.",
            levels = new[]
            {
                "Revive|Grants 1 Self-Revive per run.",
                "Revive Recovery|More HP recovered when you use a Self-Revive.",
                "Revive Guard|Recovers 1 Accessory Guard block after a Self-Revive.",
                "Extra Revive|Grants a second Self-Revive per run."
            } },
        new Line { id = PermanentUpgradeId.Coop, requires = PermanentUpgradeId.Store, requiresLevel = 1, asset = "Coop", name = "Co-op", group = 3, kind = "SURVIVAL UPGRADE", accent = C(0.004f, 0.843f, 0.616f), iconFrom = "DamageReduction",
            summary = "Rescue downed teammates faster and keep fighting together. Ally revives stay unlimited.",
            levels = new[]
            {
                "Fast Rescue|Revive downed allies 20% faster.",
                "Last Stand|Stay Downed 20% longer before being eliminated.",
                "Rescue Recovery|Allies you revive recover 20% additional max HP."
            } },
    };

    [MenuItem("Tools/RiftRaiders/Upgrades/Rebuild Exploration Upgrades")]
    private static void Rebuild()
    {
        Line[] lines = Lines();

        // Each line's icon is its Upgrade_<asset> sprite in the icon atlas; failing that, the old donor asset's icon
        // (read before anything is deleted), and failing that the asset keeps the icon it already has.
        var atlas = new Dictionary<string, Sprite>();
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(IconAtlas))
        {
            if (o is Sprite sprite)
                atlas[sprite.name] = sprite;
        }

        var icons = new Dictionary<string, Sprite>();
        foreach (Line l in lines)
        {
            if (atlas.TryGetValue("Upgrade_" + l.asset, out Sprite own))
            {
                icons[l.asset] = own;
                continue;
            }

            var donor = AssetDatabase.LoadAssetAtPath<PermanentUpgradeData>(Folder + l.iconFrom + ".asset");
            icons[l.asset] = donor != null ? donor.icon : null;
        }

        var built = new List<PermanentUpgradeData>();
        foreach (Line l in lines)
        {
            string path = Folder + l.asset + ".asset";
            var data = AssetDatabase.LoadAssetAtPath<PermanentUpgradeData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<PermanentUpgradeData>();
                AssetDatabase.CreateAsset(data, path);
            }

            data.displayName = l.name;
            data.kindLabel = l.kind;
            data.description = l.summary;
            if (icons[l.asset] != null)
                data.icon = icons[l.asset];
            data.accentColor = l.accent;
            data.category = PermanentUpgradeCategory.Exploration;
            data.group = l.group;
            data.maxLevel = l.levels.Length;
            data.baseCost = l.baseCost > 0 ? l.baseCost : 150;
            data.costStep = l.costStep > 0 ? l.costStep : 50;
            data.id = l.id;
            data.levelZeroText = l.levelZero ?? string.Empty;
            data.requiredUpgrade = l.requires;
            data.requiredUpgradeLevel = l.requiresLevel > 0 ? l.requiresLevel : 1;
            data.requiredCategoryLevels = l.requiresExplorationLevels;
            data.levelInfos = new PermanentUpgradeLevelInfo[l.levels.Length];
            for (int i = 0; i < l.levels.Length; i++)
            {
                string[] parts = l.levels[i].Split('|');
                data.levelInfos[i] = new PermanentUpgradeLevelInfo { title = parts[0], description = parts[1] };
            }

            EditorUtility.SetDirty(data);
            built.Add(data);
        }

        foreach (string obsolete in ObsoleteAssets)
            AssetDatabase.DeleteAsset(Folder + obsolete + ".asset");
        AssetDatabase.SaveAssets();

        Rewire(built);
        Debug.Log("[Upgrades] Exploration upgrades rebuilt: " + built.Count + " lines, GameDB rewired.");
    }

    private static void Rewire(List<PermanentUpgradeData> exploration)
    {
        GameDB db = AssetDatabase.LoadAssetAtPath<GameDB>(GameDBPath);
        if (db == null)
        {
            Debug.LogWarning("[Upgrades] No GameDB at " + GameDBPath + " - create it (RiftRaiders/Game DB) and run again.");
            return;
        }

        var merged = new List<PermanentUpgradeData>();
        foreach (PermanentUpgradeData existing in db.PermanentUpgrades)
        {
            if (existing != null && existing.category == PermanentUpgradeCategory.Power)
                merged.Add(existing);
        }
        merged.AddRange(exploration);

        db.PermanentUpgrades = merged;
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
    }
}

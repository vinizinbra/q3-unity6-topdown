using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the Exploration (Rift Jumpers v1.0) PermanentUpgradeData assets from the table below and rewires
/// UpgradesTab.upgrades in the open scene: keeps the Power entries, replaces every Exploration one.
/// Idempotent - re-run after editing the table. Obsolete Exploration assets are deleted. UI placeholder only:
/// none of these drive gameplay yet (see docs/talents.md, "Exploration upgrade lines").
/// </summary>
public static class ExplorationUpgradesGenerator
{
    private const string Folder = "Assets/_Project/Data/PermanentUpgrades/";

    private static readonly string[] ObsoleteAssets =
    {
        "UnlockRift", "RiftEvents", "RiftStones", "Traversal",
        "StartingWeaponChest", "StartingHeroChest", "StartingGlobalChest"
    };

    private struct Line
    {
        public string asset, name, summary, kind, iconFrom;
        public int group, baseCost, costStep, mockLevel;
        public Color accent;
        public string[] levels; // "Title|Description" per level
    }

    private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

    private static readonly Color Equipment = C(0.992f, 0.769f, 0.008f);
    private static readonly Color World = C(0f, 0.882f, 0.992f);
    private static readonly Color Economy = C(0.992f, 0.38f, 0.004f);
    private static readonly Color Survival = C(0.933f, 0.145f, 0.988f);

    private static Line[] Lines() => new[]
    {
        new Line { asset = "Store", name = "Store", group = 0, kind = "EQUIPMENT UPGRADE", accent = Equipment, iconFrom = "Store", mockLevel = 1,
            summary = "A fixed Store per run selling weapons, healing consumables and defensive accessories, bought with Gold.",
            levels = new[]
            {
                "Unlock Store|Unlocks the Store on the map, letting you buy weapons, healing and accessories.",
                "Store Restock|The Store restocks every break, offering new things to buy.",
                "Expanded Arsenal|Adds one extra weapon to the options available in the Store."
            } },
        new Line { asset = "Gunsmith", name = "Gunsmith", group = 0, kind = "EQUIPMENT UPGRADE", accent = C(0f, 0.882f, 0.992f), iconFrom = "Gunsmith", mockLevel = 2,
            summary = "One Gunsmith per run. Buy 1 Weapon Perk out of 3 options, applied to the weapon you choose.",
            levels = new[]
            {
                "Unlock Gunsmith|Unlocks the Gunsmith and lets you buy a Weapon Perk during the run.",
                "Gunsmith Restock|Offers renew every break and allow a new perk purchase.",
                "Master Craftsman|Higher-rarity perks appear more often at the Gunsmith.",
                "Gunsmith Reroll|Reroll the 3 perk options once per break, without increasing the purchases allowed."
            } },
        new Line { asset = "Weapons", name = "Weapons", group = 0, kind = "EQUIPMENT UPGRADE", accent = C(0.992f, 0.38f, 0.004f), iconFrom = "StartingWeaponChest", mockLevel = 1,
            summary = "Improves weapons found while exploring and the perk quality of weapons from chests.",
            levels = new[]
            {
                "Weapon Level I|Weapons you find get +1 level.",
                "Chest Quality I|Higher chance of rarer perks on weapons from chests.",
                "Weapon Level II|Found-weapon level bonus rises to +2 in total.",
                "Chest Quality II|Higher chance again of rare perks on chest weapons.",
                "Weapon Level III|Found-weapon level bonus rises to +3 in total."
            } },

        new Line { asset = "Challenges", name = "Challenges", group = 1, kind = "EXPLORATION UPGRADE", accent = World, iconFrom = "Challenges",
            summary = "Unlocks optional activities on the map that reward exploration, execution and combat.",
            levels = new[]
            {
                "Traversal Challenge|Unlocks movement and platform challenges that reward you on completion.",
                "Team Challenge|Unlocks combat challenges where the team fights enemies to earn a reward."
            } },
        new Line { asset = "HealingShrine", name = "Healing Shrine", group = 1, kind = "EXPLORATION UPGRADE", accent = C(0.2f, 0.9f, 0.45f), iconFrom = "Traversal",
            summary = "A healing point found while exploring. Usable again after a cooldown.",
            levels = new[]
            {
                "Unlock Healing Shrine|Unlocks the Healing Shrine, restoring 20% of max HP per use.",
                "Improved Healing|Shrine healing rises from 20% to 30% of max HP.",
                "Faster Recovery|Shrine cooldown is 25% shorter, so it can be used more often."
            } },
        new Line { asset = "SacrificeRift", name = "Sacrifice Rift", group = 1, kind = "EXPLORATION UPGRADE", accent = C(0.933f, 0.145f, 0.988f), iconFrom = "UnlockRift",
            summary = "A risk-and-reward POI: accept a sacrifice in exchange for a benefit.",
            levels = new[]
            {
                "Unlock Sacrifice Rift|Unlocks the option to accept a sacrifice and receive a reward.",
                "Sacrifice Choice|Shows 2 sacrifice options instead of 1, so you can pick the one that suits your build."
            } },
        new Line { asset = "RiftAnchor", name = "Rift Anchor / Base", group = 1, kind = "EXPLORATION UPGRADE", accent = C(0.004f, 0.761f, 0.992f), iconFrom = "StartingGlobalChest",
            summary = "The team's preparation point: gear at the start of the run and upkeep services during breaks.",
            levels = new[]
            {
                "Starting Weapon Chest|Adds a Weapon Chest at the Base at the start of every run.",
                "Repair Station|Unlocks a Base station that repairs 1 Accessory Guard block per break on interaction.",
                "Expanded Weapon Chest|The starting chest offers 2 weapons to choose from."
            } },

        new Line { asset = "StartingGold", name = "Starting Gold", group = 2, kind = "ECONOMY UPGRADE", accent = Economy, iconFrom = "CoinGain",
            summary = "Start every run with Gold to buy gear earlier, without relying only on combat income.",
            levels = new[]
            {
                "Starting Gold I|Start every run with 25 extra Gold.",
                "Starting Gold II|Extra starting Gold rises to 50.",
                "Starting Gold III|Extra starting Gold rises to 100."
            } },
        new Line { asset = "Rerolls", name = "Rerolls", group = 2, kind = "ECONOMY UPGRADE", accent = C(0.404f, 0.055f, 0.957f), iconFrom = "RiftEvents",
            summary = "More chances to find upgrades that fit your build. Independent from the Gunsmith's own reroll.",
            levels = new[]
            {
                "Extra Reroll I|Get 1 additional reroll to refresh progression options during the run.",
                "Extra Reroll II|Rises to 2 additional rerolls per run."
            } },

        new Line { asset = "Revive", name = "Revive", group = 3, kind = "SURVIVAL UPGRADE", accent = Survival, iconFrom = "MaxHealth",
            summary = "Second chances after a death. Recovery bonuses apply to Self-Revive only.",
            levels = new[]
            {
                "Revive|Grants 1 Revive through permanent progression.",
                "Revive Recovery|More HP recovered when you use a Self-Revive.",
                "Revive Guard|Recovers 1 Accessory Guard block after a Self-Revive.",
                "Extra Revive|Grants +1 additional Revive per run."
            } },
        new Line { asset = "Coop", name = "Co-op", group = 3, kind = "SURVIVAL UPGRADE", accent = C(0.2f, 0.75f, 0.95f), iconFrom = "DamageReduction",
            summary = "Rescue downed teammates and keep fighting together. Ally revives stay unlimited, but Downed time shrinks with each one.",
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

        // Icons are read before anything is deleted: the obsolete assets are the icon donors.
        var icons = new Dictionary<string, Sprite>();
        foreach (Line l in lines)
        {
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
            data.icon = icons[l.asset];
            data.accentColor = l.accent;
            data.category = PermanentUpgradeCategory.Exploration;
            data.group = l.group;
            data.maxLevel = l.levels.Length;
            data.baseCost = l.baseCost > 0 ? l.baseCost : 150;
            data.costStep = l.costStep > 0 ? l.costStep : 50;
            data.talentKey = string.Empty;
            data.mockLevel = Mathf.Min(l.mockLevel, l.levels.Length);
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
        Debug.Log("[Upgrades] Exploration upgrades rebuilt: " + built.Count + " lines. Save the scene to keep the list.");
    }

    private static void Rewire(List<PermanentUpgradeData> exploration)
    {
        UpgradesTab tab = Object.FindFirstObjectByType<UpgradesTab>(FindObjectsInactive.Include);
        if (tab == null)
        {
            Debug.LogWarning("[Upgrades] No UpgradesTab in the open scenes - open MenuScene and run again to rewire the list.");
            return;
        }

        var so = new SerializedObject(tab);
        SerializedProperty list = so.FindProperty("upgrades");

        var merged = new List<PermanentUpgradeData>();
        for (int i = 0; i < list.arraySize; i++)
        {
            var existing = list.GetArrayElementAtIndex(i).objectReferenceValue as PermanentUpgradeData;
            if (existing != null && existing.category == PermanentUpgradeCategory.Power)
                merged.Add(existing);
        }
        merged.AddRange(exploration);

        list.arraySize = merged.Count;
        for (int i = 0; i < merged.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = merged[i];

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(tab.gameObject.scene);
    }
}

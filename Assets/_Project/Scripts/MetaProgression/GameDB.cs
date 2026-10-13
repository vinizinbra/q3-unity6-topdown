using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unity-side registry of game data the menu/view code needs to look up without a scene reference
/// (Resources/GameDB, same Instance pattern as WorldCatalog / HapticsConfig). Simulation tuning does NOT
/// belong here - Quantum can't read a ScriptableObject; that lives on RuntimeConfig's AssetRefs.
/// </summary>
[CreateAssetMenu(fileName = "GameDB", menuName = "RiftRaiders/Game DB")]
public class GameDB : ScriptableObject
{
    private const string ResourcePath = "GameDB";

    [Tooltip("Every menu Upgrades tab line, in list order (Power and Exploration mixed; the tab splits by category).")]
    public List<PermanentUpgradeData> PermanentUpgrades = new List<PermanentUpgradeData>();

    [Tooltip("Starting levels for balancing playtests - only applied while the META_PLAYTEST define is on.")]
    public MetaProgressionPlaytestConfig PlaytestConfig;

    private static GameDB instance;

    public static GameDB Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<GameDB>(ResourcePath);
                if (instance == null)
                    instance = CreateInstance<GameDB>();
            }

            return instance;
        }
    }

    public PermanentUpgradeData FindUpgrade(PermanentUpgradeId id)
    {
        foreach (PermanentUpgradeData data in PermanentUpgrades)
        {
            if (data != null && data.id == id)
                return data;
        }

        return null;
    }
}

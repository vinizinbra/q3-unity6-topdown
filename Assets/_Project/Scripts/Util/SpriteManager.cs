using System.Collections.Generic;
using QuantumUser.View.Util;
using UnityEngine;

// Aggregates every registered SpriteConfigSO (Currency, and whatever categories follow it) behind
// one name-based lookup, so call sites just do SpriteManager.GetSprite("Coin") without knowing or
// caring which config a given sprite actually lives in.
[DefaultExecutionOrder(-1000)]
public class SpriteManager : MonoBehaviour
{
    public static SpriteManager Instance;

    [SerializeField] private List<SpriteConfigSO> configs = new();

    private readonly Dictionary<string, Sprite> spritesByName = new();

    private void Awake()
    {
        Instance = this;

        foreach (var config in configs)
        {
            if (config == null)
                continue;

            foreach (var entry in config.sprites)
            {
                if (string.IsNullOrEmpty(entry.name))
                    continue;

                spritesByName.TryAdd(entry.name, entry.sprite);
            }
        }
    }

    public static Sprite GetSprite(string name)
    {
        if (Instance == null)
        {
            LogHelper.Warn("SpriteManager", $"GetSprite('{name}') called before SpriteManager.Instance was set - is a SpriteManager in the scene?");
            return null;
        }

        if (Instance.spritesByName.TryGetValue(name, out Sprite sprite))
            return sprite;

        LogHelper.Warn("SpriteManager", $"GetSprite('{name}') - no matching entry in any configured SpriteConfigSO.");
        return null;
    }
}

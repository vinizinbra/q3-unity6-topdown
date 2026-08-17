using System;
using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

// Base for a named sprite lookup table, authored per-category (see SpriteConfigCurrency) and
// registered on SpriteManager. Kept abstract - always author a category-specific subclass rather
// than a bare SpriteConfigSO, so a designer picking "Create > RiftRaiders > Sprites > ..." sees
// what each config is actually for.
public abstract class SpriteConfigSO : ScriptableObject
{
    [Serializable]
    public struct SpriteEntry
    {
        public string name;
        [ShowAssetPreview] public Sprite sprite;
    }

    public List<SpriteEntry> sprites = new();

    public bool TryGetSprite(string spriteName, out Sprite sprite)
    {
        foreach (var entry in sprites)
        {
            if (entry.name == spriteName)
            {
                sprite = entry.sprite;
                return true;
            }
        }

        sprite = null;
        return false;
    }

    private void OnValidate()
    {
        var seen = new HashSet<string>();
        foreach (var entry in sprites)
        {
            if (string.IsNullOrEmpty(entry.name))
                continue;

            if (seen.Add(entry.name) == false)
                LogHelper.Warn("SpriteConfig", $"{name} has more than one entry named '{entry.name}' - GetSprite will only ever return the first.", this);
        }
    }
}

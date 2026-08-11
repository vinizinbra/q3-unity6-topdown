using System;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

[CreateAssetMenu(fileName = "CharacterCatalog", menuName = "RiftRaiders/Character Catalog")]
public class CharacterCatalog : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string id;
        public string displayName;
        public AssetRef<EntityPrototype> avatar;
    }

    public Entry[] characters;

    public AssetRef<EntityPrototype> Resolve(string id)
    {
        if (!string.IsNullOrEmpty(id))
        {
            foreach (var entry in characters)
            {
                if (entry.id == id)
                    return entry.avatar;
            }

            LogHelper.Warn("CharacterSelect", $"CharacterCatalog.Resolve('{id}') - no matching entry, falling back to characters[0] ({(characters.Length > 0 ? characters[0].id : "none")})");
        }
        else
        {
            LogHelper.Warn("CharacterSelect", $"CharacterCatalog.Resolve - id was null/empty, falling back to characters[0] ({(characters.Length > 0 ? characters[0].id : "none")})");
        }

        return characters.Length > 0 ? characters[0].avatar : default;
    }

    public bool TryGetDisplayName(string id, out string displayName)
    {
        foreach (var entry in characters)
        {
            if (entry.id == id)
            {
                displayName = entry.displayName;
                return true;
            }
        }

        displayName = null;
        return false;
    }
}

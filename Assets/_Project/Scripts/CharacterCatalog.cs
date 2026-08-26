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

        [Tooltip("The hero's own gameplay prefab (Kai.prefab, Zara.prefab...), shown as a live animated preview in the menu by CharacterPreviewWidget. Deliberately a separate field from Avatar above rather than derived from it: Avatar is an AssetRef<EntityPrototype> resolved through Quantum's asset DB, which has no way back to the source GameObject at runtime. Leave unassigned to show no preview for this character.")]
        public GameObject viewPrefab;
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

    public GameObject ResolveViewPrefab(string id)
    {
        foreach (var entry in characters)
        {
            if (entry.id == id)
                return entry.viewPrefab;
        }

        return null;
    }

    // The hero's icon, read straight off its own view prefab's rig rather than a separately authored
    // portrait asset - the same trick PlayerPortraitUiWidget uses in-game, so a hero whose art
    // changes updates everywhere at once instead of leaving a stale second copy behind. Read off the
    // PREFAB, so no instance has to exist for a party slot to show a teammate's face.
    //
    // Prefers the head, then falls back to the first sprite on the rig as a whole. Not every hero is
    // rigged with a separate head - Lux is drawn as one piece - and for those the whole body IS the
    // portrait. The fallback only ever applies to a hero with no head to find, so it can't change
    // what the others show.
    public Sprite ResolveIconSprite(string id)
    {
        GameObject prefab = ResolveViewPrefab(id);
        if (prefab == null)
            return null;

        var rig = prefab.GetComponentInChildren<BlobAnimationView>(true);
        if (rig == null)
            return null;

        Sprite head = FirstSprite(rig.Head);
        return head != null ? head : FirstSprite(rig.Root);
    }

    private static Sprite FirstSprite(Transform root)
    {
        if (root == null)
            return null;

        var renderer = root.GetComponentInChildren<SpriteRenderer>(true);
        return renderer != null ? renderer.sprite : null;
    }

    // The hero's signature colour - the same CharacterData.RingColor that tints their ground ring
    // in-game (MovementRingView), so a party slot's background matches the marker that player will
    // actually be identified by during the match.
    //
    // It lives on CharacterData rather than on the catalog entry deliberately: duplicating it here
    // would give a hero two colours to keep in sync by hand. Reached through the view prefab's own
    // CharacterStats prototype and resolved out of Quantum's global asset DB, which is available
    // without a running simulation (the same way QuantumMenu reads SimulationConfig/Map).
    public bool TryResolveRingColor(string id, out Color color)
    {
        color = Color.white;

        GameObject prefab = ResolveViewPrefab(id);
        if (prefab == null)
            return false;

        var stats = prefab.GetComponent<QPrototypeCharacterStats>();
        if (stats == null)
            return false;

        if (QuantumUnityDB.TryGetGlobalAsset(stats.Prototype.CharacterData, out CharacterData data) == false || data == null)
            return false;

        color = data.RingColor;
        return true;
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

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// Right-click (Project window) shortcuts on one or more Texture2D assets to add them straight into
/// the UI or Gameplay Sprite Atlas, without going through <see cref="SpriteAtlasScannerWindow"/>.
/// The Build Size Analyzer's "→ UI Atlas" / "→ Game Atlas" buttons reuse <see cref="AddTexturesToAtlas"/>.
/// </summary>
public static class AddTextureToAtlasContextMenu
{
    internal const string AtlasFolder = "Assets/_Project/Art/SpriteAtlases";
    internal const string UiAtlasPath = AtlasFolder + "/UISprites.spriteatlas";
    internal const string GameplayAtlasPath = AtlasFolder + "/GameplaySprites.spriteatlas";

    [MenuItem("Assets/Add to UI Atlas", false, 20)]
    private static void AddToUiAtlas() => AddSelectedTextures(UiAtlasPath);

    [MenuItem("Assets/Add to UI Atlas", true)]
    private static bool ValidateAddToUiAtlas() => HasSelectedTextures();

    [MenuItem("Assets/Add to Gameplay Atlas", false, 21)]
    private static void AddToGameplayAtlas() => AddSelectedTextures(GameplayAtlasPath);

    [MenuItem("Assets/Add to Gameplay Atlas", true)]
    private static bool ValidateAddToGameplayAtlas() => HasSelectedTextures();

    private static bool HasSelectedTextures() => Selection.GetFiltered<Texture2D>(SelectionMode.Assets).Length > 0;

    private static void AddSelectedTextures(string atlasPath)
    {
        Texture2D[] textures = Selection.GetFiltered<Texture2D>(SelectionMode.Assets);
        if (textures.Length == 0)
            return;

        AddTexturesToAtlas(atlasPath, textures);
    }

    /// <summary>
    /// Adds the given textures to the atlas at <paramref name="atlasPath"/>, skipping ones already
    /// packed there. Returns how many were actually added (-1 if the atlas does not exist).
    /// </summary>
    internal static int AddTexturesToAtlas(string atlasPath, IEnumerable<Texture2D> textures)
    {
        var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
        if (atlas == null)
        {
            Debug.LogError($"[AddTextureToAtlasContextMenu] No Sprite Atlas found at '{atlasPath}'.");
            return -1;
        }

        var existing = new HashSet<Object>(atlas.GetPackables());
        Object[] candidates = textures.Where(t => t != null).Cast<Object>().Distinct().ToArray();
        Object[] alreadyPacked = candidates.Where(existing.Contains).ToArray();
        Object[] texturesToAdd = candidates.Except(alreadyPacked).ToArray();

        if (alreadyPacked.Length > 0)
        {
            Debug.Log($"[AddTextureToAtlasContextMenu] Already in '{atlas.name}', skipped: " +
                      string.Join(", ", alreadyPacked.Select(o => o.name)));
        }

        if (texturesToAdd.Length == 0)
            return 0;

        atlas.Add(texturesToAdd);
        EditorUtility.SetDirty(atlas);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AddTextureToAtlasContextMenu] Added {texturesToAdd.Length} texture(s) to '{atlas.name}'.");
        return texturesToAdd.Length;
    }
}

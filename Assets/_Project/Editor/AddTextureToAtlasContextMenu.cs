using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// Right-click (Project window) shortcuts on one or more Texture2D assets to add them straight into
/// the UI or Gameplay Sprite Atlas, without going through <see cref="SpriteAtlasScannerWindow"/>.
/// </summary>
public static class AddTextureToAtlasContextMenu
{
    private const string AtlasFolder = "Assets/_Project/Art/SpriteAtlases";
    private const string UiAtlasPath = AtlasFolder + "/UISprites.spriteatlas";
    private const string GameplayAtlasPath = AtlasFolder + "/GameplaySprites.spriteatlas";

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

        var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
        if (atlas == null)
        {
            Debug.LogError($"[AddTextureToAtlasContextMenu] No Sprite Atlas found at '{atlasPath}'.");
            return;
        }

        var existing = new System.Collections.Generic.HashSet<Object>(atlas.GetPackables());
        Object[] texturesToAdd = textures
            .Cast<Object>()
            .Where(t => !existing.Contains(t))
            .ToArray();

        if (texturesToAdd.Length == 0)
            return;

        atlas.Add(texturesToAdd);
        EditorUtility.SetDirty(atlas);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AddTextureToAtlasContextMenu] Added {texturesToAdd.Length} texture(s) to '{atlas.name}'.");
    }
}

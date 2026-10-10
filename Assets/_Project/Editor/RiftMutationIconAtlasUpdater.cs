using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class RiftMutationIconAtlasUpdater
{
    private const string AtlasPath = "Assets/_Project/Art/Icons/RiftMutation/RiftMutationUpgradeAtlas.png";
    private const string AssetFolder = "Assets/_QuantumUser/Resources/LevelUp/RiftMutation/";
    private const int Columns = 7;
    private const int Rows = 4;

    // Row-major, top-left to bottom-right - same order as the painted atlas. Each name is also the .asset file name.
    private static readonly string[] Names =
    {
        "AdrenalineKick", "BloodMoney", "BloodTithe", "BossDestroyer", "BroMutation", "BulletStorm", "CloseQuarters",
        "CriticalFocus", "DangerPay", "DeadWeight", "EliteTerritory", "Escalation", "Executioner", "FocusedPower",
        "GlassCore", "Greed", "HeavyArsenal", "InfiniteMomentum", "LastBastion", "Longshot", "MoneyTalks",
        "NoSafetyNet", "OneInTheChamber", "Overkill", "Overpopulation", "PressureCooker", "ScavengerRush", "SecondWind",
    };

    [MenuItem("Tools/RiftRaiders/Rift Mutations/Update Icons From Atlas")]
    public static void UpdateIcons()
    {
        var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
        if (importer == null) { Debug.LogError("Missing Rift Mutation icon atlas: " + AtlasPath); return; }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.spritePixelsPerUnit = 100;

        // The imported Texture2D can be NPOT-scaled; slice against the real source pixels instead.
        importer.GetSourceTextureWidthAndHeight(out int texWidth, out int texHeight);
        var texture = new { width = texWidth, height = texHeight };
        var sprites = new SpriteMetaData[Names.Length];
        for (int i = 0; i < sprites.Length; i++)
        {
            int x = i % Columns, y = i / Columns;
            int x0 = Mathf.RoundToInt(x * texture.width / (float)Columns);
            int x1 = Mathf.RoundToInt((x + 1) * texture.width / (float)Columns);
            int y0 = Mathf.RoundToInt(y * texture.height / (float)Rows);
            int y1 = Mathf.RoundToInt((y + 1) * texture.height / (float)Rows);
            sprites[i] = new SpriteMetaData
            {
                name = "RiftMutation_" + Names[i],
                rect = new Rect(x0, texture.height - y1, x1 - x0, y1 - y0),
                pivot = new Vector2(.5f, .5f)
            };
        }
        importer.spritesheet = sprites;
        importer.SaveAndReimport();

        var atlasSprites = new Dictionary<string, Sprite>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AtlasPath))
            if (asset is Sprite sprite) atlasSprites[sprite.name] = sprite;

        int updated = 0;
        foreach (var name in Names)
        {
            string path = AssetFolder + name + ".asset";
            var data = AssetDatabase.LoadMainAssetAtPath(path);
            if (data == null || !atlasSprites.TryGetValue("RiftMutation_" + name, out var icon))
            {
                Debug.LogWarning("Could not update " + path);
                continue;
            }
            var serialized = new SerializedObject(data);
            var prop = serialized.FindProperty("Icon");
            if (prop == null) { Debug.LogWarning("No Icon field on " + path); continue; }
            prop.objectReferenceValue = icon;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            updated++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Updated " + updated + "/" + Names.Length + " Rift Mutation icons from the atlas.");
    }
}

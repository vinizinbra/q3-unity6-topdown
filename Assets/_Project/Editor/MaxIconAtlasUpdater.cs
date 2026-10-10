using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class MaxIconAtlasUpdater
{
    private const string AtlasPath = "Assets/_Project/Art/Icons/Max/MaxUpgradeAtlas.png";

    private static readonly string[] Paths =
    {
        "Assets/_QuantumUser/Resources/Skills/Max/Max_PassiveSkill/VendettaPassiveData.asset",
        "Assets/_QuantumUser/Resources/Skills/Max/Max_HeroSkill/MaxHeroSkill.asset",
        "Assets/_QuantumUser/Resources/Skills/Max/Max_HeroSkill/Max_HeroSkillUpgrades/Overdrive/LastStandSkillAction.asset",
        "Assets/_QuantumUser/Resources/Skills/Max/Max_HeroSkill/Max_HeroSkillUpgrades/Overdrive/FullThrottleSkillAction.asset",
        "Assets/_QuantumUser/Resources/Skills/Max/Max_HeroSkill/Max_HeroSkillUpgrades/Overdrive/UncontrolledFurySkillAction.asset",
        "Assets/_QuantumUser/Resources/Skills/Max/Max_HeroSkill/Max_HeroSkillUpgrades/Overdrive/IgnitionSkillAction.asset",
        "Assets/_QuantumUser/Resources/Skills/Max/Max_PassiveSkill/Max_PassiveSkillUpgrades/Vendetta/BloodDebt.asset",
        "Assets/_QuantumUser/Resources/Skills/Max/Max_PassiveSkill/Max_PassiveSkillUpgrades/FireMastery/Wildfire.asset",
        "Assets/_QuantumUser/Resources/Skills/Max/Max_PassiveSkill/Max_PassiveSkillUpgrades/FireMastery/Flashpoint.asset",
        "Assets/_QuantumUser/Resources/Skills/Max/Max_PassiveSkill/Max_PassiveSkillUpgrades/MaxLightMastery.asset",
        "Assets/_QuantumUser/Resources/Skills/Max/Max_PassiveSkill/Max_PassiveSkillUpgrades/MaxFireMastery.asset",
        "Assets/_QuantumUser/Resources/Skills/Max/Max_DashSkillUpgrades/RunAndGunSkillAction.asset",
        "Assets/_QuantumUser/Resources/Skills/Max/Max_DashSkillUpgrades/VendettaStrikeSkillAction.asset",
    };

    [MenuItem("Tools/RiftRaiders/Max/Update Icons From Atlas")]
    public static void UpdateIcons()
    {
        var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
        if (importer == null) { Debug.LogError("Missing Max icon atlas: " + AtlasPath); return; }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.spritePixelsPerUnit = 100;

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        var sprites = new SpriteMetaData[16];
        for (int i = 0; i < sprites.Length; i++)
        {
            int x = i % 4, y = i / 4;
            int x0 = Mathf.RoundToInt(x * texture.width / 4f);
            int x1 = Mathf.RoundToInt((x + 1) * texture.width / 4f);
            int y0 = Mathf.RoundToInt(y * texture.height / 4f);
            int y1 = Mathf.RoundToInt((y + 1) * texture.height / 4f);
            sprites[i] = new SpriteMetaData
            {
                name = "MaxIcon_" + (i + 1),
                rect = new Rect(x0, texture.height - y1, x1 - x0, y1 - y0),
                pivot = new Vector2(.5f, .5f)
            };
        }
        importer.spritesheet = sprites;
        importer.SaveAndReimport();

        var all = AssetDatabase.LoadAllAssetsAtPath(AtlasPath);
        var atlasSprites = new Dictionary<string, Sprite>();
        foreach (var asset in all)
            if (asset is Sprite sprite) atlasSprites[sprite.name] = sprite;
        for (int i = 0; i < Paths.Length; i++)
        {
            var data = AssetDatabase.LoadMainAssetAtPath(Paths[i]);
            if (data == null || !atlasSprites.TryGetValue("MaxIcon_" + (i + 1), out var sprite)) { Debug.LogWarning("Could not update " + Paths[i]); continue; }
            var serialized = new SerializedObject(data);
            var icon = serialized.FindProperty("Icon");
            if (icon == null) { Debug.LogWarning("No Icon field on " + Paths[i]); continue; }
            icon.objectReferenceValue = sprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Updated " + Paths.Length + " Max icons from the 1:1 atlas.");
    }
}

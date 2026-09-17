using UnityEditor;
using UnityEngine;

/// <summary>
/// One-off slicer for Assets/_Project/Art/Sprites/Generic/Icons/RiftRaidersSilhouetteIcons.png.
/// The sheet mixes section headers, divider lines and caption labels around each glyph, which trips up
/// the generic SpriteAutoSlicerWindow detector, so the 102 icon rects here were derived externally
/// (alpha-channel content analysis, verified against the rendered sheet) rather than at Editor time.
/// </summary>
public static class SliceRiftRaidersSilhouetteIconsMenu
{
    private const string TexturePath = "Assets/_Project/Art/Sprites/Generic/Icons/RiftRaidersSilhouetteIcons.png";

    private readonly struct SpriteSlice
    {
        public readonly string Name;
        public readonly Rect Rect;

        public SpriteSlice(string name, int x, int y, int w, int h)
        {
            Name = name;
            Rect = new Rect(x, y, w, h);
        }
    }

    // Pixel rects in Unity texture space (origin bottom-left), tight-cropped to each icon glyph only.
    private static readonly SpriteSlice[] Slices =
    {
        new SpriteSlice("RR_Icon_Play", 39, 915, 49, 58),
        new SpriteSlice("RR_Icon_Heroes", 133, 916, 49, 58),
        new SpriteSlice("RR_Icon_Weapons", 224, 918, 67, 46),
        new SpriteSlice("RR_Icon_Loadout", 333, 916, 51, 56),
        new SpriteSlice("RR_Icon_Upgrades", 428, 917, 54, 60),
        new SpriteSlice("RR_Icon_Catalog", 522, 918, 70, 51),
        new SpriteSlice("RR_Icon_News", 631, 917, 57, 54),
        new SpriteSlice("RR_Icon_Settings", 729, 914, 58, 59),
        new SpriteSlice("RR_Icon_Party", 852, 914, 77, 57),
        new SpriteSlice("RR_Icon_Leader", 967, 914, 46, 63),
        new SpriteSlice("RR_Icon_InviteFriend", 1061, 917, 63, 53),
        new SpriteSlice("RR_Icon_Friend", 1155, 917, 59, 49),
        new SpriteSlice("RR_Icon_Avatar", 1257, 914, 44, 60),
        new SpriteSlice("RR_Icon_Mail", 1348, 920, 62, 44),
        new SpriteSlice("RR_Icon_Chat", 1448, 914, 53, 54),
        new SpriteSlice("RR_Icon_Back", 33, 774, 57, 53),
        new SpriteSlice("RR_Icon_Close", 140, 774, 56, 54),
        new SpriteSlice("RR_Icon_Confirm", 243, 776, 61, 48),
        new SpriteSlice("RR_Icon_Lock", 358, 772, 46, 61),
        new SpriteSlice("RR_Icon_Unlock", 460, 773, 57, 60),
        new SpriteSlice("RR_Icon_Search", 566, 771, 60, 58),
        new SpriteSlice("RR_Icon_Filter", 674, 772, 56, 55),
        new SpriteSlice("RR_Icon_Edit", 777, 770, 61, 62),
        new SpriteSlice("RR_Icon_Delete", 886, 772, 50, 60),
        new SpriteSlice("RR_Icon_Refresh", 991, 772, 57, 58),
        new SpriteSlice("RR_Icon_Copy", 1102, 775, 56, 55),
        new SpriteSlice("RR_Icon_Info", 1212, 774, 54, 56),
        new SpriteSlice("RR_Icon_Warning", 1321, 773, 68, 59),
        new SpriteSlice("RR_Icon_Gift", 1445, 771, 59, 62),
        new SpriteSlice("RR_Icon_InviteParty", 34, 625, 64, 56),
        new SpriteSlice("RR_Icon_Kick", 135, 625, 63, 56),
        new SpriteSlice("RR_Icon_Ready", 234, 625, 63, 56),
        new SpriteSlice("RR_Icon_NotReady", 331, 625, 60, 58),
        new SpriteSlice("RR_Icon_Mic", 435, 623, 41, 60),
        new SpriteSlice("RR_Icon_Mute", 525, 623, 51, 60),
        new SpriteSlice("RR_Icon_RoomCode", 621, 626, 65, 55),
        new SpriteSlice("RR_Icon_Crown", 723, 626, 65, 56),
        new SpriteSlice("RR_Icon_Achievement", 823, 624, 59, 57),
        new SpriteSlice("RR_Icon_Daily", 928, 626, 53, 55),
        new SpriteSlice("RR_Icon_Reward", 1021, 618, 56, 66),
        new SpriteSlice("RR_Icon_Coin", 1142, 622, 56, 55),
        new SpriteSlice("RR_Icon_Premium", 1237, 624, 70, 54),
        new SpriteSlice("RR_Icon_Xp", 1341, 628, 63, 47),
        new SpriteSlice("RR_Icon_LevelUp", 1443, 622, 55, 60),
        new SpriteSlice("RR_Icon_Health", 33, 475, 61, 54),
        new SpriteSlice("RR_Icon_Armor", 136, 475, 53, 54),
        new SpriteSlice("RR_Icon_Damage", 226, 474, 65, 65),
        new SpriteSlice("RR_Icon_Target", 326, 471, 67, 66),
        new SpriteSlice("RR_Icon_Enemy", 435, 475, 55, 57),
        new SpriteSlice("RR_Icon_Boss", 532, 473, 71, 65),
        new SpriteSlice("RR_Icon_EnemiesKilled", 651, 474, 55, 59),
        new SpriteSlice("RR_Icon_Downed", 758, 474, 64, 53),
        new SpriteSlice("RR_Icon_Revived", 861, 474, 90, 58),
        new SpriteSlice("RR_Icon_Cooldown", 994, 474, 53, 65),
        new SpriteSlice("RR_Icon_FireRate", 1110, 475, 50, 57),
        new SpriteSlice("RR_Icon_Reload", 1214, 473, 62, 63),
        new SpriteSlice("RR_Icon_Crit", 1332, 473, 58, 64),
        new SpriteSlice("RR_Icon_Pierce", 1440, 471, 58, 67),
        new SpriteSlice("RR_Icon_Perk", 34, 323, 57, 57),
        new SpriteSlice("RR_Icon_Mutation", 138, 323, 57, 59),
        new SpriteSlice("RR_Icon_Relic", 253, 321, 41, 67),
        new SpriteSlice("RR_Icon_Skill", 351, 321, 56, 63),
        new SpriteSlice("RR_Icon_Passive", 449, 319, 65, 65),
        new SpriteSlice("RR_Icon_Item", 556, 326, 52, 56),
        new SpriteSlice("RR_Icon_Equipment", 661, 323, 48, 60),
        new SpriteSlice("RR_Icon_Inventory", 775, 323, 60, 60),
        new SpriteSlice("RR_Icon_World", 944, 323, 63, 59),
        new SpriteSlice("RR_Icon_Map", 1057, 327, 64, 50),
        new SpriteSlice("RR_Icon_Biome", 1171, 325, 99, 55),
        new SpriteSlice("RR_Icon_PointOfInterest", 1333, 322, 46, 64),
        new SpriteSlice("RR_Icon_BossPoi", 1440, 324, 56, 63),
        new SpriteSlice("RR_Icon_Fire", 37, 183, 45, 55),
        new SpriteSlice("RR_Icon_Ice", 121, 184, 47, 51),
        new SpriteSlice("RR_Icon_Electric", 203, 183, 45, 58),
        new SpriteSlice("RR_Icon_Void", 287, 184, 52, 52),
        new SpriteSlice("RR_Icon_Poison", 375, 185, 39, 52),
        new SpriteSlice("RR_Icon_Bleed", 455, 185, 36, 51),
        new SpriteSlice("RR_Icon_Light", 523, 182, 60, 57),
        new SpriteSlice("RR_Icon_Dark", 613, 186, 48, 47),
        new SpriteSlice("RR_Icon_Wind", 691, 186, 59, 48),
        new SpriteSlice("RR_Icon_Neutral", 779, 184, 46, 52),
        new SpriteSlice("RR_Icon_WifiOn", 882, 187, 60, 44),
        new SpriteSlice("RR_Icon_WifiOff", 977, 182, 62, 52),
        new SpriteSlice("RR_Icon_Reconnect", 1076, 184, 52, 52),
        new SpriteSlice("RR_Icon_PlayOffline", 1163, 185, 48, 48),
        new SpriteSlice("RR_Icon_Controller", 1254, 186, 66, 47),
        new SpriteSlice("RR_Icon_Keyboard", 1356, 191, 63, 39),
        new SpriteSlice("RR_Icon_Mobile", 1462, 186, 34, 54),
        new SpriteSlice("RR_Icon_SoundOn", 38, 51, 47, 49),
        new SpriteSlice("RR_Icon_SoundOff", 137, 51, 55, 49),
        new SpriteSlice("RR_Icon_MusicOn", 236, 55, 41, 45),
        new SpriteSlice("RR_Icon_MusicOff", 331, 54, 48, 47),
        new SpriteSlice("RR_Icon_Fullscreen", 429, 54, 50, 47),
        new SpriteSlice("RR_Icon_Lamp", 557, 51, 42, 47),
        new SpriteSlice("RR_Icon_Favorite", 646, 52, 52, 49),
        new SpriteSlice("RR_Icon_Stats", 742, 53, 46, 48),
        new SpriteSlice("RR_Icon_CloudSave", 858, 54, 63, 43),
        new SpriteSlice("RR_Icon_Save", 981, 52, 49, 47),
        new SpriteSlice("RR_Icon_Help", 1093, 51, 50, 50),
        new SpriteSlice("RR_Icon_Notification", 1213, 51, 44, 50),
        new SpriteSlice("RR_Icon_Timer", 1338, 51, 39, 50),
        new SpriteSlice("RR_Icon_Exit", 1449, 52, 51, 49),
    };

    [MenuItem("Tools/RiftRaiders/Art/Slice Silhouette Icons")]
    private static void Slice()
    {
        var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[SliceRiftRaidersSilhouetteIconsMenu] Could not find TextureImporter at {TexturePath}");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;

        var metadata = new SpriteMetaData[Slices.Length];
        for (int i = 0; i < Slices.Length; i++)
        {
            metadata[i] = new SpriteMetaData
            {
                name = Slices[i].Name,
                rect = Slices[i].Rect,
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
            };
        }

#pragma warning disable CS0618 // legacy spritesheet API - still the simplest way to script slicing
        importer.spritesheet = metadata;
#pragma warning restore CS0618

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

        Debug.Log($"[SliceRiftRaidersSilhouetteIconsMenu] Sliced {metadata.Length} named sprites from {TexturePath}.");
    }
}

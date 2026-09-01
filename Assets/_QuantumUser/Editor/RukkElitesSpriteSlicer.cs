namespace QuantumUser.Editor
{
    using System.Linq;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// One-off slicer for Assets/0_Refs/RukkElites.png - splits the 5-pose reference sheet into
    /// individual sprites (Rukk_01..Rukk_05, reading order left-to-right, top-to-bottom).
    /// Rects were derived from connected-component detection on the source image (dilated to bridge
    /// dark fur/strap regions close to the pure-black background before measuring each pose's real
    /// tight bounding box).
    /// </summary>
    public static class RukkElitesSpriteSlicer
    {
        private const string TexturePath = "Assets/0_Refs/RukkElites.png";

        private static readonly Rect[] SpriteRects =
        {
            new Rect(14f, 569f, 544f, 512f),   // top-left: melee brute
            new Rect(977f, 569f, 431f, 506f),  // top-right: gunner
            new Rect(16f, 34f, 470f, 471f),    // bottom-left: standing
            new Rect(495f, 25f, 407f, 570f),   // bottom-middle: raised fist
            new Rect(908f, 0f, 490f, 394f),    // bottom-right: crouching
        };

        [MenuItem("Tools/RiftRaiders/Slice Rukk Elites Sprite")]
        public static void Slice()
        {
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"Could not find TextureImporter at {TexturePath}");
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;

            var metadata = SpriteRects.Select((rect, i) => new SpriteMetaData
            {
                name = $"Rukk_{i + 1:00}",
                rect = rect,
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
            }).ToArray();

#pragma warning disable CS0618 // legacy spritesheet API - still the simplest way to script slicing
            importer.spritesheet = metadata;
#pragma warning restore CS0618

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            Debug.Log($"Sliced {metadata.Length} Rukk sprites from {TexturePath}");
        }
    }
}

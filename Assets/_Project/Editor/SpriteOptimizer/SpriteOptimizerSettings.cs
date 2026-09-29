using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

namespace Project.EditorTools.SpriteOptimizer
{
    /// <summary>Which batching domain a sprite (or atlas) belongs to.</summary>
    public enum SpriteContext
    {
        /// <summary>Not decided yet (unbound atlas / data field with no rule).</summary>
        Unknown = 0,
        /// <summary>Drawn by a uGUI Canvas (Image, Selectable sprite states, UI particles...).</summary>
        UI = 1,
        /// <summary>Drawn by a Renderer in the world (SpriteRenderer, ParticleSystem, SpriteMask...).</summary>
        Gameplay = 2,
        /// <summary>Deliberately left alone by the optimizer (e.g. the Intro's own atlas).</summary>
        Ignore = 3,
    }

    [Serializable]
    public sealed class AtlasBinding
    {
        public SpriteAtlas Atlas;
        public SpriteContext Context;
        [Tooltip("New sprites of this context are packed here. One primary per context.")]
        public bool Primary;
    }

    /// <summary>
    /// Context for a sprite field the scanner can't classify on its own: ScriptableObject fields
    /// (<c>UpgradeData.Icon</c>) and custom MonoBehaviour fields (<c>MaxView.berserkHeadSprite</c>).
    /// <see cref="SpriteContext.Unknown"/> means "use the automatic guess".
    /// </summary>
    [Serializable]
    public sealed class FieldRule
    {
        public string Key;
        public SpriteContext Context;
    }

    [Serializable]
    public sealed class TextureOverride
    {
        public Texture2D Texture;
        public SpriteContext Context;
    }

    /// <summary>
    /// Persistent configuration of the Sprite Atlas Optimizer (Tools ▸ RiftRaiders ▸ Optimize).
    /// Editor-only asset; created with sensible defaults on first open.
    /// </summary>
    public sealed class SpriteOptimizerSettings : ScriptableObject
    {
        public const string AssetPath = "Assets/_Project/Editor/SpriteOptimizer/SpriteOptimizerSettings.asset";

        [Header("Atlases")]
        public List<AtlasBinding> Atlases = new List<AtlasBinding>();

        [Header("Scan")]
        [Tooltip("Prefabs / ScriptableObjects / clips under these folders are not scanned as *users* of sprites " +
                 "(their textures can still be packed). Prefabs referenced by scanned scenes/prefabs are always scanned.")]
        public List<string> ExcludedUsageFolders = new List<string>
        {
            "Assets/3rd-party",
            "Assets/Photon",
            "Assets/Plugins",
            "Assets/TextMesh Pro",
            "Assets/Samples",
            "Assets/0_Refs",
            "Assets/_Delete",
        };

        [Tooltip("Off: only scenes enabled in Build Settings. On: every scene outside the excluded folders.")]
        public bool ScanAllScenes;

        [Header("Duplicates (cross-context sprites)")]
        public string UiDuplicateFolder = "Assets/_Project/Art/Sprites/_AtlasSplit/UI";
        public string GameplayDuplicateFolder = "Assets/_Project/Art/Sprites/_AtlasSplit/Gameplay";

        [Header("Rules")]
        public List<FieldRule> FieldRules = new List<FieldRule>();
        public List<TextureOverride> TextureOverrides = new List<TextureOverride>();

        public static SpriteOptimizerSettings LoadOrCreate()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SpriteOptimizerSettings>(AssetPath);
            if (settings != null)
                return settings;

            settings = CreateInstance<SpriteOptimizerSettings>();
            settings.BindUnknownAtlasesByName();
            SpriteOptimizerUtil.EnsureFolder(System.IO.Path.GetDirectoryName(AssetPath)?.Replace('\\', '/'));
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        /// <summary>Adds a binding for every project atlas not bound yet, guessing the context from its name.</summary>
        public void BindUnknownAtlasesByName()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:SpriteAtlas"))
            {
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AssetDatabase.GUIDToAssetPath(guid));
                if (atlas == null || Atlases.Any(b => b.Atlas == atlas))
                    continue;

                string n = atlas.name.ToLowerInvariant();
                SpriteContext context =
                    n.Contains("ui") || n.Contains("menu") || n.Contains("hud") ? SpriteContext.UI :
                    n.Contains("gameplay") || n.Contains("game") || n.Contains("world") ? SpriteContext.Gameplay :
                    SpriteContext.Unknown;

                Atlases.Add(new AtlasBinding
                {
                    Atlas = atlas,
                    Context = context,
                    Primary = context != SpriteContext.Unknown && Atlases.All(b => b.Context != context || !b.Primary),
                });
            }
            MarkDirty();
        }

        public AtlasBinding BindingFor(SpriteAtlas atlas) => Atlases.FirstOrDefault(b => b.Atlas == atlas);

        public SpriteAtlas PrimaryFor(SpriteContext context)
        {
            AtlasBinding primary = Atlases.FirstOrDefault(b => b.Atlas != null && b.Context == context && b.Primary)
                                   ?? Atlases.FirstOrDefault(b => b.Atlas != null && b.Context == context);
            return primary?.Atlas;
        }

        public void SetPrimary(AtlasBinding binding)
        {
            foreach (AtlasBinding b in Atlases)
                if (b.Context == binding.Context)
                    b.Primary = b == binding;
            MarkDirty();
        }

        public SpriteContext RuleFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return SpriteContext.Unknown;
            FieldRule rule = FieldRules.FirstOrDefault(r => r.Key == key);
            return rule?.Context ?? SpriteContext.Unknown;
        }

        public void SetRule(string key, SpriteContext context)
        {
            FieldRule rule = FieldRules.FirstOrDefault(r => r.Key == key);
            if (rule == null)
            {
                if (context == SpriteContext.Unknown) return;
                FieldRules.Add(new FieldRule { Key = key, Context = context });
            }
            else if (context == SpriteContext.Unknown)
            {
                FieldRules.Remove(rule);
            }
            else
            {
                rule.Context = context;
            }
            FieldRules.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            MarkDirty();
        }

        public SpriteContext OverrideFor(string texturePath)
        {
            foreach (TextureOverride o in TextureOverrides)
                if (o.Texture != null && AssetDatabase.GetAssetPath(o.Texture) == texturePath)
                    return o.Context;
            return SpriteContext.Unknown;
        }

        public void SetOverride(Texture2D texture, SpriteContext context)
        {
            TextureOverrides.RemoveAll(o => o.Texture == null || o.Texture == texture);
            if (context != SpriteContext.Unknown)
                TextureOverrides.Add(new TextureOverride { Texture = texture, Context = context });
            MarkDirty();
        }

        public string DuplicateFolderFor(SpriteContext context) =>
            context == SpriteContext.UI ? UiDuplicateFolder : GameplayDuplicateFolder;

        public bool IsExcluded(string assetPath)
        {
            foreach (string folder in ExcludedUsageFolders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                string f = folder.TrimEnd('/');
                if (assetPath.StartsWith(f + "/", StringComparison.Ordinal) || assetPath == f)
                    return true;
            }
            return false;
        }

        public void MarkDirty()
        {
            EditorUtility.SetDirty(this);
        }
    }
}

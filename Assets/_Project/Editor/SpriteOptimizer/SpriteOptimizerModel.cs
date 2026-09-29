using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Project.EditorTools.SpriteOptimizer
{
    /// <summary>Stable identity of a sprite sub-asset: texture GUID + local file id.</summary>
    internal readonly struct SpriteKey : IEquatable<SpriteKey>
    {
        public readonly string Guid;
        public readonly long FileId;

        public SpriteKey(string guid, long fileId)
        {
            Guid = guid;
            FileId = fileId;
        }

        public static bool TryGet(Object sprite, out SpriteKey key)
        {
            key = default;
            if (sprite == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long id))
                return false;
            key = new SpriteKey(guid, id);
            return true;
        }

        public bool Equals(SpriteKey other) => FileId == other.FileId && Guid == other.Guid;
        public override bool Equals(object obj) => obj is SpriteKey other && Equals(other);
        public override int GetHashCode() => ((Guid?.GetHashCode() ?? 0) * 397) ^ FileId.GetHashCode();
    }

    internal enum UsageSource { Prefab, Scene, Data, Animation, Runtime }

    /// <summary>One place a sprite is referenced from.</summary>
    internal sealed class SpriteUsage
    {
        public SpriteKey Sprite;
        public UsageSource Source;
        /// <summary>Prefab / scene / ScriptableObject / clip path. Empty for runtime sightings.</summary>
        public string AssetPath;
        /// <summary>Readable hierarchy path ("Canvas/Panel/Icon") or runtime "where" text.</summary>
        public string ObjectPath;
        /// <summary>Sibling-index path from the prefab root / scene root list ("2/0/5"), used to find the object again.</summary>
        public string IndexPath;
        public int ComponentIndex;
        /// <summary>Local id of the ScriptableObject inside its file (Data usages).</summary>
        public long LocalId;
        public string ComponentType;
        public string PropertyPath;
        /// <summary>"Type.field" key for rule-driven usages (custom MonoBehaviours / ScriptableObjects); null for built-ins.</summary>
        public string RuleKey;
        public SpriteContext AutoContext;
        /// <summary>Resolved by the analysis (rules + texture overrides applied).</summary>
        public SpriteContext Context;

        public bool CanReassign => Source != UsageSource.Runtime;

        public string Describe()
        {
            string where = string.IsNullOrEmpty(ObjectPath) ? "" : $" › {ObjectPath}";
            string what = string.IsNullOrEmpty(ComponentType) ? "" : $" [{ComponentType}.{PropertyPath}]";
            string asset = string.IsNullOrEmpty(AssetPath) ? "(runtime)" : System.IO.Path.GetFileName(AssetPath);
            return $"{Source}: {asset}{where}{what}";
        }
    }

    internal sealed class SpriteInfo
    {
        public SpriteKey Key;
        public string Name;
        public TextureInfo Texture;
        public Rect Rect;
        public readonly List<SpriteUsage> Usages = new List<SpriteUsage>();

        // Resolved by the analysis.
        public bool UsedUI;
        public bool UsedGameplay;
        public bool HasUnknownUsage;
        public readonly List<AtlasInfo> PackedIn = new List<AtlasInfo>();

        public bool CrossContext => UsedUI && UsedGameplay;
        public bool UsedIn(SpriteContext c) => c == SpriteContext.UI ? UsedUI : c == SpriteContext.Gameplay && UsedGameplay;

        public Sprite Load()
        {
            foreach (Object o in AssetDatabase.LoadAllAssetRepresentationsAtPath(Texture.Path))
                if (o is Sprite s && SpriteKey.TryGet(s, out SpriteKey k) && k.Equals(Key))
                    return s;
            return null;
        }
    }

    internal enum PackKind { Texture, Sprite, Folder }

    internal sealed class AtlasMembership
    {
        public AtlasInfo Atlas;
        public PackKind Kind;
        /// <summary>Folder packable path when <see cref="Kind"/> is Folder.</summary>
        public string FolderPath;
        /// <summary>Individually packed sprite ids when <see cref="Kind"/> is Sprite.</summary>
        public readonly HashSet<long> SpriteIds = new HashSet<long>();

        public bool Covers(SpriteInfo sprite) => Kind != PackKind.Sprite || SpriteIds.Contains(sprite.Key.FileId);
    }

    internal sealed class TextureInfo
    {
        public string Path;
        public string Guid;
        public string Name;
        public bool IsSpriteType;
        public bool IsMultiple;
        public bool InResources;
        public bool IsTmpSpriteSheet;
        public int Width;
        public int Height;
        public readonly Dictionary<long, SpriteInfo> Sprites = new Dictionary<long, SpriteInfo>();
        /// <summary>Materials / RawImages that reference the texture directly (not through a Sprite).</summary>
        public readonly List<string> RawUsers = new List<string>();

        // Rebuilt by every analysis.
        public readonly List<AtlasMembership> Memberships = new List<AtlasMembership>();
        public SpriteContext Override;

        public Texture2D Load() => AssetDatabase.LoadAssetAtPath<Texture2D>(Path);
        public bool HasSpriteUsage => Sprites.Values.Any(s => s.Usages.Count > 0);
        public IEnumerable<AtlasInfo> Atlases => Memberships.Select(m => m.Atlas).Distinct();
    }

    internal sealed class AtlasInfo
    {
        public SpriteAtlas Atlas;
        public string Path;
        public string Name;
        public SpriteContext Context;
        public bool Primary;
        public bool Bound;
        public int RawEntries;
        public int DistinctEntries;
        public int NullEntries;
        public int MaxSize;
        public int Padding;
        public long PackedArea;
        public int SpriteCount;
        /// <summary>Real page count from a pack preview; -1 when the atlas has not been packed in this session.</summary>
        public int PreviewPages = -1;

        public bool Managed => Context == SpriteContext.UI || Context == SpriteContext.Gameplay;
        public int EstimatedPages => MaxSize <= 0 ? 1 : Mathf.Max(1, Mathf.CeilToInt(PackedArea / (MaxSize * (float)MaxSize * 0.85f)));
    }

    internal enum IssueKind
    {
        MissingFromAtlas,
        WrongContextAtlas,
        MultipleAtlases,
        CrossContextSprite,
        SplitSheet,
        UnusedInAtlas,
        RawTextureInAtlas,
        TmpSheetInAtlas,
        NonSpriteInAtlas,
        UnresolvedRule,
        DuplicateEntries,
        NullEntries,
        AtlasUnbound,
        NoPrimaryAtlas,
        AtlasOverflow,
        FolderPackable,
    }

    internal enum Severity { Info, Warning, Error }

    internal sealed class IssueFix
    {
        public string Label;
        public string Tooltip;
        /// <summary>Atlas-only fix: queues edits into a batch so many fixes apply in one save.</summary>
        public Action<AtlasEditBatch> Plan;
        /// <summary>Any other fix (settings change, duplicate + reassign...).</summary>
        public Action Run;
        /// <summary>Included in the per-context "Sync" / "Apply all safe fixes".</summary>
        public bool Safe;
        /// <summary>Changing references/files needs a rescan afterwards; settings-only fixes just re-analyze.</summary>
        public bool NeedsRescan;
    }

    internal sealed class Issue
    {
        public IssueKind Kind;
        public Severity Severity;
        /// <summary>UI / Gameplay tab it belongs to; Unknown = Conflicts tab.</summary>
        public SpriteContext Context;
        public TextureInfo Texture;
        public SpriteInfo Sprite;
        public AtlasInfo Atlas;
        public string RuleKey;
        public string Message;
        public readonly List<IssueFix> Fixes = new List<IssueFix>();

        public string Title => Kind switch
        {
            IssueKind.MissingFromAtlas => "Not in atlas",
            IssueKind.WrongContextAtlas => "Wrong atlas",
            IssueKind.MultipleAtlases => "In several atlases",
            IssueKind.CrossContextSprite => "UI + Gameplay",
            IssueKind.SplitSheet => "Split sheet",
            IssueKind.UnusedInAtlas => "Unused, packed",
            IssueKind.RawTextureInAtlas => "Raw texture use",
            IssueKind.TmpSheetInAtlas => "TMP sprite sheet",
            IssueKind.NonSpriteInAtlas => "Not a Sprite",
            IssueKind.UnresolvedRule => "Unclassified field",
            IssueKind.DuplicateEntries => "Duplicate entries",
            IssueKind.NullEntries => "Missing entries",
            IssueKind.AtlasUnbound => "Atlas has no context",
            IssueKind.NoPrimaryAtlas => "No atlas for context",
            IssueKind.AtlasOverflow => "Multiple pages",
            IssueKind.FolderPackable => "Folder packable",
            _ => Kind.ToString(),
        };
    }

    internal static class SpriteOptimizerUtil
    {
        public const string LogTag = "SpriteOptimizer";

        public static void EnsureFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
                return;

            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        /// <summary>"Array.data[3]" → "[]" so every element of a list shares one rule key.</summary>
        public static string CleanPropertyPath(string propertyPath) =>
            System.Text.RegularExpressions.Regex.Replace(propertyPath, @"\.Array\.data\[\d+\]", "[]");

        public static string HierarchyPath(Transform t, Transform stopAt = null)
        {
            var names = new List<string>();
            for (Transform c = t; c != null && c != stopAt; c = c.parent)
                names.Add(c.name);
            if (stopAt != null) names.Add(stopAt.name);
            names.Reverse();
            return string.Join("/", names);
        }

        public static string IndexPath(Transform t, Transform root)
        {
            var indices = new List<int>();
            for (Transform c = t; c != null && c != root; c = c.parent)
                indices.Add(c.GetSiblingIndex());
            indices.Reverse();
            return string.Join("/", indices);
        }

        public static Transform ResolveIndexPath(Transform root, string indexPath)
        {
            if (root == null) return null;
            if (string.IsNullOrEmpty(indexPath)) return root;
            Transform current = root;
            foreach (string part in indexPath.Split('/'))
            {
                if (!int.TryParse(part, out int index) || index < 0 || index >= current.childCount)
                    return null;
                current = current.GetChild(index);
            }
            return current;
        }

        public static string ContextLabel(SpriteContext c) => c switch
        {
            SpriteContext.UI => "UI",
            SpriteContext.Gameplay => "Gameplay",
            SpriteContext.Ignore => "Ignore",
            _ => "—",
        };
    }
}

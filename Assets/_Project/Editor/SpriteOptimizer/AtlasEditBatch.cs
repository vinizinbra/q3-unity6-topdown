using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Project.EditorTools.SpriteOptimizer
{
    /// <summary>
    /// Queues packable edits for many atlases and applies them in one pass per atlas, rewriting
    /// <c>m_EditorData.packables</c> through a SerializedObject (exact with duplicated entries, and it
    /// handles individual Sprite packables as well as whole textures).
    /// </summary>
    internal sealed class AtlasEditBatch
    {
        private sealed class Ops
        {
            public readonly List<Object> Add = new List<Object>();
            public readonly HashSet<string> RemoveTexturePaths = new HashSet<string>();
            public readonly HashSet<Object> RemoveObjects = new HashSet<Object>();
            public bool Dedupe;
            public bool DropNulls;
        }

        private readonly Dictionary<SpriteAtlas, Ops> _ops = new Dictionary<SpriteAtlas, Ops>();
        private readonly List<string> _log = new List<string>();

        public bool IsEmpty => _ops.Count == 0;

        private Ops For(SpriteAtlas atlas)
        {
            if (!_ops.TryGetValue(atlas, out Ops ops))
                _ops[atlas] = ops = new Ops();
            return ops;
        }

        public void AddTexture(SpriteAtlas atlas, string texturePath)
        {
            if (atlas == null) return;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null) return;
            For(atlas).Add.Add(texture);
            _log.Add($"+ {System.IO.Path.GetFileName(texturePath)} → {atlas.name}");
        }

        public void AddSprites(SpriteAtlas atlas, IEnumerable<Sprite> sprites)
        {
            if (atlas == null) return;
            foreach (Sprite s in sprites.Where(s => s != null))
            {
                For(atlas).Add.Add(s);
                _log.Add($"+ {s.name} (sprite) → {atlas.name}");
            }
        }

        /// <summary>Removes the texture entry and every individual sprite entry of that texture.</summary>
        public void RemoveTexture(SpriteAtlas atlas, string texturePath)
        {
            if (atlas == null) return;
            For(atlas).RemoveTexturePaths.Add(texturePath);
            _log.Add($"- {System.IO.Path.GetFileName(texturePath)} ✕ {atlas.name}");
        }

        public void RemoveObject(SpriteAtlas atlas, Object packable)
        {
            if (atlas == null || packable == null) return;
            For(atlas).RemoveObjects.Add(packable);
            _log.Add($"- {packable.name} ✕ {atlas.name}");
        }

        public void Dedupe(SpriteAtlas atlas)
        {
            if (atlas == null) return;
            For(atlas).Dedupe = true;
            _log.Add($"dedupe {atlas.name}");
        }

        public void DropNulls(SpriteAtlas atlas)
        {
            if (atlas == null) return;
            For(atlas).DropNulls = true;
            _log.Add($"drop missing entries {atlas.name}");
        }

        /// <summary>Applies every queued edit. Returns the number of atlases changed.</summary>
        public int Apply()
        {
            int changed = 0;
            foreach (KeyValuePair<SpriteAtlas, Ops> pair in _ops)
            {
                if (Rewrite(pair.Key, pair.Value))
                    changed++;
            }

            if (changed > 0)
            {
                AssetDatabase.SaveAssets();
                LogHelper.Log(SpriteOptimizerUtil.LogTag,
                    $"Updated {changed} atlas(es):\n" + string.Join("\n", _log.Distinct()));
            }

            _ops.Clear();
            _log.Clear();
            return changed;
        }

        private static bool Rewrite(SpriteAtlas atlas, Ops ops)
        {
            var so = new SerializedObject(atlas);
            SerializedProperty list = so.FindProperty("m_EditorData.packables");
            if (list == null || !list.isArray)
            {
                LogHelper.Error(SpriteOptimizerUtil.LogTag, $"'{atlas.name}': packables property not found — atlas left untouched.", atlas);
                return false;
            }

            var original = new List<Object>();
            for (int i = 0; i < list.arraySize; i++)
                original.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);

            var kept = new List<Object>();
            var seen = new HashSet<Object>();
            foreach (Object obj in original)
            {
                if (obj == null)
                {
                    if (!ops.DropNulls) kept.Add(null);
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(obj);
                bool isTextureOrSprite = obj is Texture2D || obj is Sprite;
                if (isTextureOrSprite && ops.RemoveTexturePaths.Contains(path)) continue;
                if (ops.RemoveObjects.Contains(obj)) continue;
                if (ops.Dedupe && !seen.Add(obj)) continue;
                seen.Add(obj);
                kept.Add(obj);
            }

            foreach (Object add in ops.Add)
            {
                if (kept.Contains(add)) continue;
                // A sprite is already covered when its whole texture is packed here.
                if (add is Sprite s && kept.Any(k => k is Texture2D && AssetDatabase.GetAssetPath(k) == AssetDatabase.GetAssetPath(s)))
                    continue;
                // Adding the whole texture supersedes its individual sprite entries.
                if (add is Texture2D)
                {
                    string texPath = AssetDatabase.GetAssetPath(add);
                    kept.RemoveAll(k => k is Sprite && AssetDatabase.GetAssetPath(k) == texPath);
                }
                kept.Add(add);
            }

            if (kept.Count == original.Count && kept.SequenceEqual(original))
                return false;

            list.arraySize = kept.Count;
            for (int i = 0; i < kept.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = kept[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(atlas);
            return true;
        }
    }

    /// <summary>Reads every project atlas into <see cref="AtlasInfo"/> + per-texture memberships.</summary>
    internal static class AtlasReader
    {
        public static List<AtlasInfo> ReadAll(SpriteOptimizerSettings settings, Func<string, TextureInfo> getTexture)
        {
            var atlases = new List<AtlasInfo>();
            string platform = ActivePlatformName();

            foreach (string guid in AssetDatabase.FindAssets("t:SpriteAtlas"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (atlas == null) continue;

                AtlasBinding binding = settings.BindingFor(atlas);
                var info = new AtlasInfo
                {
                    Atlas = atlas,
                    Path = path,
                    Name = atlas.name,
                    Bound = binding != null && binding.Context != SpriteContext.Unknown,
                    Context = binding?.Context ?? SpriteContext.Unknown,
                    Primary = binding != null && settings.PrimaryFor(binding.Context) == atlas,
                    MaxSize = MaxSizeFor(atlas, platform),
                    Padding = atlas.GetPackingSettings().padding,
                };
                atlases.Add(info);

                var so = new SerializedObject(atlas);
                SerializedProperty list = so.FindProperty("m_EditorData.packables");
                var packables = new List<Object>();
                if (list != null && list.isArray)
                {
                    for (int i = 0; i < list.arraySize; i++)
                        packables.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
                }
                else
                {
                    packables.AddRange(atlas.GetPackables());
                }

                info.RawEntries = packables.Count;
                info.NullEntries = packables.Count(p => p == null);
                info.DistinctEntries = packables.Where(p => p != null).Distinct().Count();

                foreach (Object packable in packables.Where(p => p != null).Distinct())
                {
                    string packablePath = AssetDatabase.GetAssetPath(packable);
                    if (packable is DefaultAsset && AssetDatabase.IsValidFolder(packablePath))
                    {
                        foreach (string texGuid in AssetDatabase.FindAssets("t:Texture2D", new[] { packablePath }))
                        {
                            TextureInfo tex = getTexture(AssetDatabase.GUIDToAssetPath(texGuid));
                            if (tex != null)
                                tex.Memberships.Add(new AtlasMembership { Atlas = info, Kind = PackKind.Folder, FolderPath = packablePath });
                        }
                    }
                    else if (packable is Texture2D)
                    {
                        TextureInfo tex = getTexture(packablePath);
                        if (tex != null)
                            tex.Memberships.Add(new AtlasMembership { Atlas = info, Kind = PackKind.Texture });
                    }
                    else if (packable is Sprite sprite && SpriteKey.TryGet(sprite, out SpriteKey key))
                    {
                        TextureInfo tex = getTexture(packablePath);
                        if (tex == null) continue;
                        AtlasMembership m = tex.Memberships.FirstOrDefault(x => x.Atlas == info && x.Kind == PackKind.Sprite);
                        if (m == null)
                            tex.Memberships.Add(m = new AtlasMembership { Atlas = info, Kind = PackKind.Sprite });
                        m.SpriteIds.Add(key.FileId);
                    }
                }
            }

            return atlases;
        }

        public static string ActivePlatformName()
        {
            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iPhone";
                case BuildTarget.WebGL: return "WebGL";
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneLinux64:
                    return "Standalone";
                default: return "DefaultTexturePlatform";
            }
        }

        private static int MaxSizeFor(SpriteAtlas atlas, string platform)
        {
            TextureImporterPlatformSettings ps = atlas.GetPlatformSettings(platform);
            if (ps == null || !ps.overridden)
                ps = atlas.GetPlatformSettings("DefaultTexturePlatform");
            return ps?.maxTextureSize ?? 2048;
        }

        /// <summary>Packs the atlas for the active target and returns its real page count (-1 if unavailable).</summary>
        public static int PackAndCountPages(SpriteAtlas atlas)
        {
            SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
            // SpriteAtlasExtensions.GetPreviewTextures is internal; it is how the atlas inspector lists pages.
            MethodInfo method = typeof(SpriteAtlasExtensions).GetMethod("GetPreviewTextures",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null) return -1;
            return method.Invoke(null, new object[] { atlas }) is Texture2D[] pages ? pages.Length : -1;
        }
    }
}

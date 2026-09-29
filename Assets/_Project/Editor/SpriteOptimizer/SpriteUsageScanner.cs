using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using QuantumUser.View.Util;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Project.EditorTools.SpriteOptimizer
{
    internal sealed class ScanResult
    {
        public readonly Dictionary<string, TextureInfo> Textures = new Dictionary<string, TextureInfo>();
        public readonly List<SpriteUsage> Usages = new List<SpriteUsage>();
        public readonly List<string> Warnings = new List<string>();
        public int Prefabs, Scenes, DataAssets, Clips, Materials, RuntimeSightings;

        /// <summary>Scanned scene paths, in Build Settings order.</summary>
        public readonly List<string> ScenePaths = new List<string>();
        /// <summary>Asset path → scenes whose (recursive) dependencies include it.</summary>
        public readonly Dictionary<string, List<string>> ScenesByAsset = new Dictionary<string, List<string>>();

        private static readonly List<string> NoScenes = new List<string>();

        /// <summary>
        /// Scenes a usage ends up in: the scene itself, or every scene that references the prefab / data / clip
        /// (directly or through other assets). Runtime sightings use the scene they were recorded in.
        /// Empty = only reachable from code / Resources / other prefabs no scene references.
        /// </summary>
        public List<string> ScenesOf(SpriteUsage usage)
        {
            if (usage.Source == UsageSource.Scene)
                return ScenesByAsset.TryGetValue(usage.AssetPath, out List<string> self) ? self : NoScenes;

            if (usage.Source == UsageSource.Runtime)
            {
                int colon = usage.ObjectPath?.IndexOf(':') ?? -1;
                if (colon <= 0) return NoScenes;
                string sceneName = usage.ObjectPath.Substring(0, colon);
                string path = ScenePaths.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == sceneName);
                return path != null ? ScenesByAsset[path] : NoScenes;
            }

            return usage.AssetPath != null && ScenesByAsset.TryGetValue(usage.AssetPath, out List<string> scenes) ? scenes : NoScenes;
        }
        public DateTime Time;
        public double Seconds;
        public bool Cancelled;

        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".psd", ".psb", ".tga", ".tif", ".tiff", ".gif", ".bmp", ".exr", ".hdr", ".iff", ".pict",
        };

        private readonly Dictionary<string, bool> _isSpriteTexture = new Dictionary<string, bool>();

        public TextureInfo GetOrAddTexture(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                return null;
            if (Textures.TryGetValue(path, out TextureInfo info))
                return info;
            if (!ImageExtensions.Contains(Path.GetExtension(path)) && AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(Texture2D))
                return null;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            var texture = AssetDatabase.LoadMainAssetAtPath(path) as Texture2D;
            if (texture == null)
                return null;

            info = new TextureInfo
            {
                Path = path,
                Guid = AssetDatabase.AssetPathToGUID(path),
                Name = Path.GetFileNameWithoutExtension(path),
                IsSpriteType = importer != null && importer.textureType == TextureImporterType.Sprite,
                IsMultiple = importer != null && importer.spriteImportMode == SpriteImportMode.Multiple,
                InResources = path.Contains("/Resources/"),
                Width = texture.width,
                Height = texture.height,
            };

            foreach (Object o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                if (o is Sprite sprite && SpriteKey.TryGet(sprite, out SpriteKey key))
                {
                    info.Sprites[key.FileId] = new SpriteInfo
                    {
                        Key = key,
                        Name = sprite.name,
                        Texture = info,
                        Rect = sprite.rect,
                    };
                }
            }

            Textures[path] = info;
            return info;
        }

        public SpriteInfo SpriteFor(SpriteKey key)
        {
            TextureInfo tex = GetOrAddTexture(AssetDatabase.GUIDToAssetPath(key.Guid));
            return tex != null && tex.Sprites.TryGetValue(key.FileId, out SpriteInfo s) ? s : null;
        }

        public bool IsSpriteTexture(string path)
        {
            if (_isSpriteTexture.TryGetValue(path, out bool result))
                return result;
            result = ImageExtensions.Contains(Path.GetExtension(path))
                     && AssetImporter.GetAtPath(path) is TextureImporter ti
                     && ti.textureType == TextureImporterType.Sprite;
            _isSpriteTexture[path] = result;
            return result;
        }

        public void Add(SpriteUsage usage)
        {
            SpriteInfo sprite = SpriteFor(usage.Sprite);
            if (sprite == null) return; // built-in (unity_builtin_extra) or non-texture sprite
            sprite.Usages.Add(usage);
            Usages.Add(usage);
        }
    }

    /// <summary>
    /// Static usage scan: every Sprite reference in build scenes, prefabs, ScriptableObjects and sprite
    /// animation clips, tagged UI or Gameplay, plus the runtime recorder's sightings. Only the place a value
    /// is *defined* is recorded (prefab-instance values that are not overridden belong to the source prefab),
    /// so every usage can be rewritten in exactly one place.
    /// </summary>
    internal static class SpriteUsageScanner
    {
        private const string ProgressTitle = "Sprite Atlas Optimizer";

        private static readonly Regex WordSplit = new Regex(@"[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z]+|[A-Z]+|\d+");

        private static readonly HashSet<string> UiWords = new HashSet<string>
        {
            "icon", "icons", "ui", "hud", "portrait", "avatar", "badge", "thumbnail", "thumb", "emblem", "banner",
            "card", "button", "frame", "lobby", "minimap", "menu", "cursor", "logo",
        };

        private static readonly HashSet<string> GameplayWords = new HashSet<string>
        {
            "projectile", "pawn", "body", "world", "ground", "decal", "particle", "collectible", "pickup", "bullet",
            "vfx", "fx", "tile", "prop", "shadow", "corpse", "renderer",
        };

        public static ScanResult Run(SpriteOptimizerSettings settings)
        {
            var result = new ScanResult { Time = DateTime.Now };
            var watch = Stopwatch.StartNew();

            try
            {
                // Packed textures first: raw (material / RawImage) users only matter for textures an atlas packs.
                AtlasReader.ReadAll(settings, result.GetOrAddTexture);
                var packed = new HashSet<string>(result.Textures.Values.Where(t => t.Memberships.Count > 0).Select(t => t.Path));
                foreach (TextureInfo t in result.Textures.Values) t.Memberships.Clear();

                bool Relevant(string assetPath) =>
                    AssetDatabase.GetDependencies(assetPath, false).Any(d => d != assetPath && (packed.Contains(d) || result.IsSpriteTexture(d)));

                // ---- Collect what to scan
                List<string> scenes = CollectScenes(settings);
                var usedDeps = new HashSet<string>();
                foreach (string scene in scenes)
                {
                    string[] deps = AssetDatabase.GetDependencies(scene, true);
                    usedDeps.UnionWith(deps);
                    result.ScenePaths.Add(scene);
                    foreach (string dep in deps.Append(scene).Distinct())
                    {
                        if (!result.ScenesByAsset.TryGetValue(dep, out List<string> list))
                            result.ScenesByAsset[dep] = list = new List<string>();
                        list.Add(scene);
                    }
                }

                List<string> rootPrefabs = Find("t:Prefab", settings);
                foreach (string prefab in rootPrefabs)
                    usedDeps.UnionWith(AssetDatabase.GetDependencies(prefab, true));

                var prefabs = new HashSet<string>(rootPrefabs);
                prefabs.UnionWith(usedDeps.Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)));

                var data = new HashSet<string>(Find("t:ScriptableObject", settings));
                data.UnionWith(usedDeps.Where(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)));

                var clips = new HashSet<string>(Find("t:AnimationClip", settings).Where(p => p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)));
                clips.UnionWith(usedDeps.Where(p => p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)));

                var materials = new HashSet<string>(Find("t:Material", settings));
                materials.UnionWith(usedDeps.Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)));

                // ---- Prefabs
                List<string> prefabList = prefabs.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).OrderBy(p => p).ToList();
                for (int i = 0; i < prefabList.Count; i++)
                {
                    string path = prefabList[i];
                    if (Progress($"Prefabs {i + 1}/{prefabList.Count}", path, 0.05f + 0.45f * i / Math.Max(1, prefabList.Count)))
                        return Cancel(result);
                    if (!Relevant(path)) continue;

                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (root == null) continue;
                    ScanHierarchy(result, root.transform, UsageSource.Prefab, path, "", packed);
                    result.Prefabs++;
                }

                // ---- Scenes
                for (int i = 0; i < scenes.Count; i++)
                {
                    string path = scenes[i];
                    if (Progress($"Scenes {i + 1}/{scenes.Count}", path, 0.5f + 0.2f * i / Math.Max(1, scenes.Count)))
                        return Cancel(result);
                    ScanScene(result, path, packed);
                    result.Scenes++;
                }

                // ---- ScriptableObjects
                List<string> dataList = data.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && p != SpriteOptimizerSettings.AssetPath).OrderBy(p => p).ToList();
                for (int i = 0; i < dataList.Count; i++)
                {
                    string path = dataList[i];
                    if (i % 20 == 0 && Progress($"Data {i + 1}/{dataList.Count}", path, 0.7f + 0.15f * i / Math.Max(1, dataList.Count)))
                        return Cancel(result);
                    if (!Relevant(path)) continue;
                    if (ScanData(result, path)) result.DataAssets++;
                }

                // ---- Sprite animation clips
                List<string> clipList = clips.OrderBy(p => p).ToList();
                for (int i = 0; i < clipList.Count; i++)
                {
                    string path = clipList[i];
                    if (i % 20 == 0 && Progress($"Clips {i + 1}/{clipList.Count}", path, 0.85f + 0.05f * i / Math.Max(1, clipList.Count)))
                        return Cancel(result);
                    if (!Relevant(path)) continue;
                    if (ScanClip(result, path)) result.Clips++;
                }

                // ---- Raw texture users of packed textures (materials, TMP sprite assets)
                Progress("Materials", "", 0.9f);
                foreach (string path in materials)
                {
                    bool any = false;
                    foreach (string dep in AssetDatabase.GetDependencies(path, false))
                    {
                        if (!packed.Contains(dep)) continue;
                        result.GetOrAddTexture(dep)?.RawUsers.Add(path);
                        any = true;
                    }
                    if (any) result.Materials++;
                }

                foreach (string guid in AssetDatabase.FindAssets("t:TMP_SpriteAsset"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var spriteAsset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(path);
                    if (spriteAsset == null || spriteAsset.spriteSheet == null) continue;
                    TextureInfo sheet = result.GetOrAddTexture(AssetDatabase.GetAssetPath(spriteAsset.spriteSheet));
                    if (sheet == null) continue;
                    sheet.IsTmpSpriteSheet = true;
                    sheet.RawUsers.Add(path);
                }

                // ---- Runtime recorder
                foreach (RuntimeSighting s in RuntimeSpriteRecorder.Sightings)
                {
                    result.Add(new SpriteUsage
                    {
                        Sprite = new SpriteKey(s.Guid, s.FileId),
                        Source = UsageSource.Runtime,
                        ObjectPath = s.Where,
                        AutoContext = s.Context,
                    });
                    result.RuntimeSightings++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            result.Seconds = watch.Elapsed.TotalSeconds;
            LogHelper.Log(SpriteOptimizerUtil.LogTag,
                $"Scan: {result.Usages.Count} sprite references from {result.Prefabs} prefabs, {result.Scenes} scenes, " +
                $"{result.DataAssets} data assets, {result.Clips} clips, {result.RuntimeSightings} runtime sightings " +
                $"({result.Seconds:0.0}s).");
            return result;
        }

        private static ScanResult Cancel(ScanResult result)
        {
            result.Cancelled = true;
            LogHelper.Warn(SpriteOptimizerUtil.LogTag, "Scan cancelled.");
            return result;
        }

        private static bool Progress(string title, string info, float t) =>
            EditorUtility.DisplayCancelableProgressBar(ProgressTitle, $"{title}  {info}", t);

        private static List<string> Find(string filter, SpriteOptimizerSettings settings) =>
            AssetDatabase.FindAssets(filter, new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !settings.IsExcluded(p))
                .Distinct()
                .ToList();

        private static List<string> CollectScenes(SpriteOptimizerSettings settings)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled && File.Exists(s.path))
                .Select(s => s.path)
                .ToList();
            if (settings.ScanAllScenes)
                scenes.AddRange(Find("t:Scene", settings).Where(p => p.EndsWith(".unity", StringComparison.Ordinal)));
            return scenes.Distinct().ToList();
        }

        // ------------------------------------------------------------------ hierarchy (prefabs + scenes)

        private static void ScanScene(ScanResult result, string path, HashSet<string> packed)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                try
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    openedHere = true;
                }
                catch (Exception e)
                {
                    result.Warnings.Add($"Could not open scene '{path}': {e.Message}");
                    return;
                }
            }

            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                    ScanHierarchy(result, roots[r].transform, UsageSource.Scene, path, r.ToString(), packed);
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <param name="rootIndexPrefix">Scene root index ("3") for scenes; empty for prefab roots.</param>
        private static void ScanHierarchy(ScanResult result, Transform root, UsageSource source, string assetPath,
            string rootIndexPrefix, HashSet<string> packed)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string childIndexPath = SpriteOptimizerUtil.IndexPath(t, root);
                string indexPath = string.IsNullOrEmpty(rootIndexPrefix)
                    ? childIndexPath
                    : string.IsNullOrEmpty(childIndexPath) ? rootIndexPrefix : rootIndexPrefix + "/" + childIndexPath;

                bool isRect = t is RectTransform;
                Component[] components = t.GetComponents<Component>();
                string objectPath = null;

                for (int ci = 0; ci < components.Length; ci++)
                {
                    Component component = components[ci];
                    if (component == null || component is Transform) continue;

                    bool inherited = PrefabUtility.IsPartOfPrefabInstance(component) && !PrefabUtility.IsAddedComponentOverride(component);
                    SpriteContext auto = AutoContextFor(component, isRect);
                    Type type = component.GetType();
                    bool ruleDriven = component is MonoBehaviour && !IsBuiltInType(type);
                    int componentIndex = ci;

                    WalkReferences(component, (prop, value) =>
                    {
                        if (inherited && !prop.prefabOverride) return;

                        if (value is Sprite sprite)
                        {
                            if (!SpriteKey.TryGet(sprite, out SpriteKey key)) return;
                            objectPath ??= SpriteOptimizerUtil.HierarchyPath(t);
                            string clean = SpriteOptimizerUtil.CleanPropertyPath(prop.propertyPath);
                            result.Add(new SpriteUsage
                            {
                                Sprite = key,
                                Source = source,
                                AssetPath = assetPath,
                                ObjectPath = objectPath,
                                IndexPath = indexPath,
                                ComponentIndex = componentIndex,
                                ComponentType = type.Name,
                                PropertyPath = prop.propertyPath,
                                RuleKey = ruleDriven ? $"{DeclaringTypeName(type, prop.propertyPath)}.{clean}" : null,
                                AutoContext = auto,
                            });
                        }
                        else if (value is Texture2D texture && component is RawImage)
                        {
                            string texPath = AssetDatabase.GetAssetPath(texture);
                            if (packed.Contains(texPath))
                                result.GetOrAddTexture(texPath)?.RawUsers.Add($"{assetPath} › {SpriteOptimizerUtil.HierarchyPath(t)} (RawImage)");
                        }
                    });
                }
            }
        }

        private static SpriteContext AutoContextFor(Component component, bool isRect)
        {
            // Renderers are batched by the render pipeline, never by a Canvas, even under a RectTransform.
            if (component is Renderer || component is ParticleSystem)
                return SpriteContext.Gameplay;
            return isRect ? SpriteContext.UI : SpriteContext.Gameplay;
        }

        private static bool IsBuiltInType(Type type)
        {
            string ns = type.Namespace ?? "";
            return ns.StartsWith("UnityEngine", StringComparison.Ordinal)
                   || ns.StartsWith("UnityEditor", StringComparison.Ordinal)
                   || ns.StartsWith("TMPro", StringComparison.Ordinal);
        }

        /// <summary>Visits every object reference in the serialized data of <paramref name="target"/>.</summary>
        private static void WalkReferences(Object target, Action<SerializedProperty, Object> onReference)
        {
            var so = new SerializedObject(target);
            SerializedProperty it = so.GetIterator();
            bool enter = true;
            while (it.Next(enter))
            {
                enter = it.propertyType == SerializedPropertyType.Generic;
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object value = it.objectReferenceValue;
                if (value is Sprite || value is Texture2D)
                    onReference(it, value);
            }
        }

        // ------------------------------------------------------------------ data + clips

        private static bool ScanData(ScanResult result, string path)
        {
            bool any = false;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is ScriptableObject so)) continue;
                if (so is TMP_SpriteAsset || so is TMP_FontAsset || so is SpriteOptimizerSettings) continue;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(so, out _, out long localId)) continue;

                Type type = so.GetType();
                bool isTile = IsTileType(type);
                WalkReferences(so, (prop, value) =>
                {
                    if (!(value is Sprite sprite) || !SpriteKey.TryGet(sprite, out SpriteKey key)) return;
                    string clean = SpriteOptimizerUtil.CleanPropertyPath(prop.propertyPath);
                    result.Add(new SpriteUsage
                    {
                        Sprite = key,
                        Source = UsageSource.Data,
                        AssetPath = path,
                        ObjectPath = so.name,
                        LocalId = localId,
                        ComponentType = type.Name,
                        PropertyPath = prop.propertyPath,
                        RuleKey = $"{DeclaringTypeName(type, prop.propertyPath)}.{clean}",
                        AutoContext = isTile ? SpriteContext.Gameplay : GuessFromFieldName(clean),
                    });
                    any = true;
                });
            }
            return any;
        }

        /// <summary>
        /// Name of the class that declares the top-level field, so every subclass shares one rule
        /// (all <c>UpgradeData</c> subclasses → <c>UpgradeData.Icon</c>).
        /// </summary>
        private static string DeclaringTypeName(Type type, string propertyPath)
        {
            string field = propertyPath.Split('.')[0];
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject); t = t.BaseType)
                if (t.GetField(field, flags) != null) return t.Name;
            return type.Name;
        }

        private static bool IsTileType(Type type)
        {
            for (Type t = type; t != null; t = t.BaseType)
                if (t.Name == "TileBase") return true;
            return false;
        }

        /// <summary>Name-based first guess for data fields; anything unclear stays Unknown for the user to decide.</summary>
        internal static SpriteContext GuessFromFieldName(string cleanPath)
        {
            string field = cleanPath.Split('.').Last().Replace("[]", "");
            var words = WordSplit.Matches(field).Cast<Match>().Select(m => m.Value.ToLowerInvariant()).ToList();
            if (words.Any(UiWords.Contains)) return SpriteContext.UI;
            if (words.Any(GameplayWords.Contains)) return SpriteContext.Gameplay;
            return SpriteContext.Unknown;
        }

        private static bool ScanClip(ScanResult result, string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) return false;

            bool any = false;
            foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                SpriteContext context;
                string ruleKey = null;
                if (binding.type != null && typeof(Renderer).IsAssignableFrom(binding.type)) context = SpriteContext.Gameplay;
                else if (binding.type != null && typeof(Graphic).IsAssignableFrom(binding.type)) context = SpriteContext.UI;
                else
                {
                    context = SpriteContext.Unknown;
                    ruleKey = $"Anim:{binding.type?.Name}.{binding.propertyName}";
                }

                var seen = new HashSet<SpriteKey>();
                foreach (ObjectReferenceKeyframe key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                {
                    if (!(key.value is Sprite sprite) || !SpriteKey.TryGet(sprite, out SpriteKey spriteKey)) continue;
                    if (!seen.Add(spriteKey)) continue;
                    result.Add(new SpriteUsage
                    {
                        Sprite = spriteKey,
                        Source = UsageSource.Animation,
                        AssetPath = path,
                        ObjectPath = binding.path,
                        ComponentType = binding.type?.Name,
                        PropertyPath = binding.propertyName,
                        RuleKey = ruleKey,
                        AutoContext = context,
                    });
                    any = true;
                }
            }
            return any;
        }
    }
}

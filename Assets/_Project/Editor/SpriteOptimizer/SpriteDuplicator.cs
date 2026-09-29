using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Project.EditorTools.SpriteOptimizer
{
    /// <summary>
    /// Fixes a sprite used by both UI and Gameplay: makes a copy for the target context, packs the copy in that
    /// context's primary atlas, and repoints every target-context reference (prefabs, scenes, ScriptableObjects,
    /// sprite clips) at the copy. The original keeps serving the other context.
    /// <list type="bullet">
    /// <item>Single-sprite texture → the file is copied (import settings travel with the .meta).</item>
    /// <item>Sprite from a PNG/JPG sheet → just that sprite's pixels are extracted into a new Single-sprite PNG
    /// (same pivot, 9-slice border, world size and compression), so the target atlas doesn't carry the whole sheet.</item>
    /// <item>Sprite from any other sheet format → the sheet is copied once and only the needed sprites are packed.</item>
    /// </list>
    /// Copies are named deterministically, so running it again reuses the existing copy instead of making another.
    /// </summary>
    internal static class SpriteDuplicator
    {
        public static void DuplicateAndReassign(IReadOnlyList<SpriteInfo> sprites, SpriteContext target, SpriteOptimizerSettings settings)
        {
            SpriteAtlas atlas = settings.PrimaryFor(target);
            if (atlas == null)
            {
                EditorUtility.DisplayDialog("Sprite Atlas Optimizer", $"No atlas is bound to {target}. Bind one in Settings first.", "OK");
                return;
            }

            bool touchesScenes = sprites.Any(s => s.Usages.Any(u => u.Context == target && u.Source == UsageSource.Scene));
            if (touchesScenes && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var batch = new AtlasEditBatch();
            var map = new Dictionary<SpriteKey, Sprite>();
            try
            {
                for (int i = 0; i < sprites.Count; i++)
                {
                    SpriteInfo sprite = sprites[i];
                    EditorUtility.DisplayProgressBar("Duplicate sprites", sprite.Name, (float)i / sprites.Count);
                    Sprite copy = CreateCopy(sprite, target, settings, batch, atlas);
                    if (copy != null)
                        map[sprite.Key] = copy;
                    else
                        LogHelper.Error(SpriteOptimizerUtil.LogTag, $"Could not duplicate '{sprite.Name}' from {sprite.Texture.Path}.");
                }

                batch.Apply();
                EditorUtility.DisplayProgressBar("Duplicate sprites", "Repointing references", 1f);
                Reassign(sprites, target, map);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }
        }

        // ------------------------------------------------------------------ copy

        private static Sprite CreateCopy(SpriteInfo info, SpriteContext target, SpriteOptimizerSettings settings, AtlasEditBatch batch, SpriteAtlas atlas)
        {
            Sprite source = info.Load();
            if (source == null) return null;

            TextureInfo tex = info.Texture;
            string folder = settings.DuplicateFolderFor(target);
            SpriteOptimizerUtil.EnsureFolder(folder);
            string suffix = target == SpriteContext.UI ? "UI" : "Game";
            string ext = Path.GetExtension(tex.Path);

            if (!tex.IsMultiple)
            {
                string copyPath = $"{folder}/{tex.Name}_{suffix}{ext}";
                if (!File.Exists(copyPath) && !AssetDatabase.CopyAsset(tex.Path, copyPath)) return null;
                batch.AddTexture(atlas, copyPath);
                return AssetDatabase.LoadAllAssetRepresentationsAtPath(copyPath).OfType<Sprite>().FirstOrDefault();
            }

            bool extractable = ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                               || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                               || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
            if (extractable)
            {
                string copyPath = $"{folder}/{tex.Name}__{Sanitize(info.Name)}_{suffix}.png";
                if (File.Exists(copyPath) || Extract(tex, source, copyPath))
                {
                    batch.AddTexture(atlas, copyPath);
                    return AssetDatabase.LoadAllAssetRepresentationsAtPath(copyPath).OfType<Sprite>().FirstOrDefault();
                }
                LogHelper.Warn(SpriteOptimizerUtil.LogTag, $"Extracting '{info.Name}' failed; copying the whole sheet instead.");
            }

            string sheetPath = $"{folder}/{tex.Name}_{suffix}{ext}";
            if (!File.Exists(sheetPath) && !AssetDatabase.CopyAsset(tex.Path, sheetPath)) return null;
            Sprite match = AssetDatabase.LoadAllAssetRepresentationsAtPath(sheetPath).OfType<Sprite>().FirstOrDefault(s => s.name == source.name);
            if (match != null) batch.AddSprites(atlas, new[] { match });
            return match;
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        private static bool Extract(TextureInfo tex, Sprite source, string copyPath)
        {
            var importer = AssetImporter.GetAtPath(tex.Path) as TextureImporter;
            if (importer == null) return false;

            var factory = new UnityEditor.U2D.Sprites.SpriteDataProviderFactories();
            factory.Init();
            UnityEditor.U2D.Sprites.ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) return false;
            provider.InitSpriteEditorDataProvider();
            SpriteRect spriteRect = provider.GetSpriteRects().FirstOrDefault(r => r.name == source.name);
            if (spriteRect == null) return false;

            var sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D cut = null;
            try
            {
                if (!sheet.LoadImage(File.ReadAllBytes(tex.Path))) return false;

                int x = Mathf.Clamp(Mathf.RoundToInt(spriteRect.rect.x), 0, sheet.width - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(spriteRect.rect.y), 0, sheet.height - 1);
                int w = Mathf.Clamp(Mathf.RoundToInt(spriteRect.rect.width), 1, sheet.width - x);
                int h = Mathf.Clamp(Mathf.RoundToInt(spriteRect.rect.height), 1, sheet.height - y);

                cut = new Texture2D(w, h, TextureFormat.RGBA32, false);
                cut.SetPixels(sheet.GetPixels(x, y, w, h));
                cut.Apply();
                File.WriteAllBytes(copyPath, cut.EncodeToPNG());
                AssetDatabase.ImportAsset(copyPath, ImportAssetOptions.ForceSynchronousImport);

                var copyImporter = AssetImporter.GetAtPath(copyPath) as TextureImporter;
                if (copyImporter == null) return false;

                var ts = new TextureImporterSettings();
                importer.ReadTextureSettings(ts);
                ts.spriteMode = (int)SpriteImportMode.Single;
                ts.spriteAlignment = (int)SpriteAlignment.Custom;
                ts.spritePivot = new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height);
                ts.spriteBorder = spriteRect.border;
                // Keep the same world size even when the sheet was downscaled on import.
                if (source.bounds.size.x > 0f) ts.spritePixelsPerUnit = w / source.bounds.size.x;
                copyImporter.SetTextureSettings(ts);

                copyImporter.textureCompression = importer.textureCompression;
                copyImporter.compressionQuality = importer.compressionQuality;
                copyImporter.crunchedCompression = importer.crunchedCompression;
                copyImporter.maxTextureSize = importer.maxTextureSize;
                foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
                {
                    TextureImporterPlatformSettings ps = importer.GetPlatformTextureSettings(platform);
                    if (ps != null && ps.overridden) copyImporter.SetPlatformTextureSettings(ps);
                }
                copyImporter.SaveAndReimport();
                return true;
            }
            finally
            {
                Object.DestroyImmediate(sheet);
                if (cut != null) Object.DestroyImmediate(cut);
            }
        }

        // ------------------------------------------------------------------ reassign

        private static void Reassign(IReadOnlyList<SpriteInfo> sprites, SpriteContext target, Dictionary<SpriteKey, Sprite> map)
        {
            List<SpriteUsage> usages = sprites.SelectMany(s => s.Usages)
                .Where(u => u.Context == target && map.ContainsKey(u.Sprite))
                .ToList();

            int done = 0;
            var failed = new List<string>();

            foreach (IGrouping<string, SpriteUsage> group in usages.Where(u => u.CanReassign).GroupBy(u => u.AssetPath))
            {
                UsageSource source = group.First().Source;
                try
                {
                    switch (source)
                    {
                        case UsageSource.Prefab: done += ReassignPrefab(group.Key, group.ToList(), map, failed); break;
                        case UsageSource.Scene: done += ReassignScene(group.Key, group.ToList(), map, failed); break;
                        case UsageSource.Data: done += ReassignData(group.Key, group.ToList(), map, failed); break;
                        case UsageSource.Animation: done += ReassignClip(group.Key, group.ToList(), map, failed); break;
                    }
                }
                catch (Exception e)
                {
                    failed.Add($"{group.Key}: {e.Message}");
                }
            }

            List<string> manual = usages.Where(u => !u.CanReassign).Select(u => u.Describe()).Distinct().ToList();

            string summary = $"Duplicated {map.Count} sprite(s) for {target}; repointed {done} reference(s).";
            if (failed.Count > 0) summary += $"\nNot found / changed since scan ({failed.Count}):\n  " + string.Join("\n  ", failed);
            if (manual.Count > 0)
                summary += $"\nAssigned at runtime — point the code/data at the {target} copy by hand ({manual.Count}):\n  " + string.Join("\n  ", manual);

            if (failed.Count > 0 || manual.Count > 0) LogHelper.Warn(SpriteOptimizerUtil.LogTag, summary);
            else LogHelper.Log(SpriteOptimizerUtil.LogTag, summary);
        }

        private static int ReassignPrefab(string path, List<SpriteUsage> usages, Dictionary<SpriteKey, Sprite> map, List<string> failed)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) { failed.Add(path); return 0; }

            int done = 0;
            foreach (SpriteUsage u in usages)
            {
                Component c = ResolveComponent(SpriteOptimizerUtil.ResolveIndexPath(root.transform, u.IndexPath), u);
                if (c != null && Set(c, u, map)) done++;
                else failed.Add(u.Describe());
            }

            if (done > 0) PrefabUtility.SavePrefabAsset(root);
            return done;
        }

        private static int ReassignScene(string path, List<SpriteUsage> usages, Dictionary<SpriteKey, Sprite> map, List<string> failed)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                openedHere = true;
            }

            int done = 0;
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                foreach (SpriteUsage u in usages)
                {
                    string[] parts = u.IndexPath.Split(new[] { '/' }, 2);
                    Transform t = null;
                    if (int.TryParse(parts[0], out int rootIndex) && rootIndex >= 0 && rootIndex < roots.Length)
                        t = SpriteOptimizerUtil.ResolveIndexPath(roots[rootIndex].transform, parts.Length > 1 ? parts[1] : "");

                    Component c = ResolveComponent(t, u);
                    if (c != null && Set(c, u, map)) done++;
                    else failed.Add(u.Describe());
                }

                if (done > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
            return done;
        }

        private static int ReassignData(string path, List<SpriteUsage> usages, Dictionary<SpriteKey, Sprite> map, List<string> failed)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            int done = 0;
            foreach (SpriteUsage u in usages)
            {
                Object asset = assets.FirstOrDefault(a => a != null
                    && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(a, out _, out long id) && id == u.LocalId);
                if (asset != null && Set(asset, u, map)) done++;
                else failed.Add(u.Describe());
            }
            return done;
        }

        private static int ReassignClip(string path, List<SpriteUsage> usages, Dictionary<SpriteKey, Sprite> map, List<string> failed)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { failed.Add(path); return 0; }

            int done = 0;
            foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                bool relevant = usages.Any(u => u.ObjectPath == binding.path && u.PropertyPath == binding.propertyName && u.ComponentType == binding.type?.Name);
                if (!relevant) continue;

                ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                bool changed = false;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (SpriteKey.TryGet(keys[i].value, out SpriteKey k) && map.TryGetValue(k, out Sprite copy))
                    {
                        keys[i].value = copy;
                        changed = true;
                        done++;
                    }
                }
                if (changed) AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            }

            if (done > 0) EditorUtility.SetDirty(clip);
            return done;
        }

        private static Component ResolveComponent(Transform t, SpriteUsage u)
        {
            if (t == null) return null;
            Component[] components = t.GetComponents<Component>();
            if (u.ComponentIndex < 0 || u.ComponentIndex >= components.Length) return null;
            Component c = components[u.ComponentIndex];
            return c != null && c.GetType().Name == u.ComponentType ? c : null;
        }

        private static bool Set(Object target, SpriteUsage u, Dictionary<SpriteKey, Sprite> map)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(u.PropertyPath);
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference) return false;
            if (!SpriteKey.TryGet(prop.objectReferenceValue, out SpriteKey current) || !current.Equals(u.Sprite)) return false;

            prop.objectReferenceValue = map[u.Sprite];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Quantum;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Project.EditorTools.SpriteOptimizer
{
    /// <summary>
    /// Which sprites each world can actually draw, and which ones the rest of the game (heroes, shared configs) draws.
    /// A walk per root that follows Quantum <c>AssetRef</c>s (serialized <see cref="AssetGuid"/>s → <see cref="QuantumUnityDB"/>)
    /// and object references, reading prefabs <b>flattened</b> (the loaded prefab asset holds the effective values) — so a
    /// W2 view that is a variant of a W1 view only counts the sprites it really shows, not its base prefab's file, exactly
    /// like a build. Roots: each <see cref="WorldCatalog"/> world (SurvivalConfig → enemies / groups / bosses → attacks →
    /// prototypes → views, the graph the Catalog reads) and, for "Common", every <see cref="CharacterData"/> plus
    /// <see cref="SpriteOptimizerSettings.CommonRootFolders"/>. Walks stop at world assets. Cached until <see cref="Invalidate"/>.
    /// </summary>
    internal sealed class WorldReachIndex
    {
        /// <summary>Sprite → the worlds that draw it.</summary>
        private readonly Dictionary<SpriteKey, List<WorldDefinition>> _worldsBySprite = new Dictionary<SpriteKey, List<WorldDefinition>>();
        /// <summary>Sprites drawn outside any world (heroes, weapons, shared configs).</summary>
        private readonly HashSet<SpriteKey> _common = new HashSet<SpriteKey>();
        /// <summary>(world, sprite) → readable chain of how the world reaches it, for the cross-world report.</summary>
        private readonly Dictionary<(WorldDefinition, SpriteKey), string> _via = new Dictionary<(WorldDefinition, SpriteKey), string>();

        public readonly List<WorldDefinition> Worlds = new List<WorldDefinition>();
        private static readonly List<WorldDefinition> None = new List<WorldDefinition>();
        private static WorldReachIndex _cached;

        public static WorldReachIndex GetOrBuild(SpriteOptimizerSettings settings) => _cached ??= Build(settings);

        public static void Invalidate() => _cached = null;

        public List<WorldDefinition> WorldsDrawing(SpriteKey sprite) =>
            _worldsBySprite.TryGetValue(sprite, out List<WorldDefinition> worlds) ? worlds : None;

        public bool IsCommon(SpriteKey sprite) => _common.Contains(sprite);

        /// <summary>Some world or the shared content draws it (false = no live content uses it).</summary>
        public bool IsDrawn(SpriteKey sprite) => _common.Contains(sprite) || _worldsBySprite.ContainsKey(sprite);

        public string Via(WorldDefinition world, SpriteKey sprite) =>
            _via.TryGetValue((world, sprite), out string chain) ? chain : "?";

        /// <summary>Every sprite of the texture any root draws (for callers that only know the texture).</summary>
        public IEnumerable<SpriteKey> KnownSpritesOf(string textureGuid) =>
            _worldsBySprite.Keys.Concat(_common).Where(k => k.Guid == textureGuid).Distinct();

        private static WorldReachIndex Build(SpriteOptimizerSettings settings)
        {
            var index = new WorldReachIndex();
            WorldCatalog catalog = WorldCatalog.Instance;
            IEnumerable<WorldDefinition> worlds = (catalog != null ? catalog.Worlds : Enumerable.Empty<WorldDefinition>())
                .Concat(settings.Atlases.Select(b => b.World));
            foreach (WorldDefinition world in worlds)
                if (world != null && !index.Worlds.Contains(world))
                    index.Worlds.Add(world);

            var stops = new HashSet<string>(index.Worlds.Select(AssetDatabase.GetAssetPath));
            if (catalog != null) stops.Add(AssetDatabase.GetAssetPath(catalog));

            try
            {
                for (int i = 0; i < index.Worlds.Count; i++)
                {
                    WorldDefinition world = index.Worlds[i];
                    EditorUtility.DisplayProgressBar("Sprite Atlas Optimizer", $"Walking {world.name}'s spawn graph…", (float)i / (index.Worlds.Count + 1));
                    string root = AssetDatabase.GetAssetPath(world);
                    var walker = new Walker(stops, root);
                    walker.Run(new[] { root });
                    foreach (KeyValuePair<SpriteKey, string> found in walker.Sprites)
                    {
                        if (!index._worldsBySprite.TryGetValue(found.Key, out List<WorldDefinition> list))
                            index._worldsBySprite[found.Key] = list = new List<WorldDefinition>();
                        list.Add(world);
                        index._via[(world, found.Key)] = found.Value;
                    }
                }

                EditorUtility.DisplayProgressBar("Sprite Atlas Optimizer", "Walking heroes and shared configs…", 0.95f);
                var commonRoots = AssetDatabase.FindAssets("t:" + nameof(CharacterData)).Select(AssetDatabase.GUIDToAssetPath)
                    .Concat(settings.CommonRootFolders.Where(AssetDatabase.IsValidFolder)
                        .SelectMany(f => AssetDatabase.FindAssets("", new[] { f }).Select(AssetDatabase.GUIDToAssetPath)))
                    .Concat(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path))
                    .Where(p => !AssetDatabase.IsValidFolder(p))
                    .Distinct()
                    .ToList();
                var common = new Walker(stops, null);
                common.Run(commonRoots);
                index._common.UnionWith(common.Sprites.Keys);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return index;
        }

        /// <summary>
        /// One breadth-first walk over <b>objects</b>, not files: a reference to one sub-asset (a delivery nested in another
        /// enemy's .asset) visits only that object, like a build loads it. Prefabs are visited whole and flattened.
        /// Remembers the first chain that reached each sprite.
        /// </summary>
        private sealed class Walker
        {
            public readonly Dictionary<SpriteKey, string> Sprites = new Dictionary<SpriteKey, string>();
            private readonly HashSet<string> _stops;
            private readonly string _allowedStop;
            private readonly Dictionary<Object, Object> _parent = new Dictionary<Object, Object>();
            private readonly Queue<Object> _queue = new Queue<Object>();

            public Walker(HashSet<string> stops, string allowedStop)
            {
                _stops = stops;
                _allowedStop = allowedStop;
            }

            public void Run(IEnumerable<string> roots)
            {
                foreach (string root in roots)
                    Enqueue(AssetDatabase.LoadMainAssetAtPath(root), null);

                while (_queue.Count > 0)
                {
                    Object obj = _queue.Dequeue();
                    foreach (Object part in PartsOf(obj))
                        Visit(obj, part);
                }
            }

            private void Enqueue(Object obj, Object from)
            {
                if (obj == null || obj is MonoScript || obj is Texture || obj is Sprite || obj is DefaultAsset) return;
                // Any part of a prefab means the whole (flattened) prefab: walk it once, from its root.
                if (obj is Component c) obj = c.transform.root.gameObject;
                else if (obj is GameObject go) obj = go.transform.root.gameObject;

                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || _parent.ContainsKey(obj))
                    return;
                if (_stops.Contains(path) && path != _allowedStop)
                    return;
                _parent[obj] = from;
                _queue.Enqueue(obj);
            }

            /// <summary>The objects whose references count: flattened prefab components, or the object itself.</summary>
            private IEnumerable<Object> PartsOf(Object obj)
            {
                switch (obj)
                {
                    case GameObject root:
                        return root.GetComponentsInChildren<Component>(true).Where(c => c != null);
                    case AnimationClip clip:
                        foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                            foreach (ObjectReferenceKeyframe k in AnimationUtility.GetObjectReferenceCurve(clip, b))
                                if (k.value is Sprite s)
                                    Record(s, obj);
                        return Enumerable.Empty<Object>();
                    case ScriptableObject _:
                        return new[] { obj };
                    default:
                        if (obj is SceneAsset || AssetDatabase.GetAssetPath(obj).EndsWith(".prefab", StringComparison.Ordinal))
                        {
                            // Scenes: walk what they reference directly (prefabs are flattened when reached).
                            if (obj is SceneAsset)
                                foreach (string dep in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(obj), false))
                                    Enqueue(AssetDatabase.LoadMainAssetAtPath(dep), obj);
                            return Enumerable.Empty<Object>();
                        }
                        // Controllers, materials, playables…: plain file dependencies (clips get walked above).
                        foreach (string dep in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(obj), false))
                            Enqueue(AssetDatabase.LoadMainAssetAtPath(dep), obj);
                        return Enumerable.Empty<Object>();
                }
            }

            private void Visit(Object owner, Object part)
            {
                var so = new SerializedObject(part);
                SerializedProperty it = so.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        Object value = it.objectReferenceValue;
                        // Prefab-variant links point back at the base prefab file; a build flattens them, so they don't count.
                        if (value == null || it.name == "m_Script" || it.name == "m_CorrespondingSourceObject"
                            || it.name == "m_PrefabInstance" || it.name == "m_PrefabAsset") continue;
                        if (value is Sprite sprite) Record(sprite, owner);
                        else Enqueue(value, owner);
                    }
                    else if (it.propertyType == SerializedPropertyType.Generic && it.type == nameof(AssetGuid))
                    {
                        SerializedProperty v = it.FindPropertyRelative("Value");
                        if (v != null && v.longValue != 0
                            && QuantumUnityDB.TryGetGlobalAsset(new AssetGuid(v.longValue), out AssetObject asset))
                            Enqueue(asset, owner);
                    }
                }
            }

            private void Record(Sprite sprite, Object foundIn)
            {
                if (!SpriteKey.TryGet(sprite, out SpriteKey key) || Sprites.ContainsKey(key)) return;
                Sprites[key] = Chain(foundIn);
            }

            private string Chain(Object obj)
            {
                var steps = new List<string>();
                for (Object o = obj; o != null && steps.Count < 7; _parent.TryGetValue(o, out o))
                    steps.Add(o.name);
                steps.Reverse();
                return string.Join(" → ", steps);
            }
        }
    }

    /// <summary>
    /// Gameplay atlas for a texture, by <b>where the file lives</b>: the binding whose Folders contain it (deepest wins),
    /// else the primary (Common) Gameplay atlas. Which folder an enemy sheet lives in is decided by
    /// <see cref="EnemySpriteOrganizer"/> from the worlds' spawn data. Shared by the Sprite Optimizer and the Build Size Analyzer.
    /// </summary>
    internal static class GameplayAtlasRouter
    {
        public static SpriteAtlas TargetFor(SpriteOptimizerSettings settings, string texturePath, out string reason)
        {
            AtlasBinding folder = settings.FolderBindingFor(texturePath);
            if (folder != null)
            {
                reason = "texture is under " + folder.Folders.First(f => SpriteOptimizerSettings.IsUnder(texturePath, f));
                return folder.Atlas;
            }
            reason = "not under a world / biome folder";
            return settings.PrimaryFor(SpriteContext.Gameplay);
        }
    }
}

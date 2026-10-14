using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Project.EditorTools.BuildAnalyzer
{
    /// <summary>
    /// "Why is this in the build?": a reverse dependency graph over the shipped assets, walked up from an asset
    /// to the roots that make Unity include it — Build Settings scenes, anything inside a Resources folder
    /// (always shipped, whether code loads it or not), and Player Settings ▸ Preloaded Assets.
    /// </summary>
    internal sealed class ShipReasonIndex
    {
        private readonly Dictionary<string, List<string>> _parents = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, string> _rootReason = new Dictionary<string, string>();
        private readonly Dictionary<string, List<List<string>>> _cache = new Dictionary<string, List<List<string>>>();

        public string RootReason(string path) => _rootReason.TryGetValue(path, out string reason) ? reason : null;

        public static ShipReasonIndex Build(BuildAnalysis a)
        {
            var index = new ShipReasonIndex();
            var nodes = new HashSet<string>(a.Assets.Where(e => !e.IsBuiltIn && e.Path.Length > 0).Select(e => e.Path));

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled || string.IsNullOrEmpty(scene.path)) continue;
                nodes.Add(scene.path);
                index._rootReason[scene.path] = "scene in Build Settings";
            }

            foreach (string path in nodes)
            {
                if (path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0 && !index._rootReason.ContainsKey(path))
                    index._rootReason[path] = "inside a Resources folder (always shipped)";
            }

            foreach (UnityEngine.Object preloaded in PlayerSettings.GetPreloadedAssets() ?? Array.Empty<UnityEngine.Object>())
            {
                string path = preloaded != null ? AssetDatabase.GetAssetPath(preloaded) : null;
                if (string.IsNullOrEmpty(path)) continue;
                nodes.Add(path);
                if (!index._rootReason.ContainsKey(path)) index._rootReason[path] = "Player Settings ▸ Preloaded Assets";
            }

            try
            {
                int i = 0;
                foreach (string node in nodes)
                {
                    if (i++ % 50 == 0 && EditorUtility.DisplayCancelableProgressBar("Build Size Analyzer",
                            $"Mapping why assets ship ({i}/{nodes.Count})…", (float)i / nodes.Count))
                        break;

                    string[] deps;
                    try { deps = AssetDatabase.GetDependencies(node, false); }
                    catch { continue; }

                    foreach (string dep in deps)
                    {
                        if (dep == node || !nodes.Contains(dep)) continue;
                        if (!index._parents.TryGetValue(dep, out List<string> parents))
                        {
                            parents = new List<string>();
                            index._parents.Add(dep, parents);
                        }
                        parents.Add(node);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return index;
        }

        /// <summary>Up to <paramref name="max"/> shortest chains root → … → <paramref name="path"/>, one per distinct root.</summary>
        public List<List<string>> Chains(string path, int max = 4)
        {
            if (_cache.TryGetValue(path, out List<List<string>> cached)) return cached;

            var chains = new List<List<string>>();
            var previous = new Dictionary<string, string> { [path] = null };
            var queue = new Queue<string>();
            queue.Enqueue(path);

            while (queue.Count > 0 && chains.Count < max)
            {
                string current = queue.Dequeue();
                if (_rootReason.ContainsKey(current))
                {
                    var chain = new List<string>();
                    for (string step = current; step != null; step = previous[step])
                        chain.Add(step);
                    chains.Add(chain);
                    continue; // a root explains the asset on its own; don't walk above it
                }

                if (!_parents.TryGetValue(current, out List<string> parents)) continue;
                foreach (string parent in parents)
                {
                    if (previous.ContainsKey(parent)) continue;
                    previous[parent] = current;
                    queue.Enqueue(parent);
                }
            }

            _cache[path] = chains;
            return chains;
        }

        public static string Describe(List<string> chain) =>
            string.Join("  →  ", chain.Select(System.IO.Path.GetFileName));
    }
}

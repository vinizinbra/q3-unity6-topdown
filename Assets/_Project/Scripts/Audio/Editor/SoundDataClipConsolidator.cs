using System.Collections.Generic;
using System.IO;
using System.Text;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

namespace Project.Audio.EditorTools
{
    // Finds every AudioClip the project actually uses that still lives outside the project's own
    // Audio folder - a leftover Asset Store bundle clip (Epic Toon FX, Universal Sound FX, Ultimate
    // SFX Bundle, ...) a designer wired in but never relocated - and moves just those clips into
    // Assets/_Project/Audio/Library via AssetDatabase.MoveAsset, which preserves each .meta's GUID so
    // every reference to the clip keeps working with zero re-wiring.
    //
    // "Uses" is found through AssetDatabase.GetDependencies, NOT by reading SoundData.variants, so it
    // covers every holder of an AudioClip: SoundData variants (and legacy clips), HeroVoiceBank,
    // IntroSequence, AudioSource.clip on prefabs/scenes, Animation Event object parameters
    // (AnimationEventAudio.PlaySound), mixers, ... A holder type added later is picked up for free.
    //
    // Deliberately narrow on WHERE it looks: only project-owned assets (OwnedRoots) plus the enabled
    // build scenes (minus sample/demo scenes) - an Asset Store demo prefab that plays a clip nobody
    // ships is left alone. Untracking/removing the bundle itself is a separate, manual step (see this
    // project's own "untrack Asset Store bundles" commit).
    //
    // Run via Tools > RiftRaiders > Audio. "Report" is a dry run and changes nothing.
    internal static class SoundDataClipConsolidator
    {
        private const string ProjectAudioRoot = "Assets/_Project/Audio";
        private const string DestinationFolder = "Assets/_Project/Audio/Library";

        // Folders whose every asset counts as "ours": each one's DIRECT clip references are collected.
        private static readonly string[] OwnedRoots =
        {
            "Assets/_Project",
            "Assets/_QuantumUser",
            "Assets/Resources",
        };

        // Enabled build scenes under these are sample/demo content, not shipped game - skipped so their
        // (recursive) dependency closure doesn't drag a whole Asset Store demo's audio in.
        private static readonly string[] IgnoredScenePrefixes =
        {
            "Assets/Photon/",
            "Assets/Scenes/",
            "Assets/3rd-party/",
            "Assets/Plugins/",
        };

        [MenuItem("Tools/RiftRaiders/Audio/Report Clips Outside Project Audio", false, 102)]
        private static void Report() => Run(dryRun: true);

        [MenuItem("Tools/RiftRaiders/Audio/Move Used Clips Into Project Audio", false, 103)]
        private static void Move()
        {
            bool ok = EditorUtility.DisplayDialog(
                "Move Used Clips Into Project Audio",
                "This moves every AudioClip used by a project asset (SoundData, animation events, " +
                $"prefabs, scenes, ...) that currently lives outside {ProjectAudioRoot} into {DestinationFolder}.\n\n" +
                "Each clip's GUID is preserved (AssetDatabase.MoveAsset), so every " +
                "reference keeps working - but this still touches the asset database and cannot be " +
                "undone via Ctrl+Z.\n\nRun \"Report Clips Outside Project Audio\" first if you want to see the plan.",
                "Move", "Cancel");

            if (ok) Run(dryRun: false);
        }

        private static void Run(bool dryRun)
        {
            // Read everything first (which assets reference which out-of-place clips) before any
            // write, same "read pass, then batched write pass" shape AudioImportOptimizer uses -
            // AssetDatabase.MoveAsset changes paths out from under a still-running scan.
            Dictionary<string, List<string>> clipToUsers = CollectOutsideClips();

            if (clipToUsers.Count == 0)
            {
                LogHelper.Log("Audio", $"Every used clip already lives under {ProjectAudioRoot}. Nothing to move.");
                return;
            }

            if (!dryRun && !AssetDatabase.IsValidFolder(DestinationFolder))
                CreateFolderRecursive(DestinationFolder);

            var summary = new StringBuilder();
            summary.AppendLine(dryRun
                ? $"Dry run - {clipToUsers.Count} used clip(s) live outside {ProjectAudioRoot}:"
                : $"Moving {clipToUsers.Count} used clip(s) into {DestinationFolder}:");

            try
            {
                if (!dryRun) AssetDatabase.StartAssetEditing();

                foreach (KeyValuePair<string, List<string>> entry in clipToUsers)
                {
                    string sourcePath = entry.Key;
                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = AssetDatabase.GenerateUniqueAssetPath($"{DestinationFolder}/{fileName}");

                    summary.AppendLine($"  {sourcePath} -> {destPath}  (used by: {string.Join(", ", entry.Value)})");

                    if (dryRun) continue;

                    string error = AssetDatabase.MoveAsset(sourcePath, destPath);
                    if (!string.IsNullOrEmpty(error))
                        LogHelper.Error("Audio", $"Failed to move {sourcePath} -> {destPath}: {error}");
                }
            }
            finally
            {
                if (!dryRun) AssetDatabase.StopAssetEditing();
            }

            if (!dryRun)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            LogHelper.Log("Audio", summary.ToString());
        }

        // clip asset path -> names of the assets that use it, for every used AudioClip outside
        // ProjectAudioRoot. Sorted by path so the report (and the move order) is stable run to run.
        private static Dictionary<string, List<string>> CollectOutsideClips()
        {
            var clipToUsers = new SortedDictionary<string, List<string>>(System.StringComparer.Ordinal);

            try
            {
                var rootAssets = new List<string>();
                foreach (string root in OwnedRoots)
                {
                    if (!AssetDatabase.IsValidFolder(root)) continue;

                    foreach (string guid in AssetDatabase.FindAssets("", new[] { root }))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (path.EndsWith(".cs") || AssetDatabase.IsValidFolder(path)) continue;
                        rootAssets.Add(path);
                    }
                }

                for (int i = 0; i < rootAssets.Count; i++)
                {
                    if (i % 250 == 0)
                        EditorUtility.DisplayProgressBar("Scanning audio references", rootAssets[i], (float)i / rootAssets.Count);

                    // Direct references only: a prefab that nests another prefab is scanned as its own
                    // root, and non-recursive keeps "used by" pointing at the asset that really holds
                    // the clip rather than at everything upstream of it.
                    AddOutsideClips(rootAssets[i], recursive: false, clipToUsers);
                }

                // A scene's clips live on prefabs/assets it pulls in, so it needs the full closure.
                foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                {
                    if (!scene.enabled || IsIgnoredScene(scene.path)) continue;

                    EditorUtility.DisplayProgressBar("Scanning audio references", scene.path, 1f);
                    AddOutsideClips(scene.path, recursive: true, clipToUsers);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return new Dictionary<string, List<string>>(clipToUsers);
        }

        private static bool IsIgnoredScene(string scenePath)
        {
            foreach (string prefix in IgnoredScenePrefixes)
            {
                if (scenePath.StartsWith(prefix, System.StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static void AddOutsideClips(string ownerPath, bool recursive, SortedDictionary<string, List<string>> clipToUsers)
        {
            string ownerName = Path.GetFileName(ownerPath);

            foreach (string dependency in AssetDatabase.GetDependencies(ownerPath, recursive))
            {
                // GetDependencies includes the asset itself, and a clip is never "used by" itself.
                if (dependency == ownerPath) continue;
                if (dependency.StartsWith(ProjectAudioRoot + "/", System.StringComparison.Ordinal)) continue;
                if (AssetDatabase.GetMainAssetTypeAtPath(dependency) != typeof(AudioClip)) continue;

                if (!clipToUsers.TryGetValue(dependency, out List<string> users))
                    clipToUsers[dependency] = users = new List<string>();

                if (!users.Contains(ownerName))
                    users.Add(ownerName);
            }
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}

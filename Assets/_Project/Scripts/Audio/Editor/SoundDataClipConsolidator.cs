using System.Collections.Generic;
using System.IO;
using System.Text;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

namespace Project.Audio.EditorTools
{
    // Finds every AudioClip actually referenced by a SoundData asset (variants[].clip) that still
    // lives outside the project's own Audio folder - a leftover Asset Store bundle clip (Epic Toon FX,
    // Universal Sound FX, Ultimate SFX Bundle, ...) a designer wired into a SoundData but never
    // relocated - and moves just those clips into Assets/_Project/Audio/Library via
    // AssetDatabase.MoveAsset, which preserves each .meta's GUID so every SoundData reference (and
    // any other asset pointing at the same clip) keeps working with zero re-wiring.
    //
    // Deliberately narrow: it only ever touches a clip a SoundData actually plays. An Asset Store clip
    // nothing references is left alone - untracking/removing the bundle itself is a separate, manual
    // step (see this project's own "untrack Asset Store bundles" commit).
    //
    // Run via Tools > RiftRaiders > Audio. "Report" is a dry run and changes nothing.
    internal static class SoundDataClipConsolidator
    {
        private const string ProjectAudioRoot = "Assets/_Project/Audio";
        private const string DestinationFolder = "Assets/_Project/Audio/Library";

        [MenuItem("Tools/RiftRaiders/Audio/Report Clips Outside Project Audio", false, 102)]
        private static void Report() => Run(dryRun: true);

        [MenuItem("Tools/RiftRaiders/Audio/Move Used Clips Into Project Audio", false, 103)]
        private static void Move()
        {
            bool ok = EditorUtility.DisplayDialog(
                "Move Used Clips Into Project Audio",
                "This moves every AudioClip referenced by a SoundData asset that currently lives " +
                $"outside {ProjectAudioRoot} into {DestinationFolder}.\n\n" +
                "Each clip's GUID is preserved (AssetDatabase.MoveAsset), so every SoundData/other " +
                "reference keeps working - but this still touches the asset database and cannot be " +
                "undone via Ctrl+Z.\n\nRun \"Report Clips Outside Project Audio\" first if you want to see the plan.",
                "Move", "Cancel");

            if (ok) Run(dryRun: false);
        }

        private static void Run(bool dryRun)
        {
            string[] soundDataGuids = AssetDatabase.FindAssets("t:SoundData");
            if (soundDataGuids.Length == 0)
            {
                LogHelper.Warn("Audio", "No SoundData assets found.");
                return;
            }

            // Read everything first (which SoundData assets reference which out-of-place clips)
            // before any write, same "read pass, then batched write pass" shape AudioImportOptimizer
            // uses - AssetDatabase.MoveAsset changes paths out from under a still-running scan.
            var clipToUsers = new Dictionary<AudioClip, List<string>>();

            foreach (string guid in soundDataGuids)
            {
                string sdPath = AssetDatabase.GUIDToAssetPath(guid);
                var soundData = AssetDatabase.LoadAssetAtPath<SoundData>(sdPath);
                if (soundData == null || soundData.variants == null) continue;

                foreach (SoundClip variant in soundData.variants)
                {
                    if (variant?.clip == null) continue;

                    string clipPath = AssetDatabase.GetAssetPath(variant.clip).Replace('\\', '/');
                    if (clipPath.StartsWith(ProjectAudioRoot)) continue;

                    if (!clipToUsers.TryGetValue(variant.clip, out List<string> users))
                        clipToUsers[variant.clip] = users = new List<string>();

                    users.Add(Path.GetFileNameWithoutExtension(sdPath));
                }
            }

            if (clipToUsers.Count == 0)
            {
                LogHelper.Log("Audio", $"Every SoundData-referenced clip already lives under {ProjectAudioRoot}. Nothing to move.");
                return;
            }

            if (!dryRun && !AssetDatabase.IsValidFolder(DestinationFolder))
                CreateFolderRecursive(DestinationFolder);

            var summary = new StringBuilder();
            summary.AppendLine(dryRun
                ? $"Dry run - {clipToUsers.Count} clip(s) referenced by SoundData live outside {ProjectAudioRoot}:"
                : $"Moving {clipToUsers.Count} clip(s) referenced by SoundData into {DestinationFolder}:");

            try
            {
                if (!dryRun) AssetDatabase.StartAssetEditing();

                foreach (KeyValuePair<AudioClip, List<string>> entry in clipToUsers)
                {
                    string sourcePath = AssetDatabase.GetAssetPath(entry.Key);
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

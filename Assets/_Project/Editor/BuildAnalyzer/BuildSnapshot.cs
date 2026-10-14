using System;
using System.Collections.Generic;
using System.IO;
using QuantumUser.View.Util;
using UnityEngine;

namespace Project.EditorTools.BuildAnalyzer
{
    /// <summary>
    /// Per-asset sizes of one analyzed build, kept per platform in Library/BuildAnalyzer so the next build can
    /// be diffed against it ("leviathan2.png 8.1 MB → 15 KB"). Two files per platform: <c>-last</c> (the most
    /// recently analyzed build) and <c>-prev</c> (the one before it). Re-analyzing the same build keeps diffing
    /// against <c>-prev</c>.
    /// </summary>
    [Serializable]
    internal sealed class BuildSnapshot
    {
        private const string Folder = "Library/BuildAnalyzer";

        public string Guid;
        public string Platform;
        public long BuildTimeTicks;
        public ulong TotalSize;
        public long PackedTotal;
        public List<string> Paths = new List<string>();
        public List<long> Sizes = new List<long>();

        [NonSerialized] private Dictionary<string, long> _map;

        public DateTime BuildTime => new DateTime(BuildTimeTicks);

        /// <summary>Size in that build, or -1 when the asset wasn't in it.</summary>
        public long SizeOf(string path)
        {
            if (_map == null)
            {
                _map = new Dictionary<string, long>(Paths.Count);
                for (int i = 0; i < Paths.Count && i < Sizes.Count; i++)
                    _map[Paths[i]] = Sizes[i];
            }

            return _map.TryGetValue(path ?? "", out long size) ? size : -1;
        }

        /// <summary>
        /// Saves the current build as "last" (rotating the old "last" to "prev" when it is a different build)
        /// and returns the snapshot to diff against, or null on the first analysis of this platform.
        /// Also fills <see cref="AssetEntry.PreviousSize"/>.
        /// </summary>
        public static BuildSnapshot Rotate(BuildAnalysis a)
        {
            BuildSnapshot previous = null;
            try
            {
                Directory.CreateDirectory(Folder);
                string lastPath = FileFor(a.Platform.ToString(), "last");
                string prevPath = FileFor(a.Platform.ToString(), "prev");

                BuildSnapshot last = Load(lastPath);
                if (last != null && last.Guid == a.BuildGuid)
                {
                    previous = Load(prevPath);
                }
                else
                {
                    if (last != null) File.Copy(lastPath, prevPath, true);
                    previous = last;
                    File.WriteAllText(lastPath, JsonUtility.ToJson(From(a)));
                }
            }
            catch (Exception e)
            {
                LogHelper.Warn(BuildAnalysis.LogTag, $"Could not read/write the build size snapshot: {e.Message}");
            }

            if (previous != null)
            {
                foreach (AssetEntry entry in a.Assets)
                    entry.PreviousSize = previous.SizeOf(entry.Path);
            }

            return previous;
        }

        private static BuildSnapshot From(BuildAnalysis a)
        {
            var snapshot = new BuildSnapshot
            {
                Guid = a.BuildGuid,
                Platform = a.Platform.ToString(),
                BuildTimeTicks = a.ReportTimeUtc.ToLocalTime().Ticks,
                TotalSize = a.TotalBuildSize,
                PackedTotal = a.PackedTotal,
            };

            foreach (AssetEntry entry in a.Assets)
            {
                snapshot.Paths.Add(entry.Path);
                snapshot.Sizes.Add(entry.TotalPackedSize);
            }

            return snapshot;
        }

        private static string FileFor(string platform, string slot) => $"{Folder}/{platform}-{slot}.json";

        private static BuildSnapshot Load(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<BuildSnapshot>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
    }
}

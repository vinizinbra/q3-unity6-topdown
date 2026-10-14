using System;
using System.Linq;
using Project.EditorTools.SpriteOptimizer;
using QuantumUser.View.Util;

namespace Project.EditorTools.BuildAnalyzer
{
    /// <summary>
    /// Borrows the Sprite Atlas Optimizer's usage scan (scenes, prefabs, data fields, runtime sightings — see
    /// docs/sprite-atlas-optimizer.md) to tell UI sprites from gameplay sprites, instead of guessing from paths
    /// and referencers. Results go into <see cref="BuildAnalysis.ScannedContexts"/> for the next analysis.
    /// </summary>
    internal static class SpriteUsageContextScan
    {
        /// <summary>Returns false when cancelled or failed.</summary>
        public static bool Run()
        {
            try
            {
                SpriteOptimizerSettings settings = SpriteOptimizerSettings.LoadOrCreate();
                ScanResult scan = SpriteUsageScanner.Run(settings);
                if (scan == null || scan.Cancelled) return false;

                SpriteAtlasAnalysis.Run(scan, settings); // resolves UsedUI / UsedGameplay per sprite

                BuildAnalysis.ScannedContexts.Clear();
                foreach (SpriteOptimizer.TextureInfo texture in scan.Textures.Values)
                {
                    bool ui = texture.Sprites.Values.Any(s => s.UsedUI);
                    bool gameplay = texture.Sprites.Values.Any(s => s.UsedGameplay);
                    BuildAnalysis.ScannedContexts[texture.Path] =
                        ui && gameplay ? UsageContext.Both : ui ? UsageContext.UI : gameplay ? UsageContext.Gameplay : UsageContext.Unknown;
                }

                BuildAnalysis.ScannedContextsAt = DateTime.Now;
                LogHelper.Log(BuildAnalysis.LogTag, $"Sprite usage scan: {BuildAnalysis.ScannedContexts.Count} textures classified UI / Gameplay in {scan.Seconds:0.0}s.");
                return true;
            }
            catch (Exception e)
            {
                LogHelper.Error(BuildAnalysis.LogTag, $"Sprite usage scan failed: {e}");
                return false;
            }
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Project.EditorTools.BuildAnalyzer
{
    /// <summary>
    /// Finds the report of the last player build. Unity keeps it at Library/LastBuild.buildreport;
    /// the public API to read it is <c>BuildReport.GetLatestReport()</c> (Unity 2022.2+). It is called
    /// through reflection so this file compiles regardless of the exact editor version, and if it is
    /// missing or returns nothing the report is copied under Assets/ and loaded through the AssetDatabase.
    /// </summary>
    internal static class BuildReportLoader
    {
        public const string LogTag = "BuildAnalyzer";

        private const string LibraryReportPath = "Library/LastBuild.buildreport";
        private const string FallbackFolder = "Assets/BuildReports";
        private const string FallbackAssetPath = FallbackFolder + "/LastBuild.buildreport";

        public static bool HasReportFile => File.Exists(LibraryReportPath);

        public static DateTime ReportFileTime =>
            HasReportFile ? File.GetLastWriteTime(LibraryReportPath) : DateTime.MinValue;

        public static BuildReport LoadLatest(out string error)
        {
            error = null;

            BuildReport report = TryGetLatestReportApi();
            if (report != null)
                return report;

            if (!HasReportFile)
            {
                error = $"No build report found at '{LibraryReportPath}'. Make a player build first.";
                return null;
            }

            try
            {
                if (!AssetDatabase.IsValidFolder(FallbackFolder))
                    AssetDatabase.CreateFolder("Assets", "BuildReports");

                File.Copy(LibraryReportPath, FallbackAssetPath, true);
                AssetDatabase.ImportAsset(FallbackAssetPath, ImportAssetOptions.ForceSynchronousImport);
                report = AssetDatabase.LoadAssetAtPath<BuildReport>(FallbackAssetPath);
            }
            catch (Exception e)
            {
                error = $"Could not copy/import the build report: {e.Message}";
                return null;
            }

            if (report == null)
            {
                error = $"Copied the report to '{FallbackAssetPath}' but it did not import as a BuildReport.";
                return null;
            }

            LogHelper.Log(LogTag,
                $"Loaded build report through a copy at '{FallbackAssetPath}'. Consider adding that folder to .gitignore.");
            return report;
        }

        private static BuildReport TryGetLatestReportApi()
        {
            try
            {
                MethodInfo method = typeof(BuildReport).GetMethod(
                    "GetLatestReport",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, Type.EmptyTypes, null);

                return method?.Invoke(null, null) as BuildReport;
            }
            catch (Exception e)
            {
                LogHelper.Warn(LogTag, $"BuildReport.GetLatestReport() failed, falling back to a file copy: {e.Message}");
                return null;
            }
        }

        /// <summary>Platform name as used by <see cref="TextureImporter.GetPlatformTextureSettings(string)"/>.</summary>
        public static string TexturePlatformName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iPhone";
                case BuildTarget.WebGL: return "WebGL";
                case BuildTarget.tvOS: return "tvOS";
            }

            return target.ToString().StartsWith("Standalone", StringComparison.Ordinal)
                ? "Standalone"
                : target.ToString();
        }

        /// <summary>Platform name as used by <see cref="AudioImporter.GetOverrideSampleSettings(string)"/>.</summary>
        public static string AudioPlatformName(BuildTarget target)
        {
            try
            {
                BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
                return NamedBuildTarget.FromBuildTargetGroup(group).TargetName;
            }
            catch
            {
                return target.ToString().StartsWith("Standalone", StringComparison.Ordinal)
                    ? "Standalone"
                    : target.ToString();
            }
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class GameBuilder : MonoBehaviour
{
    [MenuItem("Build/PC")]
    public static void PerformStandaloneBuild()
    {
        BuildPlayerOptions bpo = new BuildPlayerOptions();
        bpo.scenes = new[] { "Assets/Scenes/SampleScene.unity" };
        bpo.locationPathName = "build/Windows/teste.exe";
        bpo.target = BuildTarget.StandaloneWindows;
        bpo.options = BuildOptions.None;
        Debug.Log("Building for Windows");


        BuildReport report = BuildPipeline.BuildPlayer(bpo);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log("build succed =>" + summary.totalSize + "bytes");
        }

        if (summary.result == BuildResult.Failed)
        {




            Debug.Log("build failed");
        }

    }
    [MenuItem("Build/Android")]
    public static void PerformAndroidBuild()
    {
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = new[] { "Assets/Scenes/Tutorial.unity","Assets/_Project/Scenes/RunRaceMenu.unity", "Assets/Scenes/HeroRoyaleGameplayNewScene.unity" };
        buildPlayerOptions.locationPathName = "build/Android/HeroRoyale.apk";
        buildPlayerOptions.target = BuildTarget.Android;
        buildPlayerOptions.targetGroup = BuildTargetGroup.Android;
        buildPlayerOptions.options = BuildOptions.CompressWithLz4;
        buildPlayerOptions.options |= BuildOptions.Development;
        buildPlayerOptions.options |= BuildOptions.ConnectWithProfiler;
        AndroidArchitecture aac = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
        PlayerSettings.Android.targetArchitectures = aac;
        EditorUserBuildSettings.buildAppBundle = false;
        

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log("build succed =>" + summary.totalSize + "bytes");
        }

        if (summary.result == BuildResult.Failed)
        {
            Debug.Log("build failed");
        }
    }

    static string[] GetScenesFromBuildSettings()
    {
        List<string> sceneList = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if(scene.enabled)
                sceneList.Add(scene.path);
        }

        return sceneList.ToArray();
    }
    // Diagnostic build: a RELEASE player (no Development flag - the Development WebGL TLS module
    // in Unity 6000.3.6f1 fails to link with "undefined symbol unitytls_ssl_set_client_transport_id")
    // with the RR_DIAG_LOGS define, which re-enables LogHelper.Log/Warn so the browser console isn't
    // empty. Also the entry point LanBuildServerWindow's and TeamCity's "Build WebGL" call.
    // "Build/WebGL (Release)" is the same build without the log define, for shipping.
    [MenuItem("Build/WebGL")]
    public static void PerformWebBuild() => BuildWeb(BuildOptions.None, withDiagLogs: true);

    [MenuItem("Build/WebGL (Release)")]
    public static void PerformWebReleaseBuild() => BuildWeb(BuildOptions.None, withDiagLogs: false);

    private static void BuildWeb(BuildOptions options, bool withDiagLogs)
    {
        BuildPlayerOptions bpo = new BuildPlayerOptions();

        bpo.scenes = GetScenesFromBuildSettings();
        var path = "build/WebGl/";
        if (Directory.Exists(path)) { Directory.Delete(path, true); }
        Directory.CreateDirectory(path);

        bpo.locationPathName = "build/WebGl/";
        
        bpo.target = BuildTarget.WebGL;
        bpo.options = options;
        if (withDiagLogs) bpo.extraScriptingDefines = new[] { "RR_DIAG_LOGS" };
        BuildReport report = BuildPipeline.BuildPlayer(bpo);
        BuildSummary summary = report.summary;
        PlayerSettings.WebGL.initialMemorySize = 512;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log("build Succeeded");

            //BuildReportTool.ReportGenerator.CreateReport();
            //CopyLastBuildReport();
        }

        if (summary.result == BuildResult.Failed)
        {
            Debug.Log("build failed");
        }

    }
    
    public static void CopyLastBuildReport()
    {
        const string buildReportDir = "build/WebGl/";

        var date = File.GetLastWriteTime("UnityBuildReports/LastBuild.buildreport");
        var assetPath = buildReportDir + "/Build_" + date.ToString("yyyy-dd-MMM-HH-mm-ss") + ".xml";
        File.Copy("Library/LastBuild.buildreport", assetPath, true);
    }

}

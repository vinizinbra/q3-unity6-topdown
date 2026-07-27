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
    [MenuItem("Build/WebGL")]
    public static void PerformWebBuild()
    {
        BuildPlayerOptions bpo = new BuildPlayerOptions();

        bpo.scenes = GetScenesFromBuildSettings();
        var path = "build/WebGl/";
        if (Directory.Exists(path)) { Directory.Delete(path, true); }
        Directory.CreateDirectory(path);

        bpo.locationPathName = "build/WebGl/";
        
        bpo.target = BuildTarget.WebGL;
        bpo.options = BuildOptions.None;
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

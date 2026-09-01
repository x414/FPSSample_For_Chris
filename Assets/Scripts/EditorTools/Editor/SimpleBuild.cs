using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

public static class SimpleBuild
{
    [MenuItem("FPS Sample/BuildSystem/Win64/SimpleBuild")]
    public static void BuildWindows64()
    {
        Debug.Log("SimpleBuild: Starting Windows 64 build...");
        var target = BuildTarget.StandaloneWindows64;
        var buildPath = "Build/Windows64";
        var exeName = "FPSSample.exe";
        var exePath = Path.Combine(buildPath, exeName);

        Directory.CreateDirectory(buildPath);

        var levels = new string[]
        {
            "Assets/Scenes/bootstrapper.unity",
            "Assets/Scenes/empty.unity"
        };

        var options = BuildOptions.None;
        var report = BuildPipeline.BuildPlayer(levels, exePath, target, options);
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log("SimpleBuild: Build succeeded! Output: " + exePath);
            Debug.Log("SimpleBuild: Build size: " + report.summary.totalSize / (1024*1024) + " MB");
        }
        else
        {
            Debug.LogError("SimpleBuild: Build failed: " + report.summary.result);
        }
    }

    [MenuItem("FPS Sample/BuildSystem/Win64/BuildBundlesOnly")]
    public static void BuildBundlesOnly()
    {
        Debug.Log("Building asset bundles only...");
        var path = "Build/Windows64/AssetBundles";
        Directory.CreateDirectory(path);
        BuildTools.BuildBundles(path, BuildTarget.StandaloneWindows64, true, true, false);
        Debug.Log("Asset bundles built!");
    }
}

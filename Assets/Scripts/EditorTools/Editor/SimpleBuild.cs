using System;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

public static class SimpleBuild
{
    [MenuItem("FPS Sample/BuildSystem/Win64/SimpleBuild")]
    public static void BuildWindows64()
    {
        var buildStartedUtc = DateTime.UtcNow;
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
            VerifyFreshOutput(buildPath, exeName, buildStartedUtc);
            WriteBuildInfo(buildPath, exeName, buildStartedUtc, report);
            Debug.Log("SimpleBuild: Build succeeded! Output: " + exePath);
            Debug.Log("SimpleBuild: Build size: " + report.summary.totalSize / (1024*1024) + " MB");
        }
        else
        {
            Debug.LogError("SimpleBuild: Build failed: " + report.summary.result);
            throw new System.Exception("SimpleBuild failed: " + report.summary.result);
        }
    }

    static void VerifyFreshOutput(string buildPath, string exeName, DateTime buildStartedUtc)
    {
        var executablePath = Path.Combine(buildPath, exeName);
        var assemblyPath = Path.Combine(buildPath, "FPSSample_Data/Managed/Assembly-CSharp.dll");

        if (!File.Exists(executablePath) || !File.Exists(assemblyPath))
            throw new System.Exception("Build output is missing: " + executablePath + " or " + assemblyPath);

        var executableTime = File.GetLastWriteTimeUtc(executablePath);
        var assemblyTime = File.GetLastWriteTimeUtc(assemblyPath);
        if (executableTime < buildStartedUtc || assemblyTime < buildStartedUtc)
            throw new System.Exception("Build output is stale; executable and Assembly-CSharp.dll must be newer than build start.");
    }

    static void WriteBuildInfo(
        string buildPath,
        string exeName,
        DateTime buildStartedUtc,
        UnityEditor.Build.Reporting.BuildReport report)
    {
        var executablePath = Path.Combine(buildPath, exeName);
        var assemblyPath = Path.Combine(buildPath, "FPSSample_Data/Managed/Assembly-CSharp.dll");
        var buildInfo = new BuildInfo
        {
            buildCompletedUtc = DateTime.UtcNow.ToString("o"),
            buildCompletedLocal = DateTime.Now.ToString("o"),
            buildStartedUtc = buildStartedUtc.ToString("o"),
            buildStartedLocal = buildStartedUtc.ToLocalTime().ToString("o"),
            buildGuid = report.summary.guid.ToString(),
            totalSizeBytes = checked((long)report.summary.totalSize),
            unityVersion = Application.unityVersion,
            executableLastWriteTimeUtc = File.GetLastWriteTimeUtc(executablePath).ToString("o"),
            executableLastWriteTimeLocal = File.GetLastWriteTime(executablePath).ToString("o"),
            assemblyLastWriteTimeUtc = File.GetLastWriteTimeUtc(assemblyPath).ToString("o"),
            assemblyLastWriteTimeLocal = File.GetLastWriteTime(assemblyPath).ToString("o"),
            timestampVerified = true
        };

        File.WriteAllText(
            Path.Combine(buildPath, "BuildInfo.json"),
            JsonUtility.ToJson(buildInfo, true));
    }

    [System.Serializable]
    class BuildInfo
    {
        public string buildCompletedUtc;
        public string buildCompletedLocal;
        public string buildStartedUtc;
        public string buildStartedLocal;
        public string buildGuid;
        public long totalSizeBytes;
        public string unityVersion;
        public string executableLastWriteTimeUtc;
        public string executableLastWriteTimeLocal;
        public string assemblyLastWriteTimeUtc;
        public string assemblyLastWriteTimeLocal;
        public bool timestampVerified;
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

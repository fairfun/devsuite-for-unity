using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildUtil
{
    [MenuItem("Build/Build WebGL")]
    public static void BuildWebGL()
    {
        PerformBuild(BuildTarget.WebGL, "Build/WebGL");
    }

    [MenuItem("Build/Build Sample WebGL")]
    public static void BuildSampleWebGL()
    {
        var sampleScene = GetSampleScenePath();
        if (string.IsNullOrEmpty(sampleScene))
        {
            Debug.LogError("[BuildUtil] Could not locate Asteroids sample scene!");
            return;
        }

        PerformBuild(BuildTarget.WebGL, "Build/WebGL_Sample", new[] { sampleScene });
    }

    [MenuItem("Build/Build Linux")]
    public static void BuildLinux()
    {
        PerformBuild(BuildTarget.StandaloneLinux64, "Build/Linux");
    }

    [MenuItem("Build/Build Android")]
    public static void BuildAndroid()
    {
        PerformBuild(BuildTarget.Android, "Build/Android");
    }

    private const string SampleSceneDefaultPath = "Assets/Samples/Asteroids/Asteroids.unity";

    public static string GetSampleScenePath()
    {
        if (File.Exists(SampleSceneDefaultPath))
        {
            return SampleSceneDefaultPath;
        }

        var guids = AssetDatabase.FindAssets("t:Scene Asteroids");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith("Asteroids.unity", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        const string packageSamplesPath = "Assets/DevSuite/Samples~/Asteroids/Asteroids.unity";
        if (File.Exists(packageSamplesPath))
        {
            return packageSamplesPath;
        }

        return SampleSceneDefaultPath;
    }

    public static void Build()
    {
        var target = BuildTarget.WebGL;
        var outputPath = "Build/WebGL";
        string[] customScenes = null;

        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "-buildTarget" && i + 1 < args.Length)
            {
                if (Enum.TryParse(args[i + 1], true, out BuildTarget parsedTarget))
                {
                    target = parsedTarget;
                }
            }
            if (args[i] == "-outputPath" && i + 1 < args.Length)
            {
                outputPath = args[i + 1];
            }
            if (args[i] == "-sampleScene")
            {
                customScenes = new[] { GetSampleScenePath() };
            }
            if (args[i] == "-scenePath" && i + 1 < args.Length)
            {
                customScenes = new[] { args[i + 1] };
            }
        }

        PerformBuild(target, outputPath, customScenes);
    }

    private static void PerformBuild(BuildTarget target, string outputPath, string[] customScenes = null)
    {
        if (target == BuildTarget.Android && !outputPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(outputPath) || !Path.HasExtension(outputPath))
            {
                outputPath = Path.Combine(outputPath, "build.apk");
            }
            else
            {
                outputPath = Path.ChangeExtension(outputPath, ".apk");
            }
        }

        if (target == BuildTarget.StandaloneLinux64)
        {
            if (outputPath.EndsWith("StandaloneLinux64", StringComparison.OrdinalIgnoreCase))
            {
                outputPath = outputPath.Substring(0, outputPath.Length - "StandaloneLinux64".Length) + "Linux";
            }

            if (!outputPath.EndsWith(".x86_64", StringComparison.OrdinalIgnoreCase))
            {
                var exeName = string.IsNullOrEmpty(PlayerSettings.productName) ? "xArena" : PlayerSettings.productName;
                if (Directory.Exists(outputPath) || !Path.HasExtension(outputPath))
                {
                    outputPath = Path.Combine(outputPath, $"{exeName}.x86_64");
                }
                else
                {
                    outputPath = Path.ChangeExtension(outputPath, ".x86_64");
                }
            }
        }

        Debug.Log($"[BuildUtil] Starting build for target: {target} to path: {outputPath}");

        var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(target);

        var scenes = customScenes != null && customScenes.Length > 0
            ? customScenes
            : EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

        if (scenes.Length == 0)
        {
            scenes = AssetDatabase.FindAssets("t:Scene")
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .ToArray();
            Debug.LogWarning($"[BuildUtil] No scenes enabled in EditorBuildSettings. Falling back to all found scenes: {string.Join(", ", scenes)}");
        }

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = target,
            targetGroup = buildTargetGroup,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        switch (summary.result)
        {
            case BuildResult.Succeeded:
                Debug.Log($"[BuildUtil] Build Succeeded! Output size: {summary.totalSize} bytes");
                break;

            case BuildResult.Failed:
                Debug.LogError($"[BuildUtil] Build Failed with {summary.totalErrors} errors!");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
                break;
        }
    }
}
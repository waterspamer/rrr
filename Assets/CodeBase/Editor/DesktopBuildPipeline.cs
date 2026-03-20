using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class DesktopBuildPipeline
{
    private const string DefaultOutputRoot = "Builds/Windows64";
    private const string DefaultBuildName = "Latest";

    [MenuItem("Tools/Build/Build Windows x64")]
    public static void BuildFromMenu()
    {
        string outputPath = Path.GetFullPath(Path.Combine(DefaultOutputRoot, DefaultBuildName));
        Build(outputPath);
    }

    public static BuildReport Build(string outputDirectory)
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes found in EditorBuildSettings.");

        Directory.CreateDirectory(outputDirectory);
        string executablePath = Path.Combine(outputDirectory, GetExecutableFileName());

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = executablePath,
            options = BuildOptions.None
        };

        Debug.Log($"Windows x64 build started. Output: {executablePath}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Windows x64 build failed: {report.summary.result}");

        Debug.Log($"Windows x64 build completed successfully. Size: {report.summary.totalSize} bytes.");
        return report;
    }

    public static string GetExecutableFileName()
    {
        string productName = string.IsNullOrWhiteSpace(PlayerSettings.productName)
            ? "RussianRoadRage"
            : PlayerSettings.productName;
        return SanitizeFileName(productName) + ".exe";
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        char[] buffer = value.ToCharArray();
        for (int i = 0; i < buffer.Length; i++)
        {
            if (Array.IndexOf(invalidChars, buffer[i]) >= 0)
                buffer[i] = '_';
        }

        return new string(buffer).Trim();
    }
}

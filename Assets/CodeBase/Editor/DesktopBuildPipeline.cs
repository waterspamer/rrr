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
    private const string OutputDirectoryEnvVar = "RRR_DESKTOP_OUTPUT_DIR";
    private const string ReleaseIdEnvVar = "RRR_RELEASE_ID";
    private const string ReleaseCommitEnvVar = "RRR_RELEASE_COMMIT";
    private const string ReleaseBranchEnvVar = "RRR_RELEASE_BRANCH";
    private const string ReleasePublicUrlEnvVar = "RRR_RELEASE_PUBLIC_URL";

    [MenuItem("Tools/Build/Build Windows x64")]
    public static void BuildFromMenu()
    {
        string outputPath = Path.GetFullPath(Path.Combine(DefaultOutputRoot, DefaultBuildName));
        Build(outputPath);
    }

    public static void BuildFromCommandLine()
    {
        string outputDirectory = Environment.GetEnvironmentVariable(OutputDirectoryEnvVar);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException($"Missing environment variable: {OutputDirectoryEnvVar}");

        BuildReport report = Build(outputDirectory);
        WriteReleaseMetadata(outputDirectory, report);
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

    private static void WriteReleaseMetadata(string outputDirectory, BuildReport report)
    {
        ReleaseMetadata metadata = new ReleaseMetadata
        {
            releaseId = Environment.GetEnvironmentVariable(ReleaseIdEnvVar) ?? string.Empty,
            commit = Environment.GetEnvironmentVariable(ReleaseCommitEnvVar) ?? string.Empty,
            branch = Environment.GetEnvironmentVariable(ReleaseBranchEnvVar) ?? string.Empty,
            builtAtUtc = DateTime.UtcNow.ToString("O"),
            target = BuildTarget.StandaloneWindows64.ToString(),
            compression = "None",
            publicUrl = Environment.GetEnvironmentVariable(ReleasePublicUrlEnvVar) ?? string.Empty,
            primaryArtifact = GetExecutableFileName(),
            totalSizeBytes = report.summary.totalSize
        };

        File.WriteAllText(
            Path.Combine(outputDirectory, "release.json"),
            JsonUtility.ToJson(metadata, prettyPrint: true));
    }

    [Serializable]
    private sealed class ReleaseMetadata
    {
        public string releaseId;
        public string commit;
        public string branch;
        public string builtAtUtc;
        public string target;
        public string compression;
        public string publicUrl;
        public string primaryArtifact;
        public ulong totalSizeBytes;
    }
}

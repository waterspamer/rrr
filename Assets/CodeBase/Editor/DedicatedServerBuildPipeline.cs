using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditorInternal;
using UnityEngine;

public static class DedicatedServerBuildPipeline
{
    private const string DefaultOutputRoot = "Builds/DedicatedServer";
    private const string DefaultBuildName = "Latest";
    private const string OutputDirectoryEnvVar = "RRR_DEDICATED_OUTPUT_DIR";
    private const string ReleaseIdEnvVar = "RRR_RELEASE_ID";
    private const string ReleaseCommitEnvVar = "RRR_RELEASE_COMMIT";
    private const string ReleaseBranchEnvVar = "RRR_RELEASE_BRANCH";
    private const string ReleasePublicUrlEnvVar = "RRR_RELEASE_PUBLIC_URL";
    private const string DedicatedScenesArg = "-rrrDedicatedScenes";
    private const string DedicatedTargetArg = "-rrrDedicatedTarget";
    private const string DefaultPurrNetBindAddress = "0.0.0.0";
    private const string DefaultPurrNetPort = "5000";
    private const string DefaultPurrNetTickRate = "30";
    private const string DefaultPurrNetSoloBots = "1";
    private const string DefaultPurrNetAutoCloseSoloSession = "1";
    private const string DefaultPurrNetSoloIdleTimeoutSec = "30";
    private const string DefaultPurrNetSoloIdlePollSec = "0.5";
    private static readonly string[] DefaultDedicatedScenes = { "Assets/Scenes/Game.unity" };
    private static readonly string[] RequiredTags = { "Obstacle", "Weapon" };

    [MenuItem("Tools/Build/Build Linux Dedicated Server")]
    public static void BuildFromMenu()
    {
        string outputPath = Path.GetFullPath(Path.Combine(DefaultOutputRoot, DefaultBuildName));
        Build(outputPath, DefaultDedicatedScenes);
    }

    public static void BuildFromCommandLine()
    {
        string outputDirectory = Environment.GetEnvironmentVariable(OutputDirectoryEnvVar);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException($"Missing environment variable: {OutputDirectoryEnvVar}");

        string[] scenes = GetCommandLineScenes();
        BuildTarget buildTarget = GetCommandLineTarget();
        BuildReport report = Build(outputDirectory, scenes, buildTarget);
        WriteReleaseMetadata(outputDirectory, report, scenes, buildTarget);
        WriteLaunchScripts(outputDirectory, buildTarget);
        WriteRuntimeConfigTemplate(outputDirectory);
    }

    public static BuildReport Build(string outputDirectory, string[] scenes = null, BuildTarget buildTarget = BuildTarget.StandaloneLinux64)
    {
        string[] buildScenes = ResolveScenes(scenes);
        if (buildScenes.Length == 0)
            throw new InvalidOperationException("No dedicated server scenes were resolved.");
        ValidateRequiredTags();

        Directory.CreateDirectory(outputDirectory);
        string executablePath = Path.Combine(outputDirectory, GetExecutableFileName(buildTarget));

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = buildScenes,
            target = buildTarget,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            locationPathName = executablePath,
            options = BuildOptions.None
        };

        Debug.Log($"Dedicated Server build started. Target: {buildTarget}. Output: {executablePath}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Dedicated Server build failed: {report.summary.result}");

        WriteLaunchScripts(outputDirectory, buildTarget);
        WriteRuntimeConfigTemplate(outputDirectory);
        Debug.Log($"Dedicated Server build completed successfully. Target: {buildTarget}. Size: {report.summary.totalSize} bytes.");
        return report;
    }

    public static string GetExecutableFileName(BuildTarget buildTarget = BuildTarget.StandaloneLinux64)
    {
        string productName = string.IsNullOrWhiteSpace(PlayerSettings.productName)
            ? "RussianRoadRageServer"
            : PlayerSettings.productName + "Server";
        string executableName = SanitizeFileName(productName);
        return buildTarget == BuildTarget.StandaloneWindows64 ? executableName + ".exe" : executableName;
    }

    private static string[] GetCommandLineScenes()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], DedicatedScenesArg, StringComparison.OrdinalIgnoreCase))
                continue;

            string rawValue = args[index + 1];
            if (string.IsNullOrWhiteSpace(rawValue))
                break;

            return rawValue
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return DefaultDedicatedScenes;
    }

    private static BuildTarget GetCommandLineTarget()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], DedicatedTargetArg, StringComparison.OrdinalIgnoreCase))
                continue;

            return ParseBuildTarget(args[index + 1]);
        }

        return BuildTarget.StandaloneLinux64;
    }

    private static BuildTarget ParseBuildTarget(string rawTarget)
    {
        if (string.IsNullOrWhiteSpace(rawTarget))
            return BuildTarget.StandaloneLinux64;

        string normalized = rawTarget.Trim().ToLowerInvariant();
        if (normalized == "windows" || normalized == "win" || normalized == "win64" || normalized == "standalonewindows64")
            return BuildTarget.StandaloneWindows64;
        if (normalized == "linux" || normalized == "linux64" || normalized == "standalonelinux64")
            return BuildTarget.StandaloneLinux64;

        throw new InvalidOperationException($"Unsupported dedicated target: {rawTarget}");
    }

    private static string[] ResolveScenes(string[] requestedScenes)
    {
        string[] candidates = requestedScenes == null || requestedScenes.Length == 0
            ? DefaultDedicatedScenes
            : requestedScenes;

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ValidateScenePath)
            .ToArray();
    }

    private static string ValidateScenePath(string scenePath)
    {
        string absolutePath = Path.GetFullPath(scenePath);
        if (!File.Exists(absolutePath))
            throw new FileNotFoundException($"Dedicated server scene not found: {scenePath}", scenePath);

        return scenePath;
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

    private static void ValidateRequiredTags()
    {
        string[] definedTags = InternalEditorUtility.tags ?? Array.Empty<string>();
        for (int i = 0; i < RequiredTags.Length; i++)
        {
            if (definedTags.Contains(RequiredTags[i], StringComparer.Ordinal))
                continue;

            throw new InvalidOperationException(
                $"Dedicated server build requires Unity tag '{RequiredTags[i]}' because runtime collision/damage filtering depends on it.");
        }
    }

    private static void WriteReleaseMetadata(string outputDirectory, BuildReport report, string[] scenes, BuildTarget buildTarget)
    {
        ReleaseMetadata metadata = new ReleaseMetadata
        {
            releaseId = Environment.GetEnvironmentVariable(ReleaseIdEnvVar) ?? string.Empty,
            commit = Environment.GetEnvironmentVariable(ReleaseCommitEnvVar) ?? string.Empty,
            branch = Environment.GetEnvironmentVariable(ReleaseBranchEnvVar) ?? string.Empty,
            builtAtUtc = DateTime.UtcNow.ToString("O"),
            target = buildTarget.ToString(),
            subtarget = StandaloneBuildSubtarget.Server.ToString(),
            publicUrl = Environment.GetEnvironmentVariable(ReleasePublicUrlEnvVar) ?? string.Empty,
            primaryArtifact = GetExecutableFileName(buildTarget),
            launchScript = "run.sh",
            totalSizeBytes = report.summary.totalSize,
            scenes = scenes ?? Array.Empty<string>()
        };

        File.WriteAllText(
            Path.Combine(outputDirectory, "release.json"),
            JsonUtility.ToJson(metadata, prettyPrint: true));
    }

    private static void WriteLaunchScripts(string outputDirectory, BuildTarget buildTarget)
    {
        string scriptPath = Path.Combine(outputDirectory, "run.sh");
        string executableName = GetExecutableFileName(buildTarget);
        string script = "#!/usr/bin/env sh\n" +
                        "set -eu\n" +
                        "SCRIPT_DIR=\"$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd)\"\n" +
                        $"RRR_PURRNET_ADDRESS=\"${{RRR_PURRNET_ADDRESS:-{DefaultPurrNetBindAddress}}}\"\n" +
                        $"RRR_PURRNET_PORT=\"${{RRR_PURRNET_PORT:-{DefaultPurrNetPort}}}\"\n" +
                        $"RRR_PURRNET_TICK_RATE=\"${{RRR_PURRNET_TICK_RATE:-{DefaultPurrNetTickRate}}}\"\n" +
                        $"RRR_PURRNET_SOLO_BOTS=\"${{RRR_PURRNET_SOLO_BOTS:-{DefaultPurrNetSoloBots}}}\"\n" +
                        $"RRR_PURRNET_AUTO_CLOSE_SOLO_SESSION=\"${{RRR_PURRNET_AUTO_CLOSE_SOLO_SESSION:-{DefaultPurrNetAutoCloseSoloSession}}}\"\n" +
                        $"RRR_PURRNET_SOLO_IDLE_TIMEOUT_SEC=\"${{RRR_PURRNET_SOLO_IDLE_TIMEOUT_SEC:-{DefaultPurrNetSoloIdleTimeoutSec}}}\"\n" +
                        $"RRR_PURRNET_SOLO_IDLE_POLL_SEC=\"${{RRR_PURRNET_SOLO_IDLE_POLL_SEC:-{DefaultPurrNetSoloIdlePollSec}}}\"\n" +
                        $"exec \"$SCRIPT_DIR/{executableName}\" -batchmode -nographics -rrrNetMode server -rrrNetAddress \"$RRR_PURRNET_ADDRESS\" -rrrNetPort \"$RRR_PURRNET_PORT\" -rrrNetTickRate \"$RRR_PURRNET_TICK_RATE\" -rrrNetSoloBots \"$RRR_PURRNET_SOLO_BOTS\" \"$@\"\n";
        File.WriteAllText(scriptPath, script);

        if (buildTarget != BuildTarget.StandaloneWindows64)
            return;

        string cmdPath = Path.Combine(outputDirectory, "run.cmd");
        string cmdScript = "@echo off\r\n" +
                           "setlocal\r\n" +
                           "set SCRIPT_DIR=%~dp0\r\n" +
                           $"\"%SCRIPT_DIR%{executableName}\" -batchmode -nographics %*\r\n";
        File.WriteAllText(cmdPath, cmdScript);
    }

    private static void WriteRuntimeConfigTemplate(string outputDirectory)
    {
        string templatePath = Path.Combine(outputDirectory, "server.env.example");
        string template = "RRR_PURRNET_ADDRESS=0.0.0.0\n" +
                          "RRR_PURRNET_PORT=5000\n" +
                          "RRR_PURRNET_TICK_RATE=30\n" +
                          "RRR_PURRNET_SOLO_BOTS=1\n" +
                          "RRR_PURRNET_AUTO_CLOSE_SOLO_SESSION=1\n" +
                          "RRR_PURRNET_SOLO_IDLE_TIMEOUT_SEC=30\n" +
                          "RRR_PURRNET_SOLO_IDLE_POLL_SEC=0.5\n" +
                          "RRR_PURRNET_MATCH_ID=purrnet-live\n" +
                          "RRR_PURRNET_MAP_ID=city_default\n" +
                          "\n" +
                          "RRR_MATCH_BACKEND_URL=http://127.0.0.1:8083\n" +
                          "RRR_DEDICATED_BIND=0.0.0.0\n" +
                          "RRR_DEDICATED_PORT=7777\n" +
                          "RRR_DEDICATED_LOG_LEVEL=info\n" +
                          "RRR_DEDICATED_CONTROL_TOKEN=\n" +
                          "RRR_DEDICATED_PUBLIC_HTTP_BASE_URL=http://127.0.0.1:7777\n" +
                          "RRR_DEDICATED_PUBLIC_WS_BASE_URL=ws://127.0.0.1:7777\n";
        File.WriteAllText(templatePath, template);
    }

    [Serializable]
    private sealed class ReleaseMetadata
    {
        public string releaseId;
        public string commit;
        public string branch;
        public string builtAtUtc;
        public string target;
        public string subtarget;
        public string publicUrl;
        public string primaryArtifact;
        public string launchScript;
        public ulong totalSizeBytes;
        public string[] scenes;
    }
}

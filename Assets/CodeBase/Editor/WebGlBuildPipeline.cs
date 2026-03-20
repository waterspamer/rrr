using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebGlBuildPipeline
{
    private const string DefaultOutputRoot = "Builds/WebGL";
    private const string DefaultBuildName = "Latest";
    private const string BuildPathArg = "-rrrBuildPath";
    private const string CompressionArg = "-rrrWebGlCompression";

    [MenuItem("Tools/Build/Build WebGL")]
    public static void BuildFromMenu()
    {
        string outputPath = Path.GetFullPath(Path.Combine(DefaultOutputRoot, DefaultBuildName));
        ExecuteBuild(outputPath, WebGLCompressionFormat.Disabled);
    }

    public static void BuildFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        string outputPath = GetCommandLineValue(args, BuildPathArg);
        string compression = GetCommandLineValue(args, CompressionArg);

        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = Path.GetFullPath(Path.Combine(DefaultOutputRoot, DefaultBuildName));

        WebGLCompressionFormat compressionFormat = ParseCompression(compression);
        ExecuteBuild(outputPath, compressionFormat);
    }

    private static void ExecuteBuild(string outputPath, WebGLCompressionFormat compressionFormat)
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes found in EditorBuildSettings.");

        Directory.CreateDirectory(outputPath);

        WebGLCompressionFormat previousCompression = PlayerSettings.WebGL.compressionFormat;
        bool previousDecompressionFallback = PlayerSettings.WebGL.decompressionFallback;

        try
        {
            PlayerSettings.WebGL.compressionFormat = compressionFormat;
            PlayerSettings.WebGL.decompressionFallback = compressionFormat != WebGLCompressionFormat.Disabled;

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                target = BuildTarget.WebGL,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                locationPathName = outputPath,
                options = BuildOptions.None
            };

            Debug.Log($"WebGL build started. Output: {outputPath}, compression: {compressionFormat}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"WebGL build failed: {report.summary.result}");

            Debug.Log($"WebGL build completed successfully. Size: {report.summary.totalSize} bytes.");
        }
        finally
        {
            PlayerSettings.WebGL.compressionFormat = previousCompression;
            PlayerSettings.WebGL.decompressionFallback = previousDecompressionFallback;
        }
    }

    private static WebGLCompressionFormat ParseCompression(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return WebGLCompressionFormat.Disabled;

        if (string.Equals(rawValue, "gzip", StringComparison.OrdinalIgnoreCase))
            return WebGLCompressionFormat.Gzip;
        if (string.Equals(rawValue, "brotli", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawValue, "br", StringComparison.OrdinalIgnoreCase))
            return WebGLCompressionFormat.Brotli;

        return WebGLCompressionFormat.Disabled;
    }

    private static string GetCommandLineValue(string[] args, string key)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase))
                continue;

            return args[index + 1];
        }

        return null;
    }
}

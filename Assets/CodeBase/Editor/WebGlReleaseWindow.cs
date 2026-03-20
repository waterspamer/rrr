using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class WebGlReleaseWindow : EditorWindow
{
    private const string Title = "WebGL Release";
    private const string MenuPath = "Tools/Build/WebGL Release";
    private const string ConfigFileName = "WebGlReleaseSettings.json";
    private const string DefaultOutputRoot = @"C:\Work\BuildAgents\RRR-WebGL\editor-releases";
    private const string DefaultHost = "93.183.80.30";
    private const string DefaultUsername = "root";
    private const string DefaultRemoteRoot = "/var/www/rrr-webgl";
    private const string DefaultPublicUrl = "https://rrr-demo.tonforspeed.space/play/";
    private const int DefaultKeepServerReleases = 5;

    private string outputRoot;
    private WebGLCompressionFormat compressionFormat;
    private string serverHost;
    private string username;
    private string password;
    private string remoteRoot;
    private int keepServerReleases;
    private string lastReleasePath;
    private string lastReleaseId;
    private Vector2 scrollPosition;
    private WebGlReleaseConfig config;

    [MenuItem(MenuPath)]
    public static void OpenWindow()
    {
        WebGlReleaseWindow window = GetWindow<WebGlReleaseWindow>(Title);
        window.minSize = new Vector2(520f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        config = LoadConfig();
        ApplyConfig(config);
    }

    private void OnDisable()
    {
        SaveConfig();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
        outputRoot = EditorGUILayout.TextField("Release Root", outputRoot);
        compressionFormat = (WebGLCompressionFormat)EditorGUILayout.EnumPopup("Compression", compressionFormat);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Deploy", EditorStyles.boldLabel);
        serverHost = EditorGUILayout.TextField("Server Host", serverHost);
        username = EditorGUILayout.TextField("Username", username);
        password = EditorGUILayout.PasswordField("Password", password ?? string.Empty);
        remoteRoot = EditorGUILayout.TextField("Remote Root", remoteRoot);
        keepServerReleases = Mathf.Max(1, EditorGUILayout.IntField("Keep Releases", keepServerReleases));

        if (EditorGUI.EndChangeCheck())
            SaveConfig();

        EditorGUILayout.Space(8f);
        DrawLastReleaseInfo();

        EditorGUILayout.Space(12f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Build WebGL", GUILayout.Height(36f)))
                BuildOnly();

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(password)))
            {
                if (GUILayout.Button("Build + Deploy", GUILayout.Height(36f)))
                    BuildAndDeploy();
            }
        }

        using (new EditorGUI.DisabledScope(!CanDeployLastRelease()))
        {
            if (GUILayout.Button("Deploy Last Build", GUILayout.Height(30f)))
                DeployLastBuild();
        }

        EditorGUILayout.Space(12f);
        EditorGUILayout.HelpBox(
            "This tool builds WebGL from the opened Unity Editor without batchmode or nographics. " +
            "Source changes belong in git, but build artifacts should stay only on the local machine and on the release server.",
            MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void DrawLastReleaseInfo()
    {
        EditorGUILayout.LabelField("Last Release", EditorStyles.boldLabel);
        EditorGUILayout.SelectableLabel(
            string.IsNullOrWhiteSpace(lastReleaseId) ? "No releases yet" : lastReleaseId,
            EditorStyles.textField,
            GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.SelectableLabel(
            string.IsNullOrWhiteSpace(lastReleasePath) ? "No local path recorded" : lastReleasePath,
            EditorStyles.textField,
            GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
        EditorGUILayout.SelectableLabel(DefaultPublicUrl, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
    }

    private bool CanDeployLastRelease()
    {
        return !string.IsNullOrWhiteSpace(password) &&
               !string.IsNullOrWhiteSpace(lastReleasePath) &&
               Directory.Exists(lastReleasePath) &&
               File.Exists(Path.Combine(lastReleasePath, "index.html"));
    }

    private void BuildOnly()
    {
        string releasePath = BuildRelease();
        EditorUtility.RevealInFinder(releasePath);
        EditorUtility.DisplayDialog(Title, $"WebGL build completed.\n\n{releasePath}", "OK");
    }

    private void BuildAndDeploy()
    {
        string releasePath = BuildRelease();
        DeployRelease(releasePath, lastReleaseId);
        EditorUtility.DisplayDialog(Title, $"WebGL build deployed.\n\n{DefaultPublicUrl}", "OK");
    }

    private void DeployLastBuild()
    {
        DeployRelease(lastReleasePath, lastReleaseId);
        EditorUtility.DisplayDialog(Title, $"Existing WebGL build deployed.\n\n{DefaultPublicUrl}", "OK");
    }

    private string BuildRelease()
    {
        SaveConfig();
        string releaseId = CreateReleaseId();
        string releasePath = Path.Combine(outputRoot, releaseId);

        if (Directory.Exists(releasePath))
            Directory.Delete(releasePath, recursive: true);

        Directory.CreateDirectory(releasePath);

        try
        {
            EditorUtility.DisplayProgressBar(Title, "Building WebGL player...", 0.35f);
            BuildReport report = WebGlBuildPipeline.Build(releasePath, compressionFormat);
            WriteReleaseMetadata(releasePath, releaseId, report);
            RememberLastRelease(releaseId, releasePath);
            return releasePath;
        }
        catch
        {
            if (Directory.Exists(releasePath) && !File.Exists(Path.Combine(releasePath, "index.html")))
                Directory.Delete(releasePath, recursive: true);
            throw;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void DeployRelease(string releasePath, string releaseId)
    {
        SaveConfig();

        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Enter the server password before deploy.");
        if (string.IsNullOrWhiteSpace(releasePath) || !Directory.Exists(releasePath))
            throw new DirectoryNotFoundException($"Release path not found: {releasePath}");
        if (!File.Exists(Path.Combine(releasePath, "index.html")))
            throw new FileNotFoundException("index.html not found in release folder.", Path.Combine(releasePath, "index.html"));

        string publishScript = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Scripts", "Publish-WebGLRelease.ps1"));
        if (!File.Exists(publishScript))
            throw new FileNotFoundException("Publish script not found.", publishScript);

        StringBuilder arguments = new StringBuilder();
        AppendArgument(arguments, "-ExecutionPolicy");
        AppendArgument(arguments, "Bypass");
        AppendArgument(arguments, "-File");
        AppendArgument(arguments, publishScript);
        AppendArgument(arguments, "-ReleasePath");
        AppendArgument(arguments, releasePath);
        AppendArgument(arguments, "-ReleaseId");
        AppendArgument(arguments, releaseId);
        AppendArgument(arguments, "-ServerHost");
        AppendArgument(arguments, serverHost);
        AppendArgument(arguments, "-Username");
        AppendArgument(arguments, username);
        AppendArgument(arguments, "-Password");
        AppendArgument(arguments, password);
        AppendArgument(arguments, "-RemoteRoot");
        AppendArgument(arguments, remoteRoot);
        AppendArgument(arguments, "-KeepServerReleases");
        AppendArgument(arguments, keepServerReleases.ToString());
        AppendArgument(arguments, "-PublicUrl");
        AppendArgument(arguments, DefaultPublicUrl);

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments.ToString(),
            WorkingDirectory = Path.GetDirectoryName(publishScript),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new Process { StartInfo = startInfo };
        process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Deploy failed.\n\nSTDOUT:\n{stdout}\n\nSTDERR:\n{stderr}");

        UnityEngine.Debug.Log(stdout);
        Application.OpenURL(DefaultPublicUrl);
    }

    private void WriteReleaseMetadata(string releasePath, string releaseId, BuildReport report)
    {
        ReleaseMetadata metadata = new ReleaseMetadata
        {
            releaseId = releaseId,
            commit = TryGetGitValue("rev-parse --short=12 HEAD"),
            branch = TryGetGitValue("rev-parse --abbrev-ref HEAD"),
            builtAtUtc = DateTime.UtcNow.ToString("O"),
            compression = compressionFormat.ToString(),
            publicUrl = DefaultPublicUrl,
            totalSizeBytes = report.summary.totalSize
        };

        File.WriteAllText(
            Path.Combine(releasePath, "release.json"),
            JsonUtility.ToJson(metadata, prettyPrint: true));
    }

    private string CreateReleaseId()
    {
        string commit = TryGetGitValue("rev-parse --short=12 HEAD");
        string suffix = string.IsNullOrWhiteSpace(commit) ? "manual" : commit;
        return $"{DateTime.Now:yyyyMMdd-HHmmss}-{suffix}";
    }

    private static string TryGetGitValue(string arguments)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new Process { StartInfo = startInfo };
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void RememberLastRelease(string releaseId, string releasePath)
    {
        lastReleaseId = releaseId;
        lastReleasePath = releasePath;
        SaveConfig();
    }

    private void SaveConfig()
    {
        config ??= new WebGlReleaseConfig();
        config.outputRoot = outputRoot;
        config.compressionFormat = (int)compressionFormat;
        config.serverHost = serverHost;
        config.username = username;
        config.password = password;
        config.remoteRoot = remoteRoot;
        config.keepServerReleases = Mathf.Max(1, keepServerReleases);
        config.lastReleasePath = lastReleasePath;
        config.lastReleaseId = lastReleaseId;

        string configPath = GetConfigPath();
        string configDirectory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(configDirectory))
            Directory.CreateDirectory(configDirectory);
        File.WriteAllText(configPath, JsonUtility.ToJson(config, prettyPrint: true));
    }

    private static WebGlReleaseConfig LoadConfig()
    {
        string configPath = GetConfigPath();
        if (!File.Exists(configPath))
            return CreateDefaultConfig();

        try
        {
            string json = File.ReadAllText(configPath);
            WebGlReleaseConfig loaded = JsonUtility.FromJson<WebGlReleaseConfig>(json);
            return loaded ?? CreateDefaultConfig();
        }
        catch
        {
            return CreateDefaultConfig();
        }
    }

    private void ApplyConfig(WebGlReleaseConfig loadedConfig)
    {
        outputRoot = string.IsNullOrWhiteSpace(loadedConfig.outputRoot) ? DefaultOutputRoot : loadedConfig.outputRoot;
        compressionFormat = Enum.IsDefined(typeof(WebGLCompressionFormat), loadedConfig.compressionFormat)
            ? (WebGLCompressionFormat)loadedConfig.compressionFormat
            : WebGLCompressionFormat.Disabled;
        serverHost = string.IsNullOrWhiteSpace(loadedConfig.serverHost) ? DefaultHost : loadedConfig.serverHost;
        username = string.IsNullOrWhiteSpace(loadedConfig.username) ? DefaultUsername : loadedConfig.username;
        password = loadedConfig.password ?? string.Empty;
        remoteRoot = string.IsNullOrWhiteSpace(loadedConfig.remoteRoot) ? DefaultRemoteRoot : loadedConfig.remoteRoot;
        keepServerReleases = Mathf.Max(1, loadedConfig.keepServerReleases <= 0 ? DefaultKeepServerReleases : loadedConfig.keepServerReleases);
        lastReleasePath = loadedConfig.lastReleasePath ?? string.Empty;
        lastReleaseId = loadedConfig.lastReleaseId ?? string.Empty;
    }

    private static WebGlReleaseConfig CreateDefaultConfig()
    {
        return new WebGlReleaseConfig
        {
            outputRoot = DefaultOutputRoot,
            compressionFormat = (int)WebGLCompressionFormat.Disabled,
            serverHost = DefaultHost,
            username = DefaultUsername,
            password = string.Empty,
            remoteRoot = DefaultRemoteRoot,
            keepServerReleases = DefaultKeepServerReleases,
            lastReleasePath = string.Empty,
            lastReleaseId = string.Empty
        };
    }

    private static string GetConfigPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserSettings", ConfigFileName));
    }

    private static void AppendArgument(StringBuilder builder, string value)
    {
        if (builder.Length > 0)
            builder.Append(' ');

        builder.Append('"');
        builder.Append(value.Replace("\"", "`\""));
        builder.Append('"');
    }

    [Serializable]
    private sealed class ReleaseMetadata
    {
        public string releaseId;
        public string commit;
        public string branch;
        public string builtAtUtc;
        public string compression;
        public string publicUrl;
        public ulong totalSizeBytes;
    }

    [Serializable]
    private sealed class WebGlReleaseConfig
    {
        public string outputRoot;
        public int compressionFormat;
        public string serverHost;
        public string username;
        public string password;
        public string remoteRoot;
        public int keepServerReleases;
        public string lastReleasePath;
        public string lastReleaseId;
    }
}

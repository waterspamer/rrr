using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class WebGlReleaseWindow : EditorWindow
{
    private const string Title = "Release Manager";
    private const string MenuPath = "Tools/Build/Release Manager";
    private const string LegacyMenuPath = "Tools/Build/WebGL Release";
    private const string ConfigFileName = "WebGlReleaseSettings.json";
    private const string DefaultWebGlOutputRoot = @"C:\Work\BuildAgents\RRR-WebGL\editor-releases";
    private const string DefaultDesktopOutputRoot = @"C:\Work\BuildAgents\RRR-Win64\editor-releases";
    private const string DefaultDesktopMirrorProjectPath = @"C:\Work\BuildAgents\RRR-Win64\mirror-project";
    private const string DefaultHost = "93.183.80.30";
    private const string DefaultUsername = "root";
    private const string DefaultWebGlRemoteRoot = "/var/www/rrr-webgl";
    private const string DefaultDesktopRemoteRoot = "/var/www/rrr-downloads/windows";
    private const string DefaultWebGlPublicUrl = "https://rrr-demo.tonforspeed.space/play/";
    private const string DefaultDesktopPublicUrl = "https://rrr-demo.tonforspeed.space/downloads/windows/latest.zip";
    private const int DefaultKeepServerReleases = 5;

    private enum ReleaseTarget
    {
        WebGL,
        WindowsX64
    }

    private enum ExternalProcessMode
    {
        None,
        DeployOnly,
        MirrorBuildOnly,
        MirrorBuildAndDeploy
    }

    private ReleaseTarget releaseTarget;
    private string webGlOutputRoot;
    private string desktopOutputRoot;
    private string desktopMirrorProjectPath;
    private WebGLCompressionFormat webGlCompressionFormat;
    private string serverHost;
    private string username;
    private string password;
    private string webGlRemoteRoot;
    private string desktopRemoteRoot;
    private bool useDesktopBuildMirror;
    private int keepServerReleases;
    private string lastWebGlReleasePath;
    private string lastWebGlReleaseId;
    private string lastDesktopReleasePath;
    private string lastDesktopReleaseId;
    private Vector2 scrollPosition;
    private Vector2 logScrollPosition;
    private WebGlReleaseConfig config;
    private Process deployProcess;
    private readonly List<string> deployLogLines = new List<string>();
    private string currentStage = "Idle";
    private float currentStageProgress;
    private bool isDeployRunning;
    private string pendingDialogMessage;
    private bool pendingDialogIsError;
    private Action queuedOperation;
    private string queuedOperationName;
    private DateTime operationStartedAtUtc;
    private string activeReleaseId;
    private string activeReleasePath;
    private ExternalProcessMode externalProcessMode;

    [MenuItem(MenuPath)]
    public static void OpenWindow()
    {
        OpenSharedWindow();
    }

    [MenuItem(LegacyMenuPath)]
    public static void OpenLegacyWindow()
    {
        OpenSharedWindow();
    }

    private static void OpenSharedWindow()
    {
        WebGlReleaseWindow window = GetWindow<WebGlReleaseWindow>(Title);
        window.minSize = new Vector2(560f, 520f);
        window.Show();
    }

    private string CurrentOutputRoot
    {
        get => releaseTarget == ReleaseTarget.WebGL ? webGlOutputRoot : desktopOutputRoot;
        set
        {
            if (releaseTarget == ReleaseTarget.WebGL)
                webGlOutputRoot = value;
            else
                desktopOutputRoot = value;
        }
    }

    private string CurrentRemoteRoot
    {
        get => releaseTarget == ReleaseTarget.WebGL ? webGlRemoteRoot : desktopRemoteRoot;
        set
        {
            if (releaseTarget == ReleaseTarget.WebGL)
                webGlRemoteRoot = value;
            else
                desktopRemoteRoot = value;
        }
    }

    private string CurrentLastReleasePath
    {
        get => releaseTarget == ReleaseTarget.WebGL ? lastWebGlReleasePath : lastDesktopReleasePath;
        set
        {
            if (releaseTarget == ReleaseTarget.WebGL)
                lastWebGlReleasePath = value;
            else
                lastDesktopReleasePath = value;
        }
    }

    private string CurrentLastReleaseId
    {
        get => releaseTarget == ReleaseTarget.WebGL ? lastWebGlReleaseId : lastDesktopReleaseId;
        set
        {
            if (releaseTarget == ReleaseTarget.WebGL)
                lastWebGlReleaseId = value;
            else
                lastDesktopReleaseId = value;
        }
    }

    private string CurrentPublicUrl => releaseTarget == ReleaseTarget.WebGL ? DefaultWebGlPublicUrl : DefaultDesktopPublicUrl;
    private string CurrentTargetDisplayName => releaseTarget == ReleaseTarget.WebGL ? "WebGL" : "Windows x64";

    private void OnEnable()
    {
        config = LoadConfig();
        ApplyConfig(config);
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        SaveConfig();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUI.BeginChangeCheck();

        releaseTarget = (ReleaseTarget)EditorGUILayout.EnumPopup("Release Target", releaseTarget);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
        CurrentOutputRoot = EditorGUILayout.TextField("Release Root", CurrentOutputRoot);
        if (releaseTarget == ReleaseTarget.WebGL)
        {
            webGlCompressionFormat = (WebGLCompressionFormat)EditorGUILayout.EnumPopup("Compression", webGlCompressionFormat);
        }
        else
        {
            useDesktopBuildMirror = EditorGUILayout.ToggleLeft("Build Windows x64 from mirror project", useDesktopBuildMirror);
            using (new EditorGUI.DisabledScope(!useDesktopBuildMirror))
            {
                desktopMirrorProjectPath = EditorGUILayout.TextField("Mirror Project Path", desktopMirrorProjectPath);
            }
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Deploy", EditorStyles.boldLabel);
        serverHost = EditorGUILayout.TextField("Server Host", serverHost);
        username = EditorGUILayout.TextField("Username", username);
        password = EditorGUILayout.PasswordField("Password", password ?? string.Empty);
        CurrentRemoteRoot = EditorGUILayout.TextField("Remote Root", CurrentRemoteRoot);
        keepServerReleases = Mathf.Max(1, EditorGUILayout.IntField("Keep Releases", keepServerReleases));

        if (EditorGUI.EndChangeCheck())
            SaveConfig();

        EditorGUILayout.Space(8f);
        DrawLastReleaseInfo();
        DrawOperationStatus();

        EditorGUILayout.Space(12f);
        using (new EditorGUI.DisabledScope(isDeployRunning))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button($"Build {CurrentTargetDisplayName}", GUILayout.Height(36f)))
                QueueOperation($"Build {CurrentTargetDisplayName}", BuildOnly);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(password)))
            {
                if (GUILayout.Button($"Build + Deploy {CurrentTargetDisplayName}", GUILayout.Height(36f)))
                    QueueOperation($"Build + Deploy {CurrentTargetDisplayName}", BuildAndDeploy);
            }
        }

        using (new EditorGUI.DisabledScope(!CanDeployLastRelease() || isDeployRunning))
        {
            if (GUILayout.Button($"Deploy Last {CurrentTargetDisplayName} Build", GUILayout.Height(30f)))
                QueueOperation($"Deploy Last {CurrentTargetDisplayName} Build", DeployLastBuild);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(CurrentLastReleasePath) || !Directory.Exists(CurrentLastReleasePath)))
            {
                if (GUILayout.Button("Reveal Last Build"))
                    EditorUtility.RevealInFinder(CurrentLastReleasePath);
            }

            if (GUILayout.Button(releaseTarget == ReleaseTarget.WebGL ? "Open Site" : "Open Download URL"))
                Application.OpenURL(CurrentPublicUrl);
        }

        EditorGUILayout.Space(12f);
        EditorGUILayout.HelpBox(
            releaseTarget == ReleaseTarget.WebGL
                ? "WebGL builds stay editable in the opened Unity Editor and deploy to /play/ on the site."
                : useDesktopBuildMirror
                    ? "Windows x64 builds sync the working tree into a separate mirror project, then build there in batchmode so your main Unity Editor can stay open."
                    : "Windows x64 builds run in the opened editor and are published as downloadable .zip artifacts for the future launcher.",
            MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void DrawLastReleaseInfo()
    {
        EditorGUILayout.LabelField($"Last {CurrentTargetDisplayName} Release", EditorStyles.boldLabel);
        EditorGUILayout.SelectableLabel(
            string.IsNullOrWhiteSpace(CurrentLastReleaseId) ? "No releases yet" : CurrentLastReleaseId,
            EditorStyles.textField,
            GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.SelectableLabel(
            string.IsNullOrWhiteSpace(CurrentLastReleasePath) ? "No local path recorded" : CurrentLastReleasePath,
            EditorStyles.textField,
            GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
        EditorGUILayout.SelectableLabel(CurrentPublicUrl, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
    }

    private void DrawOperationStatus()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(currentStage);
        Rect rect = GUILayoutUtility.GetRect(18f, 18f, "TextField");
        EditorGUI.ProgressBar(rect, currentStageProgress, Mathf.RoundToInt(currentStageProgress * 100f) + "%");

        if (operationStartedAtUtc != default)
            EditorGUILayout.LabelField("Elapsed", FormatElapsed(DateTime.UtcNow - operationStartedAtUtc));

        if (!string.IsNullOrWhiteSpace(activeReleaseId))
            EditorGUILayout.LabelField("Release ID", activeReleaseId);

        if (!string.IsNullOrWhiteSpace(activeReleasePath))
        {
            EditorGUILayout.LabelField("Release Path");
            EditorGUILayout.SelectableLabel(activeReleasePath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
        }

        EditorGUILayout.HelpBox(
            releaseTarget == ReleaseTarget.WebGL
                ? "Pipeline: build player -> write release.json -> package -> upload -> extract -> activate /play/"
                : "Pipeline: build player -> write release.json -> package -> upload zip -> update latest download links",
            MessageType.None);

        if (deployLogLines.Count > 0)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Recent Log", EditorStyles.boldLabel);
            logScrollPosition = EditorGUILayout.BeginScrollView(logScrollPosition, GUILayout.Height(140f));
            for (int i = 0; i < deployLogLines.Count; i++)
                EditorGUILayout.SelectableLabel(deployLogLines[i], EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndScrollView();
        }
    }

    private bool CanDeployLastRelease()
    {
        return !string.IsNullOrWhiteSpace(password) &&
               !string.IsNullOrWhiteSpace(CurrentLastReleasePath) &&
               IsValidReleaseFolder(CurrentLastReleasePath);
    }

    private bool IsValidReleaseFolder(string releasePath)
    {
        if (string.IsNullOrWhiteSpace(releasePath) || !Directory.Exists(releasePath))
            return false;

        return releaseTarget == ReleaseTarget.WebGL
            ? File.Exists(Path.Combine(releasePath, "index.html"))
            : File.Exists(Path.Combine(releasePath, "release.json")) && Directory.GetFiles(releasePath, "*.exe", SearchOption.TopDirectoryOnly).Length > 0;
    }

    private void BuildOnly()
    {
        if (ShouldUseDesktopMirrorBuild())
        {
            StartDesktopMirrorBuild(deployAfterBuild: false);
            return;
        }

        string releasePath = BuildRelease();
        SetStage("Build complete", 1.0f);
        EditorUtility.RevealInFinder(releasePath);
        QueueDialog($"{CurrentTargetDisplayName} build completed.\n\n{releasePath}", false);
    }

    private void BuildAndDeploy()
    {
        if (ShouldUseDesktopMirrorBuild())
        {
            StartDesktopMirrorBuild(deployAfterBuild: true);
            return;
        }

        string releasePath = BuildRelease();
        StartDeploy(releasePath, CurrentLastReleaseId);
    }

    private void DeployLastBuild()
    {
        StartDeploy(CurrentLastReleasePath, CurrentLastReleaseId);
    }

    private string BuildRelease()
    {
        SaveConfig();
        string releaseId = CreateReleaseId();
        string releasePath = Path.Combine(CurrentOutputRoot, releaseId);
        activeReleaseId = releaseId;
        activeReleasePath = releasePath;

        if (Directory.Exists(releasePath))
            Directory.Delete(releasePath, recursive: true);

        Directory.CreateDirectory(releasePath);

        try
        {
            EditorUtility.DisplayProgressBar(Title, $"Preparing {CurrentTargetDisplayName} release folder...", 0.1f);
            SetStage("Preparing release folder", 0.1f);
            BuildReport report;
            if (releaseTarget == ReleaseTarget.WebGL)
            {
                EditorUtility.DisplayProgressBar(Title, "Building WebGL player...", 0.35f);
                SetStage("Building WebGL player", 0.35f);
                report = WebGlBuildPipeline.Build(releasePath, webGlCompressionFormat);
            }
            else
            {
                EditorUtility.DisplayProgressBar(Title, "Building Windows x64 player...", 0.35f);
                SetStage("Building Windows x64 player", 0.35f);
                report = DesktopBuildPipeline.Build(releasePath);
            }

            EditorUtility.DisplayProgressBar(Title, "Writing release metadata...", 0.55f);
            SetStage("Writing release metadata", 0.55f);
            WriteReleaseMetadata(releasePath, releaseId, report);
            RememberLastRelease(releaseId, releasePath);
            SetStage("Build finished", 0.65f);
            return releasePath;
        }
        catch
        {
            if (Directory.Exists(releasePath))
                Directory.Delete(releasePath, recursive: true);
            throw;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void StartDeploy(string releasePath, string releaseId)
    {
        SaveConfig();

        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Enter the server password before deploy.");
        if (string.IsNullOrWhiteSpace(releasePath) || !Directory.Exists(releasePath))
            throw new DirectoryNotFoundException($"Release path not found: {releasePath}");
        if (!IsValidReleaseFolder(releasePath))
            throw new InvalidOperationException($"Release folder is missing required {CurrentTargetDisplayName} files: {releasePath}");

        string publishScript = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Scripts", GetPublishScriptFileName()));
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
        AppendArgument(arguments, CurrentRemoteRoot);
        AppendArgument(arguments, "-KeepServerReleases");
        AppendArgument(arguments, keepServerReleases.ToString());
        AppendArgument(arguments, "-PublicUrl");
        AppendArgument(arguments, CurrentPublicUrl);

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

        deployProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        deployProcess.OutputDataReceived += OnDeployOutputDataReceived;
        deployProcess.ErrorDataReceived += OnDeployErrorDataReceived;
        deployProcess.Start();
        deployProcess.BeginOutputReadLine();
        deployProcess.BeginErrorReadLine();
        isDeployRunning = true;
        externalProcessMode = ExternalProcessMode.DeployOnly;
        SetStage("Starting deploy process", 0.68f);
        AppendDeployLog($"Deploy started for {releaseId}");
        AppendDeployLog($"Local release: {releasePath}");
    }

    private bool ShouldUseDesktopMirrorBuild()
    {
        return releaseTarget == ReleaseTarget.WindowsX64 && useDesktopBuildMirror;
    }

    private void StartDesktopMirrorBuild(bool deployAfterBuild)
    {
        SaveConfig();

        if (string.IsNullOrWhiteSpace(desktopMirrorProjectPath))
            throw new InvalidOperationException("Enter the mirror project path before starting a mirror build.");
        if (deployAfterBuild && string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Enter the server password before deploy.");

        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        string sourceProjectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string mirrorScript = Path.GetFullPath(Path.Combine(sourceProjectPath, "Scripts", "Build-MirrorDesktopRelease.ps1"));
        if (!File.Exists(mirrorScript))
            throw new FileNotFoundException("Mirror build script not found.", mirrorScript);

        string releaseId = CreateReleaseId();
        string releasePath = Path.Combine(CurrentOutputRoot, releaseId);
        activeReleaseId = releaseId;
        activeReleasePath = releasePath;

        StringBuilder arguments = new StringBuilder();
        AppendArgument(arguments, "-ExecutionPolicy");
        AppendArgument(arguments, "Bypass");
        AppendArgument(arguments, "-File");
        AppendArgument(arguments, mirrorScript);
        AppendArgument(arguments, "-SourceProjectPath");
        AppendArgument(arguments, sourceProjectPath);
        AppendArgument(arguments, "-MirrorProjectPath");
        AppendArgument(arguments, desktopMirrorProjectPath);
        AppendArgument(arguments, "-ReleaseRoot");
        AppendArgument(arguments, CurrentOutputRoot);
        AppendArgument(arguments, "-UnityExePath");
        AppendArgument(arguments, EditorApplication.applicationPath);
        AppendArgument(arguments, "-ReleaseId");
        AppendArgument(arguments, releaseId);
        AppendArgument(arguments, "-SourceCommit");
        AppendArgument(arguments, TryGetGitValue("rev-parse --short=12 HEAD"));
        AppendArgument(arguments, "-SourceBranch");
        AppendArgument(arguments, TryGetGitValue("rev-parse --abbrev-ref HEAD"));
        AppendArgument(arguments, "-PublicUrl");
        AppendArgument(arguments, CurrentPublicUrl);

        if (deployAfterBuild)
        {
            AppendArgument(arguments, "-DeployAfterBuild");
            AppendArgument(arguments, "-ServerHost");
            AppendArgument(arguments, serverHost);
            AppendArgument(arguments, "-Username");
            AppendArgument(arguments, username);
            AppendArgument(arguments, "-Password");
            AppendArgument(arguments, password);
            AppendArgument(arguments, "-RemoteRoot");
            AppendArgument(arguments, CurrentRemoteRoot);
            AppendArgument(arguments, "-KeepServerReleases");
            AppendArgument(arguments, keepServerReleases.ToString());
        }

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments.ToString(),
            WorkingDirectory = Path.GetDirectoryName(mirrorScript),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        deployProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        deployProcess.OutputDataReceived += OnDeployOutputDataReceived;
        deployProcess.ErrorDataReceived += OnDeployErrorDataReceived;
        deployProcess.Start();
        deployProcess.BeginOutputReadLine();
        deployProcess.BeginErrorReadLine();
        isDeployRunning = true;
        externalProcessMode = deployAfterBuild ? ExternalProcessMode.MirrorBuildAndDeploy : ExternalProcessMode.MirrorBuildOnly;
        SetStage("Starting mirror build process", 0.05f);
        AppendDeployLog($"Mirror project: {desktopMirrorProjectPath}");
        AppendDeployLog($"Release target: {releasePath}");
    }

    private string GetPublishScriptFileName()
    {
        return releaseTarget == ReleaseTarget.WebGL
            ? "Publish-WebGLRelease.ps1"
            : "Publish-DesktopRelease.ps1";
    }

    private void WriteReleaseMetadata(string releasePath, string releaseId, BuildReport report)
    {
        ReleaseMetadata metadata = new ReleaseMetadata
        {
            releaseId = releaseId,
            commit = TryGetGitValue("rev-parse --short=12 HEAD"),
            branch = TryGetGitValue("rev-parse --abbrev-ref HEAD"),
            builtAtUtc = DateTime.UtcNow.ToString("O"),
            target = releaseTarget.ToString(),
            compression = releaseTarget == ReleaseTarget.WebGL ? webGlCompressionFormat.ToString() : "None",
            publicUrl = CurrentPublicUrl,
            primaryArtifact = GetPrimaryArtifactName(releasePath),
            totalSizeBytes = report.summary.totalSize
        };

        File.WriteAllText(
            Path.Combine(releasePath, "release.json"),
            JsonUtility.ToJson(metadata, prettyPrint: true));
    }

    private string GetPrimaryArtifactName(string releasePath)
    {
        if (releaseTarget == ReleaseTarget.WebGL)
            return "index.html";

        string[] executables = Directory.GetFiles(releasePath, "*.exe", SearchOption.TopDirectoryOnly);
        return executables.Length > 0 ? Path.GetFileName(executables[0]) : string.Empty;
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
        CurrentLastReleaseId = releaseId;
        CurrentLastReleasePath = releasePath;
        SaveConfig();
    }

    private void SaveConfig()
    {
        config ??= new WebGlReleaseConfig();
        config.releaseTarget = (int)releaseTarget;
        config.webGlOutputRoot = webGlOutputRoot;
        config.desktopOutputRoot = desktopOutputRoot;
        config.desktopMirrorProjectPath = desktopMirrorProjectPath;
        config.webGlCompressionFormat = (int)webGlCompressionFormat;
        config.serverHost = serverHost;
        config.username = username;
        config.password = password;
        config.webGlRemoteRoot = webGlRemoteRoot;
        config.desktopRemoteRoot = desktopRemoteRoot;
        config.useDesktopBuildMirror = useDesktopBuildMirror;
        config.keepServerReleases = Mathf.Max(1, keepServerReleases);
        config.lastWebGlReleasePath = lastWebGlReleasePath;
        config.lastWebGlReleaseId = lastWebGlReleaseId;
        config.lastDesktopReleasePath = lastDesktopReleasePath;
        config.lastDesktopReleaseId = lastDesktopReleaseId;

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
        releaseTarget = Enum.IsDefined(typeof(ReleaseTarget), loadedConfig.releaseTarget)
            ? (ReleaseTarget)loadedConfig.releaseTarget
            : ReleaseTarget.WebGL;
        webGlOutputRoot = string.IsNullOrWhiteSpace(loadedConfig.webGlOutputRoot) ? DefaultWebGlOutputRoot : loadedConfig.webGlOutputRoot;
        desktopOutputRoot = string.IsNullOrWhiteSpace(loadedConfig.desktopOutputRoot) ? DefaultDesktopOutputRoot : loadedConfig.desktopOutputRoot;
        desktopMirrorProjectPath = string.IsNullOrWhiteSpace(loadedConfig.desktopMirrorProjectPath) ? DefaultDesktopMirrorProjectPath : loadedConfig.desktopMirrorProjectPath;
        webGlCompressionFormat = Enum.IsDefined(typeof(WebGLCompressionFormat), loadedConfig.webGlCompressionFormat)
            ? (WebGLCompressionFormat)loadedConfig.webGlCompressionFormat
            : WebGLCompressionFormat.Disabled;
        serverHost = string.IsNullOrWhiteSpace(loadedConfig.serverHost) ? DefaultHost : loadedConfig.serverHost;
        username = string.IsNullOrWhiteSpace(loadedConfig.username) ? DefaultUsername : loadedConfig.username;
        password = loadedConfig.password ?? string.Empty;
        webGlRemoteRoot = string.IsNullOrWhiteSpace(loadedConfig.webGlRemoteRoot) ? DefaultWebGlRemoteRoot : loadedConfig.webGlRemoteRoot;
        desktopRemoteRoot = string.IsNullOrWhiteSpace(loadedConfig.desktopRemoteRoot) ? DefaultDesktopRemoteRoot : loadedConfig.desktopRemoteRoot;
        useDesktopBuildMirror = loadedConfig.useDesktopBuildMirror;
        keepServerReleases = Mathf.Max(1, loadedConfig.keepServerReleases <= 0 ? DefaultKeepServerReleases : loadedConfig.keepServerReleases);
        lastWebGlReleasePath = loadedConfig.lastWebGlReleasePath ?? string.Empty;
        lastWebGlReleaseId = loadedConfig.lastWebGlReleaseId ?? string.Empty;
        lastDesktopReleasePath = loadedConfig.lastDesktopReleasePath ?? string.Empty;
        lastDesktopReleaseId = loadedConfig.lastDesktopReleaseId ?? string.Empty;
    }

    private static WebGlReleaseConfig CreateDefaultConfig()
    {
        return new WebGlReleaseConfig
        {
            releaseTarget = (int)ReleaseTarget.WebGL,
            webGlOutputRoot = DefaultWebGlOutputRoot,
            desktopOutputRoot = DefaultDesktopOutputRoot,
            desktopMirrorProjectPath = DefaultDesktopMirrorProjectPath,
            webGlCompressionFormat = (int)WebGLCompressionFormat.Disabled,
            serverHost = DefaultHost,
            username = DefaultUsername,
            password = string.Empty,
            webGlRemoteRoot = DefaultWebGlRemoteRoot,
            desktopRemoteRoot = DefaultDesktopRemoteRoot,
            useDesktopBuildMirror = true,
            keepServerReleases = DefaultKeepServerReleases,
            lastWebGlReleasePath = string.Empty,
            lastWebGlReleaseId = string.Empty,
            lastDesktopReleasePath = string.Empty,
            lastDesktopReleaseId = string.Empty
        };
    }

    private static string GetConfigPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserSettings", ConfigFileName));
    }

    private void OnEditorUpdate()
    {
        if (queuedOperation != null)
        {
            Action operation = queuedOperation;
            string operationName = queuedOperationName;
            queuedOperation = null;
            queuedOperationName = null;

            try
            {
                SetStage(operationName, currentStageProgress <= 0f ? 0.02f : currentStageProgress);
                operation.Invoke();
            }
            catch (Exception exception)
            {
                SetStage("Operation failed", currentStageProgress <= 0f ? 0.02f : currentStageProgress);
                AppendDeployLog("ERR: " + exception.Message);
                QueueDialog(exception.ToString(), true);
            }
        }

        if (!isDeployRunning && string.IsNullOrWhiteSpace(pendingDialogMessage))
            return;

        Repaint();

        if (deployProcess != null && deployProcess.HasExited)
        {
            int exitCode = deployProcess.ExitCode;
            ExternalProcessMode completedMode = externalProcessMode;
            CleanupDeployProcess();

            if (exitCode == 0)
            {
                if (!string.IsNullOrWhiteSpace(activeReleaseId) &&
                    !string.IsNullOrWhiteSpace(activeReleasePath) &&
                    Directory.Exists(activeReleasePath))
                {
                    RememberLastRelease(activeReleaseId, activeReleasePath);
                }

                if (completedMode == ExternalProcessMode.MirrorBuildOnly)
                {
                    SetStage("Build complete", 1.0f);
                    if (!string.IsNullOrWhiteSpace(activeReleasePath) && Directory.Exists(activeReleasePath))
                        EditorUtility.RevealInFinder(activeReleasePath);
                    QueueDialog($"{CurrentTargetDisplayName} build completed.\n\n{activeReleasePath}", false);
                }
                else
                {
                    SetStage("Deploy complete", 1.0f);
                    QueueDialog($"{CurrentTargetDisplayName} build deployed.\n\n{CurrentPublicUrl}", false);
                    Application.OpenURL(CurrentPublicUrl);
                }
            }
            else
            {
                SetStage("Deploy failed", currentStageProgress <= 0f ? 0.7f : currentStageProgress);
                QueueDialog("External build/deploy process failed.\n\nCheck the Recent Log section for details.", true);
            }
        }

        if (!string.IsNullOrWhiteSpace(pendingDialogMessage) && !EditorApplication.isCompiling)
        {
            string message = pendingDialogMessage;
            bool isError = pendingDialogIsError;
            pendingDialogMessage = null;
            pendingDialogIsError = false;
            EditorUtility.DisplayDialog(Title, message, "OK");
            if (isError)
                UnityEngine.Debug.LogError(message);
        }
    }

    private void OnDeployOutputDataReceived(object sender, DataReceivedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Data))
            HandleDeployLogLine(args.Data);
    }

    private void OnDeployErrorDataReceived(object sender, DataReceivedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Data))
            HandleDeployLogLine("ERR: " + args.Data);
    }

    private void HandleDeployLogLine(string line)
    {
        const string marker = "##rrr-progress|";
        const string releaseIdMarker = "##rrr-release-id|";
        const string releasePathMarker = "##rrr-release-path|";
        if (line.StartsWith(marker, StringComparison.Ordinal))
        {
            string[] parts = line.Split('|');
            if (parts.Length >= 3 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float progress))
            {
                SetStage(parts[2], progress);
                return;
            }
        }
        if (line.StartsWith(releaseIdMarker, StringComparison.Ordinal))
        {
            activeReleaseId = line.Substring(releaseIdMarker.Length).Trim();
            return;
        }
        if (line.StartsWith(releasePathMarker, StringComparison.Ordinal))
        {
            activeReleasePath = line.Substring(releasePathMarker.Length).Trim();
            return;
        }

        AppendDeployLog(line);
    }

    private void AppendDeployLog(string line)
    {
        if (deployLogLines.Count > 0 && string.Equals(deployLogLines[deployLogLines.Count - 1], line, StringComparison.Ordinal))
            return;

        deployLogLines.Add(line);
        if (deployLogLines.Count > 40)
            deployLogLines.RemoveAt(0);
        logScrollPosition.y = float.MaxValue;
    }

    private void SetStage(string stage, float progress)
    {
        currentStage = stage;
        currentStageProgress = Mathf.Clamp01(progress);
        AppendDeployLog(stage);
    }

    private void ResetOperationState()
    {
        deployLogLines.Clear();
        currentStage = "Idle";
        currentStageProgress = 0f;
        pendingDialogMessage = null;
        pendingDialogIsError = false;
        operationStartedAtUtc = DateTime.UtcNow;
        activeReleaseId = CurrentLastReleaseId;
        activeReleasePath = CurrentLastReleasePath;
    }

    private void CleanupDeployProcess()
    {
        isDeployRunning = false;
        externalProcessMode = ExternalProcessMode.None;
        if (deployProcess == null)
            return;

        deployProcess.OutputDataReceived -= OnDeployOutputDataReceived;
        deployProcess.ErrorDataReceived -= OnDeployErrorDataReceived;
        deployProcess.Dispose();
        deployProcess = null;
    }

    private void QueueOperation(string operationName, Action operation)
    {
        if (operation == null || isDeployRunning || queuedOperation != null)
            return;

        ResetOperationState();
        queuedOperationName = "Queued: " + operationName;
        queuedOperation = operation;
        SetStage(queuedOperationName, 0.01f);
    }

    private void QueueDialog(string message, bool isError)
    {
        pendingDialogMessage = message;
        pendingDialogIsError = isError;
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1.0)
            return elapsed.ToString(@"hh\:mm\:ss");

        return elapsed.ToString(@"mm\:ss");
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
        public string target;
        public string compression;
        public string publicUrl;
        public string primaryArtifact;
        public ulong totalSizeBytes;
    }

    [Serializable]
    private sealed class WebGlReleaseConfig
    {
        public int releaseTarget;
        public string webGlOutputRoot;
        public string desktopOutputRoot;
        public string desktopMirrorProjectPath;
        public int webGlCompressionFormat;
        public string serverHost;
        public string username;
        public string password;
        public string webGlRemoteRoot;
        public string desktopRemoteRoot;
        public bool useDesktopBuildMirror;
        public int keepServerReleases;
        public string lastWebGlReleasePath;
        public string lastWebGlReleaseId;
        public string lastDesktopReleasePath;
        public string lastDesktopReleaseId;
    }
}

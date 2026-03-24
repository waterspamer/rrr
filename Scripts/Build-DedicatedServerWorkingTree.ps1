param(
    [string]$UnityEditor = "C:\Program Files\Unity\Hub\Editor\6000.3.3f1\Editor\Unity.exe",
    [string]$ProjectPath = "C:\Work\Prototyping\Russian Road Rage",
    [string]$ReleaseId = "",
    [string]$OutputRoot = "C:\Work\BuildAgents\RRR-Dedicated\releases",
    [string[]]$Scenes = @("Assets/Scenes/Game.unity"),
    [ValidateSet("Linux", "Windows")] [string]$TargetPlatform = "Linux",
    [string]$LogPath = "",
    [int]$MaxAttempts = 3
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

if ([string]::IsNullOrWhiteSpace($ReleaseId)) {
    $ReleaseId = "working-tree-" + (Get-Date -Format "yyyyMMdd-HHmmss")
}

if (-not (Test-Path $UnityEditor)) {
    throw "Unity editor not found: $UnityEditor"
}

if (-not (Test-Path $ProjectPath)) {
    throw "Project path not found: $ProjectPath"
}

$releasePath = Join-Path $OutputRoot $ReleaseId
if (Test-Path $releasePath) {
    Remove-Item -Recurse -Force $releasePath
}

New-Item -ItemType Directory -Force -Path $releasePath | Out-Null

if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $logsRoot = Join-Path (Split-Path -Parent $OutputRoot) "logs"
    New-Item -ItemType Directory -Force -Path $logsRoot | Out-Null
    $LogPath = Join-Path $logsRoot ($ReleaseId + ".log")
}
else {
    $logDirectory = Split-Path -Parent $LogPath
    if (-not [string]::IsNullOrWhiteSpace($logDirectory)) {
        New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    }
}

$env:RRR_DEDICATED_OUTPUT_DIR = $releasePath
$env:RRR_RELEASE_ID = $ReleaseId
$env:RRR_RELEASE_COMMIT = "working-tree"
$env:RRR_RELEASE_BRANCH = "working-tree"
$env:RRR_RELEASE_PUBLIC_URL = ""

$sceneArg = ($Scenes | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ";"
$targetArg = $TargetPlatform.ToLowerInvariant()
$metadataPath = Join-Path $releasePath "release.json"

function Format-NativeArgument {
    param([string]$Value)

    if ($null -eq $Value) {
        return '""'
    }

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

for ($attempt = 1; $attempt -le [Math]::Max(1, $MaxAttempts); $attempt++) {
    $arguments = @(
        "-batchmode",
        "-quit",
        "-standaloneBuildSubtarget", "Server",
        "-projectPath", $ProjectPath,
        "-executeMethod", "DedicatedServerBuildPipeline.BuildFromCommandLine",
        "-rrrDedicatedScenes", $sceneArg,
        "-rrrDedicatedTarget", $targetArg,
        "-logFile", $LogPath
    ) | ForEach-Object { Format-NativeArgument $_ }

    $process = Start-Process `
        -FilePath $UnityEditor `
        -ArgumentList ($arguments -join ' ') `
        -Wait `
        -PassThru `
        -NoNewWindow

    if ($process.ExitCode -ne 0) {
        throw "Unity Dedicated Server build failed with exit code $($process.ExitCode). Log: $LogPath"
    }

    if ((Test-Path $metadataPath) -and (Test-Path (Join-Path $releasePath "run.sh"))) {
        Write-Host "Dedicated Server build completed."
        Write-Host "ReleaseId: $ReleaseId"
        Write-Host "ReleasePath: $releasePath"
        Write-Host "LogPath: $LogPath"
        exit 0
    }

    Start-Sleep -Seconds 2
}

throw "Unity Dedicated Server build did not produce release metadata after $MaxAttempts attempt(s). Log: $LogPath"

param(
    [string]$UnityEditor = "C:\Program Files\Unity\Hub\Editor\6000.3.3f1\Editor\Unity.exe",
    [string]$ProjectPath = "C:\Work\Prototyping\Russian Road Rage",
    [string]$MirrorProjectPath = "C:\Work\BuildAgents\RRR-Dedicated\local-workspace",
    [string]$ReleaseId = "",
    [string]$OutputRoot = "C:\Work\BuildAgents\RRR-Dedicated\local",
    [string[]]$Scenes = @("Assets/Scenes/Game.unity"),
    [string]$LogPath = "",
    [int]$MaxAttempts = 3
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$buildScript = Join-Path $PSScriptRoot "Build-DedicatedServerWorkingTree.ps1"
if (-not (Test-Path $buildScript)) {
    throw "Build script not found: $buildScript"
}

if (-not (Test-Path $ProjectPath)) {
    throw "Project path not found: $ProjectPath"
}

$ProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
$MirrorProjectPath = [System.IO.Path]::GetFullPath($MirrorProjectPath)

New-Item -ItemType Directory -Force -Path $MirrorProjectPath | Out-Null

$excludedDirectories = @(
    "Library",
    "Temp",
    "Obj",
    "Logs",
    "UserSettings",
    "MemoryCaptures",
    "Builds",
    ".git",
    ".vs"
)

$robocopyArguments = @(
    $ProjectPath,
    $MirrorProjectPath,
    "/MIR",
    "/FFT",
    "/R:2",
    "/W:1",
    "/NFL",
    "/NDL",
    "/NP",
    "/NJH",
    "/NJS"
)

if ($excludedDirectories.Count -gt 0) {
    $robocopyArguments += "/XD"
    foreach ($name in $excludedDirectories) {
        $robocopyArguments += (Join-Path $ProjectPath $name)
    }
}

& robocopy @robocopyArguments | Out-Host
$robocopyExitCode = $LASTEXITCODE
if ($robocopyExitCode -gt 7) {
    throw "robocopy failed with exit code $robocopyExitCode"
}

foreach ($transientName in @("Temp", "Obj", "Logs")) {
    $transientPath = Join-Path $MirrorProjectPath $transientName
    if (Test-Path $transientPath) {
        Remove-Item -Path $transientPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

& $buildScript `
    -UnityEditor $UnityEditor `
    -ProjectPath $MirrorProjectPath `
    -ReleaseId $ReleaseId `
    -OutputRoot $OutputRoot `
    -Scenes $Scenes `
    -TargetPlatform Windows `
    -LogPath $LogPath `
    -MaxAttempts $MaxAttempts

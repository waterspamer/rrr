param(
    [Parameter(Mandatory = $true)]
    [string]$SourceProjectPath,
    [Parameter(Mandatory = $true)]
    [string]$MirrorProjectPath,
    [Parameter(Mandatory = $true)]
    [string]$ReleaseRoot,
    [Parameter(Mandatory = $true)]
    [string]$UnityExePath,
    [string]$ReleaseId = "",
    [string]$SourceCommit = "",
    [string]$SourceBranch = "",
    [string]$PublicUrl = "https://rrr-demo.tonforspeed.space/downloads/windows/latest.zip",
    [switch]$DeployAfterBuild,
    [string]$ServerHost = "93.183.80.30",
    [string]$Username = "root",
    [string]$Password = $env:RRR_SERVER_PASSWORD,
    [string]$RemoteRoot = "/var/www/rrr-downloads/windows",
    [int]$KeepServerReleases = 5
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function Write-ProgressMarker([double]$Value, [string]$Message) {
    Write-Host ("##rrr-progress|{0}|{1}" -f $Value.ToString("0.00", [System.Globalization.CultureInfo]::InvariantCulture), $Message)
}

function Get-GitValue([string]$Arguments) {
    try {
        $output = git -C $SourceProjectPath $Arguments 2>$null
        if ($LASTEXITCODE -eq 0) {
            return ($output | Out-String).Trim()
        }
    }
    catch {
    }

    return ""
}

if (-not (Test-Path $SourceProjectPath)) {
    throw "Source project path not found: $SourceProjectPath"
}

if (-not (Test-Path $UnityExePath)) {
    throw "Unity executable not found: $UnityExePath"
}

$SourceProjectPath = [System.IO.Path]::GetFullPath($SourceProjectPath)
$MirrorProjectPath = [System.IO.Path]::GetFullPath($MirrorProjectPath)
$ReleaseRoot = [System.IO.Path]::GetFullPath($ReleaseRoot)

if ([string]::IsNullOrWhiteSpace($SourceCommit)) {
    $SourceCommit = Get-GitValue "rev-parse --short=12 HEAD"
}

if ([string]::IsNullOrWhiteSpace($SourceBranch)) {
    $SourceBranch = Get-GitValue "rev-parse --abbrev-ref HEAD"
}

if ([string]::IsNullOrWhiteSpace($ReleaseId)) {
    $suffix = if ([string]::IsNullOrWhiteSpace($SourceCommit)) { "manual" } else { $SourceCommit }
    $ReleaseId = "{0}-{1}" -f (Get-Date -Format "yyyyMMdd-HHmmss"), $suffix
}

$releasePath = Join-Path $ReleaseRoot $ReleaseId
Write-Host "##rrr-release-id|$ReleaseId"
Write-Host "##rrr-release-path|$releasePath"

Write-ProgressMarker 0.08 "Preparing build mirror"
New-Item -ItemType Directory -Force -Path $MirrorProjectPath | Out-Null
New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null

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
    $SourceProjectPath,
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
        $robocopyArguments += (Join-Path $SourceProjectPath $name)
    }
}

Write-ProgressMarker 0.18 "Syncing working project into build mirror"
& robocopy @robocopyArguments | Out-Host
$robocopyExitCode = $LASTEXITCODE
if ($robocopyExitCode -gt 7) {
    throw "robocopy failed with exit code $robocopyExitCode"
}

if (Test-Path $releasePath) {
    Remove-Item -Path $releasePath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $releasePath | Out-Null

$mirrorTransientPaths = @(
    (Join-Path $MirrorProjectPath "Temp"),
    (Join-Path $MirrorProjectPath "Obj"),
    (Join-Path $MirrorProjectPath "Logs")
)

foreach ($path in $mirrorTransientPaths) {
    if (Test-Path $path) {
        Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$env:RRR_DESKTOP_OUTPUT_DIR = $releasePath
$env:RRR_RELEASE_ID = $ReleaseId
$env:RRR_RELEASE_COMMIT = $SourceCommit
$env:RRR_RELEASE_BRANCH = $SourceBranch
$env:RRR_RELEASE_PUBLIC_URL = $PublicUrl

$unityArguments = @(
    "-batchmode",
    "-quit",
    "-nographics",
    "-projectPath", $MirrorProjectPath,
    "-executeMethod", "DesktopBuildPipeline.BuildFromCommandLine",
    "-logFile", "-"
)

Write-ProgressMarker 0.34 "Launching Unity batch build from mirror"
& $UnityExePath @unityArguments
if ($LASTEXITCODE -ne 0) {
    throw "Unity batch build failed with exit code $LASTEXITCODE"
}

Write-ProgressMarker 0.72 "Mirror build finished"

if ($DeployAfterBuild) {
    $publishScript = Join-Path $SourceProjectPath "Scripts\\Publish-DesktopRelease.ps1"
    if (-not (Test-Path $publishScript)) {
        throw "Publish script not found: $publishScript"
    }

    Write-ProgressMarker 0.76 "Publishing desktop release"
    & powershell.exe -ExecutionPolicy Bypass -File $publishScript `
        -ReleasePath $releasePath `
        -ReleaseId $ReleaseId `
        -ServerHost $ServerHost `
        -Username $Username `
        -Password $Password `
        -RemoteRoot $RemoteRoot `
        -KeepServerReleases $KeepServerReleases `
        -PublicUrl $PublicUrl

    if ($LASTEXITCODE -ne 0) {
        throw "Desktop publish failed with exit code $LASTEXITCODE"
    }
}
else {
    Write-ProgressMarker 1.00 "Mirror build ready"
}

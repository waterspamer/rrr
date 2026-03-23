param(
    [string]$UnityEditor = "C:\Program Files\Unity\Hub\Editor\6000.3.3f1\Editor\Unity.exe",
    [string]$SourceRepo = "C:\Work\Prototyping\Russian Road Rage",
    [string]$WorkspacePath = "C:\Work\BuildAgents\RRR-Dedicated\workspace",
    [string]$ReleasesRoot = "C:\Work\BuildAgents\RRR-Dedicated\releases",
    [string]$Branch = "main",
    [string[]]$Scenes = @("Assets/Scenes/Game.unity"),
    [int]$KeepLocalReleases = 5,
    [int]$KeepServerReleases = 5,
    [string]$ServerHost = "93.183.80.30",
    [string]$Username = "root",
    [string]$Password = $env:RRR_SERVER_PASSWORD,
    [string]$RemoteRoot = "/opt/rrr-dedicated",
    [string]$ServiceName = "rrr-dedicated"
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "Pass -Password or set RRR_SERVER_PASSWORD."
}

if (-not (Test-Path $UnityEditor)) {
    throw "Unity editor not found: $UnityEditor"
}

$prepareScript = Join-Path $PSScriptRoot "Prepare-DedicatedWorkspace.ps1"
if (-not (Test-Path $prepareScript)) {
    throw "Prepare script not found: $prepareScript"
}

& $prepareScript -SourceRepo $SourceRepo -WorkspacePath $WorkspacePath -Branch $Branch

$workspacePathNormalized = [IO.Path]::GetFullPath($WorkspacePath)
$staleUnityProcesses = Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -eq "Unity.exe" -and
        $_.CommandLine -and
        $_.CommandLine.Contains($workspacePathNormalized)
    }

foreach ($process in $staleUnityProcesses) {
    try {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
    }
    catch {
    }
}

$lockFile = Join-Path $WorkspacePath "Temp\\UnityLockfile"
if (Test-Path $lockFile) {
    Remove-Item -Force $lockFile -ErrorAction SilentlyContinue
}

Start-Sleep -Seconds 2

Push-Location $WorkspacePath
try {
    $commit = (git rev-parse --short=12 HEAD).Trim()
    $branchName = (git rev-parse --abbrev-ref HEAD).Trim()
}
finally {
    Pop-Location
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$releaseId = "$timestamp-$commit"
$releasePath = Join-Path $ReleasesRoot $releaseId

New-Item -ItemType Directory -Force -Path $ReleasesRoot | Out-Null
if (Test-Path $releasePath) {
    Remove-Item -Recurse -Force $releasePath
}
New-Item -ItemType Directory -Force -Path $releasePath | Out-Null

$env:RRR_DEDICATED_OUTPUT_DIR = $releasePath
$env:RRR_RELEASE_ID = $releaseId
$env:RRR_RELEASE_COMMIT = $commit
$env:RRR_RELEASE_BRANCH = $branchName
$env:RRR_RELEASE_PUBLIC_URL = ""

$scenesArg = ($Scenes | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ";"

& $UnityEditor `
    -batchmode `
    -quit `
    -standaloneBuildSubtarget Server `
    -projectPath $WorkspacePath `
    -executeMethod DedicatedServerBuildPipeline.BuildFromCommandLine `
    -rrrDedicatedScenes $scenesArg `
    -logFile -

if ($LASTEXITCODE -ne 0) {
    throw "Unity Dedicated Server build failed with exit code $LASTEXITCODE."
}

$metadataPath = Join-Path $releasePath "release.json"
$launchScript = Join-Path $releasePath "run.sh"
if (-not (Test-Path $metadataPath)) {
    throw "Dedicated Server build did not produce release.json at $releasePath"
}
if (-not (Test-Path $launchScript)) {
    throw "Dedicated Server build did not produce run.sh at $releasePath"
}

$publishScript = Join-Path $PSScriptRoot "Publish-DedicatedServerRelease.ps1"
if (-not (Test-Path $publishScript)) {
    throw "Publish script not found: $publishScript"
}

& $publishScript `
    -ReleasePath $releasePath `
    -ReleaseId $releaseId `
    -ServerHost $ServerHost `
    -Username $Username `
    -Password $Password `
    -RemoteRoot $RemoteRoot `
    -KeepServerReleases $KeepServerReleases `
    -ServiceName $ServiceName

$localReleases = Get-ChildItem -Path $ReleasesRoot -Directory | Sort-Object LastWriteTime -Descending
if ($localReleases.Count -gt $KeepLocalReleases) {
    $localReleases | Select-Object -Skip $KeepLocalReleases | ForEach-Object {
        Remove-Item -Recurse -Force $_.FullName
        $archive = Join-Path $ReleasesRoot ($_.Name + ".zip")
        if (Test-Path $archive) {
            Remove-Item -Force $archive
        }
    }
}

$localArchives = Get-ChildItem -Path $ReleasesRoot -Filter "*.zip" -File | Sort-Object LastWriteTime -Descending
if ($localArchives.Count -gt $KeepLocalReleases) {
    $localArchives | Select-Object -Skip $KeepLocalReleases | Remove-Item -Force
}

Write-Host "Dedicated Server release created: $releaseId"
Write-Host "Local release: $releasePath"
Write-Host "Remote root: $RemoteRoot"

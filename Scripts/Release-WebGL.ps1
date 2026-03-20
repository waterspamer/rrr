param(
    [string]$UnityEditor = "C:\Program Files\Unity\Hub\Editor\6000.3.3f1\Editor\Unity.exe",
    [string]$SourceRepo = "C:\Work\Prototyping\Russian Road Rage",
    [string]$WorkspacePath = "C:\Work\BuildAgents\RRR-WebGL\workspace",
    [string]$ReleasesRoot = "C:\Work\BuildAgents\RRR-WebGL\releases",
    [string]$Branch = "main",
    [string]$Compression = "Disabled",
    [int]$KeepLocalReleases = 5,
    [int]$KeepServerReleases = 5,
    [string]$ServerHost = "93.183.80.30",
    [string]$Username = "root",
    [string]$Password = $env:RRR_SERVER_PASSWORD,
    [string]$RemoteRoot = "/var/www/rrr-webgl"
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

$prepareScript = Join-Path $PSScriptRoot "Prepare-WebGLWorkspace.ps1"
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
$archivePath = Join-Path $ReleasesRoot ($releaseId + ".zip")

New-Item -ItemType Directory -Force -Path $ReleasesRoot | Out-Null
if (Test-Path $releasePath) {
    Remove-Item -Recurse -Force $releasePath
}
New-Item -ItemType Directory -Force -Path $releasePath | Out-Null

$compressionArg = switch ($Compression.ToLowerInvariant()) {
    "gzip" { "gzip" }
    "brotli" { "brotli" }
    "br" { "brotli" }
    default { "disabled" }
}

& $UnityEditor `
    -batchmode `
    -nographics `
    -quit `
    -projectPath $WorkspacePath `
    -executeMethod WebGlBuildPipeline.BuildFromCommandLine `
    -rrrBuildPath $releasePath `
    -rrrWebGlCompression $compressionArg `
    -logFile -

if ($LASTEXITCODE -ne 0) {
    throw "Unity WebGL build failed with exit code $LASTEXITCODE."
}

$indexPath = Join-Path $releasePath "index.html"
if (-not (Test-Path $indexPath)) {
    throw "WebGL build did not produce index.html at $releasePath"
}

$metadata = [ordered]@{
    releaseId = $releaseId
    commit = $commit
    branch = $branchName
    sourceRepo = $SourceRepo
    workspacePath = $WorkspacePath
    builtAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    compression = $compressionArg
    publicUrl = "https://rrr-demo.tonforspeed.space/play/"
}
$metadata | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $releasePath "release.json") -Encoding UTF8

$publishScript = Join-Path $PSScriptRoot "Publish-WebGLRelease.ps1"
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
    -PublicUrl "https://rrr-demo.tonforspeed.space/play/"

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

Write-Host "WebGL release created: $releaseId"
Write-Host "Local release: $releasePath"
Write-Host "Public URL: https://rrr-demo.tonforspeed.space/play/"

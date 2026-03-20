param(
    [string]$SourceRepo = "C:\Work\Prototyping\Russian Road Rage",
    [string]$WorkspacePath = "C:\Work\BuildAgents\RRR-WebGL\workspace",
    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

if (-not (Test-Path $SourceRepo)) {
    throw "Source repo not found: $SourceRepo"
}

if (-not (Test-Path (Join-Path $SourceRepo ".git"))) {
    throw "Source repo does not look like a git repository: $SourceRepo"
}

$workspaceRoot = Split-Path -Parent $WorkspacePath
if (-not [string]::IsNullOrWhiteSpace($workspaceRoot)) {
    New-Item -ItemType Directory -Force -Path $workspaceRoot | Out-Null
}

if (-not (Test-Path (Join-Path $WorkspacePath ".git"))) {
    Write-Host "Creating WebGL build workspace at $WorkspacePath"
    git clone $SourceRepo $WorkspacePath
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to clone source repo into build workspace."
    }
}

Push-Location $WorkspacePath
try {
    git remote set-url origin $SourceRepo
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to point workspace origin to source repo."
    }
    git fetch origin
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to fetch latest refs for build workspace."
    }
    git checkout $Branch
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to checkout branch '$Branch' in build workspace."
    }
    git reset --hard ("origin/" + $Branch)
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to reset build workspace to origin/$Branch."
    }

    $cleanExcludes = @(
        "-e", "Library",
        "-e", "Temp",
        "-e", "Logs",
        "-e", "UserSettings",
        "-e", "Builds",
        "-e", "Obj"
    )
    & git clean -fdx @cleanExcludes | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to clean build workspace."
    }

    $commit = (git rev-parse --short=12 HEAD).Trim()
    Write-Host "Workspace is ready. Branch: $Branch, commit: $commit"
}
finally {
    Pop-Location
}

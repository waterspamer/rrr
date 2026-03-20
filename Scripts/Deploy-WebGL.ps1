param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$releaseScript = Join-Path $PSScriptRoot "Release-WebGL.ps1"
if (-not (Test-Path $releaseScript)) {
    throw "Release script not found: $releaseScript"
}

& $releaseScript @RemainingArgs

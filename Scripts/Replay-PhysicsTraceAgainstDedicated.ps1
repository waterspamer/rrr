param(
    [Parameter(Mandatory = $true)] [string]$TracePath,
    [string]$DedicatedReleasePath = "",
    [string]$DedicatedExePath = "",
    [string]$DedicatedHost = "127.0.0.1",
    [int]$Port = 7777,
    [string]$ControlToken = "",
    [string]$OutputPath = "",
    [string]$Label = "",
    [switch]$StartDedicated,
    [switch]$KeepDedicatedRunning,
    [int]$StartupTimeoutSec = 60
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function New-DedicatedHeaders {
    $headers = @{}
    if (-not [string]::IsNullOrWhiteSpace($ControlToken)) {
        $headers["X-RRR-Service-Token"] = $ControlToken
    }
    return $headers
}

function Invoke-DedicatedJson {
    param(
        [Parameter(Mandatory = $true)] [string]$Method,
        [Parameter(Mandatory = $true)] [string]$Path,
        [object]$Body = $null
    )

    $uri = "http://$DedicatedHost`:$Port$Path"
    $headers = New-DedicatedHeaders
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -TimeoutSec 30
    }

    $payload = $Body | ConvertTo-Json -Depth 12 -Compress
    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -Body $payload -ContentType "application/json" -TimeoutSec 30
}

function Wait-DedicatedHealth {
    $deadline = (Get-Date).AddSeconds([Math]::Max(5, $StartupTimeoutSec))
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-DedicatedJson -Method Get -Path "/health"
            if ($response -and $response.status -eq "ok") {
                return
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 750
    }

    throw "Dedicated server did not become healthy on http://$DedicatedHost`:$Port within $StartupTimeoutSec second(s)."
}

function Get-DedicatedExePath {
    if (-not [string]::IsNullOrWhiteSpace($DedicatedExePath)) {
        return (Resolve-Path $DedicatedExePath).Path
    }

    if ([string]::IsNullOrWhiteSpace($DedicatedReleasePath)) {
        throw "Pass -DedicatedExePath or -DedicatedReleasePath when using -StartDedicated."
    }

    $releasePath = (Resolve-Path $DedicatedReleasePath).Path
    $metadataPath = Join-Path $releasePath "release.json"
    if (-not (Test-Path $metadataPath)) {
        throw "release.json not found in $releasePath"
    }

    $metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
    if (-not $metadata -or [string]::IsNullOrWhiteSpace($metadata.primaryArtifact)) {
        throw "release.json in $releasePath does not contain primaryArtifact"
    }

    $resolvedExe = Join-Path $releasePath $metadata.primaryArtifact
    if (-not (Test-Path $resolvedExe)) {
        throw "Dedicated executable not found: $resolvedExe"
    }

    return $resolvedExe
}

function Get-VectorDistance {
    param([object]$Left, [object]$Right)

    if ($null -eq $Left -or $null -eq $Right) {
        return 0.0
    }

    $dx = [double]$Left.x - [double]$Right.x
    $dy = [double]$Left.y - [double]$Right.y
    $dz = [double]$Left.z - [double]$Right.z
    return [Math]::Sqrt(($dx * $dx) + ($dy * $dy) + ($dz * $dz))
}

function Convert-EulerToQuaternion {
    param([object]$Euler)

    if ($null -eq $Euler) {
        return [System.Numerics.Quaternion]::Identity
    }

    $pitch = [double]$Euler.x * [Math]::PI / 180.0
    $yaw = [double]$Euler.y * [Math]::PI / 180.0
    $roll = [double]$Euler.z * [Math]::PI / 180.0
    return [System.Numerics.Quaternion]::CreateFromYawPitchRoll([float]$yaw, [float]$pitch, [float]$roll)
}

function Get-RotationAngleDegrees {
    param([object]$LeftEuler, [object]$RightEuler)

    $left = Convert-EulerToQuaternion $LeftEuler
    $right = Convert-EulerToQuaternion $RightEuler
    $dot = [Math]::Abs([double]([System.Numerics.Quaternion]::Dot($left, $right)))
    $dot = [Math]::Min(1.0, [Math]::Max(-1.0, $dot))
    return 2.0 * [Math]::Acos($dot) * 180.0 / [Math]::PI
}

function Get-WheelMetrics {
    param([object[]]$Expected, [object[]]$Actual)

    if ($null -eq $Expected -or $null -eq $Actual) {
        return @{
            wheel_position_error = 0.0
            wheel_rotation_error_deg = 0.0
            wheel_samples = 0
        }
    }

    $count = [Math]::Min($Expected.Count, $Actual.Count)
    if ($count -le 0) {
        return @{
            wheel_position_error = 0.0
            wheel_rotation_error_deg = 0.0
            wheel_samples = 0
        }
    }

    $positionError = 0.0
    $rotationError = 0.0
    for ($index = 0; $index -lt $count; $index++) {
        $positionError += Get-VectorDistance $Expected[$index].position $Actual[$index].position
        $rotationError += Get-RotationAngleDegrees $Expected[$index].rotation $Actual[$index].rotation
    }

    return @{
        wheel_position_error = $positionError / $count
        wheel_rotation_error_deg = $rotationError / $count
        wheel_samples = $count
    }
}

if (-not (Test-Path $TracePath)) {
    throw "Trace file not found: $TracePath"
}

$trace = Get-Content $TracePath -Raw | ConvertFrom-Json
if ($null -eq $trace -or $null -eq $trace.frames -or $trace.frames.Count -eq 0) {
    throw "Trace file does not contain frames: $TracePath"
}

$traceLabel = if (-not [string]::IsNullOrWhiteSpace($Label)) { $Label } elseif (-not [string]::IsNullOrWhiteSpace($trace.label)) { $trace.label } else { [IO.Path]::GetFileNameWithoutExtension($TracePath) }
$tickRate = if ($trace.tick_rate) { [int]$trace.tick_rate } else { 30 }
$playerId = if (-not [string]::IsNullOrWhiteSpace($trace.player_id)) { $trace.player_id } else { "local_player" }
$mapId = if (-not [string]::IsNullOrWhiteSpace($trace.map_id)) { $trace.map_id } else { "city_default" }
$matchId = "trace-" + ([Guid]::NewGuid().ToString("N"))

$reportBasePath = if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath
}
else {
    Join-Path (Split-Path -Parent (Resolve-Path $TracePath).Path) ($traceLabel + "-dedicated-report")
}
$reportJsonPath = if ([IO.Path]::HasExtension($reportBasePath)) { $reportBasePath } else { $reportBasePath + ".json" }
$reportCsvPath = [IO.Path]::ChangeExtension($reportJsonPath, ".csv")

$dedicatedProcess = $null
$startedDedicatedHere = $false
$createdRoom = $false

try {
    if ($StartDedicated) {
        $resolvedExePath = Get-DedicatedExePath
        $startedDedicatedHere = $true
        $stdoutLogPath = [IO.Path]::ChangeExtension($reportJsonPath, ".dedicated.stdout.log")
        $stderrLogPath = [IO.Path]::ChangeExtension($reportJsonPath, ".dedicated.stderr.log")
        $env:RRR_DEDICATED_BIND = "127.0.0.1"
        $env:RRR_DEDICATED_PORT = "$Port"
        $env:RRR_DEDICATED_PUBLIC_HTTP_BASE_URL = "http://127.0.0.1:$Port"
        $env:RRR_DEDICATED_PUBLIC_WS_BASE_URL = "ws://127.0.0.1:$Port"
        $env:RRR_DEDICATED_CONTROL_TOKEN = $ControlToken

        $dedicatedProcess = Start-Process `
            -FilePath $resolvedExePath `
            -ArgumentList @("-batchmode", "-nographics") `
            -PassThru `
            -WindowStyle Hidden `
            -RedirectStandardOutput $stdoutLogPath `
            -RedirectStandardError $stderrLogPath
    }

    Wait-DedicatedHealth

    $createRoomBody = @{
        match_id = $matchId
        map_id = $mapId
        tick_rate = $tickRate
        broadcast_rate = $tickRate
        manual_tick = $true
        players = @(
            @{
                player_id = $playerId
                player_name = $playerId
                authority_order = 0
                spawn_point_id = ""
                spawn_position = $trace.spawn_position
                spawn_rotation = $trace.spawn_rotation
                car_config = $trace.car_config
            }
        )
    }
    $null = Invoke-DedicatedJson -Method Post -Path "/api/v1/rooms" -Body $createRoomBody
    $createdRoom = $true

    $comparisonRows = New-Object System.Collections.Generic.List[object]
    $positionErrorSum = 0.0
    $rotationErrorSum = 0.0
    $velocityErrorSum = 0.0
    $speedErrorSum = 0.0
    $wheelPositionErrorSum = 0.0
    $wheelRotationErrorSum = 0.0

    $maxPositionError = 0.0
    $maxRotationError = 0.0
    $maxVelocityError = 0.0
    $maxAngularVelocityError = 0.0
    $maxSpeedError = 0.0
    $maxRpmError = 0.0
    $maxWheelPositionError = 0.0
    $maxWheelRotationError = 0.0

    foreach ($frame in $trace.frames) {
        $inputBody = @{
            players = @(
                @{
                    player_id = $playerId
                    seq = [int]$frame.seq
                    client_time = if ($frame.client_time) { [long]$frame.client_time } else { [long]$frame.seq }
                    input = $frame.input
                }
            )
        }
        $null = Invoke-DedicatedJson -Method Post -Path "/api/v1/rooms/$matchId/inputs" -Body $inputBody
        $stepResponse = Invoke-DedicatedJson -Method Post -Path "/api/v1/rooms/$matchId/step" -Body @{ ticks = 1 }
        if ($null -eq $stepResponse -or $null -eq $stepResponse.snapshot -or $null -eq $stepResponse.snapshot.players) {
            throw "Dedicated step response for seq $($frame.seq) does not contain a snapshot."
        }

        $playerSnapshot = $stepResponse.snapshot.players | Where-Object { $_.player_id -eq $playerId } | Select-Object -First 1
        if ($null -eq $playerSnapshot) {
            throw "Dedicated snapshot for seq $($frame.seq) does not contain player '$playerId'."
        }

        $positionError = Get-VectorDistance $frame.state.position $playerSnapshot.position
        $rotationError = Get-RotationAngleDegrees $frame.state.rotation $playerSnapshot.rotation
        $velocityError = Get-VectorDistance $frame.state.velocity $playerSnapshot.velocity
        $angularVelocityError = Get-VectorDistance $frame.state.angular_velocity $playerSnapshot.angular_velocity
        $speedError = if ($frame.debug -and $playerSnapshot.debug) { [Math]::Abs([double]$frame.debug.speed_kph - [double]$playerSnapshot.debug.speed_kph) } else { 0.0 }
        $rpmError = if ($frame.debug -and $playerSnapshot.debug) { [Math]::Abs([double]$frame.debug.current_rpm - [double]$playerSnapshot.debug.current_rpm) } else { 0.0 }
        $wheelMetrics = Get-WheelMetrics $frame.state.wheel_states $playerSnapshot.wheel_states

        $positionErrorSum += $positionError
        $rotationErrorSum += $rotationError
        $velocityErrorSum += $velocityError
        $speedErrorSum += $speedError
        $wheelPositionErrorSum += [double]$wheelMetrics.wheel_position_error
        $wheelRotationErrorSum += [double]$wheelMetrics.wheel_rotation_error_deg

        $maxPositionError = [Math]::Max($maxPositionError, $positionError)
        $maxRotationError = [Math]::Max($maxRotationError, $rotationError)
        $maxVelocityError = [Math]::Max($maxVelocityError, $velocityError)
        $maxAngularVelocityError = [Math]::Max($maxAngularVelocityError, $angularVelocityError)
        $maxSpeedError = [Math]::Max($maxSpeedError, $speedError)
        $maxRpmError = [Math]::Max($maxRpmError, $rpmError)
        $maxWheelPositionError = [Math]::Max($maxWheelPositionError, [double]$wheelMetrics.wheel_position_error)
        $maxWheelRotationError = [Math]::Max($maxWheelRotationError, [double]$wheelMetrics.wheel_rotation_error_deg)

        $comparisonRows.Add([pscustomobject]@{
            tick = [int]$frame.tick
            seq = [int]$frame.seq
            server_tick = [int]$stepResponse.server_tick
            ack_input_seq = [int]$playerSnapshot.ack_input_seq
            position_error = [Math]::Round($positionError, 6)
            rotation_error_deg = [Math]::Round($rotationError, 6)
            velocity_error = [Math]::Round($velocityError, 6)
            angular_velocity_error = [Math]::Round($angularVelocityError, 6)
            speed_kph_error = [Math]::Round($speedError, 6)
            rpm_error = [Math]::Round($rpmError, 6)
            wheel_position_error = [Math]::Round([double]$wheelMetrics.wheel_position_error, 6)
            wheel_rotation_error_deg = [Math]::Round([double]$wheelMetrics.wheel_rotation_error_deg, 6)
            current_gear_client = if ($frame.debug) { [int]$frame.debug.current_gear } else { 0 }
            current_gear_server = if ($playerSnapshot.debug) { [int]$playerSnapshot.debug.current_gear } else { 0 }
            speed_kph_client = if ($frame.debug) { [Math]::Round([double]$frame.debug.speed_kph, 6) } else { 0.0 }
            speed_kph_server = if ($playerSnapshot.debug) { [Math]::Round([double]$playerSnapshot.debug.speed_kph, 6) } else { 0.0 }
        })
    }

    $frameCount = [Math]::Max(1, $comparisonRows.Count)
    $report = [ordered]@{
        label = $traceLabel
        trace_path = (Resolve-Path $TracePath).Path
        dedicated_host = $DedicatedHost
        dedicated_port = $Port
        match_id = $matchId
        player_id = $playerId
        tick_rate = $tickRate
        total_frames = $comparisonRows.Count
        max_position_error = [Math]::Round($maxPositionError, 6)
        avg_position_error = [Math]::Round($positionErrorSum / $frameCount, 6)
        max_rotation_error_deg = [Math]::Round($maxRotationError, 6)
        avg_rotation_error_deg = [Math]::Round($rotationErrorSum / $frameCount, 6)
        max_velocity_error = [Math]::Round($maxVelocityError, 6)
        avg_velocity_error = [Math]::Round($velocityErrorSum / $frameCount, 6)
        max_angular_velocity_error = [Math]::Round($maxAngularVelocityError, 6)
        max_speed_kph_error = [Math]::Round($maxSpeedError, 6)
        avg_speed_kph_error = [Math]::Round($speedErrorSum / $frameCount, 6)
        max_rpm_error = [Math]::Round($maxRpmError, 6)
        max_wheel_position_error = [Math]::Round($maxWheelPositionError, 6)
        avg_wheel_position_error = [Math]::Round($wheelPositionErrorSum / $frameCount, 6)
        max_wheel_rotation_error_deg = [Math]::Round($maxWheelRotationError, 6)
        avg_wheel_rotation_error_deg = [Math]::Round($wheelRotationErrorSum / $frameCount, 6)
        frames = $comparisonRows
    }

    $reportDirectory = Split-Path -Parent $reportJsonPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
    }

    $report | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 $reportJsonPath
    $comparisonRows | Export-Csv -Path $reportCsvPath -NoTypeInformation -Encoding UTF8

    Write-Host "Physics replay completed."
    Write-Host "ReportJson: $reportJsonPath"
    Write-Host "ReportCsv: $reportCsvPath"
    Write-Host ("MaxPositionError: {0}" -f [Math]::Round($maxPositionError, 6))
    Write-Host ("MaxRotationErrorDeg: {0}" -f [Math]::Round($maxRotationError, 6))
    Write-Host ("MaxSpeedKphError: {0}" -f [Math]::Round($maxSpeedError, 6))
}
finally {
    if ($createdRoom) {
        try {
            $null = Invoke-DedicatedJson -Method Delete -Path "/api/v1/rooms/$matchId"
        }
        catch {
        }
    }

    if ($startedDedicatedHere -and -not $KeepDedicatedRunning -and $dedicatedProcess -and -not $dedicatedProcess.HasExited) {
        try {
            Stop-Process -Id $dedicatedProcess.Id -Force -ErrorAction Stop
        }
        catch {
        }
    }
}

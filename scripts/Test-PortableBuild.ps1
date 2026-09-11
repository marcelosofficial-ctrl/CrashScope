[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ExpectedVersion,

    [switch]$RequireBuildInfo,

    [switch]$LeaveRunning
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Step([string]$Message) {
    Write-Host "[CrashScope] $Message"
}

$resolvedPublish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$exe = Join-Path $resolvedPublish "CrashScope.exe"
$expectedConfigTraceSha256 = "fe1c470a58402e82e97ee529c6a6b02822430da70e65ffc5fc5a71359ad4e521"
$configTraceExe = Join-Path $resolvedPublish "providers\ConfigTrace\configtrace.exe"
$configTraceLicense = Join-Path $resolvedPublish "providers\ConfigTrace\LICENSE.txt"
$required = @(
    $exe,
    (Join-Path $resolvedPublish "wwwroot\index.html"),
    (Join-Path $resolvedPublish "LICENSE.txt"),
    (Join-Path $resolvedPublish "THIRD-PARTY-NOTICES.md"),
    $configTraceExe,
    $configTraceLicense
)

if ($RequireBuildInfo) {
    $required += (Join-Path $resolvedPublish "BUILD-INFO.txt")
}

foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Portable build is missing required file: $path"
    }
}

$configTraceHash = (
    Get-FileHash -LiteralPath $configTraceExe -Algorithm SHA256
).Hash.ToLowerInvariant()
if ($configTraceHash -ne $expectedConfigTraceSha256) {
    throw "Portable ConfigTrace SHA256 mismatch. Expected $expectedConfigTraceSha256 but got $configTraceHash."
}
Write-Step "Verified bundled ConfigTrace SHA256: $configTraceHash"

$existingListeners = @(Get-NetTCPConnection -LocalPort 5077 -State Listen -ErrorAction SilentlyContinue)
if ($existingListeners.Count -gt 0) {
    $owners = @($existingListeners | Select-Object -ExpandProperty OwningProcess -Unique)
    throw "Port 5077 is already in use by process ID(s): $($owners -join ', '). Stop the existing CrashScope instance before running this smoke test."
}

Write-Step "Starting portable build without opening a browser..."
$process = Start-Process -FilePath $exe -ArgumentList "--no-browser" -PassThru -WindowStyle Hidden
$keepRunning = $false

try {
    $status = $null
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        if ($process.HasExited) {
            throw "CrashScope exited before becoming healthy. Exit code: $($process.ExitCode)"
        }

        try {
            $status = Invoke-RestMethod -Uri "http://127.0.0.1:5077/api/status" -TimeoutSec 2
            if ($status.status -eq "running") {
                break
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    if ($null -eq $status -or $status.status -ne "running") {
        throw "Portable CrashScope did not become healthy on localhost:5077."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $status.crashScopeVersion -ne $ExpectedVersion) {
        throw "Portable status reported version '$($status.crashScopeVersion)' but '$ExpectedVersion' was expected."
    }

    Write-Step "Verifying loopback-only listener..."
    $listeners = @(Get-NetTCPConnection -LocalPort 5077 -State Listen)
    $invalid = @($listeners | Where-Object { $_.LocalAddress -notin @("127.0.0.1", "::1") })
    if ($listeners.Count -eq 0 -or $invalid.Count -gt 0) {
        throw "Portable CrashScope exposed an unexpected network listener."
    }

    $listenerPids = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($process.Id -notin $listenerPids) {
        throw "Portable CrashScope did not own the expected loopback listener."
    }

    Write-Step "Verifying dashboard and browser security headers..."
    $root = Invoke-WebRequest -Uri "http://127.0.0.1:5077/" -UseBasicParsing -TimeoutSec 5
    if ($root.StatusCode -ne 200 -or $root.Content -notmatch '<div id="root"></div>') {
        throw "Portable CrashScope did not serve the dashboard root."
    }

    $nosniffOk = $root.Headers["X-Content-Type-Options"] -eq "nosniff"
    $frameOk = $root.Headers["X-Frame-Options"] -eq "DENY"
    $cspOk = -not [string]::IsNullOrWhiteSpace([string]$root.Headers["Content-Security-Policy"])
    if (-not ($nosniffOk -and $frameOk -and $cspOk)) {
        throw "Portable CrashScope did not serve the expected browser security headers."
    }

    $allowed = Invoke-WebRequest -Uri "http://127.0.0.1:5077/api/status" -Headers @{ Origin = "http://localhost:5077" } -UseBasicParsing -TimeoutSec 5
    if ($allowed.StatusCode -ne 200) {
        throw "CrashScope rejected its own loopback browser origin."
    }

    $externalOriginRejected = $false
    try {
        Invoke-WebRequest -Uri "http://127.0.0.1:5077/api/status" -Headers @{ Origin = "https://example.com" } -UseBasicParsing -TimeoutSec 5 | Out-Null
    }
    catch {
        if ($null -ne $_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 403) {
            $externalOriginRejected = $true
        }
        else {
            throw
        }
    }

    if (-not $externalOriginRejected) {
        throw "CrashScope accepted an external browser Origin."
    }

    Write-Step "Verifying clean second-instance handling..."
    $second = Start-Process -FilePath $exe -ArgumentList "--no-browser" -PassThru -WindowStyle Hidden
    try {
        Start-Sleep -Seconds 3
        if (-not $second.HasExited) {
            throw "Second CrashScope instance did not exit cleanly."
        }
    }
    finally {
        if (-not $second.HasExited) {
            Stop-Process -Id $second.Id -Force -ErrorAction SilentlyContinue
        }
    }

    if ($process.HasExited) {
        throw "Primary CrashScope exited during the second-instance smoke test."
    }

    $ownerAfterSecond = (Get-NetTCPConnection -LocalPort 5077 -State Listen | Select-Object -First 1).OwningProcess
    if ($ownerAfterSecond -ne $process.Id) {
        throw "Second-instance handling changed the listener owner."
    }

    Write-Step "PASS: portable application smoke validation completed."

    if ($LeaveRunning) {
        $keepRunning = $true
        Write-Step "CrashScope remains running as PID $($process.Id) at http://localhost:5077/"
    }
}
finally {
    if (-not $keepRunning -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}

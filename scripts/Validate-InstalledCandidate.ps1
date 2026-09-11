[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedSha256,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$ExpectedVersion,

    [string]$EvidenceRoot = (
        Join-Path $env:TEMP (
            "CrashScope-installed-validation-{0}" -f
            ([DateTime]::UtcNow.ToString("yyyyMMddTHHmmssZ"))
        )
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$runKeyPath = "Software\Microsoft\Windows\CurrentVersion\Run"
$startupValueName = "CrashScope"
$productDataRoot = Join-Path $env:LOCALAPPDATA "CrashScope"
$installRoot = Join-Path $env:LOCALAPPDATA "Programs\CrashScope"

$safetyHelpers = Join-Path $PSScriptRoot "ValidationStateSafety.ps1"

if (-not (Test-Path -LiteralPath $safetyHelpers -PathType Leaf)) {
    throw "ValidationStateSafety.ps1 is missing."
}

. $safetyHelpers

function Write-Stage([string]$Text) {
    Write-Host "`n===== $Text =====" -ForegroundColor Cyan
}

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)

    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )
}

function Get-StartupSnapshot {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        $runKeyPath,
        $false
    )

    try {
        if (
            $null -eq $key -or
            $startupValueName -notin @($key.GetValueNames())
        ) {
            return [PSCustomObject]@{
                Exists = $false
                Value = $null
                Kind = $null
            }
        }

        return [PSCustomObject]@{
            Exists = $true
            Value = $key.GetValue(
                $startupValueName,
                $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames
            )
            Kind = $key.GetValueKind($startupValueName)
        }
    }
    finally {
        if ($null -ne $key) {
            $key.Dispose()
        }
    }
}

function Remove-StartupValue {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        $runKeyPath,
        $true
    )

    try {
        if ($null -ne $key) {
            $key.DeleteValue($startupValueName, $false)
        }
    }
    finally {
        if ($null -ne $key) {
            $key.Dispose()
        }
    }
}

function Restore-StartupSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        $Snapshot
    )

    if (-not [bool]$Snapshot.Exists) {
        Remove-StartupValue
        return
    }

    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(
        $runKeyPath,
        $true
    )

    try {
        if ($null -eq $key) {
            throw "Could not restore CrashScope startup registration."
        }

        $key.SetValue(
            $startupValueName,
            $Snapshot.Value,
            $Snapshot.Kind
        )
    }
    finally {
        if ($null -ne $key) {
            $key.Dispose()
        }
    }
}

function ConvertTo-ItemArray {
    param($InputObject)

    if ($null -eq $InputObject) {
        return @()
    }

    if ($InputObject.PSObject.Properties.Name -contains "value") {
        return @($InputObject.value)
    }

    return @($InputObject)
}

function Get-PortListener {
    return (
        Get-NetTCPConnection `
            -LocalPort 5077 `
            -State Listen `
            -ErrorAction SilentlyContinue |
            Select-Object -First 1
    )
}

function Assert-PortFree {
    $listener = Get-PortListener

    if ($null -ne $listener) {
        throw "Port 5077 is already occupied by PID $($listener.OwningProcess)."
    }
}

function Wait-PortFree {
    param([int]$TimeoutSeconds = 15)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    do {
        if ($null -eq (Get-PortListener)) {
            return
        }

        Start-Sleep -Milliseconds 250
    }
    while ([DateTime]::UtcNow -lt $deadline)

    throw "Port 5077 did not become free."
}

function Wait-CrashScopeHealthy {
    param([int]$TimeoutSeconds = 30)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    do {
        try {
            $status = Invoke-RestMethod `
                -Uri "http://127.0.0.1:5077/api/status" `
                -TimeoutSec 2

            if ($status.status -eq "running") {
                return $status
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    while ([DateTime]::UtcNow -lt $deadline)

    throw "Installed CrashScope did not become healthy."
}

function Assert-LoopbackOnly {
    $listeners = @(
        Get-NetTCPConnection `
            -LocalPort 5077 `
            -State Listen `
            -ErrorAction Stop
    )

    if ($listeners.Count -eq 0) {
        throw "CrashScope is not listening on port 5077."
    }

    $invalid = @(
        $listeners |
            Where-Object {
                $_.LocalAddress -notin @("127.0.0.1", "::1")
            }
    )

    if ($invalid.Count -gt 0) {
        throw (
            "CrashScope exposed a non-loopback listener: " +
            ($invalid.LocalAddress -join ", ")
        )
    }

    return $listeners
}

function Assert-BrowserSecurity {
    $root = Invoke-WebRequest `
        -Uri "http://127.0.0.1:5077/" `
        -UseBasicParsing `
        -TimeoutSec 5

    if (
        $root.StatusCode -ne 200 -or
        $root.Content -notmatch '<div id="root"></div>'
    ) {
        throw "Installed CrashScope did not serve the bundled dashboard."
    }

    if ($root.Headers["Cache-Control"] -ne "no-store") {
        throw "Expected Cache-Control: no-store."
    }

    if ($root.Headers["X-Content-Type-Options"] -ne "nosniff") {
        throw "Missing X-Content-Type-Options: nosniff."
    }

    if ($root.Headers["X-Frame-Options"] -ne "DENY") {
        throw "Missing X-Frame-Options: DENY."
    }

    if (
        [string]::IsNullOrWhiteSpace(
            [string]$root.Headers["Content-Security-Policy"]
        )
    ) {
        throw "Missing Content-Security-Policy."
    }

    $allowed = Invoke-WebRequest `
        -Uri "http://127.0.0.1:5077/api/status" `
        -Headers @{ Origin = "http://localhost:5077" } `
        -UseBasicParsing `
        -TimeoutSec 5

    if ($allowed.StatusCode -ne 200) {
        throw "CrashScope rejected its own localhost Origin."
    }

    $hostileStatus = $null

    try {
        Invoke-WebRequest `
            -Uri "http://127.0.0.1:5077/api/status" `
            -Headers @{ Origin = "https://example.com" } `
            -UseBasicParsing `
            -TimeoutSec 5 |
            Out-Null
    }
    catch {
        if ($null -ne $_.Exception.Response) {
            try {
                $hostileStatus = [int]$_.Exception.Response.StatusCode
            }
            catch {
            }
        }
    }

    if ($hostileStatus -ne 403) {
        throw "Hostile browser Origin did not receive HTTP 403."
    }
}

function Get-UninstallEntries {
    $result = @()

    $roots = @(
        @{
            Scope = "HKCU"
            Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall"
        },
        @{
            Scope = "HKLM64"
            Path = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall"
        },
        @{
            Scope = "HKLM32"
            Path = "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        }
    )

    foreach ($root in $roots) {
        $keys = @(
            Get-ChildItem `
                -LiteralPath $root.Path `
                -ErrorAction SilentlyContinue
        )

        foreach ($key in $keys) {
            $item = Get-ItemProperty `
                -LiteralPath $key.PSPath `
                -ErrorAction SilentlyContinue

            if ($null -eq $item) {
                continue
            }

            $displayProperty = $item.PSObject.Properties["DisplayName"]

            if ($null -eq $displayProperty) {
                continue
            }

            $displayName = [string]$displayProperty.Value

            if (
                [string]::IsNullOrWhiteSpace($displayName) -or
                $displayName -notlike "CrashScope*"
            ) {
                continue
            }

            $versionProperty = $item.PSObject.Properties["DisplayVersion"]

            $version = if ($null -eq $versionProperty) {
                ""
            }
            else {
                [string]$versionProperty.Value
            }

            $result += [PSCustomObject]@{
                Scope = $root.Scope
                DisplayName = $displayName
                DisplayVersion = $version
            }
        }
    }

    return @($result)
}

function Install-Candidate {
    param([string]$LogPath)

    $arguments = @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/NOCLOSEAPPLICATIONS",
        "/NORESTARTAPPLICATIONS",
        "/TASKS=`"`"",
        "/LOG=`"$LogPath`""
    )

    $process = Start-Process `
        -FilePath $InstallerPath `
        -ArgumentList $arguments `
        -PassThru `
        -Wait

    if ($process.ExitCode -ne 0) {
        throw "Installer exited with code $($process.ExitCode)."
    }
}

function Uninstall-Candidate {
    param([string]$LogPath)

    if (-not (Test-Path -LiteralPath $installRoot -PathType Container)) {
        return
    }

    $uninstallers = @(
        Get-ChildItem `
            -LiteralPath $installRoot `
            -Filter "unins*.exe" `
            -File `
            -ErrorAction Stop
    )

    if ($uninstallers.Count -ne 1) {
        throw "Expected exactly one CrashScope uninstaller."
    }

    $arguments = @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/LOG=`"$LogPath`""
    )

    $process = Start-Process `
        -FilePath $uninstallers[0].FullName `
        -ArgumentList $arguments `
        -PassThru `
        -Wait

    if ($process.ExitCode -ne 0) {
        throw "Uninstaller exited with code $($process.ExitCode)."
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(45)

    do {
        if (-not (Test-Path -LiteralPath $installRoot)) {
            return
        }

        Start-Sleep -Milliseconds 250
    }
    while ([DateTime]::UtcNow -lt $deadline)

    throw "Install directory remained after uninstall."
}

if ($env:OS -ne "Windows_NT") {
    throw "Installed-candidate validation must run on Windows."
}

if (Test-IsElevated) {
    throw "Run installed-candidate validation from normal non-Administrator PowerShell."
}

$installer = (Resolve-Path -LiteralPath $InstallerPath).Path

$actualSha256 = (
    Get-FileHash `
        -LiteralPath $installer `
        -Algorithm SHA256
).Hash.ToLowerInvariant()

$expected = $ExpectedSha256.ToLowerInvariant()

if ($actualSha256 -ne $expected) {
    throw "Installer SHA256 mismatch."
}

if (@(Get-Process -Name "CrashScope" -ErrorAction SilentlyContinue).Count -gt 0) {
    throw "CrashScope is already running."
}

Assert-PortFree

if (Test-Path -LiteralPath $installRoot) {
    throw "CrashScope installation directory already exists."
}

$existingInstallEntries = @(Get-UninstallEntries)

if ($existingInstallEntries.Count -gt 0) {
    throw "CrashScope already appears installed."
}

Assert-CrashScopeValidationStateSafeToStart `
    -WorkingRoot $EvidenceRoot

New-Item `
    -ItemType Directory `
    -Path $EvidenceRoot `
    -Force |
    Out-Null

$startupSnapshot = Get-StartupSnapshot
$stateContext = $null
$primary = $null
$testProcess = $null
$validationError = $null
$restoreErrors = New-Object "System.Collections.Generic.List[string]"
$validationPassed = $false
$dataRestored = $false
$startupRestored = $false
$quarantinePath = $null
$markerId = $null
$installed = $false

try {
    Write-Stage "PRESERVE ORIGINAL USER STATE"

    $stateContext = Protect-CrashScopeValidationState `
        -ProductDataRoot $productDataRoot `
        -StartupSnapshot $startupSnapshot

    Write-Host "Durable vault: $($stateContext.VaultRoot)"
    Write-Host "Existing data preserved: $($stateContext.HadProductData)"
    Write-Host "Existing startup preserved: $([bool]$startupSnapshot.Exists)"

    Remove-StartupValue

    Write-Stage "INSTALL CANDIDATE"

    $installLog = Join-Path $EvidenceRoot "install.log"

    Install-Candidate -LogPath $installLog
    $installed = $true

    $exe = Join-Path $installRoot "CrashScope.exe"

    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "Installed CrashScope.exe is missing."
    }

    $entries = @(Get-UninstallEntries)

    if (
        $entries.Count -ne 1 -or
        $entries[0].Scope -ne "HKCU" -or
        $entries[0].DisplayVersion -ne $ExpectedVersion
    ) {
        $entries | Format-Table -AutoSize
        throw "Installed candidate registration is invalid."
    }

    $startupAfterInstall = Get-StartupSnapshot

    if ([bool]$startupAfterInstall.Exists) {
        throw "Installer unexpectedly created Start-with-Windows state."
    }

    Write-Host "Per-user install registration: PASS"
    Write-Host "Installer forced startup: NO"

    Write-Stage "START INSTALLED CRASHSCOPE"

    $primary = Start-Process `
        -FilePath $exe `
        -ArgumentList "--no-browser" `
        -PassThru `
        -WindowStyle Hidden

    $initialStatus = Wait-CrashScopeHealthy

    if ($initialStatus.crashScopeVersion -ne $ExpectedVersion) {
        throw "Installed candidate reported an unexpected version."
    }

    $listeners = @(Assert-LoopbackOnly)

    $listenerPids = @(
        $listeners |
            Select-Object -ExpandProperty OwningProcess -Unique
    )

    if ($primary.Id -notin $listenerPids) {
        throw "Port 5077 is not owned by the installed candidate."
    }

    Write-Host "Installed runtime healthy: PASS"
    Write-Host "Loopback-only listener:    PASS"

    Write-Stage "BROWSER / LOCALHOST SECURITY"

    Assert-BrowserSecurity

    Write-Host "Browser security contract: PASS"

    Write-Stage "SECOND INSTANCE PROCESS SAFETY"

    $second = Start-Process `
        -FilePath $exe `
        -ArgumentList "--no-browser" `
        -PassThru `
        -WindowStyle Hidden

    Start-Sleep -Seconds 3

    if (-not $second.HasExited) {
        Stop-Process `
            -Id $second.Id `
            -Force `
            -ErrorAction SilentlyContinue

        throw "Second installed CrashScope instance did not exit."
    }

    if ($primary.HasExited) {
        throw "Primary installed instance exited during second-instance test."
    }

    Wait-CrashScopeHealthy | Out-Null

    $ownerAfterSecond = (
        Get-NetTCPConnection `
            -LocalPort 5077 `
            -State Listen |
            Select-Object -First 1
    ).OwningProcess

    if ($ownerAfterSecond -ne $primary.Id) {
        throw "Second instance changed the port listener owner."
    }

    Write-Host "Second-instance safety: PASS"

    Write-Stage "WORKLOAD ATTACH / STOP"

    $testProcess = Start-Process `
        "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -ArgumentList "-NoProfile","-Command","Start-Sleep -Seconds 180" `
        -WindowStyle Hidden `
        -PassThru

    Start-Sleep -Seconds 2

    $rawCandidates = Invoke-RestMethod `
        -Uri "http://127.0.0.1:5077/api/workloads?maximum=200&includeUnlikely=true"

    $candidates = @(
        ConvertTo-ItemArray `
            -InputObject $rawCandidates
    )

    $candidate = $candidates |
        Where-Object {
            $_.processId -eq $testProcess.Id
        } |
        Select-Object -First 1

    if ($null -eq $candidate) {
        throw "Workload discovery did not return the test process."
    }

    $startUtc = [uri]::EscapeDataString(
        [string]$candidate.processStartTimeUtc
    )

    Invoke-RestMethod `
        -Method Post `
        -Uri (
            "http://127.0.0.1:5077/api/sessions/attach-candidate/" +
            "$($candidate.processId)?processStartTimeUtc=$startUtc"
        ) |
        Out-Null

    Start-Sleep -Seconds 3

    $activeStatus = Invoke-RestMethod `
        -Uri "http://127.0.0.1:5077/api/status"

    if (
        $activeStatus.samplingMode -ne "Active" -or
        $null -eq $activeStatus.activeSessionId
    ) {
        throw "Installed workload attach did not enter Active sampling."
    }

    Invoke-RestMethod `
        -Method Post `
        -Uri "http://127.0.0.1:5077/api/sessions/stop" |
        Out-Null

    Start-Sleep -Seconds 2

    $backgroundStatus = Invoke-RestMethod `
        -Uri "http://127.0.0.1:5077/api/status"

    if (
        $backgroundStatus.samplingMode -ne "Background" -or
        $null -ne $backgroundStatus.activeSessionId
    ) {
        throw "Installed workload stop did not return to Background."
    }

    Stop-Process `
        -Id $testProcess.Id `
        -Force `
        -ErrorAction SilentlyContinue

    $testProcess = $null

    Write-Host "Active -> Background transition: PASS"

    Write-Stage "SAFE INCIDENT + RESTART PERSISTENCE"

    $baselineIncidentCount = [int](
        Invoke-RestMethod `
            -Uri "http://127.0.0.1:5077/api/status"
    ).incidentCount

    Write-Host "Capturing safe marker. This intentionally takes about 30 seconds..."

    $marker = Invoke-RestMethod `
        -Method Post `
        -Uri "http://127.0.0.1:5077/api/incidents/capture-marker" `
        -TimeoutSec 60

    if ($marker.classification -ne "UserDiagnosticMarker") {
        throw "Safe marker classification was unexpected."
    }

    $markerId = [string]$marker.incidentId

    $afterMarker = Invoke-RestMethod `
        -Uri "http://127.0.0.1:5077/api/status"

    if ([int]$afterMarker.incidentCount -le $baselineIncidentCount) {
        throw "Incident count did not increase."
    }

    Stop-Process -Id $primary.Id -Force
    $primary.WaitForExit()
    Wait-PortFree

    $primary = Start-Process `
        -FilePath $exe `
        -ArgumentList "--no-browser" `
        -PassThru `
        -WindowStyle Hidden

    $restartStatus = Wait-CrashScopeHealthy

    if ($restartStatus.crashScopeVersion -ne $ExpectedVersion) {
        throw "Restarted installed candidate reported wrong version."
    }

    $rawIncidents = Invoke-RestMethod `
        -Uri "http://127.0.0.1:5077/api/incidents"

    $incidents = @(
        ConvertTo-ItemArray `
            -InputObject $rawIncidents
    )

    $persisted = $incidents |
        Where-Object {
            [string]$_.incidentId -eq $markerId
        } |
        Select-Object -First 1

    if ($null -eq $persisted) {
        throw "Safe marker did not survive installed-app restart."
    }

    Assert-BrowserSecurity

    $testStartup = Get-StartupSnapshot

    if ([bool]$testStartup.Exists) {
        throw "Installed runtime created Start-with-Windows state without user opt-in."
    }

    Write-Host "Safe incident capture: PASS"
    Write-Host "SQLite restart persistence: PASS"
    Write-Host "Runtime forced startup: NO"

    Write-Stage "STOP + UNINSTALL TEST INSTALLATION"

    Stop-Process `
        -Id $primary.Id `
        -Force `
        -ErrorAction SilentlyContinue

    $primary.WaitForExit()
    $primary = $null

    Wait-PortFree

    $uninstallLog = Join-Path $EvidenceRoot "uninstall.log"

    Uninstall-Candidate -LogPath $uninstallLog
    $installed = $false

    if (Test-Path -LiteralPath $installRoot) {
        throw "Install directory remained after uninstall."
    }

    if (@(Get-UninstallEntries).Count -gt 0) {
        throw "Uninstall registration remained after uninstall."
    }

    Write-Host "Runtime stopped: PASS"
    Write-Host "Installed candidate removed: PASS"

    $validationPassed = $true
}
catch {
    $validationError = $_
}
finally {
    if (
        $null -ne $testProcess -and
        -not $testProcess.HasExited
    ) {
        Stop-Process `
            -Id $testProcess.Id `
            -Force `
            -ErrorAction SilentlyContinue
    }

    if (
        $null -ne $primary -and
        -not $primary.HasExited
    ) {
        Stop-Process `
            -Id $primary.Id `
            -Force `
            -ErrorAction SilentlyContinue
    }

    try {
        Wait-PortFree -TimeoutSeconds 10
    }
    catch {
        $restoreErrors.Add(
            "Port cleanup failed: $($_.Exception.Message)"
        )
    }

    if (
        $installed -or
        (Test-Path -LiteralPath $installRoot)
    ) {
        try {
            $cleanupLog = Join-Path $EvidenceRoot "cleanup-uninstall.log"
            Uninstall-Candidate -LogPath $cleanupLog
            $installed = $false
        }
        catch {
            $restoreErrors.Add(
                "Installer cleanup failed: $($_.Exception.Message)"
            )
        }
    }

    if ($null -ne $stateContext) {
        Write-Stage "RESTORE AND VERIFY ORIGINAL USER STATE"

        try {
            $restoreResult = Restore-CrashScopeValidationState `
                -Context $stateContext `
                -ProductDataRoot $productDataRoot

            $dataRestored = [bool]$restoreResult.DataRestoredAndVerified
            $quarantinePath = $restoreResult.QuarantinePath

            if ($null -ne $quarantinePath) {
                Write-Host "Validation-created state quarantined at:"
                Write-Host "  $quarantinePath"
            }
        }
        catch {
            $restoreErrors.Add(
                "Local data restore failed: $($_.Exception.Message)"
            )
        }

        try {
            Restore-StartupSnapshot `
                -Snapshot $startupSnapshot

            $currentStartup = Get-StartupSnapshot

            if (
                -not (
                    Test-CrashScopeStartupSnapshotMatch `
                        -Expected $startupSnapshot `
                        -Actual $currentStartup
                )
            ) {
                throw "Startup registration does not match captured state."
            }

            $startupRestored = $true
        }
        catch {
            $restoreErrors.Add(
                "Startup restore failed: $($_.Exception.Message)"
            )
        }

        if ($dataRestored -and $startupRestored) {
            try {
                Complete-CrashScopeValidationState `
                    -Context $stateContext

                Write-Host (
                    "Original CrashScope data and startup registration " +
                    "restored and verified."
                ) -ForegroundColor Green
            }
            catch {
                $restoreErrors.Add(
                    "Backup-vault cleanup failed: $($_.Exception.Message)"
                )
            }
        }
        else {
            Write-Warning "Durable backup vault intentionally retained:"
            Write-Warning $stateContext.VaultRoot
        }
    }
}

$result = if (
    $validationPassed -and
    $restoreErrors.Count -eq 0
) {
    "PASS"
}
else {
    "FAIL"
}

$report = [PSCustomObject]@{
    result = $result
    candidate = $installer
    sha256 = $actualSha256
    version = $ExpectedVersion
    installedExecutable = Join-Path $installRoot "CrashScope.exe"
    markerIncidentId = $markerId
    originalDataRestored = $dataRestored
    originalStartupRestored = $startupRestored
    testStateQuarantine = $quarantinePath
    restoreErrors = @($restoreErrors)
    validationError = if ($null -eq $validationError) {
        $null
    }
    else {
        [string]$validationError.Exception.Message
    }
    completedAtUtc = [DateTime]::UtcNow.ToString("o")
}

$reportPath = Join-Path $EvidenceRoot "installed-validation-report.json"

$report |
    ConvertTo-Json -Depth 8 |
    Set-Content `
        -LiteralPath $reportPath `
        -Encoding UTF8

if ($result -eq "PASS") {
    Write-Host ""
    Write-Host "PASS: installed candidate validation completed." `
        -ForegroundColor Green

    Write-Host "Report: $reportPath"
    Write-Host "GitHub Actions used: ZERO"
}
else {
    Write-Host ""
    Write-Host "FAIL: installed candidate validation did not complete." `
        -ForegroundColor Red

    Write-Host "Report: $reportPath"

    if ($null -ne $validationError) {
        Write-Host "Validation error:"
        Write-Host "  $($validationError.Exception.Message)"
    }

    if ($restoreErrors.Count -gt 0) {
        Write-Host "Restore/cleanup errors:"
        $restoreErrors |
            ForEach-Object {
                Write-Host "  $_"
            }
    }

    throw "Installed candidate validation failed."
}
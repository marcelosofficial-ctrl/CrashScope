[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CandidateZip,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedSha256,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ExpectedVersion,

    [string]$ExtractRoot = (Join-Path $env:TEMP 'CrashScope-portable-validation'),

    [string]$ReportPath = (Join-Path $env:TEMP 'CrashScope-portable-validation-report.json'),

    [switch]$LeaveRunning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Stage([string]$Text) {
    Write-Host "`n===== $Text =====" -ForegroundColor Cyan
}

function ConvertTo-ItemArray {
    param($InputObject)

    if ($null -eq $InputObject) {
        return @()
    }

    # Windows PowerShell 5.1 can expose JSON collections through a wrapper
    # with `value`/`Count`, while newer PowerShell commonly returns an array.
    if ($InputObject.PSObject.Properties.Name -contains 'value') {
        return @($InputObject.value)
    }

    return @($InputObject)
}

function Wait-CrashScopeHealthy {
    param([int]$TimeoutSeconds = 30)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            $status = Invoke-RestMethod -Uri 'http://127.0.0.1:5077/api/status' -TimeoutSec 2
            if ($status.status -eq 'running') {
                return $status
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "CrashScope did not become healthy on localhost:5077 within $TimeoutSeconds seconds."
}

function Get-PortListener {
    return Get-NetTCPConnection -LocalPort 5077 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Assert-PortFree {
    $listener = Get-PortListener
    if ($null -eq $listener) {
        return
    }

    $process = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    $name = if ($null -ne $process) { $process.ProcessName } else { 'unknown' }
    $path = 'unknown'
    if ($null -ne $process) {
        try { $path = $process.Path } catch { $path = 'unavailable' }
    }
    throw "Port 5077 is already in use by PID $($listener.OwningProcess) ($name) at '$path'. Stop it before validating the candidate."
}

function Wait-PortFree {
    param([int]$TimeoutSeconds = 10)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if ($null -eq (Get-PortListener)) {
            return
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Port 5077 did not become free within $TimeoutSeconds seconds."
}

function Assert-LoopbackOnly {
    $listeners = @(Get-NetTCPConnection -LocalPort 5077 -State Listen -ErrorAction Stop)
    if ($listeners.Count -eq 0) {
        throw 'CrashScope is not listening on port 5077.'
    }

    $invalid = @($listeners | Where-Object { $_.LocalAddress -notin @('127.0.0.1', '::1') })
    if ($invalid.Count -gt 0) {
        $addresses = ($invalid.LocalAddress -join ', ')
        throw "CrashScope exposed a non-loopback listener: $addresses"
    }

    return $listeners
}

function Start-Candidate {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [switch]$OpenBrowser
    )

    if ($OpenBrowser) {
        $process = Start-Process -FilePath $Executable -PassThru -WindowStyle Hidden
    }
    else {
        $process = Start-Process -FilePath $Executable -ArgumentList '--no-browser' -PassThru -WindowStyle Hidden
    }
    $status = Wait-CrashScopeHealthy
    return [PSCustomObject]@{ Process = $process; Status = $status }
}

function Assert-BrowserSecurity {
    Write-Stage 'BROWSER / LOCALHOST SECURITY'

    $root = Invoke-WebRequest -Uri 'http://127.0.0.1:5077/' -UseBasicParsing -TimeoutSec 5
    if ($root.StatusCode -ne 200 -or $root.Content -notmatch '<div id="root"></div>') {
        throw 'CrashScope did not serve the bundled dashboard root.'
    }

    if ($root.Headers['Cache-Control'] -ne 'no-store') {
        throw "Expected Cache-Control: no-store but received '$($root.Headers['Cache-Control'])'."
    }
    if ($root.Headers['X-Content-Type-Options'] -ne 'nosniff') {
        throw 'Missing X-Content-Type-Options: nosniff.'
    }
    if ($root.Headers['X-Frame-Options'] -ne 'DENY') {
        throw 'Missing X-Frame-Options: DENY.'
    }
    if ([string]::IsNullOrWhiteSpace($root.Headers['Content-Security-Policy'])) {
        throw 'Missing Content-Security-Policy header.'
    }

    $allowed = Invoke-WebRequest `
        -Uri 'http://127.0.0.1:5077/api/status' `
        -Headers @{ Origin = 'http://localhost:5077' } `
        -UseBasicParsing `
        -TimeoutSec 5
    if ($allowed.StatusCode -ne 200) {
        throw 'CrashScope rejected its own localhost browser Origin.'
    }

    $hostileStatus = $null
    try {
        Invoke-WebRequest `
            -Uri 'http://127.0.0.1:5077/api/status' `
            -Headers @{ Origin = 'https://example.com' } `
            -UseBasicParsing `
            -TimeoutSec 5 | Out-Null
    }
    catch {
        if ($null -ne $_.Exception.Response) {
            try { $hostileStatus = [int]$_.Exception.Response.StatusCode } catch { }
        }
    }
    if ($hostileStatus -ne 403) {
        throw "Expected hostile browser Origin to receive HTTP 403, got '$hostileStatus'."
    }

    Write-Host 'Loopback browser-origin and response-header policy OK.'
}

$primary = $null
$testProcess = $null
$markerId = $null
$baselineIncidentCount = $null
$performance = $null
$finalStatus = $null
$listeners = @()
$validationSucceeded = $false
$buildInfo = @()

try {
    Write-Stage 'VERIFY PACKAGE'
    $zipPath = (Resolve-Path -LiteralPath $CandidateZip).Path
    $actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedHash = $ExpectedSha256.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "SHA-256 mismatch. Expected $expectedHash but got $actualHash."
    }
    Write-Host "SHA-256 OK: $actualHash"

    Assert-PortFree

    Write-Stage 'EXTRACT PORTABLE BUILD'
    if (Test-Path -LiteralPath $ExtractRoot) {
        Remove-Item -LiteralPath $ExtractRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $ExtractRoot -Force | Out-Null
    Expand-Archive -LiteralPath $zipPath -DestinationPath $ExtractRoot -Force

    $exe = Join-Path $ExtractRoot 'CrashScope.exe'
    $requiredFiles = @(
        $exe,
        (Join-Path $ExtractRoot 'wwwroot\index.html'),
        (Join-Path $ExtractRoot 'LICENSE.txt'),
        (Join-Path $ExtractRoot 'THIRD-PARTY-NOTICES.md'),
        (Join-Path $ExtractRoot 'BUILD-INFO.txt')
    )
    foreach ($path in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Portable package is missing required file '$path'."
        }
    }

    $buildInfo = @(Get-Content -LiteralPath (Join-Path $ExtractRoot 'BUILD-INFO.txt'))
    Write-Host "Executable: $exe"
    $buildInfo | ForEach-Object { Write-Host $_ }

    Write-Stage 'START PORTABLE CRASHSCOPE'
    # The initial normal launch intentionally opens the dashboard so this same
    # run measures CrashScope with its real browser/WebSocket client connected.
    $started = Start-Candidate -Executable $exe -OpenBrowser
    $primary = $started.Process
    $initialStatus = $started.Status
    if ($initialStatus.crashScopeVersion -ne $ExpectedVersion) {
        throw "Candidate reported CrashScope version '$($initialStatus.crashScopeVersion)' instead of expected version ''."
    }
    Write-Host "Healthy PID: $($primary.Id) · CrashScope $($initialStatus.crashScopeVersion)"

    $listeners = @(Assert-LoopbackOnly)
    $listenerPids = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($primary.Id -notin $listenerPids) {
        throw "Port 5077 is not owned by the launched candidate PID $($primary.Id)."
    }
    Write-Host 'Loopback-only listener OK.'

    Assert-BrowserSecurity

    Write-Stage 'SECOND INSTANCE PROCESS SAFETY'
    # Browser-opening UX is a separate visual check. This automated check proves
    # the second process recognizes the healthy instance and exits without taking
    # over the listener.
    $second = Start-Process -FilePath $exe -ArgumentList '--no-browser' -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 3
    if (-not $second.HasExited) {
        Stop-Process -Id $second.Id -Force -ErrorAction SilentlyContinue
        throw 'Second CrashScope instance did not exit cleanly.'
    }
    if ($primary.HasExited) {
        throw 'Original CrashScope instance exited during second-instance validation.'
    }
    Wait-CrashScopeHealthy | Out-Null
    $ownerAfterSecond = (Get-NetTCPConnection -LocalPort 5077 -State Listen | Select-Object -First 1).OwningProcess
    if ($ownerAfterSecond -ne $primary.Id) {
        throw "Second-instance validation changed the listener owner from $($primary.Id) to $ownerAfterSecond."
    }
    Write-Host 'Second-instance process safety OK.'

    Write-Stage 'WORKLOAD ATTACH / STOP'
    $testProcess = Start-Process "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 180' `
        -WindowStyle Hidden `
        -PassThru
    Start-Sleep -Seconds 2

    $rawCandidates = Invoke-RestMethod -Uri 'http://127.0.0.1:5077/api/workloads?maximum=200&includeUnlikely=true'
    $candidates = @(ConvertTo-ItemArray -InputObject $rawCandidates)
    $candidate = $candidates | Where-Object { $_.processId -eq $testProcess.Id } | Select-Object -First 1
    if ($null -eq $candidate) {
        throw "CrashScope workload discovery did not return test PID $($testProcess.Id)."
    }

    $startUtc = [uri]::EscapeDataString([string]$candidate.processStartTimeUtc)
    Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:5077/api/sessions/attach-candidate/$($candidate.processId)?processStartTimeUtc=$startUtc" | Out-Null
    Start-Sleep -Seconds 3
    $activeStatus = Invoke-RestMethod -Uri 'http://127.0.0.1:5077/api/status'
    if ($activeStatus.samplingMode -ne 'Active' -or $null -eq $activeStatus.activeSessionId) {
        throw 'Workload attach did not switch CrashScope to Active sampling with an active session.'
    }
    Write-Host "Attached session: $($activeStatus.activeSessionId)"

    Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5077/api/sessions/stop' | Out-Null
    Start-Sleep -Seconds 2
    $backgroundStatus = Invoke-RestMethod -Uri 'http://127.0.0.1:5077/api/status'
    if ($backgroundStatus.samplingMode -ne 'Background' -or $null -ne $backgroundStatus.activeSessionId) {
        throw 'Stopping the workload did not return CrashScope to Background sampling.'
    }
    Write-Host 'Active -> Background session transition OK.'

    Stop-Process -Id $testProcess.Id -Force -ErrorAction SilentlyContinue
    $testProcess = $null

    Write-Stage 'SAFE INCIDENT MARKER'
    $baselineIncidentCount = [int](Invoke-RestMethod -Uri 'http://127.0.0.1:5077/api/status').incidentCount
    Write-Host 'Capturing the safe marker. This intentionally takes about 30 seconds...'
    $marker = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5077/api/incidents/capture-marker' -TimeoutSec 60
    if ($marker.classification -ne 'UserDiagnosticMarker') {
        throw "Safe marker was classified as '$($marker.classification)' instead of UserDiagnosticMarker."
    }
    $markerId = [string]$marker.incidentId
    $afterMarker = Invoke-RestMethod -Uri 'http://127.0.0.1:5077/api/status'
    if ([int]$afterMarker.incidentCount -le $baselineIncidentCount) {
        throw 'Incident count did not increase after safe marker capture.'
    }
    Write-Host "Safe marker captured: $markerId"

    Write-Stage 'RESTART PERSISTENCE'
    Stop-Process -Id $primary.Id -Force
    $primary.WaitForExit()
    Wait-PortFree

    # Do not open another browser tab. The first dashboard should reconnect to
    # the restarted Agent by itself.
    $restarted = Start-Candidate -Executable $exe
    $primary = $restarted.Process
    if ($restarted.Status.crashScopeVersion -ne $ExpectedVersion) {
        throw 'Restarted candidate reported an unexpected version.'
    }

    $rawIncidents = Invoke-RestMethod -Uri 'http://127.0.0.1:5077/api/incidents'
    $incidents = @(ConvertTo-ItemArray -InputObject $rawIncidents)
    $persisted = $incidents | Where-Object { [string]$_.incidentId -eq $markerId } | Select-Object -First 1
    if ($null -eq $persisted) {
        throw "Safe marker $markerId did not survive CrashScope restart."
    }
    Write-Host 'SQLite incident persistence OK.'

    Assert-BrowserSecurity

    Write-Stage 'WAIT FOR DASHBOARD RECONNECT'
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $connectedStatus = Invoke-RestMethod -Uri 'http://127.0.0.1:5077/api/status'
        if ([int]$connectedStatus.streamSubscribers -gt 0) { break }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    if ([int]$connectedStatus.streamSubscribers -eq 0) {
        Write-Warning 'No dashboard WebSocket subscriber reconnected. Performance will still be measured, but dashboard-open stream validation is incomplete.'
    }
    else {
        Write-Host "Dashboard subscriber reconnected: $($connectedStatus.streamSubscribers)"
    }

    Write-Stage 'PORTABLE PERFORMANCE (30 SECONDS)'
    $logical = [Environment]::ProcessorCount
    $p = Get-Process -Id $primary.Id
    $cpuStart = $p.TotalProcessorTime
    $measureStart = [DateTime]::UtcNow
    $ws = @()
    $private = @()

    1..30 | ForEach-Object {
        $p = Get-Process -Id $primary.Id
        $ws += $p.WorkingSet64 / 1MB
        $private += $p.PrivateMemorySize64 / 1MB
        Start-Sleep -Seconds 1
    }

    $p = Get-Process -Id $primary.Id
    $elapsed = ([DateTime]::UtcNow - $measureStart).TotalSeconds
    $cpu = (($p.TotalProcessorTime - $cpuStart).TotalSeconds / $elapsed / $logical) * 100
    $performance = [PSCustomObject]@{
        AvgCPUPercent = [math]::Round($cpu, 4)
        AvgWorkingSetMB = [math]::Round(($ws | Measure-Object -Average).Average, 2)
        PeakWorkingSetMB = [math]::Round(($ws | Measure-Object -Maximum).Maximum, 2)
        AvgPrivateMB = [math]::Round(($private | Measure-Object -Average).Average, 2)
    }
    $performance | Format-Table -AutoSize

    if ($performance.AvgCPUPercent -gt 0.5) {
        Write-Warning "Average Agent CPU exceeded the 0.5% product budget: $($performance.AvgCPUPercent)%"
    }
    if ($performance.PeakWorkingSetMB -gt 100) {
        Write-Warning "Peak working set exceeded the current 100 MB target: $($performance.PeakWorkingSetMB) MB"
    }

    Write-Stage 'FINAL STATUS'
    Write-Host 'Reading final API status...'
    $finalStatus = Invoke-RestMethod -Uri 'http://127.0.0.1:5077/api/status' -TimeoutSec 5
    Write-Host 'Final API status OK.'
    Write-Host 'Rechecking loopback listener...'
    $listeners = @(Assert-LoopbackOnly)
    Write-Host 'Final loopback listener check OK.'

    try {
        Write-Host 'Validating final status fields...'
        $requiredStatusFields = @(
            'crashScopeVersion', 'incidentCount', 'sessionCount', 'samplingMode',
            'streamSubscribers', 'streamFramesPublished', 'streamDroppedStaleFrames',
            'streamDeliveryMisses', 'schemaVersion'
        )
        foreach ($field in $requiredStatusFields) {
            if ($finalStatus.PSObject.Properties.Name -notcontains $field) {
                throw "Final status response is missing required field '$field'."
            }
        }
        if ([string]$finalStatus.crashScopeVersion -ne $ExpectedVersion) {
            throw "Final status reported unexpected CrashScope version '$($finalStatus.crashScopeVersion)'."
        }
        if ([int]$finalStatus.streamDroppedStaleFrames -ne 0) {
            Write-Warning "Stream dropped stale frames: $($finalStatus.streamDroppedStaleFrames). This protects sampling and is not automatically a release failure, but record it."
        }
        if ([int]$finalStatus.streamDeliveryMisses -ne 0) {
            throw "WebSocket stream delivery misses were observed: $($finalStatus.streamDeliveryMisses)."
        }
        Write-Host 'Final status fields OK.'

        Write-Host 'Snapshotting final listener metadata...'
        $listenerSnapshots = @($listeners | ForEach-Object {
            [PSCustomObject]@{
                address = [string]$_.LocalAddress
                port = [int]$_.LocalPort
                processId = [int]$_.OwningProcess
            }
        })
        Write-Host "Listener metadata snapshot OK: $($listenerSnapshots.Count) listener(s)."

        Write-Host 'Assembling portable validation report...'
        $result = [PSCustomObject]@{
            passed = $true
            crashScopeVersion = [string]$finalStatus.crashScopeVersion
            candidate = [IO.Path]::GetFileName([string]$zipPath)
            sha256 = [string]$actualHash
            buildInfo = @($buildInfo | ForEach-Object { [string]$_ })
            executable = [string]$exe
            sourceProcessId = [int]$primary.Id
            markerIncidentId = [string]$markerId
            incidentCount = [int]$finalStatus.incidentCount
            sessionCount = [int]$finalStatus.sessionCount
            samplingMode = [string]$finalStatus.samplingMode
            streamSubscribers = [int]$finalStatus.streamSubscribers
            streamFramesPublished = [long]$finalStatus.streamFramesPublished
            streamDroppedStaleFrames = [long]$finalStatus.streamDroppedStaleFrames
            streamDeliveryMisses = [long]$finalStatus.streamDeliveryMisses
            schemaVersion = [int]$finalStatus.schemaVersion
            listeners = $listenerSnapshots
            performance = [PSCustomObject]@{
                AvgCPUPercent = [double]$performance.AvgCPUPercent
                AvgWorkingSetMB = [double]$performance.AvgWorkingSetMB
                PeakWorkingSetMB = [double]$performance.PeakWorkingSetMB
                AvgPrivateMB = [double]$performance.AvgPrivateMB
            }
            visualChecksRemaining = @(
                'Selecting/capturing an incident visibly scrolls/focuses Incident detail into view.',
                'Launching CrashScope.exe normally a second time opens/reuses the existing dashboard.'
            )
            completedAtUtc = [DateTime]::UtcNow.ToString('o')
        }
        Write-Host 'Portable validation report object OK.'

        $reportDirectory = Split-Path -Parent $ReportPath
        if ($reportDirectory -and -not (Test-Path -LiteralPath $reportDirectory)) {
            New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
        }
        Write-Host 'Serializing portable validation report...'
        $reportJson = ConvertTo-Json -InputObject $result -Depth 8
        Write-Host 'Portable validation report serialization OK.'
        Write-Host "Writing portable validation report: $ReportPath"
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        [IO.File]::WriteAllText($ReportPath, $reportJson, $utf8)
        Write-Host 'Portable validation report write OK.'
    }
    catch {
        Write-Host ''
        Write-Host 'PORTABLE FINALIZATION ERROR' -ForegroundColor Red
        Write-Host "Type: $($_.Exception.GetType().FullName)" -ForegroundColor Red
        Write-Host "Message: $($_.Exception.Message)" -ForegroundColor Red
        if ($null -ne $_.InvocationInfo) {
            Write-Host "Position: $($_.InvocationInfo.PositionMessage)" -ForegroundColor Red
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$_.ScriptStackTrace)) {
            Write-Host "Stack: $($_.ScriptStackTrace)" -ForegroundColor Red
        }
        throw
    }

    Write-Host "`nPASS: automated portable release candidate validation completed." -ForegroundColor Green
    Write-Host "Report: $ReportPath"
    Write-Host 'Two visual UX checks remain; they are listed in the JSON report.' -ForegroundColor Yellow
    $result | ConvertTo-Json -Depth 8
    $validationSucceeded = $true
}
finally {
    if ($null -ne $testProcess -and -not $testProcess.HasExited) {
        Stop-Process -Id $testProcess.Id -Force -ErrorAction SilentlyContinue
    }

    if ($null -ne $primary -and -not $primary.HasExited -and -not $LeaveRunning) {
        Stop-Process -Id $primary.Id -Force -ErrorAction SilentlyContinue
    }

    if (-not $validationSucceeded) {
        Write-Host "`nFAIL: portable release candidate validation did not complete." -ForegroundColor Red
    }
}

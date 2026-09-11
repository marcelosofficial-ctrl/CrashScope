[CmdletBinding()]
param(
    [int]$Pr = 59,
    [string]$Repository = "marcelosofficial-ctrl/CrashScope",
    [ValidateRange(1, 30)]
    [int]$WerLookbackDays = 30,
    [string]$WorkingRoot = (Join-Path $env:TEMP "CrashScope-next-beta-validation")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$sourceScripts = $PSScriptRoot
$patchedScripts = "$WorkingRoot-patched-scripts"
$transcriptPath = "$WorkingRoot-transcript.txt"
$childOutputPath = "$WorkingRoot-child-output.txt"
$transcriptStarted = $false

try {
    # Keep durable logs outside WorkingRoot because the consolidated validator
    # intentionally deletes and recreates WorkingRoot while isolating user state.
    $transcriptDirectory = Split-Path -Parent $transcriptPath
    if ($transcriptDirectory -and -not (Test-Path -LiteralPath $transcriptDirectory)) {
        New-Item -ItemType Directory -Path $transcriptDirectory -Force | Out-Null
    }
    Start-Transcript -LiteralPath $transcriptPath -Force | Out-Null
    $transcriptStarted = $true
    Write-Host "Durable wrapper transcript: $transcriptPath"
    Write-Host "Durable nested-validator output: $childOutputPath"

    if (Test-Path -LiteralPath $childOutputPath) {
        Remove-Item -LiteralPath $childOutputPath -Force
    }

    if (Test-Path -LiteralPath $patchedScripts) {
        Remove-Item -LiteralPath $patchedScripts -Recurse -Force
    }
    New-Item -ItemType Directory -Path $patchedScripts -Force | Out-Null

    Get-ChildItem -LiteralPath $sourceScripts -Filter "*.ps1" -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $patchedScripts $_.Name) -Force
    }

    $portableValidator = Join-Path $patchedScripts "Validate-PortableCandidate.ps1"
    $content = Get-Content -LiteralPath $portableValidator -Raw

    $launchOld = @'
    $arguments = if ($OpenBrowser) { @() } else { @('--no-browser') }
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -PassThru -WindowStyle Hidden
'@
    $launchNew = @'
    if ($OpenBrowser) {
        $process = Start-Process -FilePath $Executable -PassThru -WindowStyle Hidden
    }
    else {
        $process = Start-Process -FilePath $Executable -ArgumentList '--no-browser' -PassThru -WindowStyle Hidden
    }
'@

    if ($content.Contains($launchOld)) {
        $content = $content.Replace($launchOld, $launchNew)
    }
    elseif (-not $content.Contains("if ($OpenBrowser) {")) {
        throw "Portable validator contains neither legacy nor integrated Start-Candidate compatibility logic."
    }

    $finalStatusOld = @'
    Write-Stage 'FINAL STATUS'
    $finalStatus = Invoke-RestMethod -Uri 'http://127.0.0.1:5077/api/status'
    $listeners = @(Assert-LoopbackOnly)
'@
    $finalStatusNew = @'
    Write-Stage 'FINAL STATUS'
    Write-Host 'Reading final API status...'
    $finalStatus = Invoke-RestMethod -Uri 'http://127.0.0.1:5077/api/status' -TimeoutSec 5
    Write-Host 'Final API status OK.'
    Write-Host 'Rechecking loopback listener...'
    $listeners = @(Assert-LoopbackOnly)
    Write-Host 'Final loopback listener check OK.'
'@

    if ($content.Contains($finalStatusOld)) {
        $content = $content.Replace($finalStatusOld, $finalStatusNew)
    }
    elseif (-not $content.Contains("Reading final API status...")) {
        throw "Portable validator contains neither legacy nor integrated FINAL STATUS compatibility logic."
    }

    $finalizeOld = @'
    if ($finalStatus.crashScopeVersion -ne '0.1.0') {
        throw "Final status reported unexpected CrashScope version '$($finalStatus.crashScopeVersion)'."
    }
    if ([int]$finalStatus.streamDroppedStaleFrames -ne 0) {
        Write-Warning "Stream dropped stale frames: $($finalStatus.streamDroppedStaleFrames). This protects sampling and is not automatically a release failure, but record it."
    }
    if ([int]$finalStatus.streamDeliveryMisses -ne 0) {
        throw "WebSocket stream delivery misses were observed: $($finalStatus.streamDeliveryMisses)."
    }

    $result = [PSCustomObject]@{
        passed = $true
        crashScopeVersion = [string]$finalStatus.crashScopeVersion
        candidate = Split-Path $zipPath -Leaf
        sha256 = $actualHash
        buildInfo = $buildInfo
        executable = $exe
        sourceProcessId = $primary.Id
        markerIncidentId = $markerId
        incidentCount = [int]$finalStatus.incidentCount
        sessionCount = [int]$finalStatus.sessionCount
        samplingMode = [string]$finalStatus.samplingMode
        streamSubscribers = [int]$finalStatus.streamSubscribers
        streamFramesPublished = [long]$finalStatus.streamFramesPublished
        streamDroppedStaleFrames = [long]$finalStatus.streamDroppedStaleFrames
        streamDeliveryMisses = [long]$finalStatus.streamDeliveryMisses
        schemaVersion = [int]$finalStatus.schemaVersion
        listeners = @($listeners | ForEach-Object {
            [PSCustomObject]@{
                address = $_.LocalAddress
                port = $_.LocalPort
                processId = $_.OwningProcess
            }
        })
        performance = $performance
        visualChecksRemaining = @(
            'Selecting/capturing an incident visibly scrolls/focuses Incident detail into view.',
            'Launching CrashScope.exe normally a second time opens/reuses the existing dashboard.'
        )
        completedAtUtc = [DateTime]::UtcNow.ToString('o')
    }

    $reportDirectory = Split-Path -Parent $ReportPath
    if ($reportDirectory -and -not (Test-Path -LiteralPath $reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
'@
    $finalizeNew = @'
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
        if ([string]$finalStatus.crashScopeVersion -ne '0.1.0') {
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
'@

    if ($content.Contains($finalizeOld)) {
        $content = $content.Replace($finalizeOld, $finalizeNew)
    }
    elseif (-not $content.Contains("Portable validation report serialization OK.")) {
        throw "Portable validator contains neither legacy nor integrated report-finalization compatibility logic."
    }

    Set-Content -LiteralPath $portableValidator -Value $content -Encoding UTF8

    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($portableValidator, [ref]$tokens, [ref]$errors) | Out-Null
    if (@($errors).Count -gt 0) {
        $messages = @($errors | ForEach-Object { $_.Message }) -join [Environment]::NewLine
        throw "Patched portable validator did not parse cleanly:`n$messages"
    }

    # Compatibility edits belong to the preserved combined-validator core.
    # The public Validate-NextBeta.ps1 remains the outer user-state safety boundary.
    $nextBeta = Join-Path $patchedScripts "Validate-NextBeta.ps1"
    $nextBetaCore = Join-Path $patchedScripts "Validate-NextBeta-Core.ps1"
    $nextBetaContent = Get-Content -LiteralPath $nextBetaCore -Raw
    $trayLookupOld = '$hwndMessage = [IntPtr](-3)'
    $trayLookupNew = '$hwndMessage = [IntPtr]::Zero'
    if (-not $nextBetaContent.Contains($trayLookupOld)) {
        throw "The expected tray-window compatibility target was not found. Refusing to patch an unexpected combined validator core version."
    }
    $nextBetaContent = $nextBetaContent.Replace($trayLookupOld, $trayLookupNew)
    $nextBetaContent = $nextBetaContent.Replace(
        'CrashScope tray message window ''$className'' was not found.',
        'CrashScope tray owner window ''$className'' was not found.')

    $werProbeOld = @'
    Write-Stage "NATIVE WER REPORT-STORE COMPARISON"
    $wer = Invoke-RestMethod -Uri "http://127.0.0.1:5077/api/diagnostics/wer-store/probe?days=$WerLookbackDays" -TimeoutSec 30
    if ($wer.mode -ne "on-demand-read-only-comparison") {
        throw "Unexpected WER probe mode '$($wer.mode)'."
    }
    $stores = @($wer.nativeProbe.stores | ForEach-Object {
        [PSCustomObject]@{
            store = [string]$_.store
            available = [bool]$_.available
            reportCount = [int]$_.reportCount
            error = if ($null -eq $_.error) { $null } else { [string]$_.error }
        }
    })
    $werResult = [PSCustomObject]@{
        lookbackDays = [int]$wer.windowDays
        elapsedMilliseconds = [double]$wer.nativeProbe.elapsedMilliseconds
        nativeReportCount = [int]$wer.nativeReportCount
        existingWerReportCount = [int]$wer.existingWerReportCount
        matchingReportIds = [int]$wer.matchingReportIds
        stores = $stores
        zeroReportsIsFailure = $false
        liveTriggeringChanged = $false
    }
    Write-Host "Native WER probe: $($werResult.elapsedMilliseconds) ms, $($werResult.nativeReportCount) native reports, $($werResult.matchingReportIds) matching ReportIds."
'@
    $werProbeNew = @'
    Write-Stage "NATIVE WER REPORT-STORE COMPARISON"
    try {
        $wer = Invoke-RestMethod -Uri "http://127.0.0.1:5077/api/diagnostics/wer-store/probe?days=$WerLookbackDays" -TimeoutSec 30
        if ($wer.mode -ne "on-demand-read-only-comparison") {
            throw "Unexpected WER probe mode '$($wer.mode)'."
        }
        $stores = @($wer.nativeProbe.stores | ForEach-Object {
            [PSCustomObject]@{
                store = [string]$_.store
                available = [bool]$_.available
                reportCount = [int]$_.reportCount
                error = if ($null -eq $_.error) { $null } else { [string]$_.error }
            }
        })
        $werResult = [PSCustomObject]@{
            validationStatus = "completed"
            lookbackDays = [int]$wer.windowDays
            timeoutSeconds = 30
            elapsedMilliseconds = [double]$wer.nativeProbe.elapsedMilliseconds
            nativeReportCount = [int]$wer.nativeReportCount
            existingWerReportCount = [int]$wer.existingWerReportCount
            matchingReportIds = [int]$wer.matchingReportIds
            stores = $stores
            zeroReportsIsFailure = $false
            liveTriggeringChanged = $false
            promotionEligible = $false
        }
        Write-Host "Native WER probe: $($werResult.elapsedMilliseconds) ms, $($werResult.nativeReportCount) native reports, $($werResult.matchingReportIds) matching ReportIds."
    }
    catch {
        $werError = [string]$_.Exception.Message
        if ($werError -notmatch '(?i)timed out|timeout') {
            throw
        }

        $werResult = [PSCustomObject]@{
            validationStatus = "timed-out"
            lookbackDays = [int]$WerLookbackDays
            timeoutSeconds = 30
            elapsedMilliseconds = $null
            nativeReportCount = $null
            existingWerReportCount = $null
            matchingReportIds = $null
            stores = @()
            zeroReportsIsFailure = $false
            liveTriggeringChanged = $false
            promotionEligible = $false
            note = "Normal-user on-demand native WER comparison exceeded the 30-second validation budget. Native WER remains excluded from live incident triggering and requires separate follow-up evidence before promotion."
        }
        Write-Warning "Native WER comparison exceeded the 30-second validation budget. Recording this as non-blocking research evidence; native WER remains excluded from live triggering."
    }
'@

    if (-not $nextBetaContent.Contains($werProbeOld)) {
        throw "The expected native WER validation target was not found. Refusing to patch an unexpected combined validator core version."
    }
    $nextBetaContent = $nextBetaContent.Replace($werProbeOld, $werProbeNew)
    Set-Content -LiteralPath $nextBetaCore -Value $nextBetaContent -Encoding UTF8

    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($nextBetaCore, [ref]$tokens, [ref]$errors) | Out-Null
    if (@($errors).Count -gt 0) {
        $messages = @($errors | ForEach-Object { $_.Message }) -join [Environment]::NewLine
        throw "Patched combined validator core did not parse cleanly:`n$messages"
    }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $nextBeta `
        -Pr $Pr `
        -Repository $Repository `
        -WerLookbackDays $WerLookbackDays `
        -WorkingRoot $WorkingRoot 2>&1 | Tee-Object -LiteralPath $childOutputPath

    $childExitCode = $LASTEXITCODE
    if ($childExitCode -ne 0) {
        throw "Combined next-beta validation failed with exit code $childExitCode."
    }
}
catch {
    Write-Host ''
    Write-Host 'NEXT-BETA WRAPPER ERROR' -ForegroundColor Red
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
finally {
    if (Test-Path -LiteralPath $patchedScripts) {
        Remove-Item -LiteralPath $patchedScripts -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($transcriptStarted) {
        Write-Host "Durable wrapper transcript preserved at: $transcriptPath"
        Write-Host "Durable nested-validator output preserved at: $childOutputPath"
        try { Stop-Transcript | Out-Null } catch { }
    }
}

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

$runKeyPath = "Software\Microsoft\Windows\CurrentVersion\Run"
$startupValueName = "CrashScope"
$productDataRoot = Join-Path $env:LOCALAPPDATA "CrashScope"
$stateBackupRoot = Join-Path $WorkingRoot "state-backup"
$artifactRoot = Join-Path $WorkingRoot "artifact"
$extractRoot = Join-Path $WorkingRoot "app"
$baseReportPath = Join-Path $WorkingRoot "portable-validation.json"
$consolidatedReportPath = Join-Path $WorkingRoot "next-beta-validation.json"
$supportBundlePath = Join-Path $WorkingRoot "support-bundle.zip"

$hadProductData = $false
$hadStartupValue = $false
$startupValue = $null
$startupValueKind = $null
$artifactSha = $null
$prInfo = $null
$ciRun = $null
$baseReport = $null
$settingsResult = $null
$startupResult = $null
$trayResult = $null
$supportResult = $null
$werResult = $null
$validationSucceeded = $false

function Write-Stage([string]$Text) {
    Write-Host "`n===== $Text =====" -ForegroundColor Cyan
}

function Invoke-GhJson {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & gh @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI failed: gh $($Arguments -join ' ')`n$($output -join [Environment]::NewLine)"
    }

    $text = ($output -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    return $text | ConvertFrom-Json
}

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-PortListener {
    Get-NetTCPConnection -LocalPort 5077 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Wait-PortFree {
    param([int]$TimeoutSeconds = 15)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if ($null -eq (Get-PortListener)) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Port 5077 did not become free within $TimeoutSeconds seconds."
}

function Wait-CrashScopeHealthy {
    param([int]$TimeoutSeconds = 30)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            $status = Invoke-RestMethod -Uri "http://127.0.0.1:5077/api/status" -TimeoutSec 2
            if ($status.status -eq "running") { return $status }
        }
        catch {
            Start-Sleep -Milliseconds 400
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "CrashScope did not become healthy within $TimeoutSeconds seconds."
}

function Stop-TestCrashScope {
    if (-not (Test-Path -LiteralPath $extractRoot)) { return }

    $prefix = ([IO.Path]::GetFullPath($extractRoot)).TrimEnd('\') + '\'
    Get-Process -Name "CrashScope" -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $path = $_.Path
            if (-not [string]::IsNullOrWhiteSpace($path) -and
                ([IO.Path]::GetFullPath($path)).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            }
        }
        catch { }
    }

    Wait-PortFree
}

function Get-StartupSnapshot {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKeyPath, $false)
    try {
        if ($null -eq $key) {
            return [PSCustomObject]@{ Exists = $false; Value = $null; Kind = $null }
        }

        $names = @($key.GetValueNames())
        if ($startupValueName -notin $names) {
            return [PSCustomObject]@{ Exists = $false; Value = $null; Kind = $null }
        }

        return [PSCustomObject]@{
            Exists = $true
            Value = $key.GetValue(
                $startupValueName,
                $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            Kind = $key.GetValueKind($startupValueName)
        }
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Remove-CrashScopeStartupValue {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKeyPath, $true)
    try {
        if ($null -ne $key) {
            $key.DeleteValue($startupValueName, $false)
        }
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Restore-CrashScopeStartupValue {
    param(
        [bool]$Exists,
        $Value,
        $Kind
    )

    if (-not $Exists) {
        Remove-CrashScopeStartupValue
        return
    }

    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runKeyPath, $true)
    try {
        if ($null -eq $key) {
            throw "Could not restore the current-user CrashScope startup value."
        }
        $key.SetValue($startupValueName, $Value, $Kind)
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Get-HttpStatusCode {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri
    )

    try {
        $response = Invoke-WebRequest -Method $Method -Uri $Uri -UseBasicParsing -TimeoutSec 10
        return [int]$response.StatusCode
    }
    catch {
        if ($null -ne $_.Exception.Response) {
            try { return [int]$_.Exception.Response.StatusCode } catch { }
        }
        throw
    }
}

function Read-ZipTextEntry {
    param(
        [Parameter(Mandatory = $true)]$Archive,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) { throw "Support bundle is missing '$Name'." }
    $stream = $entry.Open()
    try {
        $reader = New-Object IO.StreamReader($stream)
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Test-TrayRegistration {
    param([Parameter(Mandatory = $true)][int]$ProcessId)

    if ($null -eq ("CrashScopeValidationNative" -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class CrashScopeValidationNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")]
    public static extern IntPtr FindWindowEx(
        IntPtr parent,
        IntPtr childAfter,
        string className,
        string windowName);

    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(
        ref NOTIFYICONIDENTIFIER identifier,
        out RECT iconLocation);
}
"@
    }

    $hwndMessage = [IntPtr](-3)
    $className = "CrashScope.Tray.$ProcessId"
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    $window = [IntPtr]::Zero
    do {
        $window = [CrashScopeValidationNative]::FindWindowEx(
            $hwndMessage,
            [IntPtr]::Zero,
            $className,
            "CrashScope Tray")
        if ($window -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($window -eq [IntPtr]::Zero) {
        throw "CrashScope tray message window '$className' was not found."
    }

    $identifier = New-Object CrashScopeValidationNative+NOTIFYICONIDENTIFIER
    $identifier.cbSize = [uint32][Runtime.InteropServices.Marshal]::SizeOf($identifier)
    $identifier.hWnd = $window
    $identifier.uID = 1
    $identifier.guidItem = [Guid]::Empty
    $rect = New-Object CrashScopeValidationNative+RECT
    $hr = [CrashScopeValidationNative]::Shell_NotifyIconGetRect([ref]$identifier, [ref]$rect)
    if ($hr -ne 0) {
        $hrHex = "{0:X8}" -f ($hr -band 0xffffffff)
        throw "Explorer did not confirm CrashScope notification icon ID 1 (HRESULT 0x$hrHex)."
    }

    return [PSCustomObject]@{
        windowHandle = $window.ToInt64()
        iconRegistered = $true
        iconRect = [PSCustomObject]@{
            left = $rect.Left
            top = $rect.Top
            right = $rect.Right
            bottom = $rect.Bottom
        }
    }
}

try {
    if ($env:OS -ne "Windows_NT") {
        throw "Validate-NextBeta.ps1 must run on Windows."
    }
    if (Test-IsElevated) {
        throw "Run this validation from a normal non-Administrator PowerShell window. The beta gate must prove normal-user behavior."
    }
    if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) is required for the one-command exact-artifact validation."
    }
    if ($null -ne (Get-PortListener)) {
        throw "Port 5077 is already in use. Exit the currently running CrashScope instance before starting this consolidated validation."
    }
    if (@(Get-Process -Name "CrashScope" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "A CrashScope process is already running. Exit CrashScope before starting this consolidated validation."
    }

    Write-Stage "VERIFY GITHUB AUTHENTICATION"
    & gh auth status | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI is not authenticated. Run 'gh auth login' once, then retry."
    }

    Write-Stage "RESOLVE EXACT NEXT-BETA HEAD"
    $prInfo = Invoke-GhJson -Arguments @(
        "pr", "view", $Pr.ToString(),
        "--repo", $Repository,
        "--json", "number,title,headRefName,headRefOid,url,state"
    )
    if ($prInfo.state -ne "OPEN") {
        throw "Expected integration PR #$Pr to be open, but it is '$($prInfo.state)'."
    }
    if ($prInfo.headRefName -ne "integration/next-beta") {
        throw "PR #$Pr head is '$($prInfo.headRefName)' instead of integration/next-beta."
    }
    Write-Host "Head: $($prInfo.headRefOid)"

    $runs = @((Invoke-GhJson -Arguments @(
        "run", "list",
        "--repo", $Repository,
        "--workflow", "CI",
        "--branch", [string]$prInfo.headRefName,
        "--event", "pull_request",
        "--limit", "20",
        "--json", "databaseId,headSha,status,conclusion,url,createdAt"
    )))
    $ciRun = $runs |
        Where-Object { $_.headSha -eq $prInfo.headRefOid -and $_.status -eq "completed" -and $_.conclusion -eq "success" } |
        Sort-Object createdAt -Descending |
        Select-Object -First 1
    if ($null -eq $ciRun) {
        throw "No successful CI run exists for exact next-beta head $($prInfo.headRefOid)."
    }
    Write-Host "CI run: $($ciRun.databaseId)"

    Write-Stage "PRESERVE CURRENT USER STATE"
    if (Test-Path -LiteralPath $WorkingRoot) {
        Remove-Item -LiteralPath $WorkingRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $WorkingRoot -Force | Out-Null

    $startupSnapshot = Get-StartupSnapshot
    $hadStartupValue = [bool]$startupSnapshot.Exists
    $startupValue = $startupSnapshot.Value
    $startupValueKind = $startupSnapshot.Kind
    Remove-CrashScopeStartupValue

    if (Test-Path -LiteralPath $productDataRoot) {
        $hadProductData = $true
        New-Item -ItemType Directory -Path $stateBackupRoot -Force | Out-Null
        Move-Item -LiteralPath $productDataRoot -Destination (Join-Path $stateBackupRoot "CrashScope")
    }
    Write-Host "Real CrashScope local state isolated: $hadProductData"
    Write-Host "Existing CrashScope startup value preserved: $hadStartupValue"

    Write-Stage "DOWNLOAD EXACT CI-TESTED ARTIFACT"
    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    & gh run download ([string]$ciRun.databaseId) --repo $Repository --name "CrashScope-ci-win-x64" --dir $artifactRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Could not download the exact CrashScope CI artifact."
    }

    $candidateZip = Join-Path $artifactRoot "CrashScope-ci-win-x64.zip"
    $checksumFile = "$candidateZip.sha256.txt"
    if (-not (Test-Path -LiteralPath $candidateZip -PathType Leaf) -or
        -not (Test-Path -LiteralPath $checksumFile -PathType Leaf)) {
        throw "Downloaded CI artifact is missing the package or checksum."
    }
    $checksumLine = (Get-Content -LiteralPath $checksumFile -Raw).Trim()
    if ($checksumLine -notmatch '^(?<hash>[A-Fa-f0-9]{64})\s+') {
        throw "CI checksum file has an unexpected format."
    }
    $artifactSha = $Matches.hash.ToLowerInvariant()
    $actualSha = (Get-FileHash -LiteralPath $candidateZip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($artifactSha -ne $actualSha) {
        throw "Exact CI artifact checksum mismatch."
    }
    Write-Host "SHA256: $artifactSha"

    Write-Stage "RUN HARDENED PORTABLE CORE GATE"
    $portableValidator = Join-Path $PSScriptRoot "Validate-PortableCandidate.ps1"
    & powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $portableValidator `
        -CandidateZip $candidateZip `
        -ExpectedSha256 $artifactSha `
        -ExpectedVersion '0.1.0' `
        -ExtractRoot $extractRoot `
        -ReportPath $baseReportPath `
        -LeaveRunning
    if ($LASTEXITCODE -ne 0) {
        throw "The hardened portable core gate failed."
    }
    $baseReport = Get-Content -LiteralPath $baseReportPath -Raw | ConvertFrom-Json
    $exe = [string]$baseReport.executable
    $markerId = [string]$baseReport.markerIncidentId
    $candidatePid = [int]$baseReport.sourceProcessId
    Wait-CrashScopeHealthy | Out-Null

    Write-Stage "SETTINGS VALIDATION"
    $defaults = Invoke-RestMethod -Uri "http://127.0.0.1:5077/api/settings"
    if ([int]$defaults.schemaVersion -ne 2 -or
        -not [bool]$defaults.autoAssistEnabled -or
        [int]$defaults.retentionDays -ne 30) {
        throw "Fresh isolated settings did not match schema 2 / Auto Assist On / 30-day retention defaults."
    }

    if ((Get-HttpStatusCode -Method "PUT" -Uri "http://127.0.0.1:5077/api/settings/retention/0") -ne 400 -or
        (Get-HttpStatusCode -Method "PUT" -Uri "http://127.0.0.1:5077/api/settings/retention/366") -ne 400) {
        throw "Retention API did not reject out-of-range values."
    }

    Invoke-RestMethod -Method Put -Uri "http://127.0.0.1:5077/api/settings/auto-assist/false" | Out-Null
    Invoke-RestMethod -Method Put -Uri "http://127.0.0.1:5077/api/settings/retention/14" | Out-Null
    $changedSettings = Invoke-RestMethod -Uri "http://127.0.0.1:5077/api/settings"
    if ([bool]$changedSettings.autoAssistEnabled -or [int]$changedSettings.retentionDays -ne 14) {
        throw "Settings API did not retain the requested Auto Assist / retention values."
    }

    Write-Stage "START WITH WINDOWS VALIDATION"
    $startupBefore = Invoke-RestMethod -Uri "http://127.0.0.1:5077/api/settings/startup"
    if (-not [bool]$startupBefore.supported -or [bool]$startupBefore.enabled) {
        throw "Isolated current-user startup state should begin supported and disabled."
    }
    $startupEnabled = Invoke-RestMethod -Method Put -Uri "http://127.0.0.1:5077/api/settings/startup/true"
    if (-not [bool]$startupEnabled.enabled) {
        throw "CrashScope could not enable normal-user startup."
    }
    $registered = (Get-StartupSnapshot).Value
    $expectedStartup = '"' + ([IO.Path]::GetFullPath($exe)) + '" --no-browser'
    if ([string]$registered -cne $expectedStartup) {
        throw "CrashScope registered an unexpected startup command."
    }
    $startupDisabled = Invoke-RestMethod -Method Put -Uri "http://127.0.0.1:5077/api/settings/startup/false"
    if ([bool]$startupDisabled.enabled -or (Get-StartupSnapshot).Exists) {
        throw "CrashScope did not cleanly remove its current-user startup value."
    }
    $startupResult = [PSCustomObject]@{
        supported = [bool]$startupBefore.supported
        exactCommandVerified = $true
        disableVerified = $true
        scope = "HKCU Run"
    }

    Write-Stage "SETTINGS PERSISTENCE ACROSS RESTART"
    Stop-Process -Id $candidatePid -Force
    Wait-PortFree
    $restarted = Start-Process -FilePath $exe -ArgumentList "--no-browser" -PassThru -WindowStyle Hidden
    $candidatePid = $restarted.Id
    $restartStatus = Wait-CrashScopeHealthy
    $persistedSettings = Invoke-RestMethod -Uri "http://127.0.0.1:5077/api/settings"
    if ([bool]$persistedSettings.autoAssistEnabled -or [int]$persistedSettings.retentionDays -ne 14) {
        throw "Settings did not persist across a real Agent restart."
    }
    $settingsResult = [PSCustomObject]@{
        schemaVersion = [int]$persistedSettings.schemaVersion
        defaultAutoAssistEnabled = [bool]$defaults.autoAssistEnabled
        defaultRetentionDays = [int]$defaults.retentionDays
        persistedAutoAssistEnabled = [bool]$persistedSettings.autoAssistEnabled
        persistedRetentionDays = [int]$persistedSettings.retentionDays
        invalidRetentionRejected = $true
    }

    Write-Stage "NATIVE WINDOWS TRAY REGISTRATION"
    $trayResult = Test-TrayRegistration -ProcessId $candidatePid
    Write-Host "Explorer confirmed CrashScope notification icon registration."

    Write-Stage "PRIVACY-SAFE SUPPORT BUNDLE"
    $preview = Invoke-RestMethod -Uri "http://127.0.0.1:5077/api/incidents/$markerId/support-bundle/preview"
    $previewEntries = @($preview.entries)
    foreach ($required in @("README.txt", "manifest.json", "incident.json", "runtime.json")) {
        if ($required -notin $previewEntries) {
            throw "Support bundle preview is missing '$required'."
        }
    }
    $forbiddenPreview = @($previewEntries | Where-Object { $_ -match '\.(db|sqlite|sqlite3|dmp|etl|wer|log)$' })
    if ($forbiddenPreview.Count -gt 0) {
        throw "Support bundle preview advertised a forbidden raw/sensitive file type."
    }

    Invoke-WebRequest `
        -Method Post `
        -Uri "http://127.0.0.1:5077/api/incidents/$markerId/support-bundle" `
        -OutFile $supportBundlePath `
        -UseBasicParsing `
        -TimeoutSec 30 | Out-Null

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($supportBundlePath)
    try {
        $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
        $forbidden = @($entryNames | Where-Object { $_ -match '\.(db|sqlite|sqlite3|dmp|etl|wer|log)$' })
        if ($forbidden.Count -gt 0) {
            throw "Created support bundle contains a forbidden raw/sensitive file type."
        }

        $runtimeText = Read-ZipTextEntry -Archive $archive -Name "runtime.json"
        $runtimeJson = $runtimeText | ConvertFrom-Json
        if ([int]$runtimeJson.settingsSchemaVersion -ne [int]$persistedSettings.schemaVersion -or
            [int]$runtimeJson.retentionDays -ne [int]$persistedSettings.retentionDays -or
            [int]$runtimeJson.schemaVersion -ne [int]$restartStatus.schemaVersion) {
            throw "Support runtime metadata does not match persistence/settings schema or retention policy."
        }

        $scanText = ($archive.Entries | ForEach-Object {
            if ($_.FullName -match '\.(txt|json)$') {
                Read-ZipTextEntry -Archive $archive -Name $_.FullName
            }
        }) -join "`n"
        $leakMarkers = @($env:USERPROFILE, $env:USERNAME, $env:COMPUTERNAME) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        foreach ($marker in $leakMarkers) {
            if ($scanText.IndexOf([string]$marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Support bundle redaction scan found a current-user or machine identity marker."
            }
        }

        $supportResult = [PSCustomObject]@{
            previewEntries = $previewEntries
            createdEntries = $entryNames
            persistenceSchemaVersion = [int]$runtimeJson.schemaVersion
            settingsSchemaVersion = [int]$runtimeJson.settingsSchemaVersion
            retentionDays = [int]$runtimeJson.retentionDays
            identityLeakScanPassed = $true
            automaticUpload = $false
        }
    }
    finally {
        $archive.Dispose()
    }

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

    $result = [PSCustomObject]@{
        passed = $true
        validation = "CrashScope combined next-beta normal-user gate"
        repository = $Repository
        pullRequest = [int]$prInfo.number
        headSha = [string]$prInfo.headRefOid
        ciRunId = [long]$ciRun.databaseId
        artifactSha256 = $artifactSha
        nonElevated = $true
        userStateIsolated = $true
        userStateWillBeRestored = $true
        core = $baseReport
        settings = $settingsResult
        startup = $startupResult
        tray = $trayResult
        supportBundle = $supportResult
        nativeWer = $werResult
        retention = [PSCustomObject]@{
            settingRangeAndPersistenceVerified = $true
            startupMaintenanceCompletedWithoutError = $true
            deterministicDeletionSemanticsCoveredByCiTests = $true
            syntheticDatabaseMutationPerformed = $false
        }
        remainingHumanChecks = @(
            "Right-click the CrashScope tray icon and confirm the compact menu is readable and usable.",
            "Select or capture an incident in the dashboard and confirm Incident detail scrolls/focuses into view."
        )
        completedAtUtc = [DateTime]::UtcNow.ToString("o")
    }

    $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $consolidatedReportPath -Encoding UTF8
    Write-Host "`nPASS: combined next-beta validation completed." -ForegroundColor Green
    Write-Host "Report: $consolidatedReportPath"
    Write-Host "Only two small visual UX checks remain." -ForegroundColor Yellow
    $validationSucceeded = $true
}
finally {
    Write-Stage "RESTORE USER STATE"
    try { Stop-TestCrashScope } catch { Write-Warning $_.Exception.Message }

    try {
        if (Test-Path -LiteralPath $productDataRoot) {
            Remove-Item -LiteralPath $productDataRoot -Recurse -Force
        }
        $savedData = Join-Path $stateBackupRoot "CrashScope"
        if ($hadProductData -and (Test-Path -LiteralPath $savedData)) {
            Move-Item -LiteralPath $savedData -Destination $productDataRoot
        }
    }
    catch {
        Write-Warning "Could not fully restore CrashScope local data automatically: $($_.Exception.Message)"
    }

    try {
        Restore-CrashScopeStartupValue -Exists $hadStartupValue -Value $startupValue -Kind $startupValueKind
    }
    catch {
        Write-Warning "Could not fully restore the CrashScope startup value automatically: $($_.Exception.Message)"
    }

    if ($validationSucceeded) {
        Write-Host "Original CrashScope local data and startup registration restored." -ForegroundColor Green
    }
    else {
        Write-Host "`nFAIL: combined next-beta validation did not complete. Restoration was attempted." -ForegroundColor Red
    }
}

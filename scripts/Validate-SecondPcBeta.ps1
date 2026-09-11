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

    [string]$EvidenceRoot = (Join-Path $env:TEMP ("CrashScope-second-pc-{0}" -f ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runKeyPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$startupValueName = 'CrashScope'
$productDataRoot = Join-Path $env:LOCALAPPDATA 'CrashScope'
$safetyHelpers = Join-Path $PSScriptRoot 'ValidationStateSafety.ps1'
$portableValidator = Join-Path $PSScriptRoot 'Validate-PortableCandidate.ps1'

. $safetyHelpers

function Write-Stage([string]$Text) {
    Write-Host "`n===== $Text =====" -ForegroundColor Cyan
}

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-StartupSnapshot {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKeyPath, $false)
    try {
        if ($null -eq $key -or $startupValueName -notin @($key.GetValueNames())) {
            return [PSCustomObject]@{ Exists = $false; Value = $null; Kind = $null }
        }

        return [PSCustomObject]@{
            Exists = $true
            Value = $key.GetValue($startupValueName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            Kind = $key.GetValueKind($startupValueName)
        }
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Remove-StartupValue {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKeyPath, $true)
    try {
        if ($null -ne $key) { $key.DeleteValue($startupValueName, $false) }
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Restore-StartupSnapshot {
    param([Parameter(Mandatory = $true)]$Snapshot)

    if (-not [bool]$Snapshot.Exists) {
        Remove-StartupValue
        return
    }

    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runKeyPath, $true)
    try {
        if ($null -eq $key) { throw 'Could not restore CrashScope startup registration.' }
        $key.SetValue($startupValueName, $Snapshot.Value, $Snapshot.Kind)
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Get-CommandPresence([string]$Name) {
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Get-MachineEvidence {
    $os = Get-CimInstance Win32_OperatingSystem
    $computer = Get-CimInstance Win32_ComputerSystem
    $processors = @(Get-CimInstance Win32_Processor)
    $gpus = @(Get-CimInstance Win32_VideoController)

    return [PSCustomObject]@{
        capturedAtUtc = [DateTime]::UtcNow.ToString('o')
        windows = [PSCustomObject]@{
            caption = [string]$os.Caption
            version = [string]$os.Version
            buildNumber = [string]$os.BuildNumber
            architecture = [string]$os.OSArchitecture
        }
        computer = [PSCustomObject]@{
            manufacturer = [string]$computer.Manufacturer
            model = [string]$computer.Model
            totalPhysicalMemoryBytes = [long]$computer.TotalPhysicalMemory
        }
        cpu = @($processors | ForEach-Object {
            [PSCustomObject]@{
                name = [string]$_.Name
                manufacturer = [string]$_.Manufacturer
                logicalProcessors = [int]$_.NumberOfLogicalProcessors
                cores = [int]$_.NumberOfCores
            }
        })
        gpu = @($gpus | ForEach-Object {
            [PSCustomObject]@{
                name = [string]$_.Name
                adapterCompatibility = [string]$_.AdapterCompatibility
                driverVersion = [string]$_.DriverVersion
                videoProcessor = [string]$_.VideoProcessor
            }
        })
        environment = [PSCustomObject]@{
            elevated = [bool](Test-IsElevated)
            powershellVersion = [string]$PSVersionTable.PSVersion
            dotnetCommandPresent = [bool](Get-CommandPresence 'dotnet')
            nodeCommandPresent = [bool](Get-CommandPresence 'node')
        }
    }
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Validate-SecondPcBeta.ps1 must run on Windows.'
}
if (Test-IsElevated) {
    throw 'Run this validation from a normal non-Administrator PowerShell window.'
}
if (-not (Test-Path -LiteralPath $portableValidator -PathType Leaf)) {
    throw "Portable validator is missing: $portableValidator"
}
if (-not (Test-Path -LiteralPath $safetyHelpers -PathType Leaf)) {
    throw "Validation state-safety helper is missing: $safetyHelpers"
}
if ($null -ne (Get-NetTCPConnection -LocalPort 5077 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1)) {
    throw 'Port 5077 is already in use. Exit CrashScope before validation.'
}
if (@(Get-Process -Name 'CrashScope' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'A CrashScope process is already running. Exit it before validation.'
}

$zipPath = (Resolve-Path -LiteralPath $CandidateZip).Path
$actualSha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = $ExpectedSha256.ToLowerInvariant()
if ($actualSha256 -ne $expected) {
    throw "SHA-256 mismatch before validation. Expected $expected but got $actualSha256."
}

$workingRoot = Join-Path $EvidenceRoot 'portable-extract'
$portableReport = Join-Path $EvidenceRoot 'portable-validation-report.json'
$machineReport = Join-Path $EvidenceRoot 'machine-evidence.json'
$summaryReport = Join-Path $EvidenceRoot 'second-pc-summary.txt'

Assert-CrashScopeValidationStateSafeToStart -WorkingRoot $workingRoot
New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null

Write-Stage 'CAPTURE SECOND-PC ENVIRONMENT'
$machine = Get-MachineEvidence
$machine | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $machineReport -Encoding UTF8
Write-Host "Machine evidence: $machineReport"
Write-Host "Windows: $($machine.windows.caption) $($machine.windows.version) build $($machine.windows.buildNumber)"
$machine.cpu | ForEach-Object { Write-Host "CPU: $($_.name)" }
$machine.gpu | ForEach-Object { Write-Host "GPU: $($_.name) | driver $($_.driverVersion)" }
Write-Host "dotnet command present: $($machine.environment.dotnetCommandPresent)"
Write-Host "node command present: $($machine.environment.nodeCommandPresent)"
Write-Host 'Validation shell elevated: False'

Write-Stage 'PRESERVE EXISTING CRASHSCOPE USER STATE'
$startupSnapshot = Get-StartupSnapshot
$stateContext = $null
$validatorError = $null
$restoreErrors = New-Object System.Collections.Generic.List[string]
$dataRestored = $false
$startupRestored = $false
$portablePassed = $false

try {
    $stateContext = Protect-CrashScopeValidationState -ProductDataRoot $productDataRoot -StartupSnapshot $startupSnapshot
    Write-Host "Durable vault: $($stateContext.VaultRoot)"
    Write-Host "Existing local data preserved: $($stateContext.HadProductData)"
    Write-Host "Existing startup value preserved: $([bool]$startupSnapshot.Exists)"

    Remove-StartupValue

    Write-Stage 'RUN EXACT PORTABLE BETA VALIDATION'
    & $portableValidator `
        -CandidateZip $zipPath `
        -ExpectedSha256 $expected `
        -ExpectedVersion $ExpectedVersion `
        -ExtractRoot $workingRoot `
        -ReportPath $portableReport

    if ($LASTEXITCODE -ne 0) {
        throw "Portable validator exited with code $LASTEXITCODE."
    }

    $portablePassed = $true
}
catch {
    $validatorError = $_
}
finally {
    if ($null -ne $stateContext) {
        Write-Stage 'RESTORE AND VERIFY ORIGINAL USER STATE'

        try {
            $restoreResult = Restore-CrashScopeValidationState -Context $stateContext -ProductDataRoot $productDataRoot
            $dataRestored = [bool]$restoreResult.DataRestoredAndVerified
            if ($null -ne $restoreResult.QuarantinePath) {
                Write-Host "Validation-created state quarantined at: $($restoreResult.QuarantinePath)" -ForegroundColor Yellow
            }
        }
        catch {
            $restoreErrors.Add("Local data restore failed: $($_.Exception.Message)")
        }

        try {
            Restore-StartupSnapshot -Snapshot $startupSnapshot
            $currentStartup = Get-StartupSnapshot
            if (-not (Test-CrashScopeStartupSnapshotMatch -Expected $startupSnapshot -Actual $currentStartup)) {
                throw 'Startup registration did not match the captured pre-validation state.'
            }
            $startupRestored = $true
        }
        catch {
            $restoreErrors.Add("Startup restore failed: $($_.Exception.Message)")
        }

        if ($dataRestored -and $startupRestored) {
            try {
                Complete-CrashScopeValidationState -Context $stateContext
                Write-Host 'Original CrashScope local data and startup registration restored and independently verified.' -ForegroundColor Green
            }
            catch {
                $restoreErrors.Add("Verified backup-vault cleanup failed: $($_.Exception.Message)")
            }
        }
        else {
            Write-Warning 'The durable validation backup vault was intentionally retained for recovery.'
            Write-Warning "Vault: $($stateContext.VaultRoot)"
        }
    }
}

$result = if ($portablePassed -and $restoreErrors.Count -eq 0) { 'PASS' } else { 'FAIL' }
$errorText = if ($null -eq $validatorError) { '' } else { [string]$validatorError.Exception.Message }

$summaryLines = @(
    'CrashScope second-PC beta validation',
    "Result: $result",
    "Captured UTC: $([DateTime]::UtcNow.ToString('o'))",
    "Candidate: $zipPath",
    "Expected SHA256: $expected",
    "Actual SHA256: $actualSha256",
    "Expected version: $ExpectedVersion",
    "Windows: $($machine.windows.caption) $($machine.windows.version) build $($machine.windows.buildNumber)",
    "CPU: $((@($machine.cpu | ForEach-Object { $_.name }) -join '; '))",
    "GPU: $((@($machine.gpu | ForEach-Object { $_.name + ' / driver ' + $_.driverVersion }) -join '; '))",
    "RAM bytes: $($machine.computer.totalPhysicalMemoryBytes)",
    "dotnet command present: $($machine.environment.dotnetCommandPresent)",
    "node command present: $($machine.environment.nodeCommandPresent)",
    'Ran non-elevated: True',
    "Portable validator report: $portableReport",
    "Original local data restored and verified: $dataRestored",
    "Original startup registration restored and verified: $startupRestored",
    "Validator error: $errorText"
)
$summaryLines | Set-Content -LiteralPath $summaryReport -Encoding UTF8

Write-Stage 'SECOND-PC EVIDENCE SUMMARY'
$summaryLines | ForEach-Object { Write-Host $_ }
Write-Host "Machine JSON: $machineReport"
Write-Host "Summary: $summaryReport"

if ($restoreErrors.Count -gt 0) {
    throw ("Second-PC validation could not verify complete user-state restoration. Durable backup retained if available.`n" + ($restoreErrors -join "`n"))
}
if ($null -ne $validatorError) {
    throw $validatorError
}

Write-Host "`nSECOND-PC VALIDATION PASS" -ForegroundColor Green

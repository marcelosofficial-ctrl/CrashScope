[CmdletBinding()]
param(
    [int]$Pr = 59,
    [string]$Repository = "marcelosofficial-ctrl/CrashScope",
    [string]$WorkingRoot = (Join-Path $env:TEMP "CrashScope-tray-probe")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$runKeyPath = "Software\Microsoft\Windows\CurrentVersion\Run"
$startupValueName = "CrashScope"
$productDataRoot = Join-Path $env:LOCALAPPDATA "CrashScope"
$safetyHelpers = Join-Path $PSScriptRoot "ValidationStateSafety.ps1"
$coreProbe = Join-Path $PSScriptRoot "Probe-TrayRegistration-Core.ps1"

. $safetyHelpers

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
        if ($null -eq $key) { throw "Could not restore CrashScope startup registration." }
        $key.SetValue($startupValueName, $Snapshot.Value, $Snapshot.Kind)
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

if ($env:OS -ne "Windows_NT") {
    throw "Probe-TrayRegistration.ps1 must run on Windows."
}
if (Test-IsElevated) {
    throw "Run this probe from a normal non-Administrator PowerShell window."
}
if ($null -ne (Get-NetTCPConnection -LocalPort 5077 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1)) {
    throw "Port 5077 is already in use. Exit CrashScope before running the tray probe."
}
if (@(Get-Process -Name "CrashScope" -ErrorAction SilentlyContinue).Count -gt 0) {
    throw "A CrashScope process is already running. Exit it before the tray probe."
}
if (-not (Test-Path -LiteralPath $coreProbe -PathType Leaf)) {
    throw "Internal tray probe core is missing: $coreProbe"
}

Assert-CrashScopeValidationStateSafeToStart -WorkingRoot $WorkingRoot

$startupSnapshot = Get-StartupSnapshot
$stateContext = $null
$coreError = $null
$restoreErrors = New-Object System.Collections.Generic.List[string]
$dataRestored = $false
$startupRestored = $false

try {
    Write-Host "`n===== OUTER USER-STATE SAFETY VAULT =====" -ForegroundColor Cyan
    $stateContext = Protect-CrashScopeValidationState -ProductDataRoot $productDataRoot -StartupSnapshot $startupSnapshot
    Write-Host "Vault: $($stateContext.VaultRoot)"
    Write-Host "Real CrashScope local state isolated: $($stateContext.HadProductData)"
    Write-Host "Existing CrashScope startup value preserved: $([bool]$startupSnapshot.Exists)"

    Remove-StartupValue

    & $coreProbe -Pr $Pr -Repository $Repository -WorkingRoot $WorkingRoot
}
catch {
    $coreError = $_
}
finally {
    if ($null -ne $stateContext) {
        Write-Host "`n===== OUTER VERIFIED USER-STATE RESTORE =====" -ForegroundColor Cyan

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
                throw "Startup registration did not match the captured pre-validation state."
            }
            $startupRestored = $true
        }
        catch {
            $restoreErrors.Add("Startup restore failed: $($_.Exception.Message)")
        }

        if ($dataRestored -and $startupRestored) {
            try {
                Complete-CrashScopeValidationState -Context $stateContext
                Write-Host "Original CrashScope local data and startup registration restored and independently verified." -ForegroundColor Green
            }
            catch {
                $restoreErrors.Add("Verified backup vault cleanup failed: $($_.Exception.Message)")
            }
        }
        else {
            Write-Warning "The durable validation backup vault was intentionally retained for recovery."
            Write-Warning "Vault: $($stateContext.VaultRoot)"
        }
    }
}

if ($restoreErrors.Count -gt 0) {
    throw ("CrashScope user-state restoration was not fully verified. The durable backup was retained.`n" + ($restoreErrors -join "`n"))
}
if ($null -ne $coreError) {
    throw $coreError
}

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedSha256,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion,

    [string]$EvidenceRoot = (
        Join-Path $env:TEMP (
            "CrashScope-startup-cleanup-{0}" -f
            [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
        )
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runKeyPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$startupValueName = 'CrashScope'
$productDataRoot = Join-Path $env:LOCALAPPDATA 'CrashScope'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\CrashScope'
$safetyHelpers = Join-Path $PSScriptRoot 'ValidationStateSafety.ps1'

. $safetyHelpers

function Write-Stage([string]$Text) {
    Write-Host ""
    Write-Host "===== $Text ====="
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
                [Microsoft.Win32.RegistryValueOptions]::
                    DoNotExpandEnvironmentNames
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

function Set-StartupString([string]$Value) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(
        $runKeyPath,
        $true
    )

    try {
        if ($null -eq $key) {
            throw 'Could not open current-user Run key.'
        }

        $key.SetValue(
            $startupValueName,
            $Value,
            [Microsoft.Win32.RegistryValueKind]::String
        )
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
            throw 'Could not restore CrashScope startup registration.'
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

function Get-UnrelatedRunSnapshot {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        $runKeyPath,
        $false
    )

    try {
        $records = @()

        if ($null -eq $key) {
            return '[]'
        }

        foreach (
            $name in @($key.GetValueNames() | Sort-Object)
        ) {
            if ($name -ceq $startupValueName) {
                continue
            }

            $value = $key.GetValue(
                $name,
                $null,
                [Microsoft.Win32.RegistryValueOptions]::
                    DoNotExpandEnvironmentNames
            )

            $kind = $key.GetValueKind($name)

            $records += [PSCustomObject]@{
                name = [string]$name
                kind = [string]$kind
                value = if ($null -eq $value) {
                    $null
                }
                elseif ($value -is [Array]) {
                    @($value | ForEach-Object { [string]$_ })
                }
                else {
                    [string]$value
                }
            }
        }

        if ($records.Count -eq 0) {
            return '[]'
        }

        return (
            $records |
                ConvertTo-Json -Depth 5 -Compress
        )
    }
    finally {
        if ($null -ne $key) {
            $key.Dispose()
        }
    }
}

function Assert-UnrelatedRunSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Expected,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $actual = Get-UnrelatedRunSnapshot

    if ($actual -cne $Expected) {
        throw "$Label changed unrelated current-user Run values."
    }
}

function Assert-RuntimeStopped {
    if (
        @(
            Get-Process `
                -Name 'CrashScope' `
                -ErrorAction SilentlyContinue
        ).Count -gt 0
    ) {
        throw 'CrashScope is running.'
    }

    $listener = Get-NetTCPConnection `
        -LocalPort 5077 `
        -State Listen `
        -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($null -ne $listener) {
        throw 'Port 5077 is occupied.'
    }
}

function Install-Candidate {
    $process = Start-Process `
        -FilePath $resolvedInstaller `
        -ArgumentList @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            '/SP-'
        ) `
        -Wait `
        -PassThru

    if ($process.ExitCode -ne 0) {
        throw "Installer exited with code $($process.ExitCode)."
    }

    $installedExe = Join-Path $installRoot 'CrashScope.exe'

    if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
        throw 'Installed CrashScope.exe is missing.'
    }

    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo(
        $installedExe
    )

    $fileVersion = ([string]$versionInfo.FileVersion).Trim()

    if ($fileVersion -ne "$ExpectedVersion.0") {
        throw "Installed FileVersion is '$fileVersion'."
    }

    return $installedExe
}

function Uninstall-Candidate {
    $uninstaller = Join-Path $installRoot 'unins000.exe'

    if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        throw 'Expected Inno uninstaller was not found.'
    }

    $process = Start-Process `
        -FilePath $uninstaller `
        -ArgumentList @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART'
        ) `
        -Wait `
        -PassThru

    if ($process.ExitCode -ne 0) {
        throw "Uninstaller exited with code $($process.ExitCode)."
    }

    Start-Sleep -Milliseconds 500

    if (Test-Path -LiteralPath $installRoot) {
        throw 'Install directory remains after uninstall.'
    }
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Validate-InstallerStartupCleanup.ps1 must run on Windows.'
}

if (Test-IsElevated) {
    throw 'Run this validator from a normal non-Administrator PowerShell window.'
}

if (-not (Test-Path -LiteralPath $safetyHelpers -PathType Leaf)) {
    throw "ValidationStateSafety.ps1 is missing: $safetyHelpers"
}

Assert-RuntimeStopped

if (Test-Path -LiteralPath $installRoot) {
    throw "CrashScope is already installed at '$installRoot'."
}

$resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path

$actualSha256 = (
    Get-FileHash `
        -LiteralPath $resolvedInstaller `
        -Algorithm SHA256
).Hash.ToLowerInvariant()

$expectedHash = $ExpectedSha256.ToLowerInvariant()

if ($actualSha256 -ne $expectedHash) {
    throw "Installer SHA256 mismatch. Expected $expectedHash, got $actualSha256."
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
$validatorError = $null
$restoreErrors = New-Object 'System.Collections.Generic.List[string]'
$dataRestored = $false
$startupRestored = $false
$ownedRemoved = $false
$foreignPreserved = $false
$unrelatedPreserved = $false
$foreignCommand = '"C:\CrashScope-foreign-test\CrashScope.exe" --no-browser'

try {
    Write-Stage 'PRESERVE ORIGINAL USER STATE'

    $stateContext = Protect-CrashScopeValidationState `
        -ProductDataRoot $productDataRoot `
        -StartupSnapshot $startupSnapshot

    Write-Host "Durable vault: $($stateContext.VaultRoot)"
    Write-Host "Existing local data preserved: $($stateContext.HadProductData)"
    Write-Host "Existing startup preserved: $([bool]$startupSnapshot.Exists)"

    Remove-StartupValue

    $unrelatedBaseline = Get-UnrelatedRunSnapshot

    Write-Stage 'CASE 1: OWNED STARTUP ENTRY MUST BE REMOVED'

    $installedExe = Install-Candidate

    $ownedCommand = '"' + $installedExe + '" --no-browser'

    Set-StartupString -Value $ownedCommand

    $ownedBefore = Get-StartupSnapshot

    if (
        -not [bool]$ownedBefore.Exists -or
        [string]$ownedBefore.Value -cne $ownedCommand
    ) {
        throw 'Could not establish exact owned startup command.'
    }

    Uninstall-Candidate

    $ownedAfter = Get-StartupSnapshot

    if ([bool]$ownedAfter.Exists) {
        throw (
            'Owned CrashScope startup value remained after uninstall: ' +
            [string]$ownedAfter.Value
        )
    }

    Assert-UnrelatedRunSnapshot `
        -Expected $unrelatedBaseline `
        -Label 'Owned-entry uninstall'

    $ownedRemoved = $true

    Write-Host 'Owned CrashScope startup value removed: PASS'
    Write-Host 'Unrelated Run values unchanged: PASS'

    Write-Stage 'CASE 2: DIFFERENT CRASHSCOPE VALUE MUST BE PRESERVED'

    $installedExe = Install-Candidate

    Set-StartupString -Value $foreignCommand

    $foreignBefore = Get-StartupSnapshot

    if (
        -not [bool]$foreignBefore.Exists -or
        [string]$foreignBefore.Value -cne $foreignCommand
    ) {
        throw 'Could not establish foreign CrashScope startup test value.'
    }

    Uninstall-Candidate

    $foreignAfter = Get-StartupSnapshot

    if (-not [bool]$foreignAfter.Exists) {
        throw 'Different CrashScope startup value was deleted by uninstall.'
    }

    if ([string]$foreignAfter.Value -cne $foreignCommand) {
        throw 'Different CrashScope startup value was modified by uninstall.'
    }

    Assert-UnrelatedRunSnapshot `
        -Expected $unrelatedBaseline `
        -Label 'Foreign-entry uninstall'

    $foreignPreserved = $true
    $unrelatedPreserved = $true

    Write-Host 'Different CrashScope startup value preserved: PASS'
    Write-Host 'Unrelated Run values unchanged: PASS'

    Remove-StartupValue

    $report = [PSCustomObject]@{
        schemaVersion = 1
        result = 'PASS'
        capturedAtUtc = [DateTime]::UtcNow.ToString('o')
        installer = $resolvedInstaller
        expectedVersion = $ExpectedVersion
        sha256 = $actualSha256
        ownedStartupRemoved = $ownedRemoved
        differentCrashScopeStartupPreserved = $foreignPreserved
        unrelatedRunValuesPreserved = $unrelatedPreserved
    }

    $reportPath = Join-Path $EvidenceRoot `
        'installer-startup-cleanup-report.json'

    $report |
        ConvertTo-Json -Depth 6 |
        Set-Content `
            -LiteralPath $reportPath `
            -Encoding UTF8

    Write-Host "Report: $reportPath"
}
catch {
    $validatorError = $_
}
finally {
    if (Test-Path -LiteralPath $installRoot) {
        try {
            Uninstall-Candidate
        }
        catch {
            $restoreErrors.Add(
                "Emergency uninstall failed: $($_.Exception.Message)"
            )
        }
    }

    try {
        Remove-StartupValue
    }
    catch {
        $restoreErrors.Add(
            "Temporary startup cleanup failed: $($_.Exception.Message)"
        )
    }

    if ($null -ne $stateContext) {
        Write-Stage 'RESTORE AND VERIFY ORIGINAL USER STATE'

        try {
            $restoreResult = Restore-CrashScopeValidationState `
                -Context $stateContext `
                -ProductDataRoot $productDataRoot

            $dataRestored = [bool](
                $restoreResult.DataRestoredAndVerified
            )

            if ($null -ne $restoreResult.QuarantinePath) {
                Write-Host "Validation-created state quarantined at: $($restoreResult.QuarantinePath)"
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
                throw 'Startup registration does not match original state.'
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
            }
            catch {
                $restoreErrors.Add(
                    "Backup-vault cleanup failed: $($_.Exception.Message)"
                )
            }
        }
        else {
            Write-Warning 'Durable validation backup retained for recovery.'
            Write-Warning $stateContext.VaultRoot
        }
    }
}

if ($restoreErrors.Count -gt 0) {
    throw (
        "Startup-cleanup validation could not restore original state." +
        [Environment]::NewLine +
        ($restoreErrors -join [Environment]::NewLine)
    )
}

if ($null -ne $validatorError) {
    throw $validatorError
}

Assert-RuntimeStopped

if (Test-Path -LiteralPath $installRoot) {
    throw 'CrashScope remains installed after startup-cleanup validation.'
}

Write-Stage 'INSTALLER STARTUP CLEANUP VALIDATION COMPLETE'
Write-Host 'Owned startup removed: PASS'
Write-Host 'Different CrashScope startup preserved: PASS'
Write-Host 'Unrelated Run values preserved: PASS'
Write-Host 'Original local data restored: PASS'
Write-Host 'Original startup restored: PASS'
Write-Host 'INSTALLER STARTUP CLEANUP VALIDATION PASS'
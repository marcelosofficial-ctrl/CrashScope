[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "ValidationStateSafety.ps1")

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (-not $Condition) { throw $Message }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$ExpectedText
    )

    $threw = $false
    try {
        & $Action
    }
    catch {
        $threw = $true
        if ($_.Exception.Message.IndexOf($ExpectedText, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "Expected error containing '$ExpectedText', got: $($_.Exception.Message)"
        }
    }

    if (-not $threw) {
        throw "Expected an error containing '$ExpectedText', but no error was thrown."
    }
}

function New-SyntheticProductState {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Label
    )

    New-Item -ItemType Directory -Path (Join-Path $Root "nested") -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $Root "settings.json") -Value ('{"label":"' + $Label + '","retentionDays":30}') -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $Root "nested\history.txt") -Value "history-$Label" -Encoding UTF8
    [IO.File]::WriteAllBytes((Join-Path $Root "crashscope.db"), [byte[]](0..255))
}

$baseTemp = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { $env:RUNNER_TEMP } else { $env:TEMP }
$testRoot = Join-Path $baseTemp ("CrashScope-state-safety-test-{0}" -f ([Guid]::NewGuid().ToString("N")))

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

    Write-Host "===== CASE 1: VERIFIED PRESERVE / QUARANTINE / RESTORE ====="
    $case1 = Join-Path $testRoot "case1"
    $working1 = Join-Path $case1 "working"
    $vault1 = Join-Path $case1 "vault"
    $product1 = Join-Path $case1 "product\CrashScope"
    New-SyntheticProductState -Root $product1 -Label "original"
    $originalManifest1 = @(Get-CrashScopeValidationDirectoryManifest -Root $product1)
    $startup1 = [PSCustomObject]@{ Exists = $true; Value = '"C:\Fake\CrashScope.exe" --no-browser'; Kind = 'String' }

    Assert-CrashScopeValidationStateSafeToStart -WorkingRoot $working1 -VaultBase $vault1
    $context1 = Protect-CrashScopeValidationState -ProductDataRoot $product1 -StartupSnapshot $startup1 -VaultBase $vault1

    Assert-True -Condition (-not (Test-Path -LiteralPath $product1)) -Message "Original synthetic product state was not isolated."
    Assert-True -Condition (Test-Path -LiteralPath (Join-Path $context1.VaultRoot "state.json") -PathType Leaf) -Message "Durable state journal is missing."
    Assert-True -Condition (Test-Path -LiteralPath (Join-Path $context1.VaultRoot "data-manifest.json") -PathType Leaf) -Message "Durable data manifest is missing."
    Assert-True -Condition ([IO.Path]::GetFullPath($context1.VaultRoot).StartsWith([IO.Path]::GetFullPath($vault1), [StringComparison]::OrdinalIgnoreCase)) -Message "Durable vault was not created under the requested vault root."
    Assert-True -Condition (-not [IO.Path]::GetFullPath($context1.VaultRoot).StartsWith([IO.Path]::GetFullPath($working1), [StringComparison]::OrdinalIgnoreCase)) -Message "Durable vault was incorrectly placed inside disposable WorkingRoot."

    New-Item -ItemType Directory -Path $product1 -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $product1 "validation-created.txt") -Value "temporary-validation-state" -Encoding UTF8

    $restore1 = Restore-CrashScopeValidationState -Context $context1 -ProductDataRoot $product1
    Assert-True -Condition ([bool]$restore1.DataRestoredAndVerified) -Message "Restore did not report verified success."
    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace([string]$restore1.QuarantinePath)) -Message "Validation-created state was not quarantined."
    Assert-True -Condition (Test-Path -LiteralPath (Join-Path $restore1.QuarantinePath "validation-created.txt") -PathType Leaf) -Message "Quarantined validation-created state is missing."

    $restoredManifest1 = @(Get-CrashScopeValidationDirectoryManifest -Root $product1)
    Assert-CrashScopeValidationManifestMatch -Expected $originalManifest1 -Actual $restoredManifest1 -Label "Case 1 restored synthetic state"

    Complete-CrashScopeValidationState -Context $context1
    Assert-True -Condition (-not (Test-Path -LiteralPath $context1.VaultRoot)) -Message "Verified durable vault was not removed after completion."

    $startupSame = [PSCustomObject]@{ Exists = $true; Value = '"C:\Fake\CrashScope.exe" --no-browser'; Kind = 'String' }
    $startupDifferent = [PSCustomObject]@{ Exists = $true; Value = 'different'; Kind = 'String' }
    Assert-True -Condition (Test-CrashScopeStartupSnapshotMatch -Expected $startup1 -Actual $startupSame) -Message "Equivalent startup snapshots did not match."
    Assert-True -Condition (-not (Test-CrashScopeStartupSnapshotMatch -Expected $startup1 -Actual $startupDifferent)) -Message "Different startup snapshots incorrectly matched."

    Write-Host "===== CASE 2: INTERRUPTED RUN FAILS CLOSED ====="
    $case2 = Join-Path $testRoot "case2"
    $working2 = Join-Path $case2 "working"
    $vault2 = Join-Path $case2 "vault"
    $product2 = Join-Path $case2 "product\CrashScope"
    New-SyntheticProductState -Root $product2 -Label "interrupted"
    $startup2 = [PSCustomObject]@{ Exists = $false; Value = $null; Kind = $null }

    $context2 = Protect-CrashScopeValidationState -ProductDataRoot $product2 -StartupSnapshot $startup2 -VaultBase $vault2
    Assert-Throws -ExpectedText "pending CrashScope validation backup" -Action {
        Assert-CrashScopeValidationStateSafeToStart -WorkingRoot $working2 -VaultBase $vault2
    }
    Assert-True -Condition (Test-Path -LiteralPath $context2.BackupDataRoot -PathType Container) -Message "Interrupted-run durable backup was modified or deleted."
    Assert-True -Condition (-not (Test-Path -LiteralPath $product2)) -Message "Interrupted-run original product path should remain isolated."

    $restore2 = Restore-CrashScopeValidationState -Context $context2 -ProductDataRoot $product2
    Assert-True -Condition ([bool]$restore2.DataRestoredAndVerified) -Message "Interrupted-run synthetic recovery did not verify."
    Complete-CrashScopeValidationState -Context $context2

    Write-Host "===== CASE 3: LEGACY WORKINGROOT BACKUP FAILS CLOSED ====="
    $case3 = Join-Path $testRoot "case3"
    $working3 = Join-Path $case3 "working"
    $vault3 = Join-Path $case3 "vault"
    $legacySentinel = Join-Path $working3 "state-backup\CrashScope\legacy-sentinel.txt"
    New-Item -ItemType Directory -Path (Split-Path -Parent $legacySentinel) -Force | Out-Null
    Set-Content -LiteralPath $legacySentinel -Value "do-not-delete" -Encoding UTF8

    Assert-Throws -ExpectedText "legacy CrashScope validator backup" -Action {
        Assert-CrashScopeValidationStateSafeToStart -WorkingRoot $working3 -VaultBase $vault3
    }
    Assert-True -Condition (Test-Path -LiteralPath $legacySentinel -PathType Leaf) -Message "Legacy backup sentinel was deleted or modified by the safety check."
    Assert-True -Condition ((Get-Content -LiteralPath $legacySentinel -Raw).Trim() -ceq "do-not-delete") -Message "Legacy backup sentinel contents changed."

    Write-Host "===== CASE 4: TAMPERED BACKUP REFUSES RESTORE BEFORE TOUCHING CURRENT STATE ====="
    $case4 = Join-Path $testRoot "case4"
    $working4 = Join-Path $case4 "working"
    $vault4 = Join-Path $case4 "vault"
    $product4 = Join-Path $case4 "product\CrashScope"
    New-SyntheticProductState -Root $product4 -Label "tamper-source"
    $startup4 = [PSCustomObject]@{ Exists = $false; Value = $null; Kind = $null }

    $context4 = Protect-CrashScopeValidationState -ProductDataRoot $product4 -StartupSnapshot $startup4 -VaultBase $vault4
    New-Item -ItemType Directory -Path $product4 -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $product4 "current-validation-state.txt") -Value "must-survive-failed-restore" -Encoding UTF8
    Add-Content -LiteralPath (Join-Path $context4.BackupDataRoot "settings.json") -Value "tampered"

    Assert-Throws -ExpectedText "manifest mismatch" -Action {
        Restore-CrashScopeValidationState -Context $context4 -ProductDataRoot $product4 | Out-Null
    }
    Assert-True -Condition (Test-Path -LiteralPath (Join-Path $product4 "current-validation-state.txt") -PathType Leaf) -Message "Current state was touched before tampered backup verification failed."
    Assert-True -Condition (Test-Path -LiteralPath $context4.VaultRoot -PathType Container) -Message "Tampered durable vault was deleted after failed verification."

    Write-Host "===== CASE 5: EMPTY ORIGINAL STATE RESTORES TO EMPTY ====="
    $case5 = Join-Path $testRoot "case5"
    $working5 = Join-Path $case5 "working"
    $vault5 = Join-Path $case5 "vault"
    $product5 = Join-Path $case5 "product\CrashScope"
    $startup5 = [PSCustomObject]@{ Exists = $false; Value = $null; Kind = $null }

    Assert-CrashScopeValidationStateSafeToStart -WorkingRoot $working5 -VaultBase $vault5
    $context5 = Protect-CrashScopeValidationState -ProductDataRoot $product5 -StartupSnapshot $startup5 -VaultBase $vault5
    Assert-True -Condition (-not [bool]$context5.HadProductData) -Message "Empty original state was incorrectly marked as present."

    New-Item -ItemType Directory -Path $product5 -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $product5 "validation-only.txt") -Value "temporary" -Encoding UTF8
    $restore5 = Restore-CrashScopeValidationState -Context $context5 -ProductDataRoot $product5
    Assert-True -Condition ([bool]$restore5.DataRestoredAndVerified) -Message "Empty-state restore did not verify."
    Assert-True -Condition (-not (Test-Path -LiteralPath $product5)) -Message "Originally empty product state was not restored to empty."
    Assert-True -Condition (Test-Path -LiteralPath (Join-Path $restore5.QuarantinePath "validation-only.txt") -PathType Leaf) -Message "Empty-state validation data was not quarantined."
    Complete-CrashScopeValidationState -Context $context5

    Write-Host "PASS: synthetic validation-state safety proof completed." -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

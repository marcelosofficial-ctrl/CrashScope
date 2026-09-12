Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-CrashScopeValidationDirectoryManifest {
    param([Parameter(Mandatory = $true)][string]$Root)

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return @()
    }

    $rootFull = ([IO.Path]::GetFullPath($Root)).TrimEnd('\') + '\'
    $reparsePoints = @(Get-ChildItem -LiteralPath $Root -Recurse -Force -ErrorAction Stop |
        Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
    if ($reparsePoints.Count -gt 0) {
        throw "Validation state contains reparse points. Refusing to copy user state through links or junctions."
    }

    return @(
        Get-ChildItem -LiteralPath $Root -Recurse -Force -File -ErrorAction Stop |
            Sort-Object FullName |
            ForEach-Object {
                $full = [IO.Path]::GetFullPath($_.FullName)
                [PSCustomObject]@{
                    path = $full.Substring($rootFull.Length).Replace('\', '/')
                    length = [long]$_.Length
                    sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            }
    )
}

function Assert-CrashScopeValidationManifestMatch {
    param(
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $expectedItems = @($Expected)
    $actualItems = @($Actual)
    if ($expectedItems.Count -ne $actualItems.Count) {
        throw "$Label manifest count mismatch. Expected $($expectedItems.Count), got $($actualItems.Count)."
    }

    for ($i = 0; $i -lt $expectedItems.Count; $i++) {
        $left = $expectedItems[$i]
        $right = $actualItems[$i]
        if ([string]$left.path -cne [string]$right.path -or
            [long]$left.length -ne [long]$right.length -or
            [string]$left.sha256 -cne [string]$right.sha256) {
            throw "$Label manifest mismatch at '$($left.path)'."
        }
    }
}

function Test-CrashScopeStartupSnapshotMatch {
    param(
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)]$Actual
    )

    if ([bool]$Expected.Exists -ne [bool]$Actual.Exists) { return $false }
    if (-not [bool]$Expected.Exists) { return $true }

    return ([string]$Expected.Value -ceq [string]$Actual.Value -and
        [string]$Expected.Kind -ceq [string]$Actual.Kind)
}

function Assert-CrashScopeValidationStateSafeToStart {
    param(
        [Parameter(Mandatory = $true)][string]$WorkingRoot,
        [string]$VaultBase = (Join-Path $env:LOCALAPPDATA "CrashScopeValidationBackups")
    )

    $legacyBackup = Join-Path $WorkingRoot "state-backup"
    if (Test-Path -LiteralPath $legacyBackup) {
        throw "A legacy CrashScope validator backup exists at '$legacyBackup'. Refusing to delete or overwrite it. Inspect/recover it before another validation run."
    }

    if (Test-Path -LiteralPath $VaultBase) {
        $pending = @(Get-ChildItem -LiteralPath $VaultBase -Directory -Force -ErrorAction Stop |
            Where-Object { $_.Name -like 'pending-*' })
        if ($pending.Count -gt 0) {
            $paths = ($pending | ForEach-Object { $_.FullName }) -join [Environment]::NewLine
            throw "A pending CrashScope validation backup already exists. Refusing to start another run until it is resolved:`n$paths"
        }
    }
}

function Protect-CrashScopeValidationState {
    param(
        [Parameter(Mandatory = $true)][string]$ProductDataRoot,
        [Parameter(Mandatory = $true)]$StartupSnapshot,
        [string]$VaultBase = (Join-Path $env:LOCALAPPDATA "CrashScopeValidationBackups")
    )

    if (-not (Test-Path -LiteralPath $VaultBase)) {
        New-Item -ItemType Directory -Path $VaultBase -Force | Out-Null
    }

    $runId = "{0}-{1}" -f ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')), ([Guid]::NewGuid().ToString('N'))
    $vaultRoot = Join-Path $VaultBase "pending-$runId"
    $backupDataRoot = Join-Path $vaultRoot "CrashScope"
    New-Item -ItemType Directory -Path $vaultRoot -Force | Out-Null

    $hadProductData = Test-Path -LiteralPath $ProductDataRoot -PathType Container
    $stateRecord = [PSCustomObject]@{
        schemaVersion = 1
        runId = $runId
        createdAtUtc = [DateTime]::UtcNow.ToString('o')
        productDataRoot = [IO.Path]::GetFullPath($ProductDataRoot)
        hadProductData = [bool]$hadProductData
        startup = [PSCustomObject]@{
            exists = [bool]$StartupSnapshot.Exists
            value = $StartupSnapshot.Value
            kind = if ($null -eq $StartupSnapshot.Kind) { $null } else { [string]$StartupSnapshot.Kind }
        }
    }
    $stateRecord | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $vaultRoot 'state.json') -Encoding UTF8

    $manifest = @()
    if ($hadProductData) {
        $manifest = @(Get-CrashScopeValidationDirectoryManifest -Root $ProductDataRoot)
        $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $vaultRoot 'data-manifest.json') -Encoding UTF8

        Copy-Item -LiteralPath $ProductDataRoot -Destination $backupDataRoot -Recurse -Force -ErrorAction Stop
        $backupManifest = @(Get-CrashScopeValidationDirectoryManifest -Root $backupDataRoot)
        Assert-CrashScopeValidationManifestMatch -Expected $manifest -Actual $backupManifest -Label 'Preserved CrashScope user state'

        Remove-Item -LiteralPath $ProductDataRoot -Recurse -Force -ErrorAction Stop
        if (Test-Path -LiteralPath $ProductDataRoot) {
            throw "CrashScope user state remained at '$ProductDataRoot' after verified preservation."
        }
    }

    return [PSCustomObject]@{
        RunId = $runId
        VaultRoot = $vaultRoot
        BackupDataRoot = $backupDataRoot
        ProductDataRoot = [IO.Path]::GetFullPath($ProductDataRoot)
        HadProductData = [bool]$hadProductData
        DataManifest = @($manifest)
        StartupSnapshot = $StartupSnapshot
    }
}

function Restore-CrashScopeValidationState {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$ProductDataRoot,
        [string]$QuarantineRoot = (Join-Path $env:TEMP ("CrashScope-validation-test-state-{0}" -f $Context.RunId))
    )

    if ([bool]$Context.HadProductData) {
        if (-not (Test-Path -LiteralPath $Context.BackupDataRoot -PathType Container)) {
            throw "Preserved CrashScope user state is missing from '$($Context.BackupDataRoot)'. Current validation state will not be deleted."
        }
        $backupManifest = @(Get-CrashScopeValidationDirectoryManifest -Root $Context.BackupDataRoot)
        Assert-CrashScopeValidationManifestMatch -Expected $Context.DataManifest -Actual $backupManifest -Label 'Pre-restore CrashScope backup'
    }

    $quarantined = $null
    if (Test-Path -LiteralPath $ProductDataRoot) {
        if (Test-Path -LiteralPath $QuarantineRoot) {
            throw "Validation-state quarantine path already exists at '$QuarantineRoot'. Refusing to overwrite it."
        }
        $quarantineParent = Split-Path -Parent $QuarantineRoot
        if ($quarantineParent -and -not (Test-Path -LiteralPath $quarantineParent)) {
            New-Item -ItemType Directory -Path $quarantineParent -Force | Out-Null
        }
        Move-Item -LiteralPath $ProductDataRoot -Destination $QuarantineRoot -ErrorAction Stop
        $quarantined = $QuarantineRoot
    }

    if ([bool]$Context.HadProductData) {
        Copy-Item -LiteralPath $Context.BackupDataRoot -Destination $ProductDataRoot -Recurse -Force -ErrorAction Stop
        $restoredManifest = @(Get-CrashScopeValidationDirectoryManifest -Root $ProductDataRoot)
        Assert-CrashScopeValidationManifestMatch -Expected $Context.DataManifest -Actual $restoredManifest -Label 'Restored CrashScope user state'
    }
    elseif (Test-Path -LiteralPath $ProductDataRoot) {
        throw "CrashScope product data unexpectedly exists after restoring an originally empty state."
    }

    return [PSCustomObject]@{
        DataRestoredAndVerified = $true
        QuarantinePath = $quarantined
    }
}

function Complete-CrashScopeValidationState {
    param([Parameter(Mandatory = $true)]$Context)

    $productDataProperty = $Context.PSObject.Properties["ProductDataRoot"]

    if ($null -eq $productDataProperty -or [string]::IsNullOrWhiteSpace([string]$productDataProperty.Value)) {
        throw "CrashScope validation context does not contain ProductDataRoot. Refusing vault completion."
    }

    $productDataRoot = [IO.Path]::GetFullPath([string]$productDataProperty.Value)

    if ([bool]$Context.HadProductData) {
        if (-not (Test-Path -LiteralPath $productDataRoot -PathType Container)) {
            throw "Original CrashScope product data is not present at '$productDataRoot'. Refusing vault completion."
        }

        $restoredManifest = @(Get-CrashScopeValidationDirectoryManifest -Root $productDataRoot)
        Assert-CrashScopeValidationManifestMatch -Expected $Context.DataManifest -Actual $restoredManifest -Label 'Vault-completion restored CrashScope state'
    }
    elseif (Test-Path -LiteralPath $productDataRoot) {
        throw "CrashScope product data exists even though the original state was empty. Refusing vault completion."
    }

    if (-not (Test-Path -LiteralPath $Context.VaultRoot -PathType Container)) {
        return [PSCustomObject]@{
            RestoredStateVerified = $true
            VaultRemoved = $true
            AlreadyCompleted = $true
        }
    }

    Remove-Item -LiteralPath $Context.VaultRoot -Recurse -Force -ErrorAction Stop

    if (Test-Path -LiteralPath $Context.VaultRoot) {
        throw "CrashScope validation vault '$($Context.VaultRoot)' could not be removed after verified restoration."
    }

    return [PSCustomObject]@{
        RestoredStateVerified = $true
        VaultRemoved = $true
        AlreadyCompleted = $false
    }
}

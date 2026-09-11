Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)

$utf8 = New-Object System.Text.UTF8Encoding($false, $true)

function Read-Text {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Release documentation contract failure: required file is missing: $Path"
    }

    return [IO.File]::ReadAllText($Path, $utf8)
}

function Assert-Match {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($Text -notmatch $Pattern) {
        throw "Release documentation contract failure: $Description"
    }
}

function Assert-NoMatch {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($Text -match $Pattern) {
        throw "Release documentation contract failure: $Description"
    }
}

function Get-Section {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$StartHeading,
        [Parameter(Mandatory = $true)][string]$NextHeading
    )

    $start = $Text.IndexOf(
        $StartHeading,
        [StringComparison]::Ordinal
    )

    if ($start -lt 0) {
        throw "Release documentation contract failure: missing section $StartHeading"
    }

    $finish = $Text.IndexOf(
        $NextHeading,
        $start + $StartHeading.Length,
        [StringComparison]::Ordinal
    )

    if ($finish -lt 0) {
        throw "Release documentation contract failure: missing section boundary $NextHeading"
    }

    return $Text.Substring(
        $start,
        $finish - $start
    )
}

$readme = Read-Text (
    Join-Path $repoRoot 'README.md'
)

$changelog = Read-Text (
    Join-Path $repoRoot 'CHANGELOG.md'
)

$notes10 = Read-Text (
    Join-Path $repoRoot 'docs\release-notes-v1.0.0.md'
)

$notes11 = Read-Text (
    Join-Path $repoRoot 'docs\release-notes-v1.1.0.md'
)

$unreleased = Get-Section `
    $changelog `
    '## [Unreleased]' `
    '## [1.1.0]'

$v11 = Get-Section `
    $changelog `
    '## [1.1.0] - 2026-09-12' `
    '## [1.0.0]'

$v10 = Get-Section `
    $changelog `
    '## [1.0.0] - 2026-09-11' `
    '## [0.1.0]'

# README: current product/release state
Assert-Match `
    $readme `
    'docs/release-notes-v1\.1\.0\.md' `
    'README does not link the 1.1.0 release notes.'

Assert-Match `
    $readme `
    'docs/release-notes-v1\.0\.0\.md' `
    'README does not preserve the 1.0.0 release-notes link.'

Assert-Match `
    $readme `
    'ConfigTrace 1\.0\.1' `
    'README does not describe the bundled ConfigTrace 1.0.1 integration.'

Assert-Match `
    $readme `
    '239 automated \.NET tests' `
    'README does not record the 239-test 1.1 baseline.'

Assert-Match `
    $readme `
    '1\.0\.0\s*->\s*1\.1\.0' `
    'README does not record the remaining installed 1.0.0 -> 1.1.0 upgrade gate.'

# CHANGELOG [Unreleased]: only truly remaining work
Assert-Match `
    $unreleased `
    '(?i)installer validation' `
    'Unreleased changelog does not record the remaining installer validation gate.'

Assert-Match `
    $unreleased `
    '1\.0\.0\s*->\s*1\.1\.0' `
    'Unreleased changelog does not identify the exact 1.0.0 -> 1.1.0 upgrade gate.'

Assert-Match `
    $unreleased `
    '(?i)user-state preservation' `
    'Unreleased changelog does not record upgrade user-state preservation.'

Assert-Match `
    $unreleased `
    '(?i)code signing' `
    'Unreleased changelog does not preserve optional code-signing work.'

# CHANGELOG 1.1
Assert-Match `
    $v11 `
    'ConfigTrace 1\.0\.1' `
    '1.1.0 changelog does not record ConfigTrace 1.0.1.'

Assert-Match `
    $v11 `
    'b629c970dfc14fca5df1e0ef2b0d1d07d0d8c56c' `
    '1.1.0 changelog does not pin the ConfigTrace source commit.'

Assert-Match `
    $v11 `
    'fe1c470a58402e82e97ee529c6a6b02822430da70e65ffc5fc5a71359ad4e521' `
    '1.1.0 changelog does not pin the ConfigTrace executable SHA-256.'

Assert-Match `
    $v11 `
    '239/239 \.NET tests passed' `
    '1.1.0 changelog does not record the 239/239 test result.'

Assert-Match `
    $v11 `
    '(?i)disabled by default' `
    '1.1.0 changelog does not preserve the ConfigTrace default-OFF contract.'

Assert-Match `
    $v11 `
    'apiToken' `
    '1.1.0 changelog does not document the camelCase sensitive-key privacy fix.'

Assert-Match `
    $v11 `
    '(?i)Context' `
    '1.1.0 changelog does not record Context evidence semantics.'

# CHANGELOG 1.0 frozen facts
Assert-Match `
    $v10 `
    '10c5769364068619f026e609a3c221d2665ede57' `
    '1.0.0 changelog does not preserve the frozen source commit.'

Assert-Match `
    $v10 `
    '185/185 \.NET tests passed' `
    '1.0.0 changelog does not preserve the 185/185 test result.'

Assert-Match `
    $v10 `
    '3a0a61969830e824b5c8d3d828b730a0e878b81679df6b8625897840c39560bb' `
    '1.0.0 changelog does not preserve the frozen portable hash.'

Assert-Match `
    $v10 `
    '8574e45f1ce649cfd78748bc42044ef5e9b79a7fa738fc862bb657d51dd52abc' `
    '1.0.0 changelog does not preserve the frozen installer hash.'

# 1.0 release notes
Assert-Match `
    $notes10 `
    '^# CrashScope 1\.0\.0 release notes' `
    '1.0.0 release notes have the wrong title.'

Assert-Match `
    $notes10 `
    '10c5769364068619f026e609a3c221d2665ede57' `
    '1.0.0 release notes do not preserve the frozen source commit.'

Assert-Match `
    $notes10 `
    '3a0a61969830e824b5c8d3d828b730a0e878b81679df6b8625897840c39560bb' `
    '1.0.0 release notes do not preserve the portable hash.'

Assert-Match `
    $notes10 `
    '8574e45f1ce649cfd78748bc42044ef5e9b79a7fa738fc862bb657d51dd52abc' `
    '1.0.0 release notes do not preserve the installer hash.'

Assert-Match `
    $notes10 `
    '185 passed, 0 failed' `
    '1.0.0 release notes do not preserve the automated-test result.'

Assert-Match `
    $notes10 `
    '(?i)user data.*preserved|preserved.*user data' `
    '1.0.0 release notes do not describe user-data preservation.'

# 1.1 release notes
Assert-Match `
    $notes11 `
    '^# CrashScope 1\.1\.0 release notes' `
    '1.1.0 release notes have the wrong title.'

Assert-Match `
    $notes11 `
    'ConfigTrace 1\.0\.1' `
    '1.1.0 release notes do not describe ConfigTrace 1.0.1.'

Assert-Match `
    $notes11 `
    'b629c970dfc14fca5df1e0ef2b0d1d07d0d8c56c' `
    '1.1.0 release notes do not pin the ConfigTrace source commit.'

Assert-Match `
    $notes11 `
    'fe1c470a58402e82e97ee529c6a6b02822430da70e65ffc5fc5a71359ad4e521' `
    '1.1.0 release notes do not pin the ConfigTrace executable SHA-256.'

Assert-Match `
    $notes11 `
    '239 \.NET tests passed, 0 failed' `
    '1.1.0 release notes do not record the 239-test result.'

Assert-Match `
    $notes11 `
    'apiToken' `
    '1.1.0 release notes do not document the camelCase sensitive-key privacy fix.'

Assert-Match `
    $notes11 `
    '(?i)OFF by default|disabled by default' `
    '1.1.0 release notes do not preserve ConfigTrace default-OFF behavior.'

Assert-Match `
    $notes11 `
    '(?i)Context' `
    '1.1.0 release notes do not describe provider evidence as Context.'

Assert-Match `
    $notes11 `
    '1\.0\.0\s*->\s*1\.1\.0' `
    '1.1.0 release notes do not record the remaining installed upgrade gate.'

Assert-Match `
    $notes11 `
    '(?i)code signing.*not implemented|not implemented.*code signing' `
    '1.1.0 release notes do not state that code signing is not implemented.'

Assert-Match `
    $notes11 `
    '(?i)SmartScreen' `
    '1.1.0 release notes do not document unsigned-build SmartScreen behavior.'

# Publication truth boundary
Assert-Match `
    ($notes10 + "`n" + $notes11) `
    '(?i)publication.*defer|defer.*publication' `
    'Release notes do not preserve the deferred-publication truth boundary.'

Assert-NoMatch `
    ($notes10 + "`n" + $notes11) `
    '(?i)already published|public release is live|GitHub Release is live' `
    'Release notes overclaim publication state.'

Write-Host 'RELEASE DOCUMENTATION CONTRACT PASS'
Write-Host 'README current-release documentation: PASS'
Write-Host 'Changelog 1.0/1.1 structure: PASS'
Write-Host 'Frozen 1.0 provenance: PASS'
Write-Host 'ConfigTrace 1.0.1 provenance/privacy semantics: PASS'
Write-Host 'Publication truth boundary: PASS'
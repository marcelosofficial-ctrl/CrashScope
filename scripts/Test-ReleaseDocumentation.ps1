Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)

function Read-Text {
    param([string]$RelativePath)

    $path = Join-Path $repoRoot $RelativePath

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Release documentation contract failure: missing $RelativePath"
    }

    return [IO.File]::ReadAllText($path,$utf8)
}

function Assert-Match {
    param([string]$Text,[string]$Pattern,[string]$Message)

    if ($Text -notmatch $Pattern) {
        throw "Release documentation contract failure: $Message"
    }
}

function Assert-NoMatch {
    param([string]$Text,[string]$Pattern,[string]$Message)

    if ($Text -match $Pattern) {
        throw "Release documentation contract failure: $Message"
    }
}

function Get-Section {
    param(
        [string]$Text,
        [string]$StartHeading,
        [string]$NextHeading
    )

    $start = $Text.IndexOf($StartHeading,[StringComparison]::Ordinal)

    if ($start -lt 0) {
        throw "Release documentation contract failure: missing $StartHeading"
    }

    $finish = $Text.IndexOf(
        $NextHeading,
        $start + $StartHeading.Length,
        [StringComparison]::Ordinal)

    if ($finish -lt 0) {
        throw "Release documentation contract failure: missing $NextHeading"
    }

    return $Text.Substring($start,$finish - $start)
}

$readme = Read-Text 'README.md'
$changelog = Read-Text 'CHANGELOG.md'
$notes10 = Read-Text 'docs\release-notes-v1.0.0.md'
$notes11 = Read-Text 'docs\release-notes-v1.1.0.md'
$notes12 = Read-Text 'docs\release-notes-v1.2.0.md'

$unreleased = Get-Section $changelog '## [Unreleased]' '## [1.2.0]'
$v12 = Get-Section $changelog '## [1.2.0] - 2026-09-12' '## [1.1.0]'
$v11 = Get-Section $changelog '## [1.1.0] - 2026-09-12' '## [1.0.0]'
$v10 = Get-Section $changelog '## [1.0.0] - 2026-09-11' '## [0.1.0]'

Assert-Match $readme 'CrashScope 1\.2\.0 (?:final local release sealing is in progress|local release validation is complete)' `
    'README must describe the current 1.2 local release boundary.'

Assert-NoMatch $unreleased '(?i)installer validation' `
    'Unreleased must not list the completed installer gate.'

Assert-NoMatch $unreleased '1\.0\.0\s*->\s*1\.1\.0' `
    'Unreleased must not list the completed upgrade gate.'

Assert-Match $unreleased '(?i)code signing' `
    'Unreleased must preserve optional code-signing work.'

Assert-Match $v11 '239/239 \.NET tests passed' `
    '1.1 changelog must preserve 239/239 tests.'

Assert-Match $v11 '1\.0\.0\s*->\s*1\.1\.0' `
    '1.1 changelog must record the validated upgrade.'

Assert-Match $v11 '0\.2365%' `
    '1.1 changelog must record final ConfigTrace-OFF performance.'

Assert-Match $v11 '97\.31 MB' `
    '1.1 changelog must record final ConfigTrace-ON peak working set.'

Assert-Match $v11 '(?i)second-PC' `
    '1.1 changelog must record genuine second-PC validation.'

Assert-Match $v11 'b629c970dfc14fca5df1e0ef2b0d1d07d0d8c56c' `
    '1.1 changelog must preserve ConfigTrace source provenance.'

Assert-Match $v11 'fe1c470a58402e82e97ee529c6a6b02822430da70e65ffc5fc5a71359ad4e521' `
    '1.1 changelog must preserve ConfigTrace executable provenance.'

Assert-Match $v10 '10c5769364068619f026e609a3c221d2665ede57' `
    '1.0 changelog must preserve frozen source.'

Assert-Match $v10 '185/185 \.NET tests passed' `
    '1.0 changelog must preserve 185/185 tests.'

Assert-Match $notes11 'e51943f6b7aecaf98223ef30cbec10ef94c1eba1' `
    '1.1 release notes must record exact release/tag target.'

Assert-Match $notes11 '4ac728d03d634218add25d94e69a1a55ddd3283b6afc786dc155e1e7e15360a9' `
    '1.1 release notes must record exact portable SHA.'

Assert-Match $notes11 'eb0acdab0f1da5ae5fbaeceadbd3691ee188f0d42204ddffc9527aa38f28062f' `
    '1.1 release notes must record exact installer SHA.'

Assert-Match $notes11 '0\.2365%' `
    '1.1 release notes must record final performance.'

Assert-Match $notes11 '4\.99 MB' `
    '1.1 release notes must record ConfigTrace peak working set.'

Assert-Match $notes11 '(?i)manual dashboard inspection:\s*\*\*PASS\*\*' `
    '1.1 release notes must record final second-PC visual PASS.'

Assert-Match $notes11 '(?i)publication remains intentionally deferred' `
    '1.1 release notes must preserve deferred publication state.'

Assert-NoMatch ($notes10 + "`n" + $notes11) `
    '(?i)already published|public release is live|GitHub Release is live' `
    'Release notes must not overclaim publication.'

Assert-Match $readme 'v1\.2\.0 release notes' `
    'README must link the 1.2 release notes.'

Assert-Match $readme '266 automated \.NET tests' `
    'README must record the 1.2 automated-test count.'

Assert-Match $v12 '266/266 \.NET tests passed' `
    '1.2 changelog must record the 266/266 test boundary.'

Assert-Match $v12 '(?i)native WPF \+ WebView2 desktop shell' `
    '1.2 changelog must record the native desktop shell.'

Assert-Match $v12 '(?i)unsigned' `
    '1.2 changelog must preserve unsigned-installer truth.'

Assert-Match $notes12 '(?i)native Windows desktop application shell' `
    '1.2 release notes must describe the desktop-shell release.'

Assert-Match $notes12 '266/266 PASS' `
    '1.2 release notes must preserve the automated-test boundary.'

Assert-Match $notes12 '(?i)publication remains intentionally deferred' `
    '1.2 release notes must preserve publication truth.'

Assert-Match $notes12 '(?i)NVIDIA real-hardware validation remains outstanding' `
    '1.2 release notes must not overclaim NVIDIA validation.'

Assert-NoMatch $notes12 '(?i)already published|public release is live|GitHub Release is live' `
    '1.2 release notes must not overclaim publication.'

Write-Host 'RELEASE DOCUMENTATION CONTRACT PASS'
Write-Host 'Completed-vs-future boundary: PASS'
Write-Host 'Frozen 1.0 provenance: PASS'
Write-Host 'Frozen 1.1 runtime provenance: PASS'
Write-Host 'Final validation evidence: PASS'
Write-Host 'Publication truth boundary: PASS'
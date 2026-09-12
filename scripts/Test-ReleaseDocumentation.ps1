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

$readme = Read-Text 'README.md'
$changelog = Read-Text 'CHANGELOG.md'
$notes10 = Read-Text 'docs\release-notes-v1.0.0.md'
$notes11 = Read-Text 'docs\release-notes-v1.1.0.md'
$notes12 = Read-Text 'docs\release-notes-v1.2.0.md'
$hardware = Read-Text 'docs\hardware-validation.md'

Assert-Match $readme 'CrashScope \*\*1\.2\.0 is publicly released for Windows x64\*\*' `
    'README must describe the public 1.2 release.'

Assert-Match $readme 'https://github\.com/marcelosofficial-ctrl/CrashScope/releases/tag/v1\.2\.0' `
    'README must link the public 1.2 release.'

Assert-Match $readme '266 automated \.NET tests' `
    'README must preserve the 266-test boundary.'

Assert-Match $readme 'native Windows Desktop shell' `
    'README must describe the native Desktop shell.'

Assert-NoMatch $readme 'â' `
    'README must not contain known mojibake markers.'

Assert-NoMatch $readme '(?i)public publication is still intentionally gated' `
    'README must not retain stale pre-publication wording.'

Assert-Match $notes12 '(?i)CrashScope 1\.2\.0 is publicly released' `
    '1.2 release notes must record completed publication.'

Assert-Match $notes12 '8ce9c25dc40f6481bf7b782d3dae67deeb3e6cef' `
    '1.2 release notes must record the public runtime/tag commit.'

Assert-Match $notes12 '5cd5821800b2e5f2c4ace319a6921267414465129c704b8e50c83b1a1a932b04' `
    '1.2 release notes must preserve the exact portable SHA.'

Assert-Match $notes12 'a37c012293a1c5e5aa94c823f1898a85ef0bc896b5b3cf03d870e8191050a12e' `
    '1.2 release notes must preserve the exact installer SHA.'

Assert-Match $notes12 '266/266 PASS' `
    '1.2 release notes must preserve the automated-test boundary.'

Assert-Match $notes12 '(?i)NVIDIA real-hardware validation remains outstanding' `
    '1.2 release notes must not overclaim NVIDIA validation.'

Assert-NoMatch $notes12 '(?i)publication remains intentionally deferred' `
    '1.2 release notes must not retain stale pre-publication wording.'

Assert-Match $hardware 'Intel Core i5-3210M' `
    'Hardware matrix must preserve the Intel second-PC validation.'

Assert-Match $hardware 'Intel HD Graphics 4000' `
    'Hardware matrix must preserve the Intel GPU second-PC validation.'

Assert-Match $hardware 'NVIDIA real-hardware validation remains outstanding' `
    'Hardware matrix must preserve the NVIDIA validation boundary.'

Assert-Match $notes10 '10c5769364068619f026e609a3c221d2665ede57' `
    '1.0 release notes must preserve frozen source provenance.'

Assert-Match $notes11 'e51943f6b7aecaf98223ef30cbec10ef94c1eba1' `
    '1.1 release notes must preserve frozen source provenance.'

Assert-Match $changelog '266/266 \.NET tests passed' `
    'Changelog must preserve the 1.2 automated-test boundary.'

Write-Host 'RELEASE DOCUMENTATION CONTRACT PASS'
Write-Host 'Published 1.2 truth boundary: PASS'
Write-Host 'Frozen historical provenance: PASS'
Write-Host 'Hardware validation wording: PASS'

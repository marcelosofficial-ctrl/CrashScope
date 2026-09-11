[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$target = Join-Path $PSScriptRoot 'Validate-SecondPcBeta.ps1'
if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
    throw "Second-PC validator is missing: $target"
}

$content = Get-Content -LiteralPath $target -Raw

$required = @(
    'ValidationStateSafety.ps1',
    'Assert-CrashScopeValidationStateSafeToStart',
    'Protect-CrashScopeValidationState',
    'Restore-CrashScopeValidationState',
    'Test-CrashScopeStartupSnapshotMatch',
    'Complete-CrashScopeValidationState',
    'Validate-PortableCandidate.ps1',
    'ExpectedVersion',
    '-ExpectedVersion $ExpectedVersion'
)

foreach ($needle in $required) {
    if ($content -notmatch [regex]::Escape($needle)) {
        throw "Second-PC validator safety contract is missing required behavior: $needle"
    }
}

if ($content -match '(?m)^\s*\[switch\]\$LeaveRunning\b' -or $content -match '(?m)^\s*-LeaveRunning\b') {
    throw 'Second-PC validator must not expose or forward -LeaveRunning; the test process must stop before verified user-state restoration.'
}

if ($content -notmatch "Run this validation from a normal non-Administrator PowerShell window") {
    throw 'Second-PC validator must retain the normal-user/non-Administrator precondition.'
}

Write-Host 'Second-PC validation safety contract PASS.' -ForegroundColor Green

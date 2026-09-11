[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..")
)

$validatorPath = Join-Path $repoRoot "scripts\Validate-InstalledCandidate.ps1"

if (-not (Test-Path -LiteralPath $validatorPath -PathType Leaf)) {
    throw "Validate-InstalledCandidate.ps1 is missing."
}

$text = Get-Content -LiteralPath $validatorPath -Raw

function Require([string]$Pattern, [string]$Description) {
    if ($text -notmatch $Pattern) {
        throw "Installed validator contract failure: $Description"
    }
}

function Forbid([string]$Pattern, [string]$Description) {
    if ($text -match $Pattern) {
        throw "Installed validator contract failure: $Description"
    }
}

Require 'Test-IsElevated' `
    "normal-user validation gate is missing."

Require 'Assert-CrashScopeValidationStateSafeToStart' `
    "durable state preflight is missing."

Require 'Protect-CrashScopeValidationState' `
    "durable user-state protection is missing."

Require 'Restore-CrashScopeValidationState' `
    "verified state restoration is missing."

Require 'Complete-CrashScopeValidationState' `
    "verified vault completion is missing."

Require 'Test-CrashScopeStartupSnapshotMatch' `
    "startup restoration verification is missing."

Require 'ExpectedSha256' `
    "installer SHA256 gate is missing."

Require 'Programs\\CrashScope' `
    "per-user install path contract is missing."

Require '/VERYSILENT' `
    "silent lifecycle installation is missing."

Require '/TASKS=' `
    "optional installer tasks are not explicitly controlled."

Require '127\.0\.0\.1:5077/api/status' `
    "installed API health validation is missing."

Require 'Assert-LoopbackOnly' `
    "loopback-only validation is missing."

Require 'https://example\.com' `
    "hostile browser Origin test is missing."

Require 'SECOND INSTANCE PROCESS SAFETY' `
    "second-instance validation is missing."

Require 'attach-candidate' `
    "workload attach validation is missing."

Require '/api/sessions/stop' `
    "manual session stop validation is missing."

Require 'capture-marker' `
    "safe incident marker validation is missing."

Require 'restart persistence' `
    "restart persistence validation is missing."

Require 'Uninstall-Candidate' `
    "official uninstaller cleanup is missing."

Require '(?im)^finally\s*\{' `
    "fail-closed restoration finally block is missing."

Forbid '(?im)^\s*git\s+(push|pull|fetch|tag|reset|checkout)\b' `
    "validator must not perform remote/history Git operations."

Forbid '(?im)^\s*gh\s+' `
    "validator must not invoke GitHub CLI."

Forbid 'Remove-Item\s+[^`r`n]*\$productDataRoot' `
    "validator must not directly delete real product data."

Write-Host "INSTALLED VALIDATOR SAFETY CONTRACT PASS"
Write-Host "Durable state isolation: PASS"
Write-Host "Startup restoration: PASS"
Write-Host "Installed runtime gates: PASS"
Write-Host "Official uninstall cleanup: PASS"
Write-Host "Remote Git operations: NONE"
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$wrapper = Join-Path $PSScriptRoot 'Validate-NextBeta-WindowsPowerShell.ps1'
$content = Get-Content -LiteralPath $wrapper -Raw

$required = @(
    '$nextBeta = Join-Path $patchedScripts "Validate-NextBeta.ps1"',
    '$nextBetaCore = Join-Path $patchedScripts "Validate-NextBeta-Core.ps1"',
    '$nextBetaContent = Get-Content -LiteralPath $nextBetaCore -Raw',
    'Set-Content -LiteralPath $nextBetaCore -Value $nextBetaContent -Encoding UTF8',
    '[System.Management.Automation.Language.Parser]::ParseFile($nextBetaCore',
    '& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $nextBeta'
)

foreach ($needle in $required) {
    if (-not $content.Contains($needle)) {
        throw "Windows PowerShell wrapper layout guard failed. Missing expected text: $needle"
    }
}

$forbidden = @(
    '$nextBetaContent = Get-Content -LiteralPath $nextBeta -Raw',
    'Set-Content -LiteralPath $nextBeta -Value $nextBetaContent -Encoding UTF8',
    '[System.Management.Automation.Language.Parser]::ParseFile($nextBeta,'
)

foreach ($needle in $forbidden) {
    if ($content.Contains($needle)) {
        throw "Windows PowerShell wrapper layout guard failed. Compatibility patching must not target the public validator wrapper: $needle"
    }
}

Write-Host 'Windows PowerShell wrapper layout guard passed.'

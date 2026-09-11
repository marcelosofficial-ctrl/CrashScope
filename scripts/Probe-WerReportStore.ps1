param(
    [ValidateRange(1, 30)]
    [int]$Days = 7,

    [string]$BaseUri = 'http://localhost:5077',

    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

$base = $BaseUri.TrimEnd('/')
$uri = "$base/api/diagnostics/wer-store/probe?days=$Days"

try {
    $result = Invoke-RestMethod -Method Get -Uri $uri -TimeoutSec 30
}
catch {
    throw "Could not query CrashScope at $uri. Start the Agent first, then retry. $($_.Exception.Message)"
}

Write-Host "CrashScope native WER report-store probe"
Write-Host "  Window:              $($result.windowDays) day(s)"
Write-Host "  Native WER reports:  $($result.nativeReportCount)"
Write-Host "  Existing WER reports:$($result.existingWerReportCount)"
Write-Host "  Matching report IDs: $($result.matchingReportIds)"
Write-Host "  Native call time:     $([math]::Round([double]$result.nativeProbe.elapsedMilliseconds, 2)) ms"

foreach ($store in $result.nativeProbe.stores) {
    $state = if ($store.available) { 'available' } else { 'unavailable' }
    $suffix = if ($store.error) { " ($($store.error))" } else { '' }
    Write-Host "  $($store.store): $state, $($store.reportCount) report(s)$suffix"
}

if ($result.nativeOnly.Count -gt 0) {
    Write-Host "  Native-only candidates: $($result.nativeOnly.Count)"
}

if ($result.existingOnly.Count -gt 0) {
    Write-Host "  Existing-only candidates: $($result.existingOnly.Count)"
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolved = [System.IO.Path]::GetFullPath($OutputPath)
    $parent = [System.IO.Path]::GetDirectoryName($resolved)
    if ($parent) {
        [System.IO.Directory]::CreateDirectory($parent) | Out-Null
    }

    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolved -Encoding UTF8
    Write-Host "  JSON:                 $resolved"
}

$result

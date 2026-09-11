[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateRange(1, 999999999)]
    [int]$Pr,

    [string]$Repository = "marcelosofficial-ctrl/CrashScope",

    [string]$DownloadRoot = (Join-Path $env:TEMP "CrashScope-ci-validation"),

    [switch]$LeaveRunning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Stage([string]$Text) {
    Write-Host "`n===== $Text =====" -ForegroundColor Cyan
}

function Invoke-GhJson {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & gh @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI failed: gh $($Arguments -join ' ')`n$($output -join [Environment]::NewLine)"
    }

    $text = ($output -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    return $text | ConvertFrom-Json
}

if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is required. Install/authenticate gh before using one-command PR validation."
}

Write-Stage "VERIFY GITHUB AUTHENTICATION"
& gh auth status | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI is not authenticated. Run 'gh auth login' once, then retry."
}

Write-Stage "RESOLVE PR #$Pr"
$prInfo = Invoke-GhJson -Arguments @(
    "pr", "view", $Pr.ToString(),
    "--repo", $Repository,
    "--json", "number,title,headRefName,headRefOid,url,state"
)

if ($prInfo.state -ne "OPEN") {
    Write-Host "PR #$Pr is $($prInfo.state); validation will use its latest head commit anyway." -ForegroundColor Yellow
}

Write-Host "PR:      $($prInfo.title)"
Write-Host "Branch:  $($prInfo.headRefName)"
Write-Host "Head:    $($prInfo.headRefOid)"
Write-Host "URL:     $($prInfo.url)"

Write-Stage "FIND SUCCESSFUL CI RUN"
$runs = Invoke-GhJson -Arguments @(
    "run", "list",
    "--repo", $Repository,
    "--workflow", "CI",
    "--branch", [string]$prInfo.headRefName,
    "--event", "pull_request",
    "--limit", "20",
    "--json", "databaseId,headSha,status,conclusion,url,createdAt"
)

$runs = @($runs)
$run = $runs |
    Where-Object { $_.headSha -eq $prInfo.headRefOid -and $_.status -eq "completed" -and $_.conclusion -eq "success" } |
    Sort-Object createdAt -Descending |
    Select-Object -First 1

if ($null -eq $run) {
    $matching = $runs | Where-Object { $_.headSha -eq $prInfo.headRefOid } | Sort-Object createdAt -Descending | Select-Object -First 1
    if ($null -ne $matching) {
        throw "The latest CI run for PR head $($prInfo.headRefOid) is '$($matching.status)/$($matching.conclusion)'. Wait for a successful run before hardware validation: $($matching.url)"
    }

    throw "No successful CI run was found for the current PR head $($prInfo.headRefOid). Do not validate an older artifact."
}

Write-Host "CI run:  $($run.databaseId)"
Write-Host "URL:     $($run.url)"

Write-Stage "DOWNLOAD EXACT CI-TESTED ARTIFACT"
$artifactRoot = Join-Path $DownloadRoot "pr-$Pr-$($prInfo.headRefOid.Substring(0, 7))"
if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

& gh run download ([string]$run.databaseId) --repo $Repository --name "CrashScope-ci-win-x64" --dir $artifactRoot
if ($LASTEXITCODE -ne 0) {
    throw "Could not download the CrashScope-ci-win-x64 artifact from CI run $($run.databaseId)."
}

$zip = Join-Path $artifactRoot "CrashScope-ci-win-x64.zip"
$checksumFile = "$zip.sha256.txt"
if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) {
    throw "Downloaded artifact is missing CrashScope-ci-win-x64.zip."
}
if (-not (Test-Path -LiteralPath $checksumFile -PathType Leaf)) {
    throw "Downloaded artifact is missing CrashScope-ci-win-x64.zip.sha256.txt."
}

$checksumLine = (Get-Content -LiteralPath $checksumFile -Raw).Trim()
if ($checksumLine -notmatch '^(?<hash>[A-Fa-f0-9]{64})\s+') {
    throw "CI checksum file has an unexpected format: $checksumLine"
}
$expectedSha = $Matches.hash.ToLowerInvariant()
$actualSha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha -ne $expectedSha) {
    throw "Downloaded CI artifact checksum mismatch. Expected $expectedSha but got $actualSha."
}

Write-Host "Artifact: $zip"
Write-Host "SHA256:   $actualSha"

Write-Stage "RUN REAL-MACHINE VALIDATION"
$validator = Join-Path $PSScriptRoot "Validate-PortableCandidate.ps1"
if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) {
    throw "Validate-PortableCandidate.ps1 was not found beside this script."
}

$arguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $validator,
    "-CandidateZip", $zip,
    "-ExpectedSha256", $expectedSha
)
if ($LeaveRunning) {
    $arguments += "-LeaveRunning"
}

& powershell.exe @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Real-machine validation failed for PR #$Pr."
}

Write-Stage "PASS"
Write-Host "PR #$Pr head $($prInfo.headRefOid) passed one-command CI artifact + real-machine validation." -ForegroundColor Green

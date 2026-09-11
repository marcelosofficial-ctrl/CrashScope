[CmdletBinding()]
param(
    [string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Stage([string]$Text) {
    Write-Host "`n===== $Text =====" -ForegroundColor Cyan
}

$repoRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\local-release'
}

Set-Location $repoRoot

if ($env:OS -ne 'Windows_NT') {
    throw 'Local CrashScope release packaging must run on Windows.'
}

$dirty = @(git status --porcelain=v1)

if ($LASTEXITCODE -ne 0) {
    throw 'Could not determine Git working-tree state.'
}

if ($dirty.Count -gt 0) {
    Write-Host 'Working tree contains changes:'
    $dirty | ForEach-Object { Write-Host "  $_" }

    throw 'Refusing to build a release candidate from a dirty working tree.'
}

$projectPath = Join-Path $repoRoot 'src\CrashScope.Agent\CrashScope.Agent.csproj'

[xml]$project = Get-Content -LiteralPath $projectPath -Raw

$versionNode = $project.SelectSingleNode('//Version')

if (
    $null -eq $versionNode -or
    [string]::IsNullOrWhiteSpace($versionNode.InnerText)
) {
    throw 'CrashScope.Agent.csproj does not define Version.'
}

$version = $versionNode.InnerText.Trim()

$head = (git rev-parse HEAD).Trim()

if ($LASTEXITCODE -ne 0) {
    throw 'Could not resolve source commit.'
}

$sourceRef = (git branch --show-current).Trim()

if ([string]::IsNullOrWhiteSpace($sourceRef)) {
    $sourceRef = 'detached-head'
}

$artifactsRoot = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot 'artifacts')
)

$outputFull = [IO.Path]::GetFullPath($OutputRoot)

$artifactPrefix = $artifactsRoot.TrimEnd('\') + '\'

if (
    -not $outputFull.StartsWith(
        $artifactPrefix,
        [StringComparison]::OrdinalIgnoreCase
    )
) {
    throw "OutputRoot must remain underneath '$artifactsRoot'."
}

Write-Stage 'SOURCE IDENTITY'

Write-Host "Version: $version"
Write-Host "Commit:  $head"
Write-Host "Ref:     $sourceRef"

Write-Stage 'PRECONDITIONS'

if (@(Get-Process CrashScope -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'CrashScope is currently running. Exit it before release packaging.'
}

if (
    @(Get-NetTCPConnection `
        -LocalPort 5077 `
        -State Listen `
        -ErrorAction SilentlyContinue).Count -gt 0
) {
    throw 'Port 5077 is already in use.'
}

if (Test-Path -LiteralPath $outputFull) {
    Remove-Item -LiteralPath $outputFull -Recurse -Force
}

New-Item -ItemType Directory -Path $outputFull -Force | Out-Null

Write-Stage 'DASHBOARD'

Push-Location (Join-Path $repoRoot 'src\CrashScope.Dashboard')

try {
    npm.cmd ci --no-audit --no-fund

    if ($LASTEXITCODE -ne 0) {
        throw 'npm ci failed.'
    }

    npm.cmd run build

    if ($LASTEXITCODE -ne 0) {
        throw 'Dashboard production build failed.'
    }
}
finally {
    Pop-Location
}

Write-Stage '.NET RESTORE / BUILD / TEST'

dotnet restore (Join-Path $repoRoot 'CrashScope.sln')

if ($LASTEXITCODE -ne 0) {
    throw 'dotnet restore failed.'
}

dotnet build `
    (Join-Path $repoRoot 'CrashScope.sln') `
    -c Release `
    --no-restore

if ($LASTEXITCODE -ne 0) {
    throw 'dotnet build failed.'
}

dotnet test `
    (Join-Path $repoRoot 'CrashScope.sln') `
    -c Release `
    --no-build

if ($LASTEXITCODE -ne 0) {
    throw 'dotnet test failed.'
}

Write-Stage 'SELF-CONTAINED WINDOWS PUBLISH'

$publish = Join-Path $outputFull 'CrashScope-win-x64'

dotnet publish `
    $projectPath `
    -p:PublishProfile=PortableWinX64 `
    -o $publish

if ($LASTEXITCODE -ne 0) {
    throw 'Self-contained win-x64 publish failed.'
}

Copy-Item `
    (Join-Path $repoRoot 'LICENSE') `
    (Join-Path $publish 'LICENSE.txt')

Copy-Item `
    (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') `
    (Join-Path $publish 'THIRD-PARTY-NOTICES.md')

@(
    'CrashScope local release candidate'
    "Version: $version"
    "Source commit: $head"
    "Source ref: $sourceRef"
    "Built UTC: $([DateTime]::UtcNow.ToString('o'))"
    'Target: win-x64 self-contained'
    'Validation: local dashboard build + .NET build/test + portable smoke'
) | Set-Content `
    -LiteralPath (Join-Path $publish 'BUILD-INFO.txt') `
    -Encoding ascii

Write-Stage 'PORTABLE SMOKE'

powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File (Join-Path $repoRoot 'scripts\Test-PortableBuild.ps1') `
    -PublishDirectory $publish `
    -ExpectedVersion $version `
    -RequireBuildInfo

if ($LASTEXITCODE -ne 0) {
    throw 'Portable smoke validation failed.'
}

Write-Stage 'PACKAGE'

$baseName = "CrashScope-v$version-win-x64"
$zip = Join-Path $outputFull "$baseName.zip"
$checksum = "$zip.sha256.txt"

Compress-Archive `
    -Path (Join-Path $publish '*') `
    -DestinationPath $zip `
    -CompressionLevel Optimal

$hash = (
    Get-FileHash `
        -LiteralPath $zip `
        -Algorithm SHA256
).Hash.ToLowerInvariant()

"$hash  $baseName.zip" |
    Set-Content `
        -LiteralPath $checksum `
        -Encoding ascii

Write-Host ""
Write-Host "LOCAL RELEASE CANDIDATE BUILD PASS" -ForegroundColor Green
Write-Host "Version: $version"
Write-Host "Commit:  $head"
Write-Host "ZIP:     $zip"
Write-Host "SHA256:  $hash"
Write-Host "Checksum:$checksum"
Write-Host "GitHub Actions used: ZERO"

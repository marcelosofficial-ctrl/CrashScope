[CmdletBinding()]
param(
    [string]$SourceDir,
    [string]$OutputDir,
    [string]$IsccPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Stage([string]$Text) {
    Write-Host "`n===== $Text =====" -ForegroundColor Cyan
}

$repoRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)

Set-Location -LiteralPath $repoRoot

if ($env:OS -ne 'Windows_NT') {
    throw 'CrashScope installer packaging must run on Windows.'
}

$dirty = @(git status --porcelain=v1)

if ($LASTEXITCODE -ne 0) {
    throw 'Could not determine Git working-tree state.'
}

if ($dirty.Count -gt 0) {
    Write-Host 'Working tree contains changes:'
    $dirty | ForEach-Object { Write-Host "  $_" }

    throw 'Refusing to build an installer from a dirty working tree.'
}

$installerHead = (git rev-parse HEAD).Trim()

if ($LASTEXITCODE -ne 0) {
    throw 'Could not resolve installer source commit.'
}

$installerRef = (git branch --show-current).Trim()

if ([string]::IsNullOrWhiteSpace($installerRef)) {
    $installerRef = 'detached-head'
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

$artifactsRoot = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot 'artifacts')
)

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = Join-Path $artifactsRoot 'local-release\CrashScope-win-x64'
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $artifactsRoot 'local-installer'
}

$sourceFull = [IO.Path]::GetFullPath($SourceDir)
$outputFull = [IO.Path]::GetFullPath($OutputDir)

$artifactPrefix = $artifactsRoot.TrimEnd([char[]]@('\','/')) + '\'

if (
    -not $sourceFull.StartsWith(
        $artifactPrefix,
        [StringComparison]::OrdinalIgnoreCase
    )
) {
    throw "SourceDir must remain underneath '$artifactsRoot'."
}

if (
    -not $outputFull.StartsWith(
        $artifactPrefix,
        [StringComparison]::OrdinalIgnoreCase
    )
) {
    throw "OutputDir must remain underneath '$artifactsRoot'."
}

$requiredProductFiles = @(
    'CrashScope.exe',
    'wwwroot\index.html',
    'LICENSE.txt',
    'THIRD-PARTY-NOTICES.md',
    'BUILD-INFO.txt'
)

foreach ($relative in $requiredProductFiles) {
    $required = Join-Path $sourceFull $relative

    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Validated portable input is missing required file: $relative"
    }
}

$productBuildInfoPath = Join-Path $sourceFull 'BUILD-INFO.txt'
$productBuildInfo = Get-Content -LiteralPath $productBuildInfoPath -Raw

$productVersionMatch = [regex]::Match(
    $productBuildInfo,
    '(?m)^Version:\s*(.+?)\s*$'
)

$productCommitMatch = [regex]::Match(
    $productBuildInfo,
    '(?m)^Source commit:\s*([0-9a-fA-F]{40})\s*$'
)

$productRefMatch = [regex]::Match(
    $productBuildInfo,
    '(?m)^Source ref:\s*(.+?)\s*$'
)

if (-not $productVersionMatch.Success) {
    throw 'Portable BUILD-INFO does not contain a Version field.'
}

if (-not $productCommitMatch.Success) {
    throw 'Portable BUILD-INFO does not contain a valid Source commit.'
}

if (-not $productRefMatch.Success) {
    throw 'Portable BUILD-INFO does not contain a Source ref.'
}

$productVersion = $productVersionMatch.Groups[1].Value.Trim()
$productCommit = $productCommitMatch.Groups[1].Value.ToLowerInvariant()
$productRef = $productRefMatch.Groups[1].Value.Trim()

if ($productVersion -ne $version) {
    throw "Portable product version '$productVersion' does not match project version '$version'."
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $candidates = @(
        (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')
    )

    $IsccPath = $candidates |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and
            (Test-Path -LiteralPath $_ -PathType Leaf)
        } |
        Select-Object -First 1
}

if (
    [string]::IsNullOrWhiteSpace($IsccPath) -or
    -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)
) {
    throw 'Inno Setup 7 compiler ISCC.exe was not found.'
}

$issPath = Join-Path $repoRoot 'installer\CrashScope.iss'
$iconPath = Join-Path $repoRoot 'src\CrashScope.Agent\Assets\CrashScope.ico'

if (-not (Test-Path -LiteralPath $issPath -PathType Leaf)) {
    throw 'installer\CrashScope.iss is missing.'
}

if (-not (Test-Path -LiteralPath $iconPath -PathType Leaf)) {
    throw 'CrashScope installer icon is missing.'
}

Write-Stage 'SOURCE IDENTITY'
Write-Host "App version:              $version"
Write-Host "Product source commit:    $productCommit"
Write-Host "Product source ref:       $productRef"
Write-Host "Installer source commit:  $installerHead"
Write-Host "Installer source ref:     $installerRef"

Write-Stage 'PRECONDITIONS'

if (Get-Process -Name 'CrashScope' -ErrorAction SilentlyContinue) {
    throw 'CrashScope must be stopped before installer packaging.'
}

$listeners = @(
    Get-NetTCPConnection `
        -LocalPort 5077 `
        -State Listen `
        -ErrorAction SilentlyContinue
)

if ($listeners.Count -gt 0) {
    throw 'Port 5077 must be free before installer packaging.'
}

New-Item -ItemType Directory -Force -Path $outputFull | Out-Null

$setupExe = Join-Path $outputFull "CrashScope-Setup-$version.exe"
$checksum = "$setupExe.sha256.txt"
$installerBuildInfo = Join-Path $outputFull "CrashScope-Setup-$version.build-info.txt"

foreach ($path in @($setupExe, $checksum, $installerBuildInfo)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

Write-Stage 'INNO SETUP COMPILE'

$isccArgs = @(
    "--define=AppVersion=$version",
    "--define=SourceDir=$sourceFull",
    "--define=SetupIcon=$iconPath",
    "--output-dir=$outputFull",
    '--no-ide-signtools',
    '--no-signing',
    $issPath
)

& $IsccPath @isccArgs

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) {
    throw "Expected installer was not created: $setupExe"
}

Write-Stage 'HASH + PROVENANCE'

$hash = (
    Get-FileHash -LiteralPath $setupExe -Algorithm SHA256
).Hash.ToLowerInvariant()

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

[IO.File]::WriteAllText(
    $checksum,
    "$hash *$(Split-Path -Leaf $setupExe)`r`n",
    $utf8NoBom
)

$isccVersion = (@(& $IsccPath --version) -join ' ').Trim()

if ($LASTEXITCODE -ne 0) {
    throw 'Unable to read Inno Setup compiler version.'
}

$info = @"
CrashScope local installer candidate
App version: $version
Product source commit: $productCommit
Product source ref: $productRef
Installer source commit: $installerHead
Installer source ref: $installerRef
Built UTC: $([DateTime]::UtcNow.ToString('o'))
Target: per-user Windows x64-compatible installer
Default install directory: %LOCALAPPDATA%\Programs\CrashScope
Compiler: $isccVersion
Input publish directory: $sourceFull
Validation: installer contract + clean-tree compile + SHA256
GitHub Actions used: ZERO
"@

[IO.File]::WriteAllText(
    $installerBuildInfo,
    $info,
    $utf8NoBom
)

$sidecar = Get-Content -LiteralPath $checksum -Raw

if ($sidecar -notmatch [regex]::Escape($hash)) {
    throw 'Installer SHA256 sidecar verification failed.'
}

Write-Host ""
Write-Host "LOCAL INSTALLER BUILD PASS"
Write-Host "App version:             $version"
Write-Host "Product source commit:   $productCommit"
Write-Host "Installer source commit: $installerHead"
Write-Host "Installer:               $setupExe"
Write-Host "SHA256:                  $hash"
Write-Host "Checksum:                $checksum"
Write-Host "Build info:              $installerBuildInfo"
Write-Host "GitHub Actions used:     ZERO"
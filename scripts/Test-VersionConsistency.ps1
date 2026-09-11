[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..")
)

$projectPath = Join-Path $repoRoot "src\CrashScope.Agent\CrashScope.Agent.csproj"
$packagePath = Join-Path $repoRoot "src\CrashScope.Dashboard\package.json"
$lockPath = Join-Path $repoRoot "src\CrashScope.Dashboard\package-lock.json"

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$package = Get-Content -LiteralPath $packagePath -Raw | ConvertFrom-Json

$version = $project.SelectSingleNode("//Version").InnerText.Trim()
$fileVersion = $project.SelectSingleNode("//FileVersion").InnerText.Trim()
$assemblyVersion = $project.SelectSingleNode("//AssemblyVersion").InnerText.Trim()

$expectedBinaryVersion = "$ExpectedVersion.0"

if ($version -ne $ExpectedVersion) {
    throw "Agent Version mismatch. Expected $ExpectedVersion, got $version."
}

if ($fileVersion -ne $expectedBinaryVersion) {
    throw "Agent FileVersion mismatch. Expected $expectedBinaryVersion, got $fileVersion."
}

if ($assemblyVersion -ne $expectedBinaryVersion) {
    throw "Agent AssemblyVersion mismatch. Expected $expectedBinaryVersion, got $assemblyVersion."
}

if ([string]$package.version -ne $ExpectedVersion) {
    throw "Dashboard package.json version mismatch."
}

$nodeCommand = Get-Command "node.exe" -ErrorAction SilentlyContinue

if ($null -eq $nodeCommand) {
    $nodeCommand = Get-Command "node" -ErrorAction SilentlyContinue
}

if ($null -eq $nodeCommand) {
    throw "Node.js is required to validate package-lock.json."
}

$tempJs = Join-Path $env:TEMP (
    "CrashScope-lock-version-" +
    [Guid]::NewGuid().ToString("N") +
    ".js"
)

$js = @"
const fs = require("fs");
const p = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));
const root = p.packages && p.packages[""];
if (!root) process.exit(21);
console.log(String(p.version || ""));
console.log(String(root.version || ""));
"@

try {
    [IO.File]::WriteAllText(
        $tempJs,
        $js,
        (New-Object System.Text.UTF8Encoding($false))
    )

    $lockVersions = @(
        & $nodeCommand.Source `
            $tempJs `
            $lockPath
    )

    if ($LASTEXITCODE -ne 0) {
        throw "Node.js could not parse package-lock.json. Exit code: $LASTEXITCODE"
    }
}
finally {
    Remove-Item `
        -LiteralPath $tempJs `
        -Force `
        -ErrorAction SilentlyContinue
}

if ($lockVersions.Count -ne 2) {
    throw "package-lock validation returned an unexpected result count."
}

$lockTopVersion = [string]$lockVersions[0]
$lockRootPackageVersion = [string]$lockVersions[1]

if ($lockTopVersion -ne $ExpectedVersion) {
    throw "package-lock top-level version mismatch. Expected $ExpectedVersion, got $lockTopVersion."
}

if ($lockRootPackageVersion -ne $ExpectedVersion) {
    throw "package-lock root package version mismatch. Expected $ExpectedVersion, got $lockRootPackageVersion."
}

$installedValidator = Get-Content -LiteralPath (
    Join-Path $repoRoot "scripts\Validate-InstalledCandidate.ps1"
) -Raw

$portableValidator = Get-Content -LiteralPath (
    Join-Path $repoRoot "scripts\Validate-PortableCandidate.ps1"
) -Raw

$secondPcValidator = Get-Content -LiteralPath (
    Join-Path $repoRoot "scripts\Validate-SecondPcBeta.ps1"
) -Raw

$releaseBuilder = Get-Content -LiteralPath (
    Join-Path $repoRoot "scripts\Build-LocalReleaseCandidate.ps1"
) -Raw

$installerBuilder = Get-Content -LiteralPath (
    Join-Path $repoRoot "scripts\Build-LocalInstaller.ps1"
) -Raw

foreach ($validator in @(
    @{ Name = "installed"; Text = $installedValidator },
    @{ Name = "portable"; Text = $portableValidator },
    @{ Name = "second-PC"; Text = $secondPcValidator }
)) {
    if ($validator.Text -notmatch '\bExpectedVersion\b') {
        throw "$($validator.Name) validator does not expose ExpectedVersion."
    }
}

if ($installedValidator -match '"0\.1\.0"') {
    throw "Installed validator contains a live hardcoded historical product version."
}

if ($releaseBuilder -match '0\.1\.0') {
    throw "Release builder contains a hardcoded historical product version."
}

if ($installerBuilder -match '0\.1\.0') {
    throw "Installer builder contains a hardcoded historical product version."
}

Write-Host "VERSION CONSISTENCY CONTRACT PASS"
Write-Host "Agent Version:          $version"
Write-Host "FileVersion:            $fileVersion"
Write-Host "AssemblyVersion:        $assemblyVersion"
Write-Host "Dashboard package:      $($package.version)"
Write-Host "package-lock top level: $lockTopVersion"
Write-Host "package-lock root pkg:  $lockRootPackageVersion"
Write-Host "Validators:             VERSION-AWARE"
Write-Host "Builders:               VERSION-INDEPENDENT"
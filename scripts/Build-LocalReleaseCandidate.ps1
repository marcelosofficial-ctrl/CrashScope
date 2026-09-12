[CmdletBinding()]
param(
    [string]$OutputRoot,
    [string]$ConfigTraceExecutable,
    [string]$ConfigTraceLicense
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Stage([string]$Text) {
    Write-Host "`n===== $Text =====" -ForegroundColor Cyan
}

$repoRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)

$configTraceVersion = '1.0.1'
$configTraceSourceCommit = 'b629c970dfc14fca5df1e0ef2b0d1d07d0d8c56c'
$expectedConfigTraceSha256 = 'fe1c470a58402e82e97ee529c6a6b02822430da70e65ffc5fc5a71359ad4e521'
$expectedConfigTraceLicenseSha256 = '914c2351f89a915bda78f2f27ceb2745603a76c7bf9b16392432de32a85b3175'

if ([string]::IsNullOrWhiteSpace($ConfigTraceExecutable)) {
    $ConfigTraceExecutable = Join-Path $repoRoot '..\ConfigTrace\target\release\configtrace.exe'
}
if ([string]::IsNullOrWhiteSpace($ConfigTraceLicense)) {
    $ConfigTraceLicense = Join-Path $repoRoot '..\ConfigTrace\LICENSE'
}

$configTraceExecutableFull = [IO.Path]::GetFullPath($ConfigTraceExecutable)
$configTraceLicenseFull = [IO.Path]::GetFullPath($ConfigTraceLicense)

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
$desktopProjectPath = Join-Path $repoRoot 'src\CrashScope.Desktop\CrashScope.Desktop.csproj'

[xml]$project = Get-Content -LiteralPath $projectPath -Raw

$versionNode = $project.SelectSingleNode('//Version')

if (
    $null -eq $versionNode -or
    [string]::IsNullOrWhiteSpace($versionNode.InnerText)
) {
    throw 'CrashScope.Agent.csproj does not define Version.'
}

$version = $versionNode.InnerText.Trim()
$runtimeExpectedVersion = ($version -split '-', 2)[0]

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

if (@(Get-Process CrashScope.Desktop -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'CrashScope Desktop is currently running. Exit it before release packaging.'
}

Write-Stage 'CONFIGTRACE INPUT'

if (-not (Test-Path -LiteralPath $configTraceExecutableFull -PathType Leaf)) {
    throw "Frozen ConfigTrace executable is missing: $configTraceExecutableFull"
}
if (-not (Test-Path -LiteralPath $configTraceLicenseFull -PathType Leaf)) {
    throw "ConfigTrace license is missing: $configTraceLicenseFull"
}

$configTraceSourceHash = (
    Get-FileHash -LiteralPath $configTraceExecutableFull -Algorithm SHA256
).Hash.ToLowerInvariant()
if ($configTraceSourceHash -ne $expectedConfigTraceSha256) {
    throw "ConfigTrace executable SHA256 mismatch. Expected $expectedConfigTraceSha256 but got $configTraceSourceHash."
}

$configTraceLicenseHash = (
    Get-FileHash -LiteralPath $configTraceLicenseFull -Algorithm SHA256
).Hash.ToLowerInvariant()
if ($configTraceLicenseHash -ne $expectedConfigTraceLicenseSha256) {
    throw "ConfigTrace license SHA256 mismatch. Expected $expectedConfigTraceLicenseSha256 but got $configTraceLicenseHash."
}

Write-Host "ConfigTrace version: $configTraceVersion"
Write-Host "ConfigTrace source commit: $configTraceSourceCommit"
Write-Host "ConfigTrace EXE SHA256: $configTraceSourceHash"

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

Write-Stage 'DESKTOP SHELL PUBLISH'

$desktopPublish = Join-Path $publish 'desktop'

dotnet publish `
    $desktopProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $desktopPublish

if ($LASTEXITCODE -ne 0) {
    throw 'CrashScope Desktop self-contained win-x64 publish failed.'
}

if (
    -not (Test-Path `
        -LiteralPath (Join-Path $desktopPublish 'CrashScope.Desktop.exe') `
        -PathType Leaf)
) {
    throw 'Desktop publish is missing CrashScope.Desktop.exe.'
}

Copy-Item `
    (Join-Path $repoRoot 'LICENSE') `
    (Join-Path $publish 'LICENSE.txt')

Copy-Item `
    (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') `
    (Join-Path $publish 'THIRD-PARTY-NOTICES.md')

Write-Stage 'CONFIGTRACE SIDECAR'

$configTracePublishDir = Join-Path $publish 'providers\ConfigTrace'
New-Item -ItemType Directory -Path $configTracePublishDir -Force | Out-Null

$configTracePublishedExe = Join-Path $configTracePublishDir 'configtrace.exe'
$configTracePublishedLicense = Join-Path $configTracePublishDir 'LICENSE.txt'

Copy-Item -LiteralPath $configTraceExecutableFull -Destination $configTracePublishedExe
Copy-Item -LiteralPath $configTraceLicenseFull -Destination $configTracePublishedLicense

$configTracePublishedHash = (
    Get-FileHash -LiteralPath $configTracePublishedExe -Algorithm SHA256
).Hash.ToLowerInvariant()
if ($configTracePublishedHash -ne $expectedConfigTraceSha256) {
    throw "Bundled ConfigTrace SHA256 mismatch. Expected $expectedConfigTraceSha256 but got $configTracePublishedHash."
}

$configTracePublishedLicenseHash = (
    Get-FileHash -LiteralPath $configTracePublishedLicense -Algorithm SHA256
).Hash.ToLowerInvariant()
if ($configTracePublishedLicenseHash -ne $expectedConfigTraceLicenseSha256) {
    throw "Bundled ConfigTrace license SHA256 mismatch."
}

@(
    'CrashScope local release candidate'
    "Version: $version"
    "Source commit: $head"
    "Source ref: $sourceRef"
    "Built UTC: $([DateTime]::UtcNow.ToString('o'))"
    'Target: win-x64 Agent root + self-contained Desktop subdirectory'
    "ConfigTrace version: $configTraceVersion"
    "ConfigTrace source commit: $configTraceSourceCommit"
    "ConfigTrace executable SHA256: $configTracePublishedHash"
    'ConfigTrace path: providers\ConfigTrace\configtrace.exe'
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
    -ExpectedVersion $runtimeExpectedVersion `
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

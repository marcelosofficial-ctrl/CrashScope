[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..")
)

$readmePath = Join-Path $repoRoot "README.md"
$userGuidePath = Join-Path $repoRoot "docs\user-guide.md"
$releasePlanPath = Join-Path $repoRoot "docs\release-plan.md"
$changelogPath = Join-Path $repoRoot "CHANGELOG.md"
$projectPath = Join-Path $repoRoot "src\CrashScope.Agent\CrashScope.Agent.csproj"

$readme = Get-Content -LiteralPath $readmePath -Raw
$userGuide = Get-Content -LiteralPath $userGuidePath -Raw
$releasePlan = Get-Content -LiteralPath $releasePlanPath -Raw
$changelog = Get-Content -LiteralPath $changelogPath -Raw
$project = Get-Content -LiteralPath $projectPath -Raw

function Require {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Description
    )

    if ($Text -notmatch $Pattern) {
        throw "Release documentation contract failure: $Description"
    }
}

function Forbid {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Description
    )

    if ($Text -match $Pattern) {
        throw "Release documentation contract failure: $Description"
    }
}

Require $readme `
    '## Windows release candidates' `
    'README does not document current Windows release candidates.'

Require $readme `
    '%LOCALAPPDATA%\\Programs\\CrashScope' `
    'README does not document the per-user installer path.'

Require $readme `
    'portable' `
    'README no longer documents portable distribution.'

Require $readme `
    'SmartScreen' `
    'README does not document unsigned-build SmartScreen behavior.'

Require $readme `
    'installer lifecycle validation' `
    'README does not describe installer validation.'

Forbid $readme `
    '## Download / public beta' `
    'obsolete portable-beta download heading returned.'

Require $userGuide `
    'per-user Windows installer' `
    'user guide does not document the installer.'

Require $userGuide `
    'CrashScope-Setup-<version>\.exe' `
    'user guide does not name the installer artifact.'

Require $userGuide `
    '%LOCALAPPDATA%\\CrashScope' `
    'user guide does not document preserved product data.'

Require $userGuide `
    'Start with Windows' `
    'user guide does not explain startup ownership.'

Forbid $userGuide `
    'current beta line is portable' `
    'obsolete portable-only user-guide wording returned.'

Require $releasePlan `
    'Checkpoint 4D' `
    'release plan does not record installed-runtime validation.'

Require $releasePlan `
    'CrashScope-Setup-1\.0\.0\.exe' `
    'release plan does not include the intended stable installer.'

Require $releasePlan `
    'CrashScope-v1\.0\.0-win-x64\.zip' `
    'release plan does not include the intended stable portable artifact.'

Forbid $releasePlan `
    'Installer/uninstaller after second-machine portable validation' `
    'obsolete installer milestone returned.'

Require $changelog `
    'Per-user Windows installer built with Inno Setup 7' `
    'Unreleased changelog does not record installer work.'

Require $changelog `
    'state-safe installed-candidate validation' `
    'Unreleased changelog does not record installed validation.'

Require $project `
    '<Title>CrashScope</Title>' `
    'Windows Title metadata is missing.'

Require $project `
    '<Company>CrashScope</Company>' `
    'Windows Company metadata is missing.'

Require $project `
    '<Version>1\.0\.0</Version>' `
    'stable release documentation requires Version 1.0.0.'

Require $project `
    '<FileVersion>1\.0\.0\.0</FileVersion>' `
    'stable release documentation requires FileVersion 1.0.0.0.'

Require $project `
    '<AssemblyVersion>1\.0\.0\.0</AssemblyVersion>' `
    'stable release documentation requires AssemblyVersion 1.0.0.0.'

Write-Host "RELEASE DOCUMENTATION CONTRACT PASS"
Write-Host "Installer documentation: PASS"
Write-Host "Portable documentation: PASS"
Write-Host "SmartScreen wording: PASS"
Write-Host "Release-plan checkpoint truth: PASS"
Write-Host "Windows metadata polish: PASS"
Write-Host "Stable 1.0.0 metadata: PASS"
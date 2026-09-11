[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)

$issPath = Join-Path $repoRoot 'installer\CrashScope.iss'
$builderPath = Join-Path $repoRoot 'scripts\Build-LocalInstaller.ps1'

if (-not (Test-Path -LiteralPath $issPath -PathType Leaf)) {
    throw 'installer\CrashScope.iss is missing.'
}

if (-not (Test-Path -LiteralPath $builderPath -PathType Leaf)) {
    throw 'scripts\Build-LocalInstaller.ps1 is missing.'
}

$iss = Get-Content -LiteralPath $issPath -Raw
$builder = Get-Content -LiteralPath $builderPath -Raw

function Assert-Match(
    [string]$Text,
    [string]$Pattern,
    [string]$Description
) {
    if ($Text -notmatch $Pattern) {
        throw "Installer contract failure: $Description"
    }
}

function Assert-NoMatch(
    [string]$Text,
    [string]$Pattern,
    [string]$Description
) {
    if ($Text -match $Pattern) {
        throw "Installer contract failure: $Description"
    }
}

Assert-Match $iss '(?im)^AppId=\{\{08FC71E2-02C3-4A5A-B5EA-F9302EADE31A\}\s*$' `
    'stable AppId is missing or changed.'

Assert-Match $iss '(?im)^PrivilegesRequired=lowest\s*$' `
    'installer must remain per-user/non-admin.'

Assert-Match $iss '(?im)^DefaultDirName=\{localappdata\}\\Programs\\CrashScope\s*$' `
    'default install directory changed.'

Assert-Match $iss '(?im)^SetupArchitecture=x64\s*$' `
    'installer must be emitted as x64 Setup.'

Assert-Match $iss '(?im)^ArchitecturesAllowed=x64compatible\s*$' `
    'x64-compatible architecture gate is missing.'

Assert-Match $iss '(?im)^ArchitecturesInstallIn64BitMode=x64compatible\s*$' `
    '64-bit install mode contract is missing.'

Assert-Match $iss '(?im)^OutputBaseFilename=CrashScope-Setup-\{#AppVersion\}\s*$' `
    'version-aware installer filename is missing.'

Assert-Match $iss '(?im)^SetupIconFile=\{#SetupIcon\}\s*$' `
    'CrashScope setup icon contract is missing.'

Assert-Match $iss '(?im)^Name:\s*"\{group\}\\CrashScope";\s*Filename:\s*"\{app\}\\CrashScope\.exe"\s*$' `
    'Start Menu shortcut is missing.'

Assert-Match $iss '(?im)^Name:\s*"desktopicon".*Flags:\s*unchecked\s*$' `
    'desktop shortcut must remain optional and unchecked by default.'

Assert-Match $iss '(?im)^Source:\s*"\{#SourceDir\}\\\*";\s*DestDir:\s*"\{app\}"' `
    'portable publish tree is not copied into the application directory.'

Assert-Match $iss '(?im)^Source:\s*"\{#SourceDir\}\\\*";.*Flags:.*\brecursesubdirs\b.*\bcreateallsubdirs\b' `
    'installer must recursively preserve bundled provider subdirectories.'

Assert-NoMatch $iss '(?im)^\s*\[Registry\]\s*$' `
    'installer must not take ownership of CrashScope Start-with-Windows registry state.'

Assert-NoMatch $iss '(?i)\bRegWrite[A-Za-z0-9_]*\s*\(' `
    'installer must not create or overwrite Start-with-Windows registry state.'

Assert-NoMatch $iss '(?im)^\s*\[UninstallDelete\]\s*$' `
    'installer must not delete CrashScope user data during uninstall.'

Assert-NoMatch $iss '(?i)crashscope\.db' `
    'installer must not manage or delete the user database.'

Assert-NoMatch $iss '(?i)\bNew-Service\b|\bsc\.exe\b' `
    'installer must not create a Windows Service.'

Assert-Match $builder 'git status --porcelain=v1' `
    'installer builder must refuse dirty source trees.'

Assert-Match $builder 'BUILD-INFO\.txt' `
    'installer builder must retain product provenance.'

Assert-Match $builder 'Get-FileHash' `
    'installer builder must generate a SHA256.'

Assert-Match $builder '--no-signing' `
    'local unsigned installer build must not inherit signing configuration.'

Assert-Match $builder 'GitHub Actions used:\s+ZERO' `
    'local installer builder must explicitly remain Actions-free.'

Assert-NoMatch $builder '(?im)^\s*git\s+(push|pull|fetch|tag|reset|checkout)\b' `
    'installer builder must not perform remote/history Git operations.'

Assert-NoMatch $builder '(?im)^\s*gh\s+' `
    'installer builder must not invoke GitHub CLI.'

Write-Host 'INSTALLER CONTRACT PASS'
Write-Host 'Per-user install: PASS'
Write-Host 'No forced startup: PASS'
Write-Host 'User-data preservation contract: PASS'
Write-Host 'No Windows Service: PASS'
Write-Host 'Local-only build contract: PASS'

# CrashScopeStartupCleanupContract
$startupCleanupInstallerPath = Join-Path `
    $PSScriptRoot `
    '..\installer\CrashScope.iss'

if (-not (Test-Path -LiteralPath $startupCleanupInstallerPath -PathType Leaf)) {
    throw 'Installer startup-cleanup contract could not find installer\CrashScope.iss.'
}

$startupCleanupInstallerText = Get-Content `
    -LiteralPath $startupCleanupInstallerPath `
    -Raw

$startupCleanupRequiredText = @(
    '; CrashScopeStartupCleanup',
    '[Code]',
    'CrashScopeStartupCleanupRunKey',
    'CrashScopeStartupCleanupValueName',
    'procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);',
    'RegQueryStringValue(',
    'ExpectedCommand :=',
    'ExpandConstant(''{app}\CrashScope.exe'')',
    'if CurrentCommand = ExpectedCommand then',
    'RegDeleteValue('
)

foreach ($startupCleanupRequired in $startupCleanupRequiredText) {
    if (-not $startupCleanupInstallerText.Contains($startupCleanupRequired)) {
        throw "Installer startup-cleanup contract is missing: $startupCleanupRequired"
    }
}

if ($startupCleanupInstallerText -match 'RegDeleteKeyIncludingSubkeys') {
    throw 'Installer startup cleanup must never delete the current-user Run key.'
}

if ($startupCleanupInstallerText -match 'DeleteValue\(.*throwOnMissingValue') {
    throw 'Installer startup cleanup unexpectedly contains .NET registry-deletion code.'
}

Write-Host 'Owned startup uninstall cleanup contract: PASS'
Write-Host 'Different CrashScope startup values are preserved by exact-match semantics: PASS'
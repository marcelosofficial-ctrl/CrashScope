[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workflow = Join-Path $PSScriptRoot '..\.github\workflows\release.yml'

if (-not (Test-Path -LiteralPath $workflow -PathType Leaf)) {
    throw "Release workflow is missing: $workflow"
}

$content = Get-Content -LiteralPath $workflow -Raw

$required = @(
    'name: Release Windows',
    'Publish a GitHub release from the selected v* tag',
    'Validate tag/version identity',
    '$tagVersion -ne $env:CRASHSCOPE_VERSION',
    "github.event_name == 'workflow_dispatch' && inputs.publish_release == true",
    '$versionFromTag = $tag.Substring(1)',
    '$notes = "docs/release-notes-$tag.md"',
    '$releaseArgs += ''--prerelease''',
    'GitHub release publication failed.'
)

foreach ($needle in $required) {
    if (-not $content.Contains($needle)) {
        throw "Release workflow contract is missing: $needle"
    }
}

$forbidden = @(
    'name: Release Windows beta',
    'Create GitHub prerelease',
    'docs/release-notes-v0.1.0.md'
)

foreach ($needle in $forbidden) {
    if ($content.Contains($needle)) {
        throw "Release workflow still contains legacy beta behavior: $needle"
    }
}

$conditionalPrerelease =
    '(?s)if \(\$versionFromTag -match ''-''\)\s*\{\s*\$releaseArgs \+= ''--prerelease'''

if ($content -notmatch $conditionalPrerelease) {
    throw 'Prerelease publication is not conditionally tied to prerelease-style version tags.'
}

Write-Host 'Release workflow contract PASS.' -ForegroundColor Green
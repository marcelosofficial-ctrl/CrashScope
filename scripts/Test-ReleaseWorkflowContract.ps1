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
    'workflow_dispatch:',
    'Manual-only by design while the private engineering repository has a strict hosted-runner budget.',
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

if ($content -match '(?m)^  push:\s*$') {
    throw 'Release workflow must remain manual-only while hosted-runner budget is constrained.'
}

if ($content -match '(?m)^- name:\s') {
    throw 'Release workflow contains a malformed top-level step.'
}

if ($content -notmatch '(?m)^      - name: Create GitHub release\s*$') {
    throw 'Create GitHub release must remain inside jobs.release.steps.'
}

if ($content.Contains('actions/upload-artifact')) {
    throw 'Release workflow must not duplicate release packages into Actions artifact storage.'
}

$conditionalPrerelease =
    '(?s)if \(\$versionFromTag -match ''-''\)\s*\{\s*\$releaseArgs \+= ''--prerelease'''

if ($content -notmatch $conditionalPrerelease) {
    throw 'Prerelease publication is not conditionally tied to prerelease-style version tags.'
}

Write-Host 'Release workflow contract PASS.' -ForegroundColor Green

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Section([string]$Title) {
    Write-Host ''
    Write-Host "===== $Title ====="
}

function Invoke-GitLines {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & git @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }

    return @($output | ForEach-Object { [string]$_ })
}

$inside = (Invoke-GitLines @('rev-parse', '--is-inside-work-tree') | Select-Object -First 1).Trim()
if ($inside -ne 'true') {
    throw 'Run this script from inside the CrashScope Git repository.'
}

$repoRoot = (Invoke-GitLines @('rev-parse', '--show-toplevel') | Select-Object -First 1).Trim()
Set-Location -LiteralPath $repoRoot

Write-Host 'CrashScope current-tree public snapshot privacy preflight'
Write-Host "Repository: $repoRoot"
Write-Host 'Mode: read-only current tracked-tree inspection; Git history is not rewritten or modified.'

Write-Section 'CURRENT TRACKED FILES'

$tracked = Invoke-GitLines @('ls-files')
Write-Host "Tracked file count: $($tracked.Count)"

$currentForbidden = @(
    $tracked |
        Where-Object {
            $_ -match '(^|/)(bin|obj|node_modules)(/|$)' -or
            $_ -match '(?i)\.(dmp|etl|db|sqlite|sqlite3|pfx|p12|pem|key|kdbx)$' -or
            $_ -match '(?i)(^|/)(\.env($|\.)|credentials?($|\.)|secrets?($|\.))' -or
            $_ -match '^src/CrashScope\.Agent/wwwroot/assets/'
        }
)

Write-Host "Forbidden/sensitive tracked filename count: $($currentForbidden.Count)"
if ($currentForbidden.Count -gt 0) {
    $currentForbidden | ForEach-Object { Write-Host "BLOCK FILE: $_" }
}

Write-Section 'CURRENT TREE CONTENT PATTERNS'

$patterns = @(
    [PSCustomObject]@{ Name = 'GitHub classic token'; Regex = 'gh[pousr]_[A-Za-z0-9_]{20,}'; Blocking = $true },
    [PSCustomObject]@{ Name = 'GitHub fine-grained token'; Regex = 'github_pat_[A-Za-z0-9_]{20,}'; Blocking = $true },
    [PSCustomObject]@{ Name = 'AWS access key'; Regex = 'AKIA[0-9A-Z]{16}'; Blocking = $true },
    [PSCustomObject]@{ Name = 'Google API key'; Regex = 'AIza[0-9A-Za-z_-]{35}'; Blocking = $true },
    [PSCustomObject]@{ Name = 'Private-key header'; Regex = 'BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY'; Blocking = $true },
    [PSCustomObject]@{ Name = 'Windows user-profile path'; Regex = 'C:\\Users\\[^\\[:space:]]+'; Blocking = $false },
    [PSCustomObject]@{ Name = 'Email address'; Regex = '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'; Blocking = $false }
)

$contentFindings = New-Object System.Collections.Generic.List[object]

foreach ($pattern in $patterns) {
    $output = & git grep -I -n -E -- $pattern.Regex HEAD -- 2>$null
    $code = $LASTEXITCODE

    if ($code -eq 0) {
        foreach ($line in @($output)) {
            $contentFindings.Add([PSCustomObject]@{
                Pattern = $pattern.Name
                Blocking = $pattern.Blocking
                Match = [string]$line
            })
        }
        continue
    }

    if ($code -ne 1) {
        throw "git grep failed while checking '$($pattern.Name)'."
    }
}

$highRisk = @($contentFindings | Where-Object { $_.Blocking })
$reviewOnly = @($contentFindings | Where-Object { -not $_.Blocking })

Write-Host "High-risk content findings: $($highRisk.Count)"
if ($highRisk.Count -gt 0) {
    $highRisk | Format-Table Pattern, Match -AutoSize
}

Write-Host "Review-only content findings: $($reviewOnly.Count)"
if ($reviewOnly.Count -gt 0) {
    $reviewOnly | Format-Table Pattern, Match -AutoSize
}

Write-Section 'COMMIT IDENTITY FOR FUTURE PUBLICATION'

$currentEmail = (& git config --get user.email 2>$null | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($currentEmail)) {
    Write-Host 'Configured Git email: (not set)' -ForegroundColor Yellow
    $identitySafe = $false
}
else {
    $currentEmail = $currentEmail.Trim()
    $identitySafe = $currentEmail -match '@users\.noreply\.github\.com$' -or $currentEmail -eq 'noreply@github.com'
    Write-Host "Configured Git email uses noreply: $identitySafe"
}

Write-Section 'SUMMARY'

Write-Host "High-risk content findings          : $($highRisk.Count)"
Write-Host "Forbidden/sensitive tracked files  : $($currentForbidden.Count)"
Write-Host "Review-only content findings       : $($reviewOnly.Count)"
Write-Host "Future Git identity privacy-safe   : $identitySafe"
Write-Host ''

if ($highRisk.Count -gt 0 -or $currentForbidden.Count -gt 0) {
    Write-Host 'RESULT: BLOCK - current tracked tree is not ready for a public snapshot.' -ForegroundColor Red
    exit 2
}

if (-not $identitySafe -or $reviewOnly.Count -gt 0) {
    Write-Host 'RESULT: REVIEW - no high-risk secret signature was proven, but review the listed identity/content findings before publication.' -ForegroundColor Yellow
    exit 1
}

Write-Host 'RESULT: PASS - current tracked tree and configured Git identity passed this publication preflight.' -ForegroundColor Green
exit 0

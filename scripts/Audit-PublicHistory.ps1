[CmdletBinding()]
param(
    [switch]$IncludeRemoteRefs
)

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

Write-Host 'CrashScope public-history privacy audit'
Write-Host "Repository: $repoRoot"
Write-Host 'Mode: read-only Git history inspection; no files, refs, commits, or settings are changed.'

$refs = if ($IncludeRemoteRefs) { @('--all') } else { @('--branches', '--tags') }

Write-Section 'COMMIT EMAIL METADATA'

$emailLines = Invoke-GitLines (@('log') + $refs + @('--format=%H%x09%ae%x09%ce'))
$emailFindings = New-Object System.Collections.Generic.List[object]

foreach ($line in $emailLines) {
    $parts = $line -split "`t", 3
    if ($parts.Count -lt 3) { continue }

    $commit = $parts[0]
    foreach ($entry in @(
        [PSCustomObject]@{ Role = 'author'; Email = $parts[1] },
        [PSCustomObject]@{ Role = 'committer'; Email = $parts[2] }
    )) {
        $email = ([string]$entry.Email).Trim()
        if ([string]::IsNullOrWhiteSpace($email)) { continue }
        if ($email -match '@users\.noreply\.github\.com$') { continue }
        if ($email -match '^noreply@github\.com$') { continue }

        $emailFindings.Add([PSCustomObject]@{
            Commit = $commit
            Role = $entry.Role
            Domain = if ($email -match '@(.+)$') { $Matches[1] } else { '(no-domain)' }
            Email = $email
        })
    }
}

$uniqueEmails = @($emailFindings | Sort-Object Email, Role -Unique)
Write-Host "Non-noreply commit-email entries: $($emailFindings.Count)"
Write-Host "Unique non-noreply emails: $($uniqueEmails.Count)"
if ($uniqueEmails.Count -gt 0) {
    $uniqueEmails | Format-Table Role, Email -AutoSize
}

Write-Section 'SENSITIVE / PRIVATE FILENAME HISTORY'

$nameLines = Invoke-GitLines (@('log') + $refs + @('--name-only', '--pretty=format:'))
$sensitiveNamePattern = '(?i)(^|/)(\.env($|\.)|.*\.(dmp|etl|db|sqlite|sqlite3|pfx|p12|pem|key|kdbx)$|sysdata\.xml$|.*wer.*\.(zip|cab|xml|txt)$|crashscope\.db($|-wal$|-shm$)|credentials?($|\.)|secrets?($|\.))'
$sensitiveNames = @(
    $nameLines |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -match $sensitiveNamePattern } |
        Sort-Object -Unique
)

Write-Host "Potentially sensitive historical filenames: $($sensitiveNames.Count)"
if ($sensitiveNames.Count -gt 0) {
    $sensitiveNames | ForEach-Object { Write-Host "REVIEW FILE: $_" }
}

Write-Section 'PATCH-HISTORY PATTERN AUDIT'

$patterns = @(
    [PSCustomObject]@{ Name = 'GitHub classic token'; Regex = 'gh[pousr]_[A-Za-z0-9_]{20,}' },
    [PSCustomObject]@{ Name = 'GitHub fine-grained token'; Regex = 'github_pat_[A-Za-z0-9_]{20,}' },
    [PSCustomObject]@{ Name = 'AWS access key'; Regex = 'AKIA[0-9A-Z]{16}' },
    [PSCustomObject]@{ Name = 'Google API key'; Regex = 'AIza[0-9A-Za-z_-]{35}' },
    [PSCustomObject]@{ Name = 'Private-key header'; Regex = 'BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY' },
    [PSCustomObject]@{ Name = 'Credential-style assignment'; Regex = '(api[_-]?key|access[_-]?token|auth[_-]?token|password|passwd|secret)[[:space:]]*[:=]' },
    [PSCustomObject]@{ Name = 'Windows user-profile path'; Regex = 'C:\\Users\\[^\\[:space:]]+' }
)

$patternFindings = New-Object System.Collections.Generic.List[object]

foreach ($pattern in $patterns) {
    $args = @('log') + $refs + @(
        "-G$($pattern.Regex)",
        '--format=COMMIT:%H',
        '--name-only',
        '--diff-filter=ACDMRT'
    )

    $lines = Invoke-GitLines $args
    $currentCommit = $null
    $filesForCommit = New-Object System.Collections.Generic.HashSet[string]

    function Flush-PatternCommit {
        if ($null -eq $currentCommit) { return }
        if ($filesForCommit.Count -eq 0) {
            $patternFindings.Add([PSCustomObject]@{
                Pattern = $pattern.Name
                Commit = $currentCommit
                File = '(commit matched; filename unavailable)'
            })
        }
        else {
            foreach ($file in $filesForCommit) {
                $patternFindings.Add([PSCustomObject]@{
                    Pattern = $pattern.Name
                    Commit = $currentCommit
                    File = $file
                })
            }
        }
    }

    foreach ($line in $lines) {
        if ($line -like 'COMMIT:*') {
            Flush-PatternCommit
            $currentCommit = $line.Substring(7).Trim()
            $filesForCommit = New-Object System.Collections.Generic.HashSet[string]
            continue
        }

        $trimmed = $line.Trim()
        if (-not [string]::IsNullOrWhiteSpace($trimmed)) {
            [void]$filesForCommit.Add($trimmed)
        }
    }

    Flush-PatternCommit
}

$patternFindings = @($patternFindings | Sort-Object Pattern, Commit, File -Unique)
Write-Host "Potential patch-history findings: $($patternFindings.Count)"
if ($patternFindings.Count -gt 0) {
    $patternFindings | Format-Table Pattern, Commit, File -AutoSize
}

Write-Section 'TRACKED FILES AT CURRENT HEAD'

$tracked = Invoke-GitLines @('ls-files')
$currentForbidden = @(
    $tracked |
        Where-Object {
            $_ -match '(^|/)(bin|obj|node_modules)(/|$)' -or
            $_ -match '(?i)\.(dmp|etl|db|sqlite|sqlite3|pfx|p12|pem|key)$' -or
            $_ -match '^src/CrashScope\.Agent/wwwroot/assets/'
        }
)
Write-Host "Current tracked forbidden/sensitive artifact count: $($currentForbidden.Count)"
if ($currentForbidden.Count -gt 0) {
    $currentForbidden | ForEach-Object { Write-Host "REVIEW TRACKED: $_" }
}

Write-Section 'SUMMARY'

$blockingPatternNames = @('GitHub classic token', 'GitHub fine-grained token', 'AWS access key', 'Google API key', 'Private-key header')
$highRisk = @($patternFindings | Where-Object { $_.Pattern -in $blockingPatternNames })

Write-Host "High-risk credential/key pattern findings : $($highRisk.Count)"
Write-Host "Other patch-history review findings       : $($patternFindings.Count - $highRisk.Count)"
Write-Host "Historical sensitive filename findings   : $($sensitiveNames.Count)"
Write-Host "Current forbidden tracked artifacts      : $($currentForbidden.Count)"
Write-Host "Unique non-noreply commit emails          : $($uniqueEmails.Count)"
Write-Host ''

if ($highRisk.Count -gt 0 -or $currentForbidden.Count -gt 0) {
    Write-Host 'RESULT: BLOCK — investigate the listed high-risk/current tracked findings before making the repository public.' -ForegroundColor Red
    exit 2
}
elseif ($sensitiveNames.Count -gt 0 -or $patternFindings.Count -gt 0 -or $uniqueEmails.Count -gt 0) {
    Write-Host 'RESULT: REVIEW — no high-risk credential signature was proven, but privacy/history findings require review before public visibility.' -ForegroundColor Yellow
    exit 1
}
else {
    Write-Host 'RESULT: PASS — no findings from this audit. This is still one layer of review, not a guarantee that arbitrary secrets cannot exist.' -ForegroundColor Green
    exit 0
}

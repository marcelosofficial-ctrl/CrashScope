[CmdletBinding()]
param(
    [int]$Pr = 59,
    [string]$Repository = "marcelosofficial-ctrl/CrashScope",
    [string]$WorkingRoot = (Join-Path $env:TEMP "CrashScope-tray-probe")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$runKeyPath = "Software\Microsoft\Windows\CurrentVersion\Run"
$startupValueName = "CrashScope"
$productDataRoot = Join-Path $env:LOCALAPPDATA "CrashScope"
$stateBackupRoot = Join-Path $WorkingRoot "state-backup"
$artifactRoot = Join-Path $WorkingRoot "artifact"
$extractRoot = Join-Path $WorkingRoot "app"
$stdoutPath = Join-Path $WorkingRoot "crashscope-stdout.txt"
$stderrPath = Join-Path $WorkingRoot "crashscope-stderr.txt"
$reportPath = Join-Path $WorkingRoot "tray-probe.json"

$hadProductData = $false
$startupStateCaptured = $false
$hadStartupValue = $false
$startupValue = $null
$startupValueKind = $null
$process = $null
$probeSucceeded = $false

function Write-Stage([string]$Text) {
    Write-Host "`n===== $Text =====" -ForegroundColor Cyan
}

function Invoke-GhJson {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $output = & gh @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI failed: gh $($Arguments -join ' ')`n$($output -join [Environment]::NewLine)"
    }
    $text = ($output -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return $text | ConvertFrom-Json
}

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-PortListener {
    Get-NetTCPConnection -LocalPort 5077 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Wait-CrashScopeHealthy {
    param([int]$TimeoutSeconds = 20)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            $status = Invoke-RestMethod -Uri "http://127.0.0.1:5077/api/status" -TimeoutSec 2
            if ($status.status -eq "running") { return $status }
        }
        catch { }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "CrashScope did not become healthy within $TimeoutSeconds seconds."
}

function Get-StartupSnapshot {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKeyPath, $false)
    try {
        if ($null -eq $key) {
            return [PSCustomObject]@{ Exists = $false; Value = $null; Kind = $null }
        }
        if ($startupValueName -notin @($key.GetValueNames())) {
            return [PSCustomObject]@{ Exists = $false; Value = $null; Kind = $null }
        }
        return [PSCustomObject]@{
            Exists = $true
            Value = $key.GetValue($startupValueName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            Kind = $key.GetValueKind($startupValueName)
        }
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Remove-CrashScopeStartupValue {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKeyPath, $true)
    try {
        if ($null -ne $key) { $key.DeleteValue($startupValueName, $false) }
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Restore-CrashScopeStartupValue {
    param([bool]$Exists, $Value, $Kind)
    if (-not $Exists) {
        Remove-CrashScopeStartupValue
        return
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runKeyPath, $true)
    try {
        if ($null -eq $key) { throw "Could not restore CrashScope startup registration." }
        $key.SetValue($startupValueName, $Value, $Kind)
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Read-CapturedText {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return "" }
    $raw = Get-Content -LiteralPath $Path -Raw
    if ($null -eq $raw) { return "" }
    return ([string]$raw).Trim()
}

function Test-TrayRegistration {
    param([Parameter(Mandatory = $true)][int]$ProcessId)

    if ($null -eq ("CrashScopeTrayProbeNative" -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class CrashScopeTrayProbeNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    public static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);
}
"@
    }

    $className = "CrashScope.Tray.$ProcessId"
    $window = [CrashScopeTrayProbeNative]::FindWindow($className, "CrashScope Tray")
    if ($null -eq $window -or $window -eq [IntPtr]::Zero) {
        return [PSCustomObject]@{
            ownerWindowFound = $false
            iconRegistered = $false
            className = $className
            windowHandle = 0
            hresult = $null
            iconRect = $null
        }
    }

    $windowHandle = ([IntPtr]$window).ToInt64()
    $identifier = New-Object CrashScopeTrayProbeNative+NOTIFYICONIDENTIFIER
    $identifier.cbSize = [uint32][Runtime.InteropServices.Marshal]::SizeOf($identifier)
    $identifier.hWnd = [IntPtr]$window
    $identifier.uID = 1
    $identifier.guidItem = [Guid]::Empty
    $rect = New-Object CrashScopeTrayProbeNative+RECT
    $hr = [CrashScopeTrayProbeNative]::Shell_NotifyIconGetRect([ref]$identifier, [ref]$rect)
    return [PSCustomObject]@{
        ownerWindowFound = $true
        iconRegistered = ($hr -eq 0)
        className = $className
        windowHandle = $windowHandle
        hresult = ("0x{0:X8}" -f ($hr -band 0xffffffff))
        iconRect = if ($hr -eq 0) {
            [PSCustomObject]@{ left = $rect.Left; top = $rect.Top; right = $rect.Right; bottom = $rect.Bottom }
        } else { $null }
    }
}

try {
    if ($env:OS -ne "Windows_NT") { throw "This tray probe must run on Windows." }
    if (Test-IsElevated) { throw "Run this probe from a normal non-Administrator PowerShell window." }
    if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) { throw "GitHub CLI (gh) is required." }
    if ($null -ne (Get-PortListener)) { throw "Port 5077 is already in use. Exit CrashScope before running the tray probe." }
    if (@(Get-Process -Name "CrashScope" -ErrorAction SilentlyContinue).Count -gt 0) { throw "A CrashScope process is already running. Exit it before the tray probe." }

    Write-Stage "RESOLVE EXACT NEXT-BETA HEAD"
    $prInfo = Invoke-GhJson -Arguments @("pr", "view", $Pr.ToString(), "--repo", $Repository, "--json", "headRefName,headRefOid,state")
    if ($prInfo.state -ne "OPEN" -or $prInfo.headRefName -ne "integration/next-beta") {
        throw "PR #$Pr is not the expected open integration/next-beta PR."
    }
    Write-Host "Head: $($prInfo.headRefOid)"

    $runs = @((Invoke-GhJson -Arguments @(
        "run", "list", "--repo", $Repository, "--workflow", "CI", "--branch", "integration/next-beta",
        "--event", "pull_request", "--limit", "20", "--json", "databaseId,headSha,status,conclusion,createdAt"
    )))
    $ciRun = $runs |
        Where-Object { $_.headSha -eq $prInfo.headRefOid -and $_.status -eq "completed" -and $_.conclusion -eq "success" } |
        Sort-Object createdAt -Descending |
        Select-Object -First 1
    if ($null -eq $ciRun) { throw "No successful CI run exists for exact head $($prInfo.headRefOid)." }
    Write-Host "CI run: $($ciRun.databaseId)"

    Write-Stage "PRESERVE USER STATE"
    if (Test-Path -LiteralPath $WorkingRoot) { Remove-Item -LiteralPath $WorkingRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $WorkingRoot -Force | Out-Null
    $startupSnapshot = Get-StartupSnapshot
    $startupStateCaptured = $true
    $hadStartupValue = [bool]$startupSnapshot.Exists
    $startupValue = $startupSnapshot.Value
    $startupValueKind = $startupSnapshot.Kind
    Remove-CrashScopeStartupValue
    if (Test-Path -LiteralPath $productDataRoot) {
        $hadProductData = $true
        New-Item -ItemType Directory -Path $stateBackupRoot -Force | Out-Null
        Move-Item -LiteralPath $productDataRoot -Destination (Join-Path $stateBackupRoot "CrashScope")
    }
    Write-Host "Real CrashScope local state isolated: $hadProductData"

    Write-Stage "DOWNLOAD EXACT CI ARTIFACT"
    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    & gh run download ([string]$ciRun.databaseId) --repo $Repository --name "CrashScope-ci-win-x64" --dir $artifactRoot
    if ($LASTEXITCODE -ne 0) { throw "Could not download exact CI artifact." }
    $zip = Join-Path $artifactRoot "CrashScope-ci-win-x64.zip"
    if (-not (Test-Path -LiteralPath $zip)) { throw "Downloaded artifact ZIP is missing." }
    Expand-Archive -LiteralPath $zip -DestinationPath $extractRoot -Force
    $exe = Join-Path $extractRoot "CrashScope.exe"
    if (-not (Test-Path -LiteralPath $exe)) { throw "CrashScope.exe is missing from artifact." }

    Write-Stage "START CRASHSCOPE WITH CAPTURED CONSOLE"
    $process = Start-Process -FilePath $exe -ArgumentList "--no-browser" -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $status = Wait-CrashScopeHealthy
    Start-Sleep -Seconds 2
    $tray = Test-TrayRegistration -ProcessId $process.Id

    Write-Host "Process ID: $($process.Id)"
    Write-Host "Owner window found: $($tray.ownerWindowFound)"
    Write-Host "Explorer icon registered: $($tray.iconRegistered)"

    $earlyResult = [PSCustomObject]@{
        head = [string]$prInfo.headRefOid
        ciRun = [long]$ciRun.databaseId
        processId = [int]$process.Id
        crashScopeVersion = [string]$status.crashScopeVersion
        tray = $tray
        stdout = ""
        stderr = ""
        passed = [bool]($tray.ownerWindowFound -and $tray.iconRegistered)
        completedAtUtc = [DateTime]::UtcNow.ToString("o")
    }
    $earlyResult | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host "Tray result persisted before console capture processing."

    $stdout = Read-CapturedText -Path $stdoutPath
    $stderr = Read-CapturedText -Path $stderrPath

    if (-not [string]::IsNullOrWhiteSpace($stdout)) {
        Write-Host "`nCaptured stdout:"
        Write-Host $stdout
    }
    if (-not [string]::IsNullOrWhiteSpace($stderr)) {
        Write-Host "`nCaptured stderr:"
        Write-Host $stderr
    }

    $result = [PSCustomObject]@{
        head = [string]$prInfo.headRefOid
        ciRun = [long]$ciRun.databaseId
        processId = [int]$process.Id
        crashScopeVersion = [string]$status.crashScopeVersion
        tray = $tray
        stdout = $stdout
        stderr = $stderr
        passed = [bool]($tray.ownerWindowFound -and $tray.iconRegistered)
        completedAtUtc = [DateTime]::UtcNow.ToString("o")
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    $probeSucceeded = $true

    Write-Stage "TRAY PROBE RESULT"
    Get-Content -LiteralPath $reportPath -Raw
}
catch {
    Write-Host ""
    Write-Host "TRAY PROBE ERROR" -ForegroundColor Red
    Write-Host "Type: $($_.Exception.GetType().FullName)" -ForegroundColor Red
    Write-Host "Message: $($_.Exception.Message)" -ForegroundColor Red
    if ($null -ne $_.InvocationInfo) {
        Write-Host "Position: $($_.InvocationInfo.PositionMessage)" -ForegroundColor Red
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$_.ScriptStackTrace)) {
        Write-Host "Stack: $($_.ScriptStackTrace)" -ForegroundColor Red
    }
    throw
}
finally {
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
        }
        catch { }
    }
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $productDataRoot) {
        $quarantine = "$WorkingRoot-test-state"
        if (Test-Path -LiteralPath $quarantine) { Remove-Item -LiteralPath $quarantine -Recurse -Force -ErrorAction SilentlyContinue }
        Move-Item -LiteralPath $productDataRoot -Destination $quarantine -ErrorAction SilentlyContinue
    }
    $backup = Join-Path $stateBackupRoot "CrashScope"
    if ($hadProductData -and (Test-Path -LiteralPath $backup)) {
        Move-Item -LiteralPath $backup -Destination $productDataRoot
    }
    if ($startupStateCaptured) {
        Restore-CrashScopeStartupValue -Exists $hadStartupValue -Value $startupValue -Kind $startupValueKind
    }
    if ($probeSucceeded) {
        Write-Host "`nOriginal CrashScope local data and startup registration restored." -ForegroundColor Green
    }
    else {
        Write-Host "`nTray probe did not complete. Restoration was attempted where state had been captured." -ForegroundColor Yellow
    }
}

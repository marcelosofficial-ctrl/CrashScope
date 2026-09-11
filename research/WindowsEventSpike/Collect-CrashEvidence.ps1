$ErrorActionPreference = "Continue"

$StartTime = (Get-Date).AddDays(-30)

$OutputFile = Join-Path $PSScriptRoot "windows-crash-evidence.txt"

"==================================================" | Out-File $OutputFile
"CrashScope Phase 0 - Windows Diagnostic Spike"     | Out-File $OutputFile -Append
"==================================================" | Out-File $OutputFile -Append
"Collected: $(Get-Date -Format o)"                  | Out-File $OutputFile -Append
"Looking back from: $StartTime"                     | Out-File $OutputFile -Append
""                                                  | Out-File $OutputFile -Append


function Write-Events {
    param(
        [string]$Title,
        [string]$LogName,
        [string[]]$Providers
    )

    "" | Out-File $OutputFile -Append
    "==================================================" | Out-File $OutputFile -Append
    $Title | Out-File $OutputFile -Append
    "==================================================" | Out-File $OutputFile -Append

    foreach ($Provider in $Providers) {

        "" | Out-File $OutputFile -Append
        "--- Provider: $Provider ---" | Out-File $OutputFile -Append

        try {
            $Events = Get-WinEvent -FilterHashtable @{
                LogName      = $LogName
                ProviderName = $Provider
                StartTime    = $StartTime
            } -ErrorAction Stop |
            Select-Object -First 100

            if (-not $Events) {
                "No matching events." |
                    Out-File $OutputFile -Append

                continue
            }

            foreach ($Event in $Events) {

                "TimeCreated : $($Event.TimeCreated)" |
                    Out-File $OutputFile -Append

                "Provider    : $($Event.ProviderName)" |
                    Out-File $OutputFile -Append

                "Event ID    : $($Event.Id)" |
                    Out-File $OutputFile -Append

                "Level       : $($Event.LevelDisplayName)" |
                    Out-File $OutputFile -Append

                "Record ID   : $($Event.RecordId)" |
                    Out-File $OutputFile -Append

                $Message = $Event.Message

                if ($null -ne $Message) {
                    $Message = $Message -replace "`r", " "
                    $Message = $Message -replace "`n", " "

                    "Message     : $Message" |
                        Out-File $OutputFile -Append
                }

                "--------------------------------------------------" |
                    Out-File $OutputFile -Append
            }
        }
        catch {
            "Could not read provider: $($_.Exception.Message)" |
                Out-File $OutputFile -Append
        }
    }
}


Write-Events `
    -Title "APPLICATION CRASH / WER EVENTS" `
    -LogName "Application" `
    -Providers @(
        "Application Error",
        "Windows Error Reporting",
        "Application Hang"
    )


Write-Events `
    -Title "SYSTEM / HARDWARE EVENTS" `
    -LogName "System" `
    -Providers @(
        "Microsoft-Windows-WHEA-Logger",
        "Microsoft-Windows-Kernel-Power",
        "Display"
    )


"" | Out-File $OutputFile -Append
"==================================================" | Out-File $OutputFile -Append
"LIVE KERNEL REPORT FILES"                           | Out-File $OutputFile -Append
"==================================================" | Out-File $OutputFile -Append

$LiveKernelPath = "C:\Windows\LiveKernelReports"

if (Test-Path $LiveKernelPath) {

    try {
        Get-ChildItem $LiveKernelPath -Recurse -File -ErrorAction Stop |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 100 `
            FullName,
            Length,
            CreationTime,
            LastWriteTime |
        Format-Table -AutoSize |
        Out-String |
        Out-File $OutputFile -Append
    }
    catch {
        "Could not enumerate LiveKernelReports: $($_.Exception.Message)" |
            Out-File $OutputFile -Append
    }
}
else {
    "LiveKernelReports directory does not exist." |
        Out-File $OutputFile -Append
}


"" | Out-File $OutputFile -Append
"==================================================" | Out-File $OutputFile -Append
"WINDOWS ERROR REPORTING ARCHIVES"                   | Out-File $OutputFile -Append
"==================================================" | Out-File $OutputFile -Append

$WerPaths = @(
    "C:\ProgramData\Microsoft\Windows\WER\ReportArchive",
    "C:\ProgramData\Microsoft\Windows\WER\ReportQueue"
)

foreach ($Path in $WerPaths) {

    "" | Out-File $OutputFile -Append
    "--- $Path ---" | Out-File $OutputFile -Append

    if (Test-Path $Path) {

        try {
            Get-ChildItem $Path -Directory -ErrorAction Stop |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 100 `
                Name,
                CreationTime,
                LastWriteTime |
            Format-Table -AutoSize |
            Out-String |
            Out-File $OutputFile -Append
        }
        catch {
            "Could not enumerate path: $($_.Exception.Message)" |
                Out-File $OutputFile -Append
        }
    }
    else {
        "Directory does not exist or is inaccessible." |
            Out-File $OutputFile -Append
    }
}


"" | Out-File $OutputFile -Append
"==================================================" | Out-File $OutputFile -Append
"END"                                                | Out-File $OutputFile -Append
"==================================================" | Out-File $OutputFile -Append

Write-Host ""
Write-Host "CrashScope diagnostic collection complete."
Write-Host ""
Write-Host "Output:"
Write-Host $OutputFile
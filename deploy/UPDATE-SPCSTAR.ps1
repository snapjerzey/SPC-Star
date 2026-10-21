param(
    [string]$InstallRoot = "C:\Program Files\SPCstar",
    [int]$Port = 5000,
    [string]$LegacyTaskName = "SPC-Star Server",
    [string]$ServiceName = "SPC-Star",
    [switch]$ApplySetupWorkbook
)

$ErrorActionPreference = "Stop"

$PackageRoot = $PSScriptRoot
$SourceApp = Join-Path $PackageRoot "app"
$InstallApp = Join-Path $InstallRoot "app"
$DataRoot = Join-Path $InstallRoot "data"
$BackupRoot = Join-Path $InstallRoot "backups"
$LogRoot = Join-Path $InstallRoot "logs"
$ArchiveRoot = Join-Path $DataRoot "archives"
$QuarantineRoot = Join-Path $InstallRoot "quarantine"
$ImportWorkbook = Join-Path $PackageRoot "imports\01 - SPC-Star Parts Inspections Materials Import - LATEST.xlsx"
$CompleteMarker = Join-Path $InstallRoot "UPDATE-COMPLETE.txt"
$AppLog = Join-Path $LogRoot "spcstar.log"

function Get-SpcStarStartupDiagnostics {
    param(
        [string]$ServiceName,
        [string]$AppLog,
        [int]$Port
    )

    $details = New-Object System.Collections.Generic.List[string]
    $details.Add("SPC-Star startup diagnostics:")

    try {
        $service = Get-Service -Name $ServiceName -ErrorAction Stop
        $details.Add("Windows service status: $($service.Status)")
    }
    catch {
        $details.Add("Windows service diagnostics unavailable: $($_.Exception.Message)")
    }

    try {
        $details.Add("Port $Port listeners:")
        $listeners = netstat -ano | Select-String ":$Port "
        if ($listeners) {
            $details.AddRange([string[]]($listeners | ForEach-Object { $_.Line.Trim() }))
        }
        else {
            $details.Add("No listener found on port $Port.")
        }
    }
    catch {
        $details.Add("Port diagnostics unavailable: $($_.Exception.Message)")
    }

    $process = Get-Process -Name "SPCStar.Api" -ErrorAction SilentlyContinue
    if ($process) {
        $details.Add("SPCStar.Api process is running. PID(s): $($process.Id -join ', ')")
    }
    else {
        $details.Add("SPCStar.Api process is not running.")
    }

    if (Test-Path -LiteralPath $AppLog) {
        $details.Add("Last SPC-Star log lines:")
        $details.AddRange([string[]](Get-Content -LiteralPath $AppLog -Tail 80))
    }
    else {
        $details.Add("SPC-Star log does not exist: $AppLog")
    }

    return ($details -join [Environment]::NewLine)
}

function Wait-SpcStarReady {
    param(
        [int]$Port,
        [string]$ServiceName,
        [string]$AppLog
    )

    $url = "http://localhost:$Port/health"
    for ($attempt = 1; $attempt -le 24; $attempt++) {
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 5 | Out-Null
            return
        }
        catch {
            Start-Sleep -Seconds 5
        }
    }

    $diagnostics = Get-SpcStarStartupDiagnostics -ServiceName $ServiceName -AppLog $AppLog -Port $Port
    throw "SPC-Star did not start on http://localhost:$Port.`n$diagnostics"
}

function Import-SpcStarSetupWorkbook {
    param(
        [Parameter(Mandatory = $true)][string]$WorkbookPath,
        [Parameter(Mandatory = $true)][string]$ExecutablePath,
        [Parameter(Mandatory = $true)][string]$DatabasePath
    )
    & $ExecutablePath --maintenance-import-workbook $WorkbookPath --SPCStar:DatabasePath $DatabasePath
    if ($LASTEXITCODE -ne 0) { throw "Local setup workbook update failed. The server remains stopped; keep the pre-update backup." }
}

Write-Host "Updating SPC-Star."

if (-not (Test-Path -LiteralPath (Join-Path $SourceApp "SPCStar.Api.exe"))) {
    throw "Missing app package: $SourceApp"
}
if ($ApplySetupWorkbook -and -not (Test-Path -LiteralPath $ImportWorkbook)) {
    throw "Missing setup workbook: $ImportWorkbook"
}
$ExpectedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $SourceApp 'SPCStar.Api.dll')).ProductVersion
if (-not $ExpectedVersion) { throw 'The package software version is missing.' }

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Open PowerShell as Administrator and run the update script again."
}

New-Item -ItemType Directory -Force -Path $InstallApp, $DataRoot, $BackupRoot, $LogRoot, $ArchiveRoot, $QuarantineRoot | Out-Null

$serviceNamesToRemove = @($ServiceName, "SPC-Star", "SPC-Star Server") | Select-Object -Unique
foreach ($name in $serviceNamesToRemove) {
    if (-not (Get-Service -Name $name -ErrorAction SilentlyContinue)) {
        continue
    }

    Stop-Service -Name $name -ErrorAction SilentlyContinue
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        $service = Get-Service -Name $name -ErrorAction SilentlyContinue
        if (-not $service -or $service.Status -eq "Stopped") {
            break
        }
        Start-Sleep -Seconds 1
    }

    & sc.exe delete $name | Out-Null
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        if (-not (Get-Service -Name $name -ErrorAction SilentlyContinue)) {
            break
        }
        Start-Sleep -Seconds 1
    }
}

if (Get-ScheduledTask -TaskName $LegacyTaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $LegacyTaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $LegacyTaskName -Confirm:$false -ErrorAction SilentlyContinue
}
Get-Process -Name "SPCStar.Api" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw "Port $Port is still in use. Stop SPC-Star before applying this update. No database update was attempted."
}

$stamp = Get-Date -Format "MMddyy HHmmss"
$preUpdateBackup = Join-Path $BackupRoot "Pre Update $stamp"
New-Item -ItemType Directory -Force -Path $preUpdateBackup | Out-Null
if (Test-Path -LiteralPath $CompleteMarker) {
    Move-Item -LiteralPath $CompleteMarker -Destination (Join-Path $preUpdateBackup 'PREVIOUS-UPDATE-COMPLETE.txt')
}

foreach ($file in @(
    (Join-Path $DataRoot "spcstar.db"),
    (Join-Path $DataRoot "spcstar.db-shm"),
    (Join-Path $DataRoot "spcstar.db-wal"),
    (Join-Path $DataRoot "spcstar-data.json")
)) {
    if (Test-Path -LiteralPath $file) {
        Copy-Item -LiteralPath $file -Destination $preUpdateBackup -Force
    }
}

robocopy $SourceApp $InstallApp /MIR /NFL /NDL /NP /R:2 /W:2
$copyCode = $LASTEXITCODE
if ($copyCode -gt 7) {
    throw "App copy failed with robocopy exit code $copyCode."
}

$installedExe = Join-Path $InstallApp "SPCStar.Api.exe"

# Code-only updates preserve all current setup. Import only when explicitly requested.
if ($ApplySetupWorkbook) {
    Import-SpcStarSetupWorkbook -WorkbookPath $ImportWorkbook -ExecutablePath $installedExe -DatabasePath (Join-Path $DataRoot "spcstar.db")
}

function Quote-ServiceArgument {
    param([string]$Value)
    return '"' + ($Value -replace '"', '\"') + '"'
}

$serviceArguments = @(
    "--urls", "http://0.0.0.0:$Port",
    "--SPCStar:DatabasePath", (Join-Path $DataRoot "spcstar.db"),
    "--SPCStar:DataPath", (Join-Path $DataRoot "spcstar-data.json"),
    "--SPCStar:ArchivePath", $ArchiveRoot,
    "--SPCStar:BackupPath", $BackupRoot,
    "--SPCStar:QuarantinePath", $QuarantineRoot
)
$binaryPath = (Quote-ServiceArgument $installedExe) + " " + (($serviceArguments | ForEach-Object { Quote-ServiceArgument $_ }) -join " ")

$ruleName = "SPC-Star TCP $Port"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port | Out-Null
}

New-Service -Name $ServiceName -DisplayName "SPC-Star" -BinaryPathName $binaryPath -StartupType Automatic | Out-Null
& sc.exe failure $ServiceName reset= 60 actions= restart/60000/restart/60000/restart/60000 | Out-Null

Start-Service -Name $ServiceName
Wait-SpcStarReady -Port $Port -ServiceName $ServiceName -AppLog $AppLog
$runningVersion = (Invoke-RestMethod -Uri "http://localhost:$Port/health").version
if ($runningVersion -ne $ExpectedVersion) {
    throw "Installed version mismatch. Expected $ExpectedVersion; running $runningVersion."
}

@"
SPC-Star update completed.
Version: $runningVersion
Completed: $(Get-Date -Format s)
App: $InstallApp
Data preserved: $DataRoot
Backup: $preUpdateBackup
Setup workbook imported: $ApplySetupWorkbook
Server URL: http://localhost:$Port/
Windows service: $ServiceName
"@ | Set-Content -LiteralPath $CompleteMarker -Encoding ASCII

Write-Host "SPC-Star update complete."
Write-Host "Installed version: $runningVersion"

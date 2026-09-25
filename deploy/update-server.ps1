param(
    [string]$InstallRoot = "C:\Program Files\SPCstar",
    [int]$Port = 5000,
    [string]$ServiceName = "SPC-Star",
    [string]$LegacyTaskName = "SPC-Star Server",
    [string]$BackupTaskName = "SPC-Star Daily Backup",
    [string]$BackupTime = "02:00"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$projectPath = Join-Path $repoRoot "src\SPCStar.Api\SPCStar.Api.csproj"
$appRoot = Join-Path $InstallRoot "app"
$backupScript = Join-Path $InstallRoot "backup-spcstar.ps1"

$serviceNamesToRemove = @($ServiceName, "SPC-Star", "SPC-Star Server") | Select-Object -Unique
foreach ($name in $serviceNamesToRemove) {
    if (-not (Get-Service -Name $name -ErrorAction SilentlyContinue)) {
        continue
    }

    Write-Host "Stopping SPC-Star service '$name'..."
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

Write-Host "Backing up SPC-Star data before update..."
& (Join-Path $repoRoot "deploy\backup-data.ps1") -InstallRoot $InstallRoot

Write-Host "Publishing updated SPC-Star app..."
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $appRoot

$appExe = Join-Path $appRoot "SPCStar.Api.exe"
$appIndex = Join-Path $appRoot "wwwroot\index.html"
if (-not (Test-Path -LiteralPath $appExe)) {
    throw "Update verification failed. Missing: $appExe"
}
if (-not (Test-Path -LiteralPath $appIndex)) {
    throw "Update verification failed. Missing web screen file: $appIndex"
}

Copy-Item -LiteralPath (Join-Path $repoRoot "deploy\backup-data.ps1") -Destination $backupScript -Force

Write-Host "Ensuring daily SPC-Star backup task exists..."
$backupAction = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$backupScript`" -InstallRoot `"$InstallRoot`" -Port $Port"
$backupTrigger = New-ScheduledTaskTrigger -Daily -At $BackupTime
$backupSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName $BackupTaskName -Action $backupAction -Trigger $backupTrigger -Settings $backupSettings -RunLevel Highest -Force | Out-Null

function Quote-ServiceArgument {
    param([string]$Value)
    return '"' + ($Value -replace '"', '\"') + '"'
}

$dataRoot = Join-Path $InstallRoot "data"
$backupRoot = Join-Path $InstallRoot "backups"
$serviceArguments = @(
    "--urls", "http://0.0.0.0:$Port",
    "--SPCStar:DatabasePath", (Join-Path $dataRoot "spcstar.db"),
    "--SPCStar:DataPath", (Join-Path $dataRoot "spcstar-data.json"),
    "--SPCStar:ArchivePath", (Join-Path $dataRoot "archives"),
    "--SPCStar:BackupPath", $backupRoot,
    "--SPCStar:QuarantinePath", (Join-Path $InstallRoot "quarantine")
)
$binaryPath = (Quote-ServiceArgument $appExe) + " " + (($serviceArguments | ForEach-Object { Quote-ServiceArgument $_ }) -join " ")

Write-Host "Creating Windows Service '$ServiceName'..."
New-Service -Name $ServiceName -DisplayName "SPC-Star" -BinaryPathName $binaryPath -StartupType Automatic | Out-Null
& sc.exe failure $ServiceName reset= 60 actions= restart/60000/restart/60000/restart/60000 | Out-Null

Write-Host "Starting SPC-Star..."
Start-Service -Name $ServiceName

Start-Sleep -Seconds 5
$healthUrl = "http://localhost:$Port/health"
$appUrl = "http://localhost:$Port/"
Invoke-RestMethod -Uri $healthUrl -TimeoutSec 15 | Out-Null
Invoke-WebRequest -UseBasicParsing -Uri $appUrl -TimeoutSec 15 | Out-Null
Write-Host "SPC-Star update complete."
Write-Host "Local health check: $healthUrl"
Write-Host "Network URL: http://$env:COMPUTERNAME`:$Port/"

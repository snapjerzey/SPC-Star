param(
    [string]$InstallRoot = "C:\SPCStar",
    [int]$Port = 5000,
    [string]$TaskName = "SPC-Star Server",
    [string]$BackupTaskName = "SPC-Star Daily Backup",
    [string]$BackupTime = "02:00"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$projectPath = Join-Path $repoRoot "src\SPCStar.Api\SPCStar.Api.csproj"
$appRoot = Join-Path $InstallRoot "app"
$backupScript = Join-Path $InstallRoot "backup-spcstar.ps1"

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping SPC-Star scheduled task..."
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
}

Write-Host "Backing up SPC-Star data before update..."
& (Join-Path $repoRoot "deploy\backup-data.ps1") -InstallRoot $InstallRoot

Write-Host "Publishing updated SPC-Star app..."
dotnet publish $projectPath -c Release -o $appRoot --self-contained false

Copy-Item -LiteralPath (Join-Path $repoRoot "deploy\start-spcstar.ps1") -Destination (Join-Path $InstallRoot "start-spcstar.ps1") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "deploy\backup-data.ps1") -Destination $backupScript -Force

Write-Host "Ensuring daily SPC-Star backup task exists..."
$backupAction = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$backupScript`" -InstallRoot `"$InstallRoot`" -Port $Port"
$backupTrigger = New-ScheduledTaskTrigger -Daily -At $BackupTime
$backupSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName $BackupTaskName -Action $backupAction -Trigger $backupTrigger -Settings $backupSettings -RunLevel Highest -Force | Out-Null

Write-Host "Starting SPC-Star..."
Start-ScheduledTask -TaskName $TaskName

Start-Sleep -Seconds 5
$healthUrl = "http://localhost:$Port/health"
Invoke-RestMethod -Uri $healthUrl -TimeoutSec 15 | Out-Null
Write-Host "SPC-Star update complete."
Write-Host "Local health check: $healthUrl"
Write-Host "Network URL: http://$env:COMPUTERNAME`:$Port/"

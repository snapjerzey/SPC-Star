param(
    [string]$InstallRoot = "C:\SPCStar",
    [int]$Port = 5000,
    [string]$TaskName = "SPC-Star Server"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$projectPath = Join-Path $repoRoot "src\SPCStar.Api\SPCStar.Api.csproj"
$appRoot = Join-Path $InstallRoot "app"
$dataRoot = Join-Path $InstallRoot "data"
$backupRoot = Join-Path $InstallRoot "backups"
$logRoot = Join-Path $InstallRoot "logs"
$startScript = Join-Path $InstallRoot "start-spcstar.ps1"

Write-Host "Installing SPC-Star server files..."
New-Item -ItemType Directory -Force -Path $appRoot, $dataRoot, $backupRoot, $logRoot | Out-Null

dotnet publish $projectPath -c Release -o $appRoot --self-contained false

Copy-Item -LiteralPath (Join-Path $repoRoot "deploy\start-spcstar.ps1") -Destination $startScript -Force

Write-Host "Creating firewall rule for TCP port $Port..."
$ruleName = "SPC-Star TCP $Port"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port | Out-Null
}

Write-Host "Creating scheduled task '$TaskName'..."
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$startScript`" -InstallRoot `"$InstallRoot`" -Port $Port"
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -RunLevel Highest -Force | Out-Null

Start-ScheduledTask -TaskName $TaskName

Start-Sleep -Seconds 5
$healthUrl = "http://localhost:$Port/health"
try {
    Invoke-RestMethod -Uri $healthUrl -TimeoutSec 15 | Out-Null
    Write-Host "SPC-Star is running."
    Write-Host "Local health check: $healthUrl"
    Write-Host "Network URL: http://$env:COMPUTERNAME`:$Port/"
}
catch {
    Write-Warning "SPC-Star task was created, but the health check did not respond yet. Check $logRoot\spcstar.log."
}

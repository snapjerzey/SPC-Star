param(
    [string]$InstallRoot = "C:\SPCStar"
)

$ErrorActionPreference = "Stop"

$dataPath = Join-Path $InstallRoot "data\spcstar-data.json"
$backupRoot = Join-Path $InstallRoot "backups"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = Join-Path $backupRoot "spcstar-data-$stamp.json"

New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

if (-not (Test-Path $dataPath)) {
    Write-Host "No SPC-Star data file exists yet at $dataPath. Nothing to back up."
    exit 0
}

Copy-Item -LiteralPath $dataPath -Destination $backupPath -Force
Write-Host "Backup created: $backupPath"

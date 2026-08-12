param(
    [string]$InstallRoot = "C:\SPCStar",
    [int]$Port = 5000
)

$ErrorActionPreference = "Stop"

$dataPath = Join-Path $InstallRoot "data\spcstar.db"
$legacyJsonPath = Join-Path $InstallRoot "data\spcstar-data.json"
$backupRoot = Join-Path $InstallRoot "backups"
$now = Get-Date
$stamp = $now.ToString("MMddyy 'Backup' HHmm")
$backupPath = Join-Path $backupRoot "$stamp.db"

New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

if (-not (Test-Path $dataPath)) {
    if (Test-Path $legacyJsonPath) {
        $legacyBackupPath = Join-Path $backupRoot "$stamp.json"
        Copy-Item -LiteralPath $legacyJsonPath -Destination $legacyBackupPath -Force
        Write-Host "Legacy JSON backup created: $legacyBackupPath"
        exit 0
    }

    Write-Host "No SPC-Star database exists yet at $dataPath. Nothing to back up."
    exit 0
}

try {
    $body = @{ backupPath = $backupPath } | ConvertTo-Json
    Invoke-RestMethod -Uri "http://localhost:$Port/admin/backups" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 60 | Out-Null
    Write-Host "Online backup created: $backupPath"
    exit 0
}
catch {
    Write-Warning "SPC-Star did not respond to the online backup request. Falling back to an offline file copy. Use this fallback only when SPC-Star is stopped or unavailable."
}

Copy-Item -LiteralPath $dataPath -Destination $backupPath -Force
foreach ($suffix in "-wal", "-shm") {
    $sidecarPath = "$dataPath$suffix"
    if (Test-Path $sidecarPath) {
        Copy-Item -LiteralPath $sidecarPath -Destination "$backupPath$suffix" -Force
    }
}
Write-Host "Backup created: $backupPath"

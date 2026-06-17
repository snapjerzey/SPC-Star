param(
    [string]$InstallRoot = "C:\SPCStar"
)

$ErrorActionPreference = "Stop"

$dataPath = Join-Path $InstallRoot "data\spcstar.db"
$legacyJsonPath = Join-Path $InstallRoot "data\spcstar-data.json"
$backupRoot = Join-Path $InstallRoot "backups"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = Join-Path $backupRoot "spcstar-$stamp.db"

New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

if (-not (Test-Path $dataPath)) {
    if (Test-Path $legacyJsonPath) {
        $legacyBackupPath = Join-Path $backupRoot "spcstar-data-$stamp.json"
        Copy-Item -LiteralPath $legacyJsonPath -Destination $legacyBackupPath -Force
        Write-Host "Legacy JSON backup created: $legacyBackupPath"
        exit 0
    }

    Write-Host "No SPC-Star database exists yet at $dataPath. Nothing to back up."
    exit 0
}

Copy-Item -LiteralPath $dataPath -Destination $backupPath -Force
foreach ($suffix in "-wal", "-shm") {
    $sidecarPath = "$dataPath$suffix"
    if (Test-Path $sidecarPath) {
        Copy-Item -LiteralPath $sidecarPath -Destination "$backupPath$suffix" -Force
    }
}
Write-Host "Backup created: $backupPath"

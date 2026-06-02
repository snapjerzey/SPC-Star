param(
    [string]$InstallRoot = "C:\SPCStar",
    [int]$Port = 5000
)

$ErrorActionPreference = "Stop"

$appPath = Join-Path $InstallRoot "app\SPCStar.Api.exe"
$dataPath = Join-Path $InstallRoot "data\spcstar-data.json"
$logPath = Join-Path $InstallRoot "logs\spcstar.log"

New-Item -ItemType Directory -Force -Path (Split-Path $dataPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $logPath) | Out-Null

$env:ASPNETCORE_URLS = "http://0.0.0.0:$Port"
$env:SPCSTAR_DATA_PATH = $dataPath

"[$(Get-Date -Format s)] Starting SPC-Star on http://0.0.0.0:$Port with data $dataPath" | Tee-Object -FilePath $logPath -Append

if (-not (Test-Path $appPath)) {
    throw "SPC-Star app was not found at $appPath. Run deploy\install-server.ps1 first."
}

& $appPath *>> $logPath

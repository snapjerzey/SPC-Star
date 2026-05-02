param(
    [int]$Port = 5000
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$apiProject = Join-Path $root "src\SPCStar.Api\SPCStar.Api.csproj"
$apiExe = Join-Path $root "src\SPCStar.Api\bin\Debug\net8.0\SPCStar.Api.exe"

$env:ASPNETCORE_URLS = "http://localhost:$Port"

Write-Host "Starting SPC Star API on http://localhost:$Port"
Write-Host "Health check: http://localhost:$Port/health"
Write-Host ""

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue

if ($dotnet) {
    & $dotnet.Source run --project $apiProject
    exit $LASTEXITCODE
}

if (Test-Path $apiExe) {
    Write-Warning "The .NET SDK is not on PATH, so this is running the last built API executable."
    Write-Warning "Install the .NET 8 SDK to rebuild or run tests with dotnet commands."
    & $apiExe
    exit $LASTEXITCODE
}

Write-Error "Could not find dotnet or the built API executable. Install the .NET 8 SDK, then run: dotnet run --project src/SPCStar.Api/SPCStar.Api.csproj"

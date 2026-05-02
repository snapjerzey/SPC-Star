@echo off
setlocal

set "ROOT=%~dp0"
set "PORT=%~1"
if "%PORT%"=="" set "PORT=5000"

set "ASPNETCORE_URLS=http://localhost:%PORT%"

echo Starting SPC Star API on http://localhost:%PORT%
echo Health check: http://localhost:%PORT%/health
echo.

where dotnet >nul 2>nul
if "%ERRORLEVEL%"=="0" (
    dotnet run --project "%ROOT%src\SPCStar.Api\SPCStar.Api.csproj"
    exit /b %ERRORLEVEL%
)

if exist "%ROOT%src\SPCStar.Api\bin\Debug\net8.0\SPCStar.Api.exe" (
    echo WARNING: The .NET SDK is not on PATH, so this is running the last built API executable.
    echo WARNING: Install the .NET 8 SDK to rebuild or run tests with dotnet commands.
    "%ROOT%src\SPCStar.Api\bin\Debug\net8.0\SPCStar.Api.exe"
    exit /b %ERRORLEVEL%
)

echo ERROR: Could not find dotnet or the built API executable.
echo Install the .NET 8 SDK, then run:
echo dotnet run --project src\SPCStar.Api\SPCStar.Api.csproj
exit /b 1

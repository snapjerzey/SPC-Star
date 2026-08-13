@echo off
setlocal

set "ROOT=%~dp0"
set "PORT=%~1"
if "%PORT%"=="" set "PORT=5000"
set "API_DIR=%ROOT%src\SPCStar.Api"
set "API_PROJECT=%API_DIR%\SPCStar.Api.csproj"
set "API_DLL=%API_DIR%\bin\Debug\net8.0\SPCStar.Api.dll"
set "API_EXE=%API_DIR%\bin\Debug\net8.0\SPCStar.Api.exe"

set "ASPNETCORE_URLS=http://localhost:%PORT%"

echo Starting SPC Star API on http://localhost:%PORT%
echo Health check: http://localhost:%PORT%/health
echo.

where dotnet >nul 2>nul
if "%ERRORLEVEL%"=="0" (
    dotnet build "%API_PROJECT%"
    if not "%ERRORLEVEL%"=="0" exit /b %ERRORLEVEL%
    pushd "%API_DIR%"
    dotnet "%API_DLL%"
    popd
    exit /b %ERRORLEVEL%
)

if exist "%API_EXE%" (
    echo WARNING: The .NET SDK is not on PATH, so this is running the last built API executable.
    echo WARNING: Install the .NET 8 SDK to rebuild or run tests with dotnet commands.
    pushd "%API_DIR%"
    "%API_EXE%"
    popd
    exit /b %ERRORLEVEL%
)

echo ERROR: Could not find dotnet or the built API executable.
echo Install the .NET 8 SDK, then run:
echo dotnet run --project src\SPCStar.Api\SPCStar.Api.csproj
exit /b 1

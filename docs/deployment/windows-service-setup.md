# SPC-Star Windows Service Setup

Use this when SPC-Star should keep running after PowerShell is closed and after the server user logs out.

## Server Folder

Copy the published SPC-Star package to:

```powershell
C:\SPC-Star\01 Published App
```

Create the database folder:

```powershell
New-Item -ItemType Directory -Force C:\SPC-Star\Data
```

## Required System Environment Variables

Run PowerShell as Administrator:

```powershell
setx /M SPCSTAR_DATABASE_PATH "C:\SPC-Star\Data\spcstar.db"
```

The service install command below passes the URL binding directly to SPC-Star, so no separate URL environment variable is required.

## Install as a Windows Service

Run PowerShell as Administrator:

```powershell
sc.exe create "SPC-Star" binPath= '"C:\Program Files\dotnet\dotnet.exe" "C:\SPC-Star\01 Published App\SPCStar.Api.dll" --urls http://0.0.0.0:5000' start= auto DisplayName= "SPC-Star"
sc.exe description "SPC-Star" "SPC-Star inspection and traceability server"
sc.exe start "SPC-Star"
```

After this is installed, SPC-Star starts automatically when the server starts. No one needs to stay logged in, and PowerShell does not need to remain open.

## Verify

On the server:

```powershell
Invoke-WebRequest http://localhost:5000/health
```

From another computer on the network:

```text
http://SERVER-NAME:5000/
```

Replace `SERVER-NAME` with the actual server name or IP address.

## Manage the Service

```powershell
sc.exe stop "SPC-Star"
sc.exe start "SPC-Star"
sc.exe query "SPC-Star"
```

The service can also be managed from Windows Services.

## Back Up the Database

Use SPC-Star's backup script from the project folder:

```powershell
.\deploy\backup-data.ps1 -InstallRoot "C:\SPC-Star"
```

When SPC-Star is running, the script uses the local server to create an online SQLite backup. Operators can remain logged in and continue working. The backup contains a consistent snapshot of all submitted/saved data up to the backup point.

## Update SPC-Star

1. Stop the service.
2. Replace the files in `C:\SPC-Star\01 Published App` with the new published package.
3. Start the service.
4. Verify `/health`.

```powershell
sc.exe stop "SPC-Star"
sc.exe start "SPC-Star"
```

## Remove the Service

```powershell
sc.exe stop "SPC-Star"
sc.exe delete "SPC-Star"
```

Deleting the service does not delete the database file. The database remains at:

```powershell
C:\SPC-Star\Data\spcstar.db
```

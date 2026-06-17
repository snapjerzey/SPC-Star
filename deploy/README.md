# SPC-Star Server Deployment

This folder contains the local-network deployment scripts for SPC-Star.

Recommended server layout:

- `C:\SPCStar\app` - published SPC-Star application files
- `C:\SPCStar\data` - SPC-Star database/storage file
- `C:\SPCStar\backups` - local backup copies
- `C:\SPCStar\logs` - server log output

SPC-Star uses a local SQLite database file by default. The scripts keep that file outside the app folder so updates can replace the application without wiping data.

## First Install

Run from the project folder on the server:

```powershell
.\deploy\install-server.ps1
```

Default local network URL:

```text
http://SERVER-NAME:5000/
```

Use the server's Windows computer name or IP address from shop-floor computers.

## Update Existing Server

After pulling the latest SPC-Star code onto the server:

```powershell
.\deploy\update-server.ps1
```

This stops the scheduled task, creates a database backup, publishes the newest app files, and restarts the scheduled task.

## Backup Only

```powershell
.\deploy\backup-data.ps1
```

Backups are stored in `C:\SPCStar\backups`.

## Notes

- The scripts use a Windows Scheduled Task named `SPC-Star Server` so the app can start automatically.
- The server must allow inbound traffic on the configured port, default `5000`.
- Operators do not install SPC-Star locally. They open the server URL in a browser.
- Keep the data folder and backup folder out of the app publish folder.

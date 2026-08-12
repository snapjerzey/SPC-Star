# SPC-Star Server Deployment

This folder contains the local-network deployment scripts for SPC-Star.

Recommended server layout:

- `C:\SPCStar\app` - published SPC-Star application files
- `C:\SPCStar\data` - SPC-Star database/storage file
- `C:\SPCStar\data\archives` - archived historical record files
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

When SPC-Star is running, the backup script asks the local SPC-Star server to create an online SQLite backup. Operators can stay logged in and continue submitting inspections while the backup is created. The backup captures a consistent snapshot of all data saved before the backup finishes; newer submissions continue into the live database and will be included in the next backup.

If SPC-Star is stopped or unavailable, the script falls back to a direct file copy for offline recovery use.

## Archiving Old Records

Use `Setup > Archive` inside SPC-Star when old historical records need to be removed from the live database for space management while keeping the seven-year record hold.

Only GOD access can create an archive. The workflow previews record counts for a selected cutoff date, requires GOD credentials, requires typing `ARCHIVE`, writes a JSON archive file, and only then removes matching history records from the live database. Archive does not delete parts, inspection plans, users, machines, rules, specifications, or control limits.

Archive files are written to `C:\SPCStar\data\archives` by the server start script through `SPCSTAR_ARCHIVE_PATH`. They should also be copied into the company's normal local retention location after creation.

## Notes

- The scripts use a Windows Scheduled Task named `SPC-Star Server` so the app can start automatically.
- The server must allow inbound traffic on the configured port, default `5000`.
- Operators do not install SPC-Star locally. They open the server URL in a browser.
- Keep the data folder and backup folder out of the app publish folder.

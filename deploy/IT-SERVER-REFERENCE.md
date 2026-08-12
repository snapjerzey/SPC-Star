# SPC-Star IT Server Reference

This file is intended for IT during SPC-Star server install, update, backup review, and recovery testing.

## Server Purpose

SPC-Star is a local browser-based inspection system. Operators use shop-floor computers or tablets to open the SPC-Star URL from the local network. Operators do not install SPC-Star on their own computers.

Default local network URL:

```text
http://SERVER-NAME:5000/
```

Use the server computer name or server IP address in place of `SERVER-NAME`.

## Server Folder Layout

Default install location:

```text
C:\SPCStar
```

Important folders:

- `C:\SPCStar\app` - published SPC-Star application files.
- `C:\SPCStar\data` - live SPC-Star database folder.
- `C:\SPCStar\data\spcstar.db` - live SPC-Star SQLite database.
- `C:\SPCStar\data\archives` - long-term archive export files.
- `C:\SPCStar\backups` - SPC-Star database backup files.
- `C:\SPCStar\quarantine` - suspect database copies saved before restore or destructive restore testing.
- `C:\SPCStar\logs` - server log output.

Keep `data`, `backups`, `quarantine`, and `archives` outside the app publish folder. Application updates should replace `C:\SPCStar\app` without deleting database or backup files.

## Required Scheduled Tasks

SPC-Star should not require an open PowerShell window or an active logged-in server session.

The deployment scripts create these Windows Scheduled Tasks:

- `SPC-Star Server`
  - Starts SPC-Star automatically when the server starts.
  - Runs SPC-Star in the background.
  - Keeps the application available after PowerShell closes and after the server user logs out.

- `SPC-Star Daily Backup`
  - Runs the SPC-Star backup script once per day.
  - Default time is `02:00`, which is 0200 / 2:00 AM.
  - Writes backup files to `C:\SPCStar\backups`.

## Install

Run from the SPC-Star project/update package folder on the server:

```powershell
.\deploy\install-server.ps1
```

To choose a different daily SPC-Star backup time:

```powershell
.\deploy\install-server.ps1 -BackupTime "03:00"
```

The `-BackupTime` value uses 24-hour time.

## Update

Run from the updated SPC-Star project/update package folder on the server:

```powershell
.\deploy\update-server.ps1
```

The update script:

1. Stops the `SPC-Star Server` scheduled task.
2. Creates a database backup before updating.
3. Publishes the updated app files.
4. Copies the current start and backup scripts into `C:\SPCStar`.
5. Creates or refreshes the `SPC-Star Daily Backup` scheduled task.
6. Starts SPC-Star again.
7. Verifies the local health endpoint.

To update and change the daily backup time:

```powershell
.\deploy\update-server.ps1 -BackupTime "03:00"
```

## Database Backup

The live database is:

```text
C:\SPCStar\data\spcstar.db
```

SPC-Star also creates its own backup files here:

```text
C:\SPCStar\backups
```

Backup file naming format:

```text
MMDDYY Backup HHMM.db
```

Example:

```text
081226 Backup 0200.db
```

Backups do not overwrite existing backups. If more than one backup is created during the same minute, SPC-Star appends seconds to keep the file name unique.

## Daily Backup Behavior

The `SPC-Star Daily Backup` task runs:

```powershell
C:\SPCStar\backup-spcstar.ps1
```

When SPC-Star is running, the backup script asks the local SPC-Star server to create an online SQLite backup. Operators may stay logged in and continue submitting inspections while this backup is created. The backup is a consistent snapshot of all data saved before the backup finishes. Any newer submissions continue into the live database and will be included in a later backup.

If SPC-Star is stopped or unavailable, the script falls back to a direct file copy. That fallback is mainly for recovery situations when the application is not running.

## Server Backup Scope

The company's normal local server backup should include:

- `C:\SPCStar\data\spcstar.db`
- `C:\SPCStar\backups`
- `C:\SPCStar\data\archives`
- `C:\SPCStar\quarantine`
- `C:\SPCStar\logs` if operational logs are retained

Best protection is:

1. SPC-Star creates daily local database backups in `C:\SPCStar\backups`.
2. The normal server backup captures `C:\SPCStar\backups` and `C:\SPCStar\data`.

This gives both application-level backups and server-level backups.

## Manual Backup

Manual script backup:

```powershell
.\deploy\backup-data.ps1
```

Manual backup from SPC-Star:

1. Log in as `Archon` or another GOD user.
2. Open `Setup > Archive`.
3. Use `Database Backup`.
4. Enter GOD credentials.
5. Click `Create Backup`.

The manual backup writes to the same backup folder:

```text
C:\SPCStar\backups
```

## Restore

Inside SPC-Star, restore testing is available under:

```text
Setup > Archive > Database Test / Restore
```

`Restore Latest Backup` restores the newest `.db` file from:

```text
C:\SPCStar\backups
```

Before restoring, SPC-Star saves the current database into quarantine:

```text
C:\SPCStar\quarantine
```

Quarantine file naming format:

```text
MMDDYY Quarantine HHMM.db
```

If SPC-Star cannot run and IT must restore manually:

1. Stop the `SPC-Star Server` scheduled task.
2. Copy the current suspect database from `C:\SPCStar\data\spcstar.db` into `C:\SPCStar\quarantine`.
3. Copy the selected known-good backup from `C:\SPCStar\backups` to `C:\SPCStar\data\spcstar.db`.
4. Start the `SPC-Star Server` scheduled task.
5. Verify:

```text
http://localhost:5000/health
```

Expected health response includes:

```json
{"status":"ok","app":"SPC Star"}
```

## Archive

Archive is different from backup.

- Backup creates a restorable copy of the database.
- Archive exports old historical records and removes them from the live database after the archive file is created.

Archive files are written to:

```text
C:\SPCStar\data\archives
```

Archive should be copied into the company's normal local retention location for the seven-year record hold process.

## Initial System Account

When the database is empty, SPC-Star seeds one protected system manager account:

- Username: `Archon`
- Password: `archon`
- Role: `GOD`

The password should be changed after server setup.

## Quick Health Checks

Local health check on the server:

```text
http://localhost:5000/health
```

Network app URL:

```text
http://SERVER-NAME:5000/
```

If the app does not respond:

1. Check that the `SPC-Star Server` scheduled task is running.
2. Check `C:\SPCStar\logs\spcstar.log`.
3. Confirm inbound TCP port `5000` is allowed on the server firewall.
4. Confirm the live database exists at `C:\SPCStar\data\spcstar.db`.

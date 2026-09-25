# SPC-Star IT Server Reference

This file is intended for IT during SPC-Star server install, update, backup review, and recovery testing.

## Server Purpose

SPC-Star is a local browser-based inspection system. Operators use shop-floor computers or tablets to open the SPC-Star URL from the local network. Operators do not install SPC-Star on their own computers.

Default local network URL:

```text
http://SERVER-NAME:5000/
```

Use the server computer name or server IP address in place of `SERVER-NAME`.

The current pilot install is a direct SPC-Star application install running in the background through the `SPC-Star` Windows Service. IIS is not part of the normal pilot runtime path.

## Server Folder Layout

Default install location:

```text
C:\Program Files\SPCstar
```

Important folders:

- `C:\Program Files\SPCstar\app` - published SPC-Star application files.
- `C:\Program Files\SPCstar\data` - live SPC-Star database folder.
- `C:\Program Files\SPCstar\data\spcstar.db` - live SPC-Star SQLite database.
- `C:\Program Files\SPCstar\data\archives` - long-term archive export files.
- `C:\Program Files\SPCstar\backups` - SPC-Star database backup files.
- `C:\Program Files\SPCstar\quarantine` - suspect database copies saved before restore or destructive restore testing.
- `C:\Program Files\SPCstar\logs` - server log output.

Keep `data`, `backups`, `quarantine`, and `archives` outside the app publish folder. Application updates should replace `C:\Program Files\SPCstar\app` without deleting database or backup files.

## Required Windows Service And Backup Task

SPC-Star should not require an open PowerShell window or an active logged-in server session.

The deployment scripts create this Windows Service:

- `SPC-Star`
  - Starts SPC-Star automatically when the server starts.
  - Runs SPC-Star in the background.
  - Keeps the application available after PowerShell closes and after the server user logs out.

The deployment scripts also create this Windows Scheduled Task:

- `SPC-Star Daily Backup`
  - Runs the SPC-Star backup script once per day.
  - Default time is `02:00`, which is 0200 / 2:00 AM.
  - Writes backup files to `C:\Program Files\SPCstar\backups`.

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

1. Stops the `SPC-Star` Windows Service.
2. Creates a database backup before updating.
3. Publishes the updated app files.
4. Copies the current support scripts into `C:\Program Files\SPCstar`.
5. Creates or refreshes the `SPC-Star Daily Backup` scheduled task.
6. Starts SPC-Star again.
7. Verifies the local health endpoint.

For the current thumb-drive patch handoff, IT should run the update script from the root of the handoff drive/package. The patch is expected to preserve the existing live data folder, replace only the application files and support scripts, restart the `SPC-Star` Windows Service, and verify that the app responds at `/health`.

To update and change the daily backup time:

```powershell
.\deploy\update-server.ps1 -BackupTime "03:00"
```

## Database Backup

The live database is:

```text
C:\Program Files\SPCstar\data\spcstar.db
```

SPC-Star also creates its own backup files here:

```text
C:\Program Files\SPCstar\backups
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
C:\Program Files\SPCstar\backup-spcstar.ps1
```

When SPC-Star is running, the backup script asks the local SPC-Star server to create an online SQLite backup. Operators may stay logged in and continue submitting inspections while this backup is created. The backup is a consistent snapshot of all data saved before the backup finishes. Any newer submissions continue into the live database and will be included in a later backup.

If SPC-Star is stopped or unavailable, the script falls back to a direct file copy. That fallback is mainly for recovery situations when the application is not running.

## Server Backup Scope

The company's normal local server backup should include:

- `C:\Program Files\SPCstar\data\spcstar.db`
- `C:\Program Files\SPCstar\backups`
- `C:\Program Files\SPCstar\data\archives`
- `C:\Program Files\SPCstar\quarantine`
- `C:\Program Files\SPCstar\logs` if operational logs are retained

Best protection is:

1. SPC-Star creates daily local database backups in `C:\Program Files\SPCstar\backups`.
2. The normal server backup captures `C:\Program Files\SPCstar\backups` and `C:\Program Files\SPCstar\data`.

This gives both application-level backups and server-level backups.

## Manual Backup

Manual script backup:

```powershell
.\deploy\backup-data.ps1
```

Manual backup from SPC-Star:

1. Log in as `Archon` or another System Manager user.
2. Open `Setup > Archive`.
3. Use `Database Backup`.
4. Enter System Manager credentials.
5. Click `Create Backup`.

The manual backup writes to the same backup folder:

```text
C:\Program Files\SPCstar\backups
```

## Restore

Inside SPC-Star, restore testing is available under:

```text
Setup > Archive > Database Test / Restore
```

`Restore Latest Backup` restores the newest `.db` file from:

```text
C:\Program Files\SPCstar\backups
```

Before restoring, SPC-Star saves the current database into quarantine:

```text
C:\Program Files\SPCstar\quarantine
```

Quarantine file naming format:

```text
MMDDYY Quarantine HHMM.db
```

If SPC-Star cannot run and IT must restore manually:

1. Stop the `SPC-Star` Windows Service.
2. Copy the current suspect database from `C:\Program Files\SPCstar\data\spcstar.db` into `C:\Program Files\SPCstar\quarantine`.
3. Copy the selected known-good backup from `C:\Program Files\SPCstar\backups` to `C:\Program Files\SPCstar\data\spcstar.db`.
4. Start the `SPC-Star` Windows Service.
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
C:\Program Files\SPCstar\data\archives
```

Archive should be copied into the company's normal local retention location for the seven-year record hold process.

## Initial System Account

When the database is empty, SPC-Star seeds one protected system manager account:

- Username: `Archon`
- Password: `archon`
- Role: `GOD` / System Manager permission level

The password should be changed after server setup.

## Current Inspection Behavior

- Operators select the shift they are actually working at login.
- Operators select job, machine, part, operation, and inspection phase before entering measurements.
- Attribute inspections are entered as one lot disposition, Accept or Reject.
- The machine counter is required when submitting an inspection and can be corrected later from job history by authorized users.
- The inspection capability panel uses saved job/machine/operation/phase history for the loaded variable, not only the current browser entry.
- The trend chart is filtered to the loaded job, machine, part, process, operation, phase, and inspection item.
- Full lockouts are created for out-of-spec measured values and rejected attributes.
- Process drift appears beside the inspection variable as a warning and is recorded for history/top-issue review, but it does not create the full lock screen.
- If an inspection fails partway through, the lock screen includes the operator/line-tech workflow to end the current inspection as failed and start a fresh inspection for the same job context.

## Quick Health Checks

Local health check on the server:

```text
http://localhost:5000/health
```

Network app URL:

```text
http://SERVER-NAME:5000/
```

Preferred operator-facing URL after HTTPS is configured:

```text
https://spcstar.bihler.com/
```

If the app does not respond:

1. Check that the `SPC-Star` Windows Service is running.
2. Check `C:\Program Files\SPCstar\logs\spcstar.log`.
3. Confirm inbound TCP port `5000` is allowed on the server firewall.
4. Confirm the live database exists at `C:\Program Files\SPCstar\data\spcstar.db`.
5. Confirm the app was not started only from a temporary Administrator PowerShell window. Closing that window must not be what keeps SPC-Star alive; the Windows Service should own the running process.

## Serial Gauge Browser Requirement

SPC-Star supports keyboard-style USB gauges automatically when the gauge types into the focused measurement field.

For machines configured as `Serial text gauge`, SPC-Star uses the browser Web Serial API. Web Serial has extra browser restrictions:

- Use desktop Chrome or desktop Microsoft Edge.
- The page must be opened in a secure browser context.
- `http://localhost:5000` can work when testing directly on the server.
- `http://SERVER-NAME:5000` from another workstation is usually not considered secure by the browser.

Current pilot issue observed:

```text
http://spcstar.bihler.com:5000
```

The page loads, but Chrome/Edge treats it as plain HTTP from a network host. In that state the browser blocks serial-port access before SPC-Star can use the ECNT machine's RS-232 settings.

Recommended IT solution:

```text
https://spcstar.bihler.com/
```

Recommended architecture:

```text
Operator workstation Chrome/Edge
    -> https://spcstar.bihler.com/
    -> trusted internal certificate / HTTPS endpoint
    -> reverse proxy on the SPC-Star server
    -> http://localhost:5000
    -> SPC-Star Windows Service / app
```

In this setup, SPC-Star can continue running internally on port `5000`. Operators should use the HTTPS URL, not the plain `http://spcstar.bihler.com:5000` URL.

HTTPS requirements:

- The certificate must be trusted by the shop-floor workstations.
- The hostname on the certificate should match `spcstar.bihler.com`.
- Desktop Chrome or desktop Microsoft Edge should be used for serial-gauge workstations.
- IT should verify that `navigator.serial` is available from the HTTPS SPC-Star page.
- IT should confirm the ECNT workstation can see the RS-232/USB serial port in Windows Device Manager as a COM port.
- If WinSPC is running on the same workstation, IT/engineering should confirm whether WinSPC is holding the COM port open. Usually only one application can actively read the same serial port at a time.

If an operator sees this message:

```text
Serial device connection is not available here.
```

then the workstation/browser is not exposing Web Serial to SPC-Star. For pilot use from shop-floor computers, IT should plan either:

1. Serve SPC-Star over HTTPS using a certificate trusted by the shop-floor computers, then use the HTTPS URL in Chrome or Edge.
2. Use gauges that act like keyboard input where possible.
3. If HTTPS is not available yet, test serial-gauge behavior only from `localhost` on the server until IT provides a secure local URL.

The baud rate setting, such as `9600`, only matters after the browser exposes serial access. If Web Serial is unavailable, the baud rate has not been reached yet.

ECNT / RS-232 information to collect from engineering:

- COM port number used by the ECNT workstation, such as `COM3`.
- Baud rate, currently expected to be `9600`.
- Data bits, parity, stop bits, and flow control.
- Example raw output from the ECNT device.
- Whether the device sends a newline/Enter after each reading.
- Whether WinSPC or any other software must be closed before SPC-Star can connect.


using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record CreateManualBackupRequest(string UserName, string Password);

public sealed record ClearHistoryDataRequest(string UserName, string Password, string ConfirmationText);

public sealed record RestoreLatestBackupRequest(string UserName, string Password, string ConfirmationText);

public sealed record BackupResultDto(
    DateTimeOffset BackedUpAt,
    string BackupFileName,
    string BackupPath,
    string DownloadPath);

public sealed record HistoryDataClearResultDto(
    DateTimeOffset ClearedAt,
    string QuarantineFileName,
    string QuarantinePath);

public sealed record DatabaseRestoreResultDto(
    DateTimeOffset RestoredAt,
    string RestoredBackupFileName,
    string RestoredBackupPath,
    string QuarantineFileName,
    string QuarantinePath);

public sealed class BackupService(
    ISpcRepository repository,
    IRepositoryPersistence persistence,
    CredentialService credentialService,
    PermissionService permissionService,
    string backupDirectory,
    string quarantineDirectory)
{
    public ServiceResult<BackupResultDto> CreateManualBackup(CreateManualBackupRequest request)
    {
        var authError = ValidateGodCredentials(request.UserName, request.Password, "Backup");
        if (authError is not null)
        {
            return ServiceResult<BackupResultDto>.Fail(authError);
        }

        Directory.CreateDirectory(backupDirectory);
        var backedUpAt = DateTimeOffset.Now;
        var fileName = UniqueBackupFileName(backedUpAt);
        var backupPath = Path.Combine(backupDirectory, fileName);
        persistence.BackupTo(backupPath);

        return ServiceResult<BackupResultDto>.Ok(new BackupResultDto(
            backedUpAt,
            fileName,
            backupPath,
            $"/setup/backups/files/{Uri.EscapeDataString(fileName)}"));
    }

    public ServiceResult<HistoryDataClearResultDto> ClearHistoryData(ClearHistoryDataRequest request)
    {
        var authError = ValidateGodCredentials(request.UserName, request.Password, "Clear database");
        if (authError is not null)
        {
            return ServiceResult<HistoryDataClearResultDto>.Fail(authError);
        }

        if (!request.ConfirmationText.Equals("CLEAR HISTORY DATA", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<HistoryDataClearResultDto>.Fail("Type CLEAR HISTORY DATA to confirm this action.");
        }

        var clearedAt = DateTimeOffset.Now;
        var quarantineFileName = UniqueQuarantineFileName(clearedAt);
        var quarantinePath = Path.Combine(quarantineDirectory, quarantineFileName);
        Directory.CreateDirectory(quarantineDirectory);
        persistence.BackupTo(quarantinePath);

        ClearOperationalData();
        persistence.SaveChanges();

        return ServiceResult<HistoryDataClearResultDto>.Ok(new HistoryDataClearResultDto(clearedAt, quarantineFileName, quarantinePath));
    }

    public ServiceResult<DatabaseRestoreResultDto> RestoreLatestBackup(RestoreLatestBackupRequest request)
    {
        var authError = ValidateGodCredentials(request.UserName, request.Password, "Restore");
        if (authError is not null)
        {
            return ServiceResult<DatabaseRestoreResultDto>.Fail(authError);
        }

        if (!request.ConfirmationText.Equals("RESTORE", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<DatabaseRestoreResultDto>.Fail("Type RESTORE to confirm this action.");
        }

        var latestBackup = Directory.Exists(backupDirectory)
            ? Directory.GetFiles(backupDirectory, "*.db")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (latestBackup is null)
        {
            return ServiceResult<DatabaseRestoreResultDto>.Fail("No backup files were found.");
        }

        var restoredAt = DateTimeOffset.Now;
        var quarantineFileName = UniqueQuarantineFileName(restoredAt);
        var quarantinePath = Path.Combine(quarantineDirectory, quarantineFileName);
        Directory.CreateDirectory(quarantineDirectory);
        persistence.BackupTo(quarantinePath);
        persistence.RestoreFrom(latestBackup.FullName);

        return ServiceResult<DatabaseRestoreResultDto>.Ok(new DatabaseRestoreResultDto(
            restoredAt,
            latestBackup.Name,
            latestBackup.FullName,
            quarantineFileName,
            quarantinePath));
    }

    public string BackupPathFor(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        return Path.Combine(backupDirectory, safeName);
    }

    private string UniqueBackupFileName(DateTimeOffset backedUpAt)
    {
        var baseName = $"{backedUpAt:MMddyy} Backup {backedUpAt:HHmm}";
        var fileName = $"{baseName}.db";
        if (!File.Exists(Path.Combine(backupDirectory, fileName)))
        {
            return fileName;
        }

        return $"{baseName} {backedUpAt:ss}.db";
    }

    private string UniqueQuarantineFileName(DateTimeOffset quarantinedAt)
    {
        var baseName = $"{quarantinedAt:MMddyy} Quarantine {quarantinedAt:HHmm}";
        var fileName = $"{baseName}.db";
        if (!File.Exists(Path.Combine(quarantineDirectory, fileName)))
        {
            return fileName;
        }

        return $"{baseName} {quarantinedAt:ss}.db";
    }

    private string? ValidateGodCredentials(string userName, string password, string actionName)
    {
        var trimmedUserName = userName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedUserName) || string.IsNullOrWhiteSpace(password))
        {
            return $"{actionName} requires valid Archon credentials.";
        }

        if (!credentialService.ValidateCredential(trimmedUserName, password) ||
            !permissionService.UserHasPermission(trimmedUserName, PermissionNames.CanUseGodMode))
        {
            return $"{actionName} requires valid Archon credentials.";
        }

        return null;
    }

    private void ClearOperationalData()
    {
        repository.Jobs.Clear();
        repository.Measurements.Clear();
        repository.MeasurementEditAudits.Clear();
        repository.JobNotes.Clear();
        repository.JobPhaseCompletions.Clear();
        repository.JobTags.Clear();
        repository.Alerts.Clear();
        repository.RuleViolations.Clear();
        repository.AlertOverrides.Clear();
        repository.MaterialChanges.Clear();
    }
}

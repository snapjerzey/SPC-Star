using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record CreateManualBackupRequest(string UserName, string Password);

public sealed record BackupResultDto(
    DateTimeOffset BackedUpAt,
    string BackupFileName,
    string BackupPath,
    string DownloadPath);

public sealed class BackupService(
    IRepositoryPersistence persistence,
    CredentialService credentialService,
    PermissionService permissionService,
    string backupDirectory)
{
    public ServiceResult<BackupResultDto> CreateManualBackup(CreateManualBackupRequest request)
    {
        var userName = request.UserName.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(request.Password))
        {
            return ServiceResult<BackupResultDto>.Fail("Backup requires valid GOD credentials.");
        }

        if (!credentialService.ValidateCredential(userName, request.Password) ||
            !permissionService.UserHasPermission(userName, PermissionNames.CanUseGodMode))
        {
            return ServiceResult<BackupResultDto>.Fail("Backup requires valid GOD credentials.");
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
}

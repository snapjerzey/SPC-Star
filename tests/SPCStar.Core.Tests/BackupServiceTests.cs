using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public void CreateManualBackup_RequiresGodCredentials()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        TestSeedData.SeedUsers(repository);
        var persistence = new FakePersistence();
        var service = new BackupService(
            persistence,
            new CredentialService(repository),
            new PermissionService(repository),
            Path.Combine(Path.GetTempPath(), $"spcstar-backup-{Guid.NewGuid():N}"));

        var result = service.CreateManualBackup(new CreateManualBackupRequest("qa1", "qa1"));

        Assert.False(result.Succeeded);
        Assert.False(persistence.BackupWasCalled);
    }

    [Fact]
    public void CreateManualBackup_CreatesDatedBackupFile()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        var backupDirectory = Path.Combine(Path.GetTempPath(), $"spcstar-backup-{Guid.NewGuid():N}");
        var persistence = new FakePersistence();
        var service = new BackupService(
            persistence,
            new CredentialService(repository),
            new PermissionService(repository),
            backupDirectory);

        var result = service.CreateManualBackup(new CreateManualBackupRequest("Archon", "archon"));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.NotNull(result.Value);
        Assert.True(File.Exists(result.Value!.BackupPath));
        Assert.Contains(" Backup ", result.Value.BackupFileName);
        Assert.EndsWith(".db", result.Value.BackupFileName);
        Assert.Equal(result.Value.BackupPath, persistence.LastBackupPath);
    }

    private sealed class FakePersistence : InMemorySpcRepository, IRepositoryPersistence
    {
        public bool BackupWasCalled { get; private set; }
        public string LastBackupPath { get; private set; } = "";
        public string StoragePath { get; } = "fake.db";

        public void SaveChanges()
        {
        }

        public void BackupTo(string backupPath)
        {
            BackupWasCalled = true;
            LastBackupPath = backupPath;
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.WriteAllText(backupPath, "backup");
        }
    }
}

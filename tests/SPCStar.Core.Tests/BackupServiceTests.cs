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
            repository,
            persistence,
            new CredentialService(repository),
            new PermissionService(repository),
            Path.Combine(Path.GetTempPath(), $"spcstar-backup-{Guid.NewGuid():N}"),
            Path.Combine(Path.GetTempPath(), $"spcstar-quarantine-{Guid.NewGuid():N}"));

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
            repository,
            persistence,
            new CredentialService(repository),
            new PermissionService(repository),
            backupDirectory,
            Path.Combine(Path.GetTempPath(), $"spcstar-quarantine-{Guid.NewGuid():N}"));

        var result = service.CreateManualBackup(new CreateManualBackupRequest("Archon", "archon"));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.NotNull(result.Value);
        Assert.True(File.Exists(result.Value!.BackupPath));
        Assert.Contains(" Backup ", result.Value.BackupFileName);
        Assert.EndsWith(".db", result.Value.BackupFileName);
        Assert.Equal(result.Value.BackupPath, persistence.LastBackupPath);
    }

    [Fact]
    public void ClearDatabase_RemovesHistoryButKeepsSetupData()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        repository.Resources.Add(new ResourceMachine { ResourceId = "M1", Description = "Machine 1" });
        repository.Measurements.Add(new InspectionMeasurement
        {
            JobNum = "J1",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "M1",
            CharacteristicName = "Diameter",
            Value = 5m,
            Timestamp = DateTimeOffset.UtcNow,
            OperatorUserId = "Archon",
            SubmittedAt = DateTimeOffset.UtcNow
        });
        repository.Jobs.Add(new Job { JobNum = "J1", PartNum = "P100" });
        var service = new BackupService(
            repository,
            new FakePersistence(),
            new CredentialService(repository),
            new PermissionService(repository),
            Path.Combine(Path.GetTempPath(), $"spcstar-backup-{Guid.NewGuid():N}"),
            Path.Combine(Path.GetTempPath(), $"spcstar-quarantine-{Guid.NewGuid():N}"));

        var result = service.ClearHistoryData(new ClearHistoryDataRequest("Archon", "archon", "CLEAR HISTORY DATA"));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Contains(repository.Users, user => user.UserName == "Archon");
        Assert.Contains(repository.Resources, resource => resource.ResourceId == "M1");
        Assert.Contains(repository.Parts, part => part.PartNum == "P100");
        Assert.Contains(repository.InspectionPlans, plan => plan.SampleSize > 0);
        Assert.Empty(repository.Measurements);
        Assert.Empty(repository.Jobs);
    }

    [Fact]
    public void RestoreLatestBackup_UsesNewestBackupAndQuarantinesCurrentDatabase()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        var backupDirectory = Path.Combine(Path.GetTempPath(), $"spcstar-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);
        var olderBackup = Path.Combine(backupDirectory, "081226 Backup 0800.db");
        var newestBackup = Path.Combine(backupDirectory, "081226 Backup 0900.db");
        File.WriteAllText(olderBackup, "old");
        File.WriteAllText(newestBackup, "new");
        File.SetLastWriteTimeUtc(olderBackup, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(newestBackup, DateTime.UtcNow);
        var persistence = new FakePersistence();
        var service = new BackupService(
            repository,
            persistence,
            new CredentialService(repository),
            new PermissionService(repository),
            backupDirectory,
            Path.Combine(Path.GetTempPath(), $"spcstar-quarantine-{Guid.NewGuid():N}"));

        var result = service.RestoreLatestBackup(new RestoreLatestBackupRequest("Archon", "archon", "RESTORE"));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(newestBackup, persistence.LastRestorePath);
        Assert.True(persistence.BackupWasCalled);
        Assert.Contains(" Quarantine ", result.Value!.QuarantineFileName);
    }

    private sealed class FakePersistence : InMemorySpcRepository, IRepositoryPersistence
    {
        public bool BackupWasCalled { get; private set; }
        public string LastBackupPath { get; private set; } = "";
        public string LastRestorePath { get; private set; } = "";
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

        public void RestoreFrom(string backupPath)
        {
            LastRestorePath = backupPath;
        }
    }
}

using System.Text.Json;
using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class ArchiveServiceTests
{
    [Fact]
    public void Preview_CountsRecordsBeforeCutoff()
    {
        var repository = BuildRepository();
        var service = BuildService(repository);
        var cutoff = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        var preview = service.Preview(new ArchivePreviewRequest(cutoff));

        Assert.Equal(1, preview.Counts.Measurements);
        Assert.Equal(1, preview.Counts.JobNotes);
        Assert.Equal(1, preview.Counts.MaterialChanges);
        Assert.Equal(1, preview.Counts.Alerts);
        Assert.Equal(1, preview.Counts.AlertOverrides);
        Assert.Equal(1, preview.Counts.RuleViolations);
        Assert.Equal(1, preview.Counts.MeasurementEditAudits);
        Assert.Equal(0, preview.ActiveLocksBeforeCutoff);
    }

    [Fact]
    public void Create_WritesArchiveAndRemovesArchivedRecords()
    {
        var repository = BuildRepository();
        var archiveDirectory = Path.Combine(Path.GetTempPath(), $"spcstar-archive-{Guid.NewGuid():N}");
        var service = BuildService(repository, archiveDirectory);

        var result = service.Create(new CreateArchiveRequest(
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "Archon",
            "archon",
            "ARCHIVE"));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.NotNull(result.Value);
        Assert.True(File.Exists(result.Value!.ArchivePath));
        Assert.DoesNotContain(repository.Measurements, item => item.JobNum == "JOLD");
        Assert.Contains(repository.Measurements, item => item.JobNum == "JNEW");

        var archiveJson = File.ReadAllText(result.Value.ArchivePath);
        using var document = JsonDocument.Parse(archiveJson);
        Assert.Equal("Archon", document.RootElement.GetProperty("ArchivedByUserId").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("Counts").GetProperty("Measurements").GetInt32());
    }

    [Fact]
    public void Create_RejectsNonGodCredentials()
    {
        var repository = BuildRepository();
        TestSeedData.SeedUsers(repository);
        var service = BuildService(repository);

        var result = service.Create(new CreateArchiveRequest(
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "qa1",
            "qa1",
            "ARCHIVE"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("GOD credentials", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(repository.Measurements, item => item.JobNum == "JOLD");
    }

    [Fact]
    public void Create_BlocksWhenOldActiveLockExists()
    {
        var repository = BuildRepository();
        repository.Alerts.Single().Status = AlertStatus.Active;
        var service = BuildService(repository);

        var result = service.Create(new CreateArchiveRequest(
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "Archon",
            "archon",
            "ARCHIVE"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("active lock", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(repository.Measurements, item => item.JobNum == "JOLD");
    }

    private static InMemorySpcRepository BuildRepository()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        var oldMeasurement = new InspectionMeasurement
        {
            JobNum = "JOLD",
            PartNum = "P1",
            ProcessCode = "Op",
            OperationSeq = 10,
            ResourceId = "M1",
            CharacteristicName = "Length",
            Value = 1m,
            Timestamp = DateTimeOffset.Parse("2025-12-01T00:00:00Z"),
            OperatorUserId = "operator",
            SubmittedAt = DateTimeOffset.Parse("2025-12-01T00:00:01Z")
        };
        var newMeasurement = new InspectionMeasurement
        {
            JobNum = "JNEW",
            PartNum = "P1",
            ProcessCode = "Op",
            OperationSeq = 10,
            ResourceId = "M1",
            CharacteristicName = "Length",
            Value = 2m,
            Timestamp = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            OperatorUserId = "operator",
            SubmittedAt = DateTimeOffset.Parse("2026-02-01T00:00:01Z")
        };
        var alert = new ProcessAlert
        {
            JobNum = "JOLD",
            PartNum = "P1",
            ResourceId = "M1",
            CharacteristicName = "Length",
            OperatorUserId = "operator",
            RuleTriggered = RuleTriggered.SpecLimitViolation,
            LockedAt = DateTimeOffset.Parse("2025-12-01T00:00:02Z"),
            Status = AlertStatus.Overridden
        };

        repository.Measurements.AddRange([oldMeasurement, newMeasurement]);
        repository.MeasurementEditAudits.Add(new MeasurementEditAudit
        {
            MeasurementId = oldMeasurement.Id,
            JobNum = "JOLD",
            PartNum = "P1",
            ResourceId = "M1",
            CharacteristicName = "Length",
            OldValue = 0m,
            NewValue = 1m,
            OldInspectionPhase = "Setup",
            NewInspectionPhase = "Setup",
            EditedByUserId = "Archon",
            EditedAt = DateTimeOffset.Parse("2025-12-01T00:00:03Z")
        });
        repository.JobNotes.Add(new JobNote
        {
            JobNum = "JOLD",
            PartNum = "P1",
            ResourceId = "M1",
            OperatorUserId = "operator",
            NoteText = "old note",
            Timestamp = DateTimeOffset.Parse("2025-12-01T00:00:04Z")
        });
        repository.Alerts.Add(alert);
        repository.RuleViolations.Add(new RuleViolation
        {
            AlertId = alert.Id,
            RuleTriggered = RuleTriggered.SpecLimitViolation,
            DetectedAt = DateTimeOffset.Parse("2025-12-01T00:00:05Z")
        });
        repository.AlertOverrides.Add(new AlertOverride
        {
            AlertId = alert.Id,
            OperatorUserId = "operator",
            OverrideUserId = "Archon",
            OverrideRole = RoleNames.GOD,
            JobNum = "JOLD",
            PartNum = "P1",
            ResourceId = "M1",
            CharacteristicName = "Length",
            RuleTriggered = RuleTriggered.SpecLimitViolation,
            CauseText = "Cause",
            SolutionText = "Solution",
            LockedAt = DateTimeOffset.Parse("2025-12-01T00:00:02Z"),
            UnlockedAt = DateTimeOffset.Parse("2025-12-01T00:00:06Z"),
            SubmittedAt = DateTimeOffset.Parse("2025-12-01T00:00:07Z")
        });
        repository.MaterialChanges.Add(new MaterialChangeLog
        {
            JobNum = "JOLD",
            PartNum = "P1",
            MaterialPartNum = "MAT1",
            OldLotNum = "",
            NewLotNum = "LOT1",
            ResourceId = "M1",
            OperatorUserId = "operator",
            Timestamp = DateTimeOffset.Parse("2025-12-01T00:00:08Z"),
            Reason = "Lot change",
            SubmittedAt = DateTimeOffset.Parse("2025-12-01T00:00:09Z")
        });

        return repository;
    }

    private static ArchiveService BuildService(InMemorySpcRepository repository, string? archiveDirectory = null)
    {
        return new ArchiveService(
            repository,
            new CredentialService(repository),
            new PermissionService(repository),
            archiveDirectory ?? Path.Combine(Path.GetTempPath(), $"spcstar-archive-{Guid.NewGuid():N}"));
    }
}

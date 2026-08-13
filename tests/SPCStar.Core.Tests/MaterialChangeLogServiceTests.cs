using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class MaterialChangeLogServiceTests
{
    [Fact]
    public void Record_RequiresTraceabilityFields()
    {
        var service = new MaterialChangeLogService(new InMemorySpcRepository());

        var result = service.Record(new MaterialChangeLogEntry(
            "",
            "P100",
            "RESIN-A",
            "LOT1",
            "LOT2",
            null,
            "PRESS1",
            "operator1",
            DateTimeOffset.UtcNow,
            "Material change"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("JobNum is required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Record_StoresMaterialLotChange()
    {
        var repository = new InMemorySpcRepository();
        var service = new MaterialChangeLogService(repository);

        var result = service.Record(new MaterialChangeLogEntry(
            "J100",
            "P100",
            "RESIN-A",
            "LOT1",
            "LOT2",
            500m,
            "PRESS1",
            "operator1",
            DateTimeOffset.UtcNow,
            "Loaded next lot"));

        Assert.True(result.Succeeded);
        Assert.Single(repository.MaterialChanges);
        Assert.Equal("LOT2", repository.MaterialChanges.Single().NewLotNum);
    }

    [Fact]
    public void Record_AllowsNewLotWithoutPreviousLot()
    {
        var repository = new InMemorySpcRepository();
        var service = new MaterialChangeLogService(repository);

        var result = service.Record(new MaterialChangeLogEntry(
            "J100",
            "P100",
            "RESIN-A",
            "",
            "LOT2",
            null,
            "PRESS1",
            "operator1",
            DateTimeOffset.UtcNow,
            "Material issue at job start"));

        Assert.True(result.Succeeded);
        Assert.Equal("", repository.MaterialChanges.Single().OldLotNum);
        Assert.Equal("LOT2", repository.MaterialChanges.Single().NewLotNum);
    }

    [Fact]
    public void Record_ReturnsExistingMaterialChange_WhenOfflineRecordIsRetried()
    {
        var repository = new InMemorySpcRepository();
        var service = new MaterialChangeLogService(repository);
        var entry = new MaterialChangeLogEntry(
            "J100",
            "P100",
            "RESIN-A",
            "LOT1",
            "LOT2",
            500m,
            "PRESS1",
            "operator1",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "Loaded next lot",
            "tablet-press1",
            "material-001",
            DateTimeOffset.Parse("2026-01-01T00:05:00Z"));

        var first = service.Record(entry);
        var retry = service.Record(entry);

        Assert.True(first.Succeeded);
        Assert.True(retry.Succeeded);
        Assert.Single(repository.MaterialChanges);
        Assert.Equal(first.Value!.Id, retry.Value!.Id);
        Assert.Equal("tablet-press1", retry.Value.DeviceId);
        Assert.Equal("material-001", retry.Value.ClientRecordId);
    }

    [Fact]
    public void Record_RejectsLotNumbersLongerThanTwentyCharacters()
    {
        var service = new MaterialChangeLogService(new InMemorySpcRepository());

        var result = service.Record(new MaterialChangeLogEntry(
            "J100",
            "P100",
            "RESIN-A",
            "",
            "123456789012345678901",
            null,
            "PRESS1",
            "operator1",
            DateTimeOffset.UtcNow,
            "Material change"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("cannot exceed 20", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateLot_ChangesLotAndAddsTraceabilityNote()
    {
        var repository = new InMemorySpcRepository();
        var god = new Role { Name = RoleNames.GOD };
        god.Permissions.Add(PermissionNames.CanUseGodMode);
        repository.Roles.Add(god);
        repository.Users.Add(new User { UserName = "Archon", PasswordHash = "hash", PasswordSalt = "salt", Roles = { god } });
        var service = new MaterialChangeLogService(repository, new PermissionService(repository));
        var recorded = service.Record(new MaterialChangeLogEntry(
            "J100",
            "P100",
            "RESIN-A",
            "",
            "LOT2",
            null,
            "PRESS1",
            "operator1",
            DateTimeOffset.UtcNow,
            "Material change"));

        var result = service.UpdateLot(recorded.Value!.Id, new UpdateMaterialLotRequest("LOT3", "Archon"));

        Assert.True(result.Succeeded);
        Assert.Equal("LOT3", repository.MaterialChanges.Single().NewLotNum);
        var note = Assert.Single(repository.JobNotes);
        Assert.Equal("Archon", note.OperatorUserId);
        Assert.Contains("from LOT2 to LOT3", note.NoteText);
    }
}

using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class InspectionAndOverrideTests
{
    [Fact]
    public void EnterMeasurement_CreatesAlertAndLocksFurtherEntry_WhenRuleViolationOccurs()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var ok = service.EnterMeasurement(Entry(13.5m));
        var locked = service.EnterMeasurement(Entry(10m, minutes: 1));

        Assert.True(ok.Succeeded);
        Assert.Single(repository.Alerts);
        Assert.False(locked.Succeeded);
        Assert.Contains(locked.Errors, error => error.Contains("locked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnterMeasurement_RejectsUnknownInspectionTarget()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(5m) with { CharacteristicName = "Unknown" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("No configured inspection characteristic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnterMeasurement_ReturnsExistingMeasurement_WhenOfflineRecordIsRetried()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        var entry = Entry(10m) with
        {
            DeviceId = "tablet-press1",
            ClientRecordId = "measurement-001",
            SubmittedAt = DateTimeOffset.Parse("2026-01-01T00:05:00Z")
        };

        var first = service.EnterMeasurement(entry);
        var retry = service.EnterMeasurement(entry);

        Assert.True(first.Succeeded);
        Assert.True(retry.Succeeded);
        Assert.Single(repository.Measurements);
        Assert.Equal(first.Value!.Id, retry.Value!.Id);
        Assert.Equal("tablet-press1", retry.Value.DeviceId);
        Assert.Equal("measurement-001", retry.Value.ClientRecordId);
    }

    [Fact]
    public void Override_RejectsOperator()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(alert.Id, "operator1", "operator1", "Cause", "Fix", null, DateTimeOffset.UtcNow));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Override_AllowsQaAndWritesAuditTrail()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(alert.Id, "qa1", "qa1", "Tool wear", "Changed tool", null, DateTimeOffset.UtcNow));

        Assert.True(result.Succeeded);
        Assert.Equal(AlertStatus.Overridden, alert.Status);
        Assert.Single(repository.AlertOverrides);
    }

    [Fact]
    public void Override_AllowsAdmin()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(alert.Id, "admin1", "admin1", "Tool wear", "Adjusted process", null, DateTimeOffset.UtcNow));

        Assert.True(result.Succeeded);
        Assert.Equal(AlertStatus.Overridden, alert.Status);
        Assert.Equal(RoleNames.Admin, result.Value!.OverrideRole);
    }

    [Fact]
    public void Override_RequiresGodBypassReason()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(alert.Id, "god1", "god1", "Emergency", "Released", null, DateTimeOffset.UtcNow));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("required for GOD", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Override_RejectsInvalidCredentials()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(alert.Id, "qa1", "wrong", "Tool wear", "Changed tool", null, DateTimeOffset.UtcNow));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("Invalid override credentials", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Override_ReturnsExistingAudit_WhenOfflineRecordIsRetried()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);
        var request = new AlertOverrideRequest(
            alert.Id,
            "qa1",
            "qa1",
            "Tool wear",
            "Changed tool",
            null,
            DateTimeOffset.Parse("2026-01-01T00:10:00Z"),
            "tablet-qa1",
            "override-001",
            DateTimeOffset.Parse("2026-01-01T00:11:00Z"));

        var first = service.Override(request);
        var retry = service.Override(request);

        Assert.True(first.Succeeded);
        Assert.True(retry.Succeeded);
        Assert.Single(repository.AlertOverrides);
        Assert.Equal(first.Value!.Id, retry.Value!.Id);
        Assert.Equal("tablet-qa1", retry.Value.DeviceId);
        Assert.Equal("override-001", retry.Value.ClientRecordId);
    }

    private static InMemorySpcRepository RepositoryWithSecurityAndLimits()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        repository.ControlLimits.Add(new ControlLimitSet
        {
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            CharacteristicName = "Diameter",
            CenterLine = 10m,
            Lcl = 7m,
            Ucl = 13m
        });
        return repository;
    }

    private static InspectionMeasurementEntry Entry(decimal value, int minutes = 0)
    {
        return new InspectionMeasurementEntry(
            "J100",
            "P100",
            "MOLD",
            10,
            "PRESS1",
            "Diameter",
            value,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddMinutes(minutes),
            "operator1");
    }

    private static ProcessAlert AddAlert(InMemorySpcRepository repository)
    {
        var alert = new ProcessAlert
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            OperatorUserId = "operator1",
            RuleTriggered = RuleTriggered.OnePointBeyondControlLimit,
            LockedAt = DateTimeOffset.UtcNow
        };
        repository.Alerts.Add(alert);
        return alert;
    }

    private static AlertOverrideService OverrideService(InMemorySpcRepository repository)
    {
        return new AlertOverrideService(repository, new PermissionService(repository), new CredentialService(repository));
    }
}

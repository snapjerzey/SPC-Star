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
    public void EnterMeasurement_CreatesSpecAlert_WhenSpecLimitOnlyRuleIsSelected()
    {
        var repository = RepositoryWithSecurityAndLimits();
        SetRuleSet(repository, "Diameter", "SpecLimitOnly");
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(6m));

        Assert.True(result.Succeeded);
        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(RuleTriggered.SpecLimitViolation, alert.RuleTriggered);
    }

    [Fact]
    public void EnterMeasurement_DoesNotCreateMeasuredAlert_WhenNoAutomaticRuleIsSelected()
    {
        var repository = RepositoryWithSecurityAndLimits();
        SetRuleSet(repository, "Diameter", "None");
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(13.5m));

        Assert.True(result.Succeeded);
        Assert.Empty(repository.Alerts);
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
    public void EnterMeasurement_CreatesJobForOperatorEnteredJobNumber()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.Jobs.Clear();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m) with { JobNum = "J200" });

        Assert.True(result.Succeeded);
        var job = Assert.Single(repository.Jobs);
        Assert.Equal("J200", job.JobNum);
        Assert.Equal("P100", job.PartNum);
    }

    [Fact]
    public void EnterMeasurement_RejectsJobAlreadyAssignedToDifferentPart()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.Jobs.Clear();
        repository.Jobs.Add(new Job { JobNum = "J200", PartNum = "P999" });
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m) with { JobNum = "J200" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("already assigned", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(repository.Measurements);
    }

    [Fact]
    public void EnterMeasurement_AllowsLineTechToInspect()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m) with { OperatorUserId = "linetech1" });

        Assert.True(result.Succeeded);
        Assert.Equal("linetech1", repository.Measurements.Single().OperatorUserId);
    }

    [Fact]
    public void EnterMeasurement_RejectsUserWithoutInspectionPermission()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m) with { OperatorUserId = "qa1" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("not authorized", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(repository.Measurements);
    }

    [Fact]
    public void EnterMeasurement_CreatesLock_WhenAttributeIsRejected()
    {
        var repository = RepositoryWithSecurityAndLimits();
        AddAttributeCharacteristic(repository);
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(0m) with { CharacteristicName = "Comparator profile" });
        var locked = service.EnterMeasurement(Entry(1m, minutes: 1) with { CharacteristicName = "Comparator profile" });

        Assert.True(result.Succeeded);
        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(RuleTriggered.AttributeRejected, alert.RuleTriggered);
        Assert.False(locked.Succeeded);
        Assert.Contains(locked.Errors, error => error.Contains("locked", StringComparison.OrdinalIgnoreCase));
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

    private static void AddAttributeCharacteristic(InMemorySpcRepository repository)
    {
        var part = repository.Parts.Single(part => part.PartNum == "P100");
        var process = repository.Processes.Single(process => process.ProcessCode == "MOLD");
        var operation = repository.Operations.Single(operation =>
            operation.PartId == part.Id &&
            operation.ProcessId == process.Id &&
            operation.OperationSeq == 10);
        var characteristic = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Comparator profile",
            Type = CharacteristicType.Attribute,
            UnitOfMeasure = "Accept/Reject",
            IsRequiredForCoa = true
        };
        repository.Characteristics.Add(characteristic);
        repository.SpecLimits.Add(new SpecLimit { CharacteristicId = characteristic.Id, Nominal = 1m, Lsl = 0m, Usl = 1m });
        repository.InspectionPlans.Add(new InspectionPlan
        {
            CharacteristicId = characteristic.Id,
            SampleSize = 1,
            AlertRuleSet = "WesternElectric",
            Frequency = new InspectionFrequency { Type = FrequencyType.Quantity, Value = 10000, Unit = FrequencyUnit.Pieces }
        });
    }

    private static void SetRuleSet(InMemorySpcRepository repository, string characteristicName, string ruleSet)
    {
        var characteristic = repository.Characteristics.Single(characteristic => characteristic.Name == characteristicName);
        var plan = repository.InspectionPlans.Single(plan => plan.CharacteristicId == characteristic.Id);
        plan.AlertRuleSet = ruleSet;
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

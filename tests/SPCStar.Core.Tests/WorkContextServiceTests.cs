using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class WorkContextServiceTests
{
    [Fact]
    public void Build_ReturnsPlanLimitsFrequencyAndActiveLock()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        TestSeedData.SeedUsers(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        repository.Measurements.Add(new InspectionMeasurement
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            Value = 5.1m,
            Timestamp = DateTimeOffset.Parse("2026-01-01T08:00:00Z"),
            OperatorUserId = "operator1",
            SubmittedAt = DateTimeOffset.Parse("2026-01-01T08:00:00Z")
        });
        repository.Alerts.Add(new ProcessAlert
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            OperatorUserId = "operator1",
            RuleTriggered = RuleTriggered.OnePointBeyondControlLimit,
            LockedAt = DateTimeOffset.Parse("2026-01-01T08:01:00Z")
        });
        var service = WorkContextService(repository);

        var context = service.Build(new WorkContextRequest(
            "J100",
            "P100",
            "MOLD",
            10,
            "PRESS1",
            "Diameter",
            DateTimeOffset.Parse("2026-01-01T08:45:00Z")));

        Assert.NotNull(context.InspectionPlan);
        Assert.Equal(4.5m, context.LowerSpecLimit);
        Assert.Equal(5.5m, context.UpperSpecLimit);
        Assert.Equal(4m, context.LowerControlLimit);
        Assert.Equal(6m, context.UpperControlLimit);
        Assert.Equal(InspectionDueStatus.Overdue, context.FrequencyStatus.Status);
        Assert.NotNull(context.ActiveLock);
        Assert.Single(context.RecentMeasurements);
    }

    [Fact]
    public void Build_ReturnsProcessWarningWithoutActiveLock()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        TestSeedData.SeedUsers(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        repository.Alerts.Add(new ProcessAlert
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            OperatorUserId = "operator1",
            RuleTriggered = RuleTriggered.OnePointBeyondControlLimit,
            Detail = "Value is above the upper control limit.",
            LockedAt = DateTimeOffset.Parse("2026-01-01T08:01:00Z"),
            Status = AlertStatus.Warning
        });
        var service = WorkContextService(repository);

        var context = service.Build(new WorkContextRequest(
            "J100",
            "P100",
            "MOLD",
            10,
            "PRESS1",
            "Diameter",
            DateTimeOffset.Parse("2026-01-01T08:45:00Z")));

        Assert.Null(context.ActiveLock);
        Assert.NotNull(context.ProcessWarning);
        Assert.Equal(RuleTriggered.OnePointBeyondControlLimit, context.ProcessWarning!.RuleTriggered);
    }

    [Fact]
    public void BuildBatch_ReturnsContextsInRequestedOrder()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        TestSeedData.SeedUsers(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        var service = WorkContextService(repository);

        var contexts = service.BuildBatch(new WorkContextBatchRequest(
            "J100",
            "P100",
            "MOLD",
            10,
            "PRESS1",
            ["Length", "Diameter"],
            "In Process",
            DateTimeOffset.Parse("2026-01-01T08:45:00Z")));

        Assert.Equal(["Length", "Diameter"], contexts.Select(context => context.Request.CharacteristicName).ToArray());
        Assert.Equal(41.5m, contexts[0].LowerSpecLimit);
        Assert.Equal(4.5m, contexts[1].LowerSpecLimit);
    }

    private static WorkContextService WorkContextService(InMemorySpcRepository repository)
    {
        var setupQuery = new SetupQueryService(repository);
        var frequencyService = new InspectionFrequencyService(repository);
        var chartService = new ChartDataService(repository);
        return new WorkContextService(repository, setupQuery, frequencyService, chartService);
    }
}

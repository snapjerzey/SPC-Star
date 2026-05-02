using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class OfflineSyncServiceTests
{
    [Fact]
    public void Sync_AcceptsQueuedMeasurementsAndReportsStableServerIdsOnRetry()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        var service = SyncService(repository);
        var measurement = new InspectionMeasurementEntry(
            "J100",
            "P100",
            "MOLD",
            10,
            "PRESS1",
            "Diameter",
            5.1m,
            DateTimeOffset.Parse("2026-01-01T08:00:00Z"),
            "operator1",
            "tablet-press1",
            "measurement-001",
            DateTimeOffset.Parse("2026-01-01T08:05:00Z"));

        var first = service.Sync(new OfflineSyncRequest([measurement], null, null));
        var retry = service.Sync(new OfflineSyncRequest([measurement], null, null));

        Assert.False(first.HasErrors);
        Assert.False(retry.HasErrors);
        Assert.Single(repository.Measurements);
        Assert.Equal(first.Accepted.Single().ServerRecordId, retry.Accepted.Single().ServerRecordId);
        Assert.Equal("tablet-press1", retry.Accepted.Single().DeviceId);
        Assert.Equal("measurement-001", retry.Accepted.Single().ClientRecordId);
    }

    private static OfflineSyncService SyncService(InMemorySpcRepository repository)
    {
        var measurementService = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        var materialChangeService = new MaterialChangeLogService(repository);
        var overrideService = new AlertOverrideService(
            repository,
            new PermissionService(repository),
            new CredentialService(repository));

        return new OfflineSyncService(measurementService, materialChangeService, overrideService);
    }
}

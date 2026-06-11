using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class SetupSnapshotServiceTests
{
    [Fact]
    public void GetSetupSnapshot_ReturnsTabletCacheData()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        SeedData.SeedSampleInspectionPlans(repository);

        var snapshot = new SetupQueryService(repository)
            .GetSetupSnapshot(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), snapshot.GeneratedAt);
        Assert.NotEmpty(snapshot.SetupVersion);
        Assert.Contains(snapshot.Parts, part => part.PartNum == "P100");
        Assert.Contains(snapshot.Processes, process => process.ProcessCode == "MOLD");
        Assert.Contains(snapshot.InspectionPlans, plan => plan.CharacteristicName == "Diameter");
        Assert.Contains(snapshot.InspectionPlans, plan => plan.CharacteristicName == "Length");
        Assert.Contains(snapshot.InspectionPlans, plan => plan.CharacteristicName == "Weight");
        Assert.Equal(3, snapshot.InspectionPlans.Count(plan => plan.PartNum == "P100" && plan.ProcessCode == "MOLD" && plan.OperationSeq == 10));
        Assert.Contains(snapshot.ControlLimits, limit => limit.ResourceIndependentKey() == "P100|MOLD|10|Diameter");
        Assert.Contains(snapshot.Jobs, job => job.JobNum == "J100");
        Assert.Contains(snapshot.Resources, resource => resource.ResourceId == "PRESS1");
    }

    [Fact]
    public void GetSetupSnapshot_ChangesVersion_WhenSetupChanges()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        var service = new SetupQueryService(repository);
        var before = service.GetSetupSnapshot();

        repository.Parts.Single(part => part.PartNum == "P100").Description = "Updated demo part";

        var after = service.GetSetupSnapshot();

        Assert.NotEqual(before.SetupVersion, after.SetupVersion);
    }
}

file static class ControlLimitSetupDtoExtensions
{
    public static string ResourceIndependentKey(this ControlLimitSetupDto dto)
    {
        return $"{dto.PartNum}|{dto.ProcessCode}|{dto.OperationSeq}|{dto.CharacteristicName}";
    }
}

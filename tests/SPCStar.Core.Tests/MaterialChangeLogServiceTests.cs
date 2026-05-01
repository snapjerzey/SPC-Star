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
}

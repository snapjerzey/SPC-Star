using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class JobTagServiceTests
{
    [Fact]
    public void Save_StoresAndUpdatesPersistentJobTags()
    {
        var repository = new InMemorySpcRepository();
        var service = new JobTagService(repository);

        var first = service.Save(new SaveJobTagsRequest(
            "J100",
            "P100",
            "PRESS1",
            "operator1",
            new Dictionary<string, string>
            {
                ["Wire Shipment"] = "WIRE-1",
                ["Coil Number"] = "COIL-1"
            },
            DateTimeOffset.Parse("2026-05-13T08:00:00Z")));
        var second = service.Save(new SaveJobTagsRequest(
            "J100",
            "P100",
            "PRESS2",
            "linetech1",
            new Dictionary<string, string>
            {
                ["Wire Shipment"] = "WIRE-2"
            },
            DateTimeOffset.Parse("2026-05-13T09:00:00Z")));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(2, repository.JobTags.Count);
        var wire = Assert.Single(repository.JobTags, tag => tag.TagName == "Wire Shipment");
        Assert.Equal("WIRE-2", wire.TagValue);
        Assert.Equal("PRESS2", wire.ResourceId);
        Assert.Equal("linetech1", wire.OperatorUserId);
        Assert.Contains(service.GetForJob("J100"), tag => tag.TagName == "Coil Number" && tag.TagValue == "COIL-1");
    }
}

using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class JobReviewServiceTests
{
    [Fact]
    public void UpdateMeasurement_ChangesValueAndPhase()
    {
        var repository = RepositoryWithMeasurement();
        var measurement = repository.Measurements.Single();
        var service = new JobReviewService(
            repository,
            new QaSummaryExportService(repository),
            new JobHistoryService(repository));

        var result = service.UpdateMeasurement(measurement.Id, new UpdateInspectionMeasurementRequest(5.25m, InspectionPhase: "Setup"));

        Assert.True(result.Succeeded);
        Assert.Equal(5.25m, measurement.Value);
        Assert.Equal("Setup", measurement.InspectionPhase);
        Assert.Equal(5.25m, result.Value!.Value);
    }

    private static InMemorySpcRepository RepositoryWithMeasurement()
    {
        var repository = new InMemorySpcRepository();
        repository.Measurements.Add(new InspectionMeasurement
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            Value = 5m,
            Timestamp = DateTimeOffset.Parse("2026-01-01T08:00:00Z"),
            OperatorUserId = "operator1"
        });
        return repository;
    }
}

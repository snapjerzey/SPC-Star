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
        var audit = Assert.Single(repository.MeasurementEditAudits);
        Assert.Equal(5m, audit.OldValue);
        Assert.Equal(5.25m, audit.NewValue);
        Assert.Equal("In Process", audit.OldInspectionPhase);
        Assert.Equal("Setup", audit.NewInspectionPhase);
    }

    [Fact]
    public void Build_FlagsOutOfSpecAndOutOfControlMeasurements()
    {
        var repository = RepositoryWithMeasurement();
        repository.Parts.Add(new Part { PartNum = "P100", Description = "Widget" });
        var process = new ManufacturingProcess { ProcessCode = "MOLD", Description = "Molding" };
        var part = repository.Parts.Single();
        repository.Processes.Add(process);
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        repository.Operations.Add(operation);
        var characteristic = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Diameter",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "mm",
            IsRequiredForCoa = true
        };
        repository.Characteristics.Add(characteristic);
        repository.SpecLimits.Add(new SpecLimit { CharacteristicId = characteristic.Id, Nominal = 5m, Lsl = 4.5m, Usl = 5.5m });
        repository.ControlLimits.Add(new ControlLimitSet { PartNum = "P100", ProcessCode = "MOLD", OperationSeq = 10, CharacteristicName = "Diameter", CenterLine = 5m, Lcl = 4.8m, Ucl = 5.2m });
        repository.Jobs.Add(new Job { JobNum = "J100", PartNum = "P100" });
        repository.Measurements.Single().Value = 5.3m;
        repository.Measurements.Add(Measurement(5.8m));
        var service = new JobReviewService(
            repository,
            new QaSummaryExportService(repository),
            new JobHistoryService(repository));

        var result = service.Build("P100", "J100");

        Assert.True(result.Succeeded);
        Assert.Contains(result.Value!.Measurements, measurement => measurement.Value == 5.3m && measurement.IsOutOfControl && !measurement.IsOutOfSpec);
        Assert.Contains(result.Value.Measurements, measurement => measurement.Value == 5.8m && measurement.IsOutOfSpec);
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

    private static InspectionMeasurement Measurement(decimal value)
    {
        return new InspectionMeasurement
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            Value = value,
            Timestamp = DateTimeOffset.Parse("2026-01-01T08:05:00Z"),
            OperatorUserId = "operator1"
        };
    }
}

using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class QaSummaryExportServiceTests
{
    [Fact]
    public void BuildSummary_RequiresSelectedCharacteristic()
    {
        var service = new QaSummaryExportService(new InMemorySpcRepository());

        var result = service.BuildSummary(new QaSummaryExportRequest([], [], [], null, null));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void ExportCsv_ReturnsSummaryColumnsAndCalculations()
    {
        var repository = RepositoryWithMeasurements();
        var service = new QaSummaryExportService(repository);

        var result = service.ExportCsv(new QaSummaryExportRequest(["P100"], ["J100"], ["Diameter"], null, null));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        var csv = result.Value!;
        Assert.Contains("PartNum,JobNum,CharacteristicName,Mean,Min,Max,StdDev,Count,LSL,USL,PassFailStatus", csv);
        Assert.Contains("P100,J100,Diameter,5,4.9,5.1", csv);
        Assert.Contains("Pass", csv);
    }

    [Fact]
    public void BuildJobVariableMeans_ReturnsRequiredCharacteristicsForJob()
    {
        var repository = RepositoryWithMeasurements();
        var service = new QaSummaryExportService(repository);

        var result = service.BuildJobVariableMeans("J100");

        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Value!);
        Assert.Equal("Diameter", row.CharacteristicName);
        Assert.True(row.IsRequiredForCoa);
        Assert.Equal(3, row.Count);
        Assert.Equal(5.0m, row.Mean);
        Assert.Equal("Pass", row.Status);
    }

    private static InMemorySpcRepository RepositoryWithMeasurements()
    {
        var repository = new InMemorySpcRepository();
        var part = new Part { PartNum = "P100", Description = "Widget" };
        var process = new ManufacturingProcess { ProcessCode = "MOLD", Description = "Molding" };
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        var characteristic = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Diameter",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "mm",
            IsRequiredForCoa = true
        };

        repository.Parts.Add(part);
        repository.Processes.Add(process);
        repository.Operations.Add(operation);
        repository.Characteristics.Add(characteristic);
        repository.SpecLimits.Add(new SpecLimit { CharacteristicId = characteristic.Id, Nominal = 5m, Lsl = 4.5m, Usl = 5.5m });
        repository.Jobs.Add(new Job { JobNum = "J100", PartNum = "P100" });
        repository.Measurements.AddRange([
            Measurement(4.9m, 0),
            Measurement(5.0m, 1),
            Measurement(5.1m, 2)
        ]);
        return repository;
    }

    private static InspectionMeasurement Measurement(decimal value, int minutes)
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
            Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddMinutes(minutes),
            OperatorUserId = "operator1"
        };
    }
}

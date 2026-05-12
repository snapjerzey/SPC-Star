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
        Assert.Contains("PartNum,JobNum,CharacteristicName,COAStatistic,COAValue,Mean,Min,Max,StdDev,Count,OutOfSpecExcluded,LSL,USL,PassFailStatus", csv);
        Assert.Contains("P100,J100,Diameter,Mean,5,5,4.9,5.1", csv);
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
        Assert.Equal(CoaStatisticType.Mean, row.CoaStatisticType);
        Assert.Equal(5.0m, row.CoaValue);
        Assert.Equal(5.0m, row.Mean);
        Assert.Equal("Pass", row.Status);
    }

    [Fact]
    public void BuildJobVariableMeans_ExcludesOutOfSpecMeasurementsFromCoaMean()
    {
        var repository = RepositoryWithMeasurements();
        repository.Measurements.Add(Measurement(6.1m, 3));
        repository.Measurements.Add(Measurement(3.9m, 4));
        var service = new QaSummaryExportService(repository);

        var result = service.BuildJobVariableMeans("J100");

        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Value!);
        Assert.Equal(3, row.Count);
        Assert.Equal(2, row.OutOfSpecExcludedCount);
        Assert.Equal(5.0m, row.Mean);
        Assert.Equal("Pass", row.Status);
    }

    [Fact]
    public void BuildJobVariableMeans_UsesStandardDeviationWhenRequiredForCoa()
    {
        var repository = RepositoryWithMeasurements(CoaStatisticType.StandardDeviation);
        var service = new QaSummaryExportService(repository);

        var result = service.BuildJobVariableMeans("J100");

        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Value!);
        Assert.Equal(CoaStatisticType.StandardDeviation, row.CoaStatisticType);
        Assert.Equal(0.1m, decimal.Round(row.CoaValue!.Value, 5));
        Assert.Equal(5.0m, row.Mean);
    }

    [Fact]
    public void ExportCsv_ExcludesOutOfSpecMeasurementsFromCoaStats()
    {
        var repository = RepositoryWithMeasurements();
        repository.Measurements.Add(Measurement(6.1m, 3));
        var service = new QaSummaryExportService(repository);

        var result = service.ExportCsv(new QaSummaryExportRequest(["P100"], ["J100"], ["Diameter"], null, null));

        Assert.True(result.Succeeded);
        Assert.Contains("P100,J100,Diameter,Mean,5,5,4.9,5.1", result.Value);
        Assert.Contains(",3,1,4.5,5.5,Pass", result.Value);
    }

    [Fact]
    public void BuildJobVariableMeans_ReturnsMultipleJobs()
    {
        var repository = RepositoryWithMeasurements();
        repository.Jobs.Add(new Job { JobNum = "J200", PartNum = "P100" });
        repository.Measurements.Add(Measurement(5.2m, 3, "J200"));
        var service = new QaSummaryExportService(repository);

        var result = service.BuildJobVariableMeans(["J100", "J200"]);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(result.Value, row => row.JobNum == "J100" && row.Mean == 5.0m);
        Assert.Contains(result.Value, row => row.JobNum == "J200" && row.Mean == 5.2m);
    }

    private static InMemorySpcRepository RepositoryWithMeasurements(CoaStatisticType coaStatisticType = CoaStatisticType.Mean)
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
            IsRequiredForCoa = true,
            CoaStatisticType = coaStatisticType
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

    private static InspectionMeasurement Measurement(decimal value, int minutes, string jobNum = "J100")
    {
        return new InspectionMeasurement
        {
            JobNum = jobNum,
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

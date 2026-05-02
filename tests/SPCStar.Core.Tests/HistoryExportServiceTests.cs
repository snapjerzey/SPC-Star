using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class HistoryExportServiceTests
{
    [Fact]
    public void ExportInspectionCsv_FiltersByJobAndCharacteristic()
    {
        var repository = RepositoryWithHistory();
        var service = new HistoryExportService(repository);

        var csv = service.ExportInspectionCsv(new InspectionHistoryExportRequest([], ["J100"], [], ["Diameter"], null, null));

        Assert.Contains("JobNum,PartNum,ProcessCode,OperationSeq,ResourceID,CharacteristicName,MeasurementValue,Timestamp,OperatorUserID", csv);
        Assert.Contains("J100,P100,MOLD,10,PRESS1,Diameter,5", csv);
        Assert.DoesNotContain("J200", csv);
    }

    [Fact]
    public void ExportAlertHistoryCsv_IncludesOverriddenWhenRequested()
    {
        var repository = RepositoryWithHistory();
        var service = new HistoryExportService(repository);

        var csv = service.ExportAlertHistoryCsv(new AlertHistoryExportRequest([], [], [], [], null, null, IncludeOverridden: true));

        Assert.Contains("RuleTriggered", csv);
        Assert.Contains("Overridden", csv);
    }

    [Fact]
    public void ExportMaterialChangeHistoryCsv_ReturnsTraceabilityFields()
    {
        var repository = RepositoryWithHistory();
        var service = new HistoryExportService(repository);

        var csv = service.ExportMaterialChangeHistoryCsv(new MaterialHistoryExportRequest(["P100"], ["J100"], ["PRESS1"], null, null));

        Assert.Contains("MaterialPartNum,OldLotNum,NewLotNum", csv);
        Assert.Contains("RESIN-A,LOT1,LOT2", csv);
    }

    private static InMemorySpcRepository RepositoryWithHistory()
    {
        var repository = new InMemorySpcRepository();
        repository.Measurements.AddRange([
            Measurement("J100", "Diameter", 5m),
            Measurement("J200", "Diameter", 5.1m),
            Measurement("J100", "Length", 10m)
        ]);
        repository.Alerts.Add(new ProcessAlert
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            OperatorUserId = "operator1",
            RuleTriggered = RuleTriggered.OnePointBeyondControlLimit,
            LockedAt = DateTimeOffset.Parse("2026-01-01T08:00:00Z"),
            Status = AlertStatus.Overridden
        });
        repository.MaterialChanges.Add(new MaterialChangeLog
        {
            JobNum = "J100",
            PartNum = "P100",
            MaterialPartNum = "RESIN-A",
            OldLotNum = "LOT1",
            NewLotNum = "LOT2",
            QuantityLoaded = 500m,
            ResourceId = "PRESS1",
            OperatorUserId = "operator1",
            Timestamp = DateTimeOffset.Parse("2026-01-01T08:00:00Z"),
            Reason = "Loaded next lot"
        });
        return repository;
    }

    private static InspectionMeasurement Measurement(string jobNum, string characteristicName, decimal value)
    {
        return new InspectionMeasurement
        {
            JobNum = jobNum,
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            CharacteristicName = characteristicName,
            Value = value,
            Timestamp = DateTimeOffset.Parse("2026-01-01T08:00:00Z"),
            OperatorUserId = "operator1"
        };
    }
}

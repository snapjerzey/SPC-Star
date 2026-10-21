using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class ChartDataServiceTests
{
    [Fact]
    public void Build_AppliesShiftTogetherWithEveryOtherFilter()
    {
        var r = RepositoryWithChartData();
        var reading = r.Measurements.First(); reading.OperatorShift = "5 Day";
        var request = new ChartDataRequest(ChartType.Run, reading.JobNum, reading.PartNum, reading.ResourceId,
            reading.CharacteristicName, reading.Timestamp, reading.Timestamp, reading.InspectionPhase,
            reading.ProcessCode, reading.OperationSeq, "5 Day");
        Assert.Equal(reading.Id, Assert.Single(new ChartDataService(r).Build(request).Points).MeasurementId);
        Assert.Empty(new ChartDataService(r).Build(request with { OperatorShift = "1st Half Nights" }).Points);
        Assert.Empty(new ChartDataService(r).Build(request with { OperationSeq = 999 }).Points);
    }

    private static void Flag(InMemorySpcRepository r, InspectionMeasurement measurement, RuleTriggered rule, string scope = "")
    {
        var alert = new ProcessAlert { JobNum = measurement.JobNum, PartNum = measurement.PartNum, ResourceId = measurement.ResourceId,
            CharacteristicName = measurement.CharacteristicName, OperatorUserId = measurement.OperatorUserId,
            RuleTriggered = rule, LockedAt = measurement.Timestamp, WatchScope = scope,
            Status = rule is RuleTriggered.SpecLimitViolation or RuleTriggered.AttributeRejected ? AlertStatus.Active : AlertStatus.Warning };
        r.Alerts.Add(alert);
        var violation = new RuleViolation { AlertId = alert.Id, RuleTriggered = rule, DetectedAt = measurement.Timestamp };
        violation.MeasurementIds.Add(measurement.Id); r.RuleViolations.Add(violation);
    }

    [Fact]
    public void Build_SeparatesWatchFailureAndNormal_AndKeepsWatchAfterRecovery()
    {
        var r = RepositoryWithChartData(); var rows = r.Measurements.Where(m => m.JobNum == "J100").ToArray();
        rows[1].WatchReason = "Near upper specification limit";
        Flag(r, rows[1], RuleTriggered.CustomRuleTriggered, "mold|10|In Process");
        r.Alerts.Single().Status = AlertStatus.Resolved;
        rows[2].Value = 6m; rows[2].WatchReason = "Earlier warning";
        Flag(r, rows[2], RuleTriggered.SpecLimitViolation);
        var points = new ChartDataService(r).Build(new(ChartType.Run,"J100","P100","PRESS1","Diameter",null,null)).Points;
        Assert.False(points[0].IsFailed); Assert.False(points[0].IsWatch);
        Assert.True(points[1].IsWatch); Assert.False(points[1].IsFailed); Assert.Equal(rows[1].WatchReason,points[1].WatchReason);
        Assert.True(points[2].IsFailed); Assert.False(points[2].IsWatch);
    }

    [Fact]
    public void Build_DoesNotColorCustomTrendEvidenceAsWatchUnlessItWasActuallyFlagged()
    {
        var r=RepositoryWithChartData();var measurement=r.Measurements[0];
        Flag(r,measurement,RuleTriggered.CustomRuleTriggered,"mold|10|in process");
        var point=new ChartDataService(r).Build(new(ChartType.Run,"J100","P100","PRESS1","Diameter",null,null)).Points[0];
        Assert.True(point.HasRuleViolation);Assert.False(point.IsWatch);Assert.False(point.IsFailed);
    }

    [Fact]
    public void Build_UserErrorCorrectedSampleIsNotRed()
    {
        var r=RepositoryWithChartData();SeedData.SeedSecurity(r);TestSeedData.SeedUsers(r);
        var measurement=r.Measurements[0];measurement.Value=6m;Flag(r,measurement,RuleTriggered.SpecLimitViolation);
        var alert=r.Alerts.Single();
        var cleared=new AlertOverrideService(r,new PermissionService(r),new CredentialService(r)).Override(
            new(alert.Id,"linetech1","linetech1","","",null,DateTimeOffset.UtcNow,CauseCategory:"User Error"));
        Assert.True(cleared.Succeeded);measurement.Value=5m;
        var point=new ChartDataService(r).Build(new(ChartType.Run,"J100","P100","PRESS1","Diameter",null,null)).Points[0];
        Assert.False(point.IsFailed);Assert.False(point.IsWatch);Assert.False(point.HasRuleViolation);
    }

    [Fact]
    public void Build_FiltersMeasurementsAndReturnsLimits()
    {
        var repository = RepositoryWithChartData();
        var service = new ChartDataService(repository);

        var result = service.Build(new ChartDataRequest(ChartType.IndividualsMovingRange, "J100", "P100", "PRESS1", "Diameter", null, null));

        Assert.Equal(3, result.Points.Count);
        Assert.Equal(5m, result.Mean);
        Assert.Equal(4m, result.LowerControlLimit);
        Assert.Equal(6m, result.UpperControlLimit);
        Assert.Equal(4.5m, result.LowerSpecLimit);
        Assert.Equal(5.5m, result.UpperSpecLimit);
        Assert.Null(result.Points[0].MovingRange);
        Assert.Equal(0.2m, result.Points[1].MovingRange);
    }

    [Fact]
    public void Build_MarksRuleViolationsOnPoints()
    {
        var repository = RepositoryWithChartData();
        var alert = new ProcessAlert
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            OperatorUserId = "operator1",
            RuleTriggered = RuleTriggered.OnePointBeyondControlLimit,
            LockedAt = DateTimeOffset.Parse("2026-01-01T08:02:00Z")
        };
        var violatedMeasurement = repository.Measurements.Last(measurement => measurement.JobNum == "J100");
        var violation = new RuleViolation
        {
            AlertId = alert.Id,
            RuleTriggered = RuleTriggered.OnePointBeyondControlLimit,
            DetectedAt = alert.LockedAt
        };
        violation.MeasurementIds.Add(violatedMeasurement.Id);
        repository.Alerts.Add(alert);
        repository.RuleViolations.Add(violation);

        var result = new ChartDataService(repository)
            .Build(new ChartDataRequest(ChartType.Run, "J100", "P100", "PRESS1", "Diameter", null, null));

        Assert.True(result.Points.Last().HasRuleViolation);
        Assert.True(result.Points.Last().IsWatch);
        Assert.False(result.Points.Last().IsFailed);
        Assert.Contains(RuleTriggered.OnePointBeyondControlLimit, result.Points.Last().RuleViolations);
    }

    [Fact]
    public void Build_FiltersByInspectionPhase()
    {
        var repository = RepositoryWithChartData();
        var startupMeasurement = Measurement(9.9m, 4);
        startupMeasurement.InspectionPhase = "Startup";
        repository.Measurements.Add(startupMeasurement);

        var result = new ChartDataService(repository)
            .Build(new ChartDataRequest(ChartType.Run, "J100", "P100", "PRESS1", "Diameter", null, null, "In Process"));

        Assert.Equal(3, result.Points.Count);
        Assert.DoesNotContain(result.Points, point => point.Value == 9.9m);
    }

    [Fact]
    public void Build_FiltersByOperation()
    {
        var repository = RepositoryWithChartData();
        var polishMeasurement = Measurement(8.1m, 4);
        polishMeasurement.ProcessCode = "POLISH";
        polishMeasurement.OperationSeq = 20;
        repository.Measurements.Add(polishMeasurement);

        var result = new ChartDataService(repository)
            .Build(new ChartDataRequest(ChartType.Run, "J100", "P100", "PRESS1", "Diameter", null, null, null, "MOLD", 10));

        Assert.Equal(3, result.Points.Count);
        Assert.DoesNotContain(result.Points, point => point.Value == 8.1m);
    }

    [Fact]
    public void Build_UsesSpecLimitsForMeasurementsOperation()
    {
        var repository = RepositoryWithChartData();
        var part = repository.Parts.Single(part => part.PartNum == "P100");
        var polishProcess = new ManufacturingProcess { ProcessCode = "POLISH", Description = "Polish" };
        var polishOperation = new Operation { PartId = part.Id, ProcessId = polishProcess.Id, OperationSeq = 10 };
        var polishCharacteristic = new Characteristic
        {
            OperationId = polishOperation.Id,
            Name = "Diameter",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "mm"
        };

        repository.Processes.Add(polishProcess);
        repository.Operations.Add(polishOperation);
        repository.Characteristics.Add(polishCharacteristic);
        repository.SpecLimits.Add(new SpecLimit { CharacteristicId = polishCharacteristic.Id, Nominal = 8m, Lsl = 7m, Usl = 9m });

        var result = new ChartDataService(repository)
            .Build(new ChartDataRequest(ChartType.Run, "J100", "P100", "PRESS1", "Diameter", null, null));

        Assert.Equal(4.5m, result.LowerSpecLimit);
        Assert.Equal(5.5m, result.UpperSpecLimit);
    }

    private static InMemorySpcRepository RepositoryWithChartData()
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
        };

        repository.Parts.Add(part);
        repository.Processes.Add(process);
        repository.Operations.Add(operation);
        repository.Characteristics.Add(characteristic);
        repository.SpecLimits.Add(new SpecLimit { CharacteristicId = characteristic.Id, Nominal = 5m, Lsl = 4.5m, Usl = 5.5m });
        repository.ControlLimits.Add(new ControlLimitSet
        {
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            CharacteristicName = "Diameter",
            CenterLine = 5m,
            Lcl = 4m,
            Ucl = 6m
        });
        repository.Measurements.AddRange([
            Measurement(4.8m, 0),
            Measurement(5.0m, 1),
            Measurement(5.2m, 2),
            Measurement(5.4m, 3, jobNum: "J200")
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
            Timestamp = DateTimeOffset.Parse("2026-01-01T08:00:00Z").AddMinutes(minutes),
            OperatorUserId = "operator1"
        };
    }
}

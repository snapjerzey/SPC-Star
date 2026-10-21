using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class WorkContextServiceTests
{
    [Fact]
    public void Build_ExposesJobCounterAcrossPhasesAndMachines()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion { JobNum = "J", PartNum = "P100", ProcessCode = "MOLD",
            OperationSeq = 10, ResourceId = "OTHER", InspectionPhase = "Coil Change", MachineCounter = 189000, CompletedByUserId = "operator" });
        var result = WorkContextService(repository).Build(new WorkContextRequest("J", "P100", "MOLD", 10,
            "PRESS1", "Diameter", DateTimeOffset.UtcNow));
        Assert.Equal(189000, result.MinimumMachineCounter);
        var json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"frequencyStatus\":", json);
        Assert.Contains("\"minimumMachineCounter\":189000", json);
    }

    [Fact]
    public void Build_CapabilityIncludesAllJobPhasesAndMachines_ButNotOtherJobsOrOperations()
    {
        var r = new InMemorySpcRepository();
        SeedData.SeedAll(r); SeedData.SeedSampleInspectionPlans(r);
        var diameter = r.Characteristics.Single(item => item.Name == "Diameter");
        r.InspectionPlans.Add(new InspectionPlan { CharacteristicId=diameter.Id, InspectionPhase="Setup",
            Lsl=4.5m, Usl=5.5m, SampleSize=1, AlertRuleSet="GlobalDefault" });
        foreach (var (phase, machine, value, job, operation) in new[] {
            ("Setup", "PRESS2", 4.8m, "J", 10),
            ("Startup", "PRESS1", 5m, "J", 10),
            ("In Process", "PRESS1", 5.2m, "J", 10),
            ("In Process", "PRESS1", 50m, "OTHER", 10),
            ("In Process", "PRESS1", 50m, "J", 20) })
        {
            var m = new InspectionMeasurement { JobNum=job, PartNum="P100", ProcessCode="MOLD",
                OperationSeq=operation, ResourceId=machine, InspectionPhase=phase,
                CharacteristicName="Diameter", OperatorUserId="operator", Value=value };
            r.Measurements.Add(m);
            var completion = new JobPhaseCompletion { JobNum=job, PartNum="P100", ProcessCode="MOLD",
                OperationSeq=operation, ResourceId=machine, InspectionPhase=phase, CompletedByUserId="operator" };
            completion.MeasurementIds.Add(m.Id); r.JobPhaseCompletions.Add(completion);
        }
        var service = WorkContextService(r);
        foreach (var phase in new[] { "Setup", "In Process" })
        {
            var result = service.Build(new WorkContextRequest("J", "P100", "MOLD", 10,
                "PRESS1", "Diameter", DateTimeOffset.UtcNow, phase)).Capability;
            Assert.Equal(3, result.Count);
            Assert.Equal(5m, result.Mean);
            Assert.Equal(4.8m, result.Min);
            Assert.Equal(5.2m, result.Max);
            Assert.Equal(0.2m, result.StdDev);
        }
        r.InspectionPlans.Single(item => item.CharacteristicId == diameter.Id && item.InspectionPhase == "Setup").Usl = 6m;
        var mixed = service.Build(new WorkContextRequest("J", "P100", "MOLD", 10,
            "PRESS1", "Diameter", DateTimeOffset.UtcNow)).Capability;
        Assert.Equal(3, mixed.Count);
        Assert.Equal(5m, mixed.Mean);
        Assert.Null(mixed.Cpk);
        Assert.Null(mixed.Ppk);
        Assert.Contains("mixed phase specification", mixed.CalculationMethod);
    }

    [Fact]
    public void Build_CapabilityUsesFinalizedGroupsAndIncludesFailure_NotUnsubmittedValues()
    {
        var r = new InMemorySpcRepository();SeedData.SeedAll(r);SeedData.SeedSampleInspectionPlans(r);
        var at=DateTimeOffset.UtcNow;
        for(var run=0;run<2;run++)
        {
            var completion=new JobPhaseCompletion { JobNum="J",PartNum="P100",ProcessCode="MOLD",OperationSeq=10,ResourceId="PRESS1",InspectionPhase="In Process",CompletedByUserId="operator",CompletedAt=at.AddMinutes(run) };
            foreach(var value in run==0?new[]{4.8m,4.9m,5m}:new[]{5m,5.1m,5.2m})
            {
                var m=new InspectionMeasurement { JobNum="J",PartNum="P100",ProcessCode="MOLD",OperationSeq=10,ResourceId="PRESS1",InspectionPhase="In Process",CharacteristicName="Diameter",OperatorUserId="operator",Value=value,Timestamp=at.AddMinutes(run) };
                r.Measurements.Add(m);completion.MeasurementIds.Add(m.Id);
            }
            r.JobPhaseCompletions.Add(completion);
        }
        var pending=new InspectionMeasurement { JobNum="J",PartNum="P100",ProcessCode="MOLD",OperationSeq=10,ResourceId="PRESS1",InspectionPhase="In Process",CharacteristicName="Diameter",OperatorUserId="operator",Value=50m };
        r.Measurements.Add(pending);
        var service=WorkContextService(r);var request=new WorkContextRequest("J","P100","MOLD",10,"PRESS1","Diameter",at);
        var result=service.Build(request).Capability;
        Assert.Equal(6,result.Count);Assert.Equal(5m,result.Mean);Assert.NotEqual(result.Cpk,result.Ppk);
        var failed=new FailedInspection { JobNum="J",PartNum="P100",ProcessCode="MOLD",OperationSeq=10,ResourceId="PRESS1",InspectionPhase="In Process",FailedByUserId="operator",Reason="Failure" };
        failed.MeasurementIds.Add(pending.Id);r.FailedInspections.Add(failed);
        result=service.Build(request).Capability;Assert.Equal(7,result.Count);Assert.Equal(1,result.OutOfSpecCount);Assert.Equal(0,result.OutOfSpecExcludedCount);
    }

    [Fact]
    public void Build_ReturnsPlanLimitsFrequencyAndActiveLock()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        TestSeedData.SeedUsers(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        repository.Measurements.Add(new InspectionMeasurement
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            Value = 5.1m,
            Timestamp = DateTimeOffset.Parse("2026-01-01T08:00:00Z"),
            OperatorUserId = "operator1",
            SubmittedAt = DateTimeOffset.Parse("2026-01-01T08:00:00Z")
        });
        repository.Alerts.Add(new ProcessAlert
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            OperatorUserId = "operator1",
            RuleTriggered = RuleTriggered.OnePointBeyondControlLimit,
            LockedAt = DateTimeOffset.Parse("2026-01-01T08:01:00Z")
        });
        var service = WorkContextService(repository);

        var context = service.Build(new WorkContextRequest(
            "J100",
            "P100",
            "MOLD",
            10,
            "PRESS1",
            "Diameter",
            DateTimeOffset.Parse("2026-01-01T08:45:00Z")));

        Assert.NotNull(context.InspectionPlan);
        Assert.Equal(4.5m, context.LowerSpecLimit);
        Assert.Equal(5.5m, context.UpperSpecLimit);
        Assert.Equal(4m, context.LowerControlLimit);
        Assert.Equal(6m, context.UpperControlLimit);
        Assert.Equal(InspectionDueStatus.Overdue, context.FrequencyStatus.Status);
        Assert.NotNull(context.ActiveLock);
        Assert.Single(context.RecentMeasurements);
    }

    [Fact]
    public void Build_ReturnsProcessWarningWithoutActiveLock()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        TestSeedData.SeedUsers(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        repository.Alerts.Add(new ProcessAlert
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            OperatorUserId = "operator1",
            RuleTriggered = RuleTriggered.OnePointBeyondControlLimit,
            Detail = "Value is above the upper control limit.",
            LockedAt = DateTimeOffset.Parse("2026-01-01T08:01:00Z"),
            Status = AlertStatus.Warning
        });
        var service = WorkContextService(repository);

        var context = service.Build(new WorkContextRequest(
            "J100",
            "P100",
            "MOLD",
            10,
            "PRESS1",
            "Diameter",
            DateTimeOffset.Parse("2026-01-01T08:45:00Z")));

        Assert.Null(context.ActiveLock);
        Assert.NotNull(context.ProcessWarning);
        Assert.Equal(RuleTriggered.OnePointBeyondControlLimit, context.ProcessWarning!.RuleTriggered);
    }

    [Fact]
    public void BuildBatch_ReturnsContextsInRequestedOrder()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        TestSeedData.SeedUsers(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        var service = WorkContextService(repository);

        var contexts = service.BuildBatch(new WorkContextBatchRequest(
            "J100",
            "P100",
            "MOLD",
            10,
            "PRESS1",
            ["Length", "Diameter"],
            "In Process",
            DateTimeOffset.Parse("2026-01-01T08:45:00Z")));

        Assert.Equal(["Length", "Diameter"], contexts.Select(context => context.Request.CharacteristicName).ToArray());
        Assert.Equal(41.5m, contexts[0].LowerSpecLimit);
        Assert.Equal(4.5m, contexts[1].LowerSpecLimit);
    }

    private static WorkContextService WorkContextService(InMemorySpcRepository repository)
    {
        var setupQuery = new SetupQueryService(repository);
        var frequencyService = new InspectionFrequencyService(repository);
        var chartService = new ChartDataService(repository);
        return new WorkContextService(repository, setupQuery, frequencyService, chartService);
    }
}

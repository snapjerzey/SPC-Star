using System.Text.Json;
using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class MachineCounterScheduleTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-21T12:00:00Z");
    private static JobPhaseCompletion Complete(InMemorySpcRepository r, long counter, string phase, int minutes, params string[] names)
    {
        var completion = new JobPhaseCompletion { JobNum="J", PartNum="P100", ProcessCode="MOLD", OperationSeq=10,
            ResourceId="PRESS1", InspectionPhase=phase, MachineCounter=counter, CompletedByUserId="operator", CompletedAt=Start.AddMinutes(minutes) };
        foreach (var name in names)
        {
            var reading = Reading(name, minutes, phase); r.Measurements.Add(reading); completion.MeasurementIds.Add(reading.Id);
        }
        r.JobPhaseCompletions.Add(completion); return completion;
    }
    private static InspectionMeasurement Reading(string name, int minutes, string phase="In Process") => new() {
        JobNum="J", PartNum="P100", ProcessCode="MOLD", OperationSeq=10, ResourceId="PRESS1", InspectionPhase=phase,
        CharacteristicName=name, OperatorUserId="operator", Timestamp=Start.AddMinutes(minutes), Value=1m,
        DeviceId="test", ClientRecordId=Guid.NewGuid().ToString("N") };
    private static long Due(InMemorySpcRepository r, string name, int interval, int? first=null) =>
        new MachineCounterSchedule(r).NextDue("J","P100","MOLD",10,name,interval,first);

    [Fact]
    public void ShortChecksDoNotPostponeLongerChecksAndOnlyActualCompletedReadingsSatisfyScheduledPoints()
    {
        var r = new InMemorySpcRepository();
        Complete(r,100,"Startup",0,"Fast","Straightness","Slow");
        Complete(r,2100,"In Process",1,"Fast");
        Assert.Equal(4100,Due(r,"Fast",2000)); Assert.Equal(4100,Due(r,"Straightness",4000)); Assert.Equal(8100,Due(r,"Slow",8000));
        Complete(r,4100,"In Process",2,"Fast","Straightness");
        Complete(r,6100,"In Process",3,"Fast");
        var before=JsonSerializer.Serialize(r.JobPhaseCompletions);
        Assert.Equal(8100,Due(r,"Fast",2000)); Assert.Equal(8100,Due(r,"Straightness",4000)); Assert.Equal(8100,Due(r,"Slow",8000));
        r.Measurements.Add(Reading("Slow",4));
        Assert.Equal(8100,Due(r,"Slow",8000));
        Assert.Equal(before,JsonSerializer.Serialize(r.JobPhaseCompletions));
    }

    [Fact]
    public void LateAndExtraInspectionsDoNotMoveTheStackedSchedule()
    {
        var r = new InMemorySpcRepository();
        Complete(r,100,"Startup",0,"Fast","Medium","Slow");
        Complete(r,2200,"In Process",1,"Fast");
        Assert.Equal(4100,Due(r,"Fast",2000)); Assert.Equal(4100,Due(r,"Medium",4000)); Assert.Equal(8100,Due(r,"Slow",8000));
        Complete(r,3500,"Coil Change",2,"Fast","Medium","Slow");
        Assert.Equal(4100,Due(r,"Fast",2000)); Assert.Equal(4100,Due(r,"Medium",4000)); Assert.Equal(8100,Due(r,"Slow",8000));
        Complete(r,4300,"In Process",3,"Fast","Medium");
        Assert.Equal(6100,Due(r,"Fast",2000)); Assert.Equal(8100,Due(r,"Medium",4000)); Assert.Equal(8100,Due(r,"Slow",8000));
        Complete(r,6200,"In Process",4,"Fast");
        Assert.Equal(8100,Due(r,"Fast",2000)); Assert.Equal(8100,Due(r,"Medium",4000)); Assert.Equal(8100,Due(r,"Slow",8000));
        Complete(r,8500,"In Process",5,"Fast","Medium","Slow");
        Assert.Equal(10100,Due(r,"Fast",2000)); Assert.Equal(12100,Due(r,"Medium",4000)); Assert.Equal(16100,Due(r,"Slow",8000));
        Assert.Equal(6,r.JobPhaseCompletions.Count);
    }

    [Fact]
    public void FailedAndLatePassingRepeatKeepTheFixedGrid()
    {
        var r = new InMemorySpcRepository(); Complete(r,0,"Startup",0);
        Complete(r,189000,"In Process",1,"Fast");
        r.FailedInspections.Add(new FailedInspection { JobNum="J", PartNum="P100", ProcessCode="MOLD", OperationSeq=10,
            ResourceId="PRESS1", InspectionPhase="In Process", FailedByUserId="operator", MachineCounter=193500,
            FailedAt=Start.AddMinutes(2), CauseCategory="Machine" });
        Assert.Equal(193500,Due(r,"Fast",4500));
        Complete(r,193510,"In Process",3,"Fast");
        Assert.Equal(198000,Due(r,"Fast",4500));
    }

    [Fact]
    public void NewRunUsesActualCounterAndConfiguredFirstIntervalThenRegularInterval()
    {
        var r = new InMemorySpcRepository();
        Assert.Equal(500,Due(r,"Fast",4500,500));
        Complete(r,3333450,"Startup",0);
        Assert.Equal(3333950,Due(r,"Fast",4500,500));
        Complete(r,3333950,"In Process",1,"Fast");
        Assert.Equal(3338450,Due(r,"Fast",4500,500));
        Complete(r,5333450,"Setup",2); Complete(r,5333550,"Startup",3);
        Assert.Equal(5334050,Due(r,"Fast",4500,500));
    }

    [Fact]
    public void OtherJobsOperationsAndFailuresDoNotMoveTheVariableBaseline()
    {
        var r = new InMemorySpcRepository(); Complete(r,100,"Startup",0);
        var other=Complete(r,900000,"Startup",1);other.JobNum="Other";
        var operation=Complete(r,800000,"Startup",2);operation.OperationSeq=20;
        r.FailedInspections.Add(new FailedInspection { JobNum="J",PartNum="P100",ProcessCode="MOLD",OperationSeq=10,ResourceId="PRESS1",
            InspectionPhase="In Process",FailedByUserId="operator",MachineCounter=4100,FailedAt=Start.AddMinutes(3),Reason="Failure",CauseCategory="Machine" });
        Assert.Equal(4100,Due(r,"Straightness",4000));
        Complete(r,4100,"Coil Change",4,"Straightness");
        Assert.Equal(8100,Due(r,"Straightness",4000)); Assert.Equal(8100,Due(r,"Slow",8000));
    }

    [Fact]
    public void WorkContextAndSubmissionUseTheSameVariableDueCounterAndRejectOnlyTheMissingRequiredSamples()
    {
        var r=new InMemorySpcRepository();SeedData.SeedAll(r);SeedData.SeedSampleInspectionPlans(r);
        r.PartMaterialFields.Clear();r.InspectionPlans.Clear();r.SpecLimits.Clear();r.ControlLimits.Clear();
        var diameter=r.Characteristics.Single(x=>x.Name=="Diameter");var length=r.Characteristics.Single(x=>x.Name=="Length");var weight=r.Characteristics.Single(x=>x.Name=="Weight");
        r.InspectionPlans.Add(new InspectionPlan { CharacteristicId=diameter.Id,InspectionPhase="In Process",SampleSize=1,AlertRuleSet="GlobalDefault",Frequency=new() { Type=FrequencyType.Quantity,Unit=FrequencyUnit.Pieces,Value=2000 } });
        r.InspectionPlans.Add(new InspectionPlan { CharacteristicId=length.Id,InspectionPhase="In Process",SampleSize=1,AlertRuleSet="GlobalDefault",Frequency=new() { Type=FrequencyType.Quantity,Unit=FrequencyUnit.Pieces,Value=4000 } });
        r.InspectionPlans.Add(new InspectionPlan { CharacteristicId=weight.Id,InspectionPhase="In Process",SampleSize=1,AlertRuleSet="GlobalDefault",Frequency=new() { Type=FrequencyType.Quantity,Unit=FrequencyUnit.Pieces,Value=8000 } });
        Complete(r,100,"Startup",0,"Diameter","Length");Complete(r,2100,"In Process",1,"Diameter");
        var query=new WorkContextService(r,new SetupQueryService(r),new InspectionFrequencyService(r),new ChartDataService(r));
        Assert.Equal(4100,query.Build(new("J","P100","MOLD",10,"PRESS1","Length",Start.AddMinutes(2))).NextMachineCounterDue);
        var current=Reading("Diameter",2);r.Measurements.Add(current);
        var service=new InspectionMeasurementService(r,new WesternElectricRuleService());
        var request=new CompleteInspectionRequest("J","P100","MOLD",10,"PRESS1","In Process",4100,[current.ClientRecordId!]);
        var failed=service.CompleteInspection(request);Assert.False(failed.Succeeded);Assert.Contains(failed.Errors,e=>e.Contains("Length (1 more sample)"));Assert.Equal(2,r.JobPhaseCompletions.Count);
        var lengthReading=Reading("Length",3);r.Measurements.Add(lengthReading);
        var passed=service.CompleteInspection(request with { MeasurementClientRecordIds=[current.ClientRecordId!,lengthReading.ClientRecordId!] });
        Assert.True(passed.Succeeded,string.Join(";",passed.Errors));Assert.Equal(2,passed.Value!.CompletionNumber);Assert.Equal(2,passed.Value.MeasurementIds.Count);
        Assert.Equal(6100,query.Build(new("J","P100","MOLD",10,"PRESS1","Diameter",Start.AddMinutes(4))).NextMachineCounterDue);
        Assert.Equal(8100,query.Build(new("J","P100","MOLD",10,"PRESS1","Length",Start.AddMinutes(4))).NextMachineCounterDue);
        var third=Reading("Diameter",4);r.Measurements.Add(third);
        var shortOnly=service.CompleteInspection(request with { MachineCounter=6100, MeasurementClientRecordIds=[third.ClientRecordId!] });
        Assert.True(shortOnly.Succeeded,string.Join(";",shortOnly.Errors));Assert.Single(shortOnly.Value!.MeasurementIds);
        Assert.Equal(8100,query.Build(new("J","P100","MOLD",10,"PRESS1","Length",Start.AddMinutes(5))).NextMachineCounterDue);
        foreach (var name in new[] { "Diameter", "Length", "Weight" })
            Assert.Equal(8100,query.Build(new("J","P100","MOLD",10,"PRESS1",name,Start.AddMinutes(5))).NextMachineCounterDue);
        var fastAtEight=Reading("Diameter",5);var mediumAtEight=Reading("Length",6);
        r.Measurements.Add(fastAtEight);r.Measurements.Add(mediumAtEight);
        var atEight=request with { MachineCounter=8100,MeasurementClientRecordIds=[fastAtEight.ClientRecordId!,mediumAtEight.ClientRecordId!] };
        var incomplete=service.CompleteInspection(atEight);Assert.False(incomplete.Succeeded);Assert.Contains(incomplete.Errors,e=>e.Contains("Weight (1 more sample)"));
        var slowAtEight=Reading("Weight",7);r.Measurements.Add(slowAtEight);
        var full=service.CompleteInspection(atEight with { MeasurementClientRecordIds=[fastAtEight.ClientRecordId!,mediumAtEight.ClientRecordId!,slowAtEight.ClientRecordId!] });
        Assert.True(full.Succeeded,string.Join(";",full.Errors));Assert.Equal(3,full.Value!.MeasurementIds.Count);
        Assert.Equal(10100,Due(r,"Diameter",2000));Assert.Equal(12100,Due(r,"Length",4000));Assert.Equal(16100,Due(r,"Weight",8000));
    }
}

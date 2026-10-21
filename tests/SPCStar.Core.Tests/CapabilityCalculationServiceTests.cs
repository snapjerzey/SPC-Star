using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class CapabilityCalculationServiceTests
{
    private static void Add(InMemorySpcRepository r, decimal[] values, bool finalized=true, bool failed=false, string job="J")
    {
        var rows=values.Select(value=>new InspectionMeasurement { JobNum=job,PartNum="P",ProcessCode="PROC",OperationSeq=10,ResourceId="M",CharacteristicName="Width",InspectionPhase="In Process",OperatorUserId="operator",Value=value,Timestamp=DateTimeOffset.Parse("2026-10-16T12:00:00Z").AddMinutes(r.Measurements.Count) }).ToArray();
        r.Measurements.AddRange(rows);
        if (!finalized) return;
        if (failed)
        {
            var failure=new FailedInspection { JobNum=job,PartNum="P",ProcessCode="PROC",OperationSeq=10,ResourceId="M",InspectionPhase="In Process",FailedByUserId="operator",Reason="Failure",FailedAt=rows[0].Timestamp };
            failure.MeasurementIds.AddRange(rows.Select(m=>m.Id));r.FailedInspections.Add(failure);
        }
        else
        {
            var run=new JobPhaseCompletion { JobNum=job,PartNum="P",ProcessCode="PROC",OperationSeq=10,ResourceId="M",InspectionPhase="In Process",CompletedByUserId="operator",CompletedAt=rows[0].Timestamp };
            run.MeasurementIds.AddRange(rows.Select(m=>m.Id));r.JobPhaseCompletions.Add(run);
        }
    }
    private static CapabilityResult Calculate(InMemorySpcRepository r,decimal? lsl=0m,decimal? usl=10m)=>new CapabilityCalculationService(r).Calculate(r.Measurements,lsl,usl);
    private static void Near(decimal expected,decimal? actual)=>Assert.InRange(actual!.Value,expected-.00000001m,expected+.00000001m);

    [Fact]
    public void PooledWithinAndOverallVariation_ProduceDifferentCorrectIndices()
    {
        var r=new InMemorySpcRepository();Add(r,[4,5,6]);Add(r,[6,7,8]);var result=Calculate(r);
        Near(1m,result.WithinStdDev);Near((decimal)Math.Sqrt(2),result.OverallStdDev);
        Near(10m/6m,result.Cp);Near(4m/3m,result.Cpk);
        Near(10m/(6m*(decimal)Math.Sqrt(2)),result.Pp);Near(4m/(3m*(decimal)Math.Sqrt(2)),result.Ppk);
        Assert.Equal(6,result.Count);Assert.Equal(0,result.OutOfSpecCount);
    }

    [Fact]
    public void SinglesUseMovingRanges_NotOverallStdDev_AndDoNotBridgeJobs()
    {
        var r=new InMemorySpcRepository();Add(r,[1]);Add(r,[2]);Add(r,[4]);var result=Calculate(r);
        Near(1.5m/1.128m,result.WithinStdDev);Assert.NotEqual(result.Cpk,result.Ppk);
        var separate=new InMemorySpcRepository();Add(separate,[1],job:"A");Add(separate,[4],job:"B");
        Assert.Null(Calculate(separate).Cp);Assert.NotNull(Calculate(separate).Pp);
    }

    [Fact]
    public void FailedValuesAreIncluded_WhileUnfinishedValuesAndDuplicateLinksAreNot()
    {
        var r=new InMemorySpcRepository();Add(r,[4,5,6]);Add(r,[100],failed:true);Add(r,[-999],finalized:false);
        r.JobPhaseCompletions[0].MeasurementIds.Add(r.Measurements[0].Id);
        var result=Calculate(r);Assert.Equal(4,result.Count);Assert.Equal(100m,result.Max);Assert.Equal(1,result.OutOfSpecCount);Assert.True(result.Ppk<0);Assert.Null(result.Cp);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OneSidedSpecs_OnlyReturnCenteredIndices(bool lowerOnly)
    {
        var r=new InMemorySpcRepository();Add(r,[4,5,6]);Add(r,[6,7,8]);var result=Calculate(r,lowerOnly?0m:null,lowerOnly?null:10m);
        Assert.Null(result.Cp);Assert.Null(result.Pp);Assert.NotNull(result.Cpk);Assert.NotNull(result.Ppk);
    }

    [Fact]
    public void InsufficientOrZeroVariation_DoesNotInventInfiniteCapability()
    {
        var r=new InMemorySpcRepository();Add(r,[5,5]);Add(r,[5,5]);var result=Calculate(r);
        Assert.Null(result.Cp);Assert.Null(result.Cpk);Assert.Null(result.Pp);Assert.Null(result.Ppk);
        var lone=new InMemorySpcRepository();Add(lone,[5]);Assert.Null(Calculate(lone).Ppk);
        var empty=new InMemorySpcRepository();Assert.Equal(0,Calculate(empty).Count);
    }
}

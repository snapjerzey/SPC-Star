using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class MeasurementLinkPersistenceTests
{
    [Fact]
    public void Restart_PreservesExactMeasurementLinksForCompletionFailureAndViolation()
    {
        var path=Path.Combine(Path.GetTempPath(),$"spcstar-links-{Guid.NewGuid():N}.db");
        try
        {
            var ids=new[]{Guid.NewGuid(),Guid.NewGuid()};var r=new SqliteBackedSpcRepository(path);
            var completion=new JobPhaseCompletion{JobNum="J",PartNum="P",ProcessCode="Process",ResourceId="R",InspectionPhase="Setup",CompletedByUserId="qa1"};completion.MeasurementIds.AddRange(ids);r.JobPhaseCompletions.Add(completion);
            var failure=new FailedInspection{JobNum="J",PartNum="P",ProcessCode="Process",ResourceId="R",InspectionPhase="Setup",FailedByUserId="qa1"};failure.MeasurementIds.AddRange(ids);r.FailedInspections.Add(failure);
            var violation=new RuleViolation{AlertId=Guid.NewGuid(),RuleTriggered=RuleTriggered.SpecLimitViolation};violation.MeasurementIds.AddRange(ids);r.RuleViolations.Add(violation);r.SaveChanges();
            for(var i=0;i<2;i++)
            {
                r=new SqliteBackedSpcRepository(path);Assert.Equal(ids,Assert.Single(r.JobPhaseCompletions).MeasurementIds);Assert.Equal(ids,Assert.Single(r.FailedInspections).MeasurementIds);Assert.Equal(ids,Assert.Single(r.RuleViolations).MeasurementIds);r.SaveChanges();
            }
        }
        finally {foreach(var suffix in new[]{"","-wal","-shm"})if(File.Exists(path+suffix))File.Delete(path+suffix);}
    }
}

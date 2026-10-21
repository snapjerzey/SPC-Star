using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class JobMachineCounterRulesTests
{
    [Fact]
    public void CounterIncludesAllPhasesMachinesAndFailuresForTheJob()
    {
        var repository = new InMemorySpcRepository();
        repository.JobPhaseCompletions.Add(Completion("JOB", 189000));
        repository.FailedInspections.Add(new FailedInspection { JobNum = "JOB", PartNum = "P", ProcessCode = "PROC", ResourceId = "M", InspectionPhase = "In Process", FailedByUserId = "tech", MachineCounter = 189100 });
        repository.JobPhaseCompletions.Add(Completion("OTHER", 999999));
        Assert.Equal(189100, JobMachineCounterRules.Minimum(repository, "job"));
        Assert.NotNull(JobMachineCounterRules.Validate(repository, "JOB", 138000));
        Assert.Null(JobMachineCounterRules.Validate(repository, "JOB", 189100));
        Assert.Null(JobMachineCounterRules.Validate(repository, "JOB", 189101));
    }

    [Fact]
    public void CompletionAndFailureCannotSaveABackwardsCounter()
    {
        var repository = new InMemorySpcRepository();
        repository.JobPhaseCompletions.Add(Completion("JOB", 189000));
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        var completion = service.CompleteInspection(new CompleteInspectionRequest("JOB", "P", "PROC", 10, "M", "In Process", 138000));
        var failure = service.FailInspection(new FailInspectionRequest("JOB", "P", "PROC", 10, "M", "Setup", "tech", 138000,
            CauseCategory: "Machine", CauseText: "Fixture", SolutionText: "Repaired"));
        Assert.False(completion.Succeeded);
        Assert.False(failure.Succeeded);
        Assert.Contains(completion.Errors, error => error.Contains("189,000"));
        Assert.Contains(failure.Errors, error => error.Contains("189,000"));
        Assert.Single(repository.JobPhaseCompletions);
        Assert.Empty(repository.FailedInspections);
    }

    private static JobPhaseCompletion Completion(string job, long counter) => new()
    {
        JobNum = job, PartNum = "P", ProcessCode = "PROC", ResourceId = "OTHER",
        InspectionPhase = "Coil Change", CompletedByUserId = "operator", MachineCounter = counter
    };
}

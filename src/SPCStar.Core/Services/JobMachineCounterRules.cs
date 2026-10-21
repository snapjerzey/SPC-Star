using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public static class JobMachineCounterRules
{
    public static long Minimum(ISpcRepository repository, string jobNum) => repository.JobPhaseCompletions
        .Where(item => item.JobNum.Equals(jobNum.Trim(), StringComparison.OrdinalIgnoreCase))
        .Select(item => item.MachineCounter ?? 0)
        .Concat(repository.FailedInspections
            .Where(item => item.JobNum.Equals(jobNum.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(item => item.MachineCounter ?? 0))
        .DefaultIfEmpty(0).Max();

    public static string? Validate(ISpcRepository repository, string jobNum, long? counter)
    {
        var minimum = Minimum(repository, jobNum);
        return counter.HasValue && counter < minimum
            ? $"Machine Counter cannot be below {minimum:N0}, the latest saved counter for this job."
            : null;
    }
}

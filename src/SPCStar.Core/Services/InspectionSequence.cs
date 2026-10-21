using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

internal static class InspectionSequence
{
    private sealed record Event(Guid Id, string Job, string Part, string Process, int Operation,
        string Phase, DateTimeOffset At, int StoredNumber);

    private static string Phase(string value) => value.Trim().ToUpperInvariant() switch
    {
        "" => "IN PROCESS",
        "SET UP" => "SETUP",
        "COILCHANGE" => "COIL CHANGE",
        "SPOOL START" => "SPOOL",
        "SPOOL END" or "ENDOFSPOOL" => "END OF SPOOL",
        var phase => phase
    };

    private static (string, string, string, int, string) Key(string job, string part, string process, int operation, string phase) =>
        (job.Trim().ToUpperInvariant(), part.Trim().ToUpperInvariant(), process.Trim().ToUpperInvariant(), operation, Phase(phase));

    private static IEnumerable<Event> Events(ISpcRepository repository) =>
        repository.JobPhaseCompletions.Select(item => new Event(item.Id, item.JobNum, item.PartNum,
            item.ProcessCode, item.OperationSeq, item.InspectionPhase, item.CompletedAt, item.CompletionNumber))
        .Concat(repository.FailedInspections.Select(item => new Event(item.Id, item.JobNum, item.PartNum,
            item.ProcessCode, item.OperationSeq, item.InspectionPhase, item.FailedAt, item.FailureNumber)));

    public static IReadOnlyDictionary<Guid, int> Numbers(ISpcRepository repository, string? job = null) =>
        Numbers(Events(repository).Where(item => job is null || item.Job.Equals(job, StringComparison.OrdinalIgnoreCase)));

    private static Dictionary<Guid, int> Numbers(IEnumerable<Event> events)
    {
        var numbers = new Dictionary<Guid, int>();
        foreach (var group in events.GroupBy(item => Key(item.Job, item.Part, item.Process, item.Operation, item.Phase)))
        {
            var number = 0;
            // Number only actual terminal records; never infer inspections from draft readings.
            foreach (var item in group.OrderBy(item => item.At).ThenBy(item => item.Id))
            {
                number = Math.Max(number + 1, item.StoredNumber);
                numbers[item.Id] = number;
            }
        }
        return numbers;
    }

    public static int Next(ISpcRepository repository, string job, string part, string process, int operation, string phase)
    {
        var key = Key(job, part, process, operation, phase);
        return Numbers(Events(repository).Where(item => Key(item.Job, item.Part, item.Process, item.Operation, item.Phase) == key))
            .Values.DefaultIfEmpty(0).Max() + 1;
    }
}

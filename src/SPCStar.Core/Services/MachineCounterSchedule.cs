using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed class MachineCounterSchedule(ISpcRepository repository)
{
    private Dictionary<Guid, InspectionMeasurement>? measurementsById;

    public long NextDue(string jobNum, string partNum, string processCode, int operationSeq,
        string characteristicName, int interval, int? firstDue)
    {
        var every = Math.Max(interval, 1);
        var first = Math.Max(firstDue ?? every, 1);
        var completions = repository.JobPhaseCompletions.Where(item =>
            item.JobNum.Equals(jobNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
            item.PartNum.Equals(partNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
            item.ProcessCode.Equals(processCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
            item.OperationSeq == operationSeq && item.MachineCounter.HasValue)
            .OrderByDescending(item => item.CompletedAt).ThenByDescending(item => item.MachineCounter).ToArray();

        var run = completions.FirstOrDefault(item =>
            item.InspectionPhase.Equals("Setup", StringComparison.OrdinalIgnoreCase) ||
            item.InspectionPhase.Equals("Set Up", StringComparison.OrdinalIgnoreCase) ||
            item.InspectionPhase.Equals("Startup", StringComparison.OrdinalIgnoreCase));
        var firstCounter = (run?.MachineCounter ?? 0) + first;

        // Keep each interval on the same run's grid, so checks stack. A late or
        // extra inspection satisfies only reached points; it cannot move the grid.
        foreach (var completion in completions
            .Where(item => item != run && (run is null || item.CompletedAt >= run.CompletedAt) && item.MachineCounter >= firstCounter)
            .OrderByDescending(item => item.MachineCounter))
        {
            measurementsById ??= repository.Measurements.ToDictionary(item => item.Id);
            if (completion.MeasurementIds.Any(id => measurementsById.TryGetValue(id, out var reading) &&
                reading.JobNum.Equals(jobNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                reading.PartNum.Equals(partNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                reading.ProcessCode.Equals(processCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                reading.OperationSeq == operationSeq &&
                reading.CharacteristicName.Equals(characteristicName, StringComparison.OrdinalIgnoreCase)))
                return firstCounter + ((completion.MachineCounter!.Value - firstCounter) / every + 1) * every;
        }
        return firstCounter;
    }
}

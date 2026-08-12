using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace SPCStar.Core.Services;

public sealed record JobHistoryEntryDto(
    Guid Id,
    string EntryType,
    string JobNum,
    string PartNum,
    string ResourceId,
    string OperatorUserId,
    string OperatorShift,
    DateTimeOffset Timestamp,
    string? NoteText = null,
    string? CharacteristicName = null,
    RuleTriggered? RuleTriggered = null,
    string? Detail = null,
    AlertStatus? Status = null,
    string? OverrideUserId = null,
    string? OverrideRole = null,
    string? CauseCategory = null,
    string? CauseText = null,
    string? SolutionText = null,
    DateTimeOffset? UnlockedAt = null,
    string? MaterialPartNum = null,
    string? NewLotNum = null,
    decimal? QuantityLoaded = null,
    string? Reason = null,
    decimal? OldValue = null,
    decimal? NewValue = null,
    string? OldInspectionPhase = null,
    string? NewInspectionPhase = null,
    string? InspectionPhase = null,
    string? ProcessCode = null,
    int? OperationSeq = null,
    int? CompletionNumber = null,
    IReadOnlyList<Guid>? MeasurementIds = null);

public sealed class JobHistoryService(ISpcRepository repository)
{
    public IReadOnlyList<JobHistoryEntryDto> GetForJob(string jobNum)
    {
        if (string.IsNullOrWhiteSpace(jobNum))
        {
            return [];
        }

        var normalizedJob = jobNum.Trim();
        var notes = repository.JobNotes
            .Where(note => note.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase))
            .Select(note => new JobHistoryEntryDto(
                note.Id,
                "Note",
                note.JobNum,
                note.PartNum,
                note.ResourceId,
                note.OperatorUserId,
                UserShift(note.OperatorUserId),
                note.Timestamp,
                NoteText: note.NoteText));

        var locks = repository.Alerts
            .Where(alert => alert.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase))
            .Select(alert =>
            {
                var audit = repository.AlertOverrides
                    .Where(overrideRow => overrideRow.AlertId == alert.Id)
                    .OrderByDescending(overrideRow => overrideRow.UnlockedAt)
                    .FirstOrDefault();

                return new JobHistoryEntryDto(
                    alert.Id,
                    "Lock",
                    alert.JobNum,
                    alert.PartNum,
                    alert.ResourceId,
                    alert.OperatorUserId,
                    alert.OperatorShift,
                    audit?.UnlockedAt ?? alert.LockedAt,
                    CharacteristicName: alert.CharacteristicName,
                    RuleTriggered: alert.RuleTriggered,
                    Detail: alert.Detail,
                    Status: alert.Status,
                    OverrideUserId: audit?.OverrideUserId,
                    OverrideRole: audit?.OverrideRole,
                    CauseCategory: audit?.CauseCategory,
                    CauseText: audit?.CauseText,
                    SolutionText: audit?.SolutionText,
                    UnlockedAt: audit?.UnlockedAt);
            });

        var materialChanges = repository.MaterialChanges
            .Where(change => change.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase))
            .Select(change => new JobHistoryEntryDto(
                change.Id,
                "Material",
                change.JobNum,
                change.PartNum,
                change.ResourceId,
                change.OperatorUserId,
                UserShift(change.OperatorUserId),
                change.Timestamp,
                MaterialPartNum: change.MaterialPartNum,
                NewLotNum: change.NewLotNum,
                QuantityLoaded: change.QuantityLoaded,
                Reason: change.Reason));

        var phaseCompletions = BuildPhaseCompletions(normalizedJob);

        var edits = repository.MeasurementEditAudits
            .Where(edit => edit.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase))
            .Select(edit => new JobHistoryEntryDto(
                edit.Id,
                "MeasurementEdit",
                edit.JobNum,
                edit.PartNum,
                edit.ResourceId,
                edit.EditedByUserId,
                UserShift(edit.EditedByUserId),
                edit.EditedAt,
                CharacteristicName: edit.CharacteristicName,
                OldValue: edit.OldValue,
                NewValue: edit.NewValue,
                OldInspectionPhase: edit.OldInspectionPhase,
                NewInspectionPhase: edit.NewInspectionPhase));

        return notes
            .Concat(locks)
            .Concat(materialChanges)
            .Concat(phaseCompletions)
            .Concat(edits)
            .OrderByDescending(entry => entry.Timestamp)
            .ToArray();
    }

    private string UserShift(string userName)
    {
        return repository.Users
            .FirstOrDefault(user => user.UserName.Equals(userName.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.Shift
            .Trim() ?? string.Empty;
    }

    private IReadOnlyList<JobHistoryEntryDto> BuildPhaseCompletions(string jobNum)
    {
        var persisted = repository.JobPhaseCompletions
            .Where(completion => completion.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase))
            .Select(completion =>
            {
                var measurementIds = completion.MeasurementIds.Count > 0
                    ? [.. completion.MeasurementIds]
                    : MeasurementIdsForCompletion(
                        completion.JobNum,
                        completion.PartNum,
                        completion.ProcessCode,
                        completion.OperationSeq,
                        completion.ResourceId,
                        completion.InspectionPhase,
                        Math.Max(completion.CompletionNumber, 1));

                return new JobHistoryEntryDto(
                    completion.Id,
                    "PhaseComplete",
                    completion.JobNum,
                    completion.PartNum,
                    completion.ResourceId,
                    completion.CompletedByUserId,
                    completion.OperatorShift,
                    completion.CompletedAt,
                    InspectionPhase: completion.InspectionPhase,
                    ProcessCode: completion.ProcessCode,
                    OperationSeq: completion.OperationSeq,
                    CompletionNumber: Math.Max(completion.CompletionNumber, 1),
                    MeasurementIds: measurementIds);
            })
            .ToList();

        var existingKeys = persisted
            .Select(entry => CompletionKey(entry.JobNum, entry.PartNum, entry.ProcessCode ?? "", entry.OperationSeq ?? 0, entry.ResourceId, entry.InspectionPhase ?? "", entry.CompletionNumber ?? 1))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var calculated = CalculatedPhaseCompletions(jobNum)
            .Where(entry => !existingKeys.Contains(CompletionKey(entry.JobNum, entry.PartNum, entry.ProcessCode ?? "", entry.OperationSeq ?? 0, entry.ResourceId, entry.InspectionPhase ?? "", entry.CompletionNumber ?? 1)));

        persisted.AddRange(calculated);
        return persisted;
    }

    private IReadOnlyList<JobHistoryEntryDto> CalculatedPhaseCompletions(string jobNum)
    {
        return repository.Measurements
            .Where(measurement => measurement.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase))
            .GroupBy(measurement => new
            {
                measurement.JobNum,
                measurement.PartNum,
                measurement.ProcessCode,
                measurement.OperationSeq,
                measurement.ResourceId,
                InspectionPhase = NormalizeInspectionPhase(measurement.InspectionPhase)
            })
            .SelectMany(group => CalculatedPhaseCompletionsForGroup(group.Key.JobNum, group.Key.PartNum, group.Key.ProcessCode, group.Key.OperationSeq, group.Key.ResourceId, group.Key.InspectionPhase))
            .ToArray();
    }

    private IReadOnlyList<JobHistoryEntryDto> CalculatedPhaseCompletionsForGroup(
        string jobNum,
        string partNum,
        string processCode,
        int operationSeq,
        string resourceId,
        string inspectionPhase)
    {
        var plans = PlansForPhase(partNum, processCode, operationSeq, inspectionPhase);
        if (plans.Count == 0)
        {
            return [];
        }

        var completedRuns = plans
            .Select(plan => MeasurementsForPlan(jobNum, partNum, processCode, operationSeq, resourceId, inspectionPhase, plan.Characteristic.Name).Count / plan.Plan.SampleSize)
            .DefaultIfEmpty(0)
            .Min();

        var rows = new List<JobHistoryEntryDto>();
        for (var run = 1; run <= completedRuns; run++)
        {
            var runMeasurements = plans
                .SelectMany(plan => MeasurementsForPlan(jobNum, partNum, processCode, operationSeq, resourceId, inspectionPhase, plan.Characteristic.Name)
                    .Skip((run - 1) * plan.Plan.SampleSize)
                    .Take(plan.Plan.SampleSize))
                .OrderBy(measurement => measurement.Timestamp)
                .ToArray();
            if (runMeasurements.Length == 0)
            {
                continue;
            }

            var finalMeasurement = runMeasurements.OrderByDescending(measurement => measurement.Timestamp).First();
            rows.Add(new JobHistoryEntryDto(
                DeterministicGuid(CompletionKey(jobNum, partNum, processCode, operationSeq, resourceId, inspectionPhase, run)),
                "PhaseComplete",
                jobNum,
                partNum,
                resourceId,
                finalMeasurement.OperatorUserId,
                finalMeasurement.OperatorShift,
                finalMeasurement.Timestamp,
                InspectionPhase: inspectionPhase,
                ProcessCode: processCode,
                OperationSeq: operationSeq,
                CompletionNumber: run,
                MeasurementIds: runMeasurements.Select(measurement => measurement.Id).ToArray()));
        }

        return rows;
    }

    private IReadOnlyList<Guid> MeasurementIdsForCompletion(
        string jobNum,
        string partNum,
        string processCode,
        int operationSeq,
        string resourceId,
        string inspectionPhase,
        int completionNumber)
    {
        return PlansForPhase(partNum, processCode, operationSeq, inspectionPhase)
            .SelectMany(plan => MeasurementsForPlan(jobNum, partNum, processCode, operationSeq, resourceId, inspectionPhase, plan.Characteristic.Name)
                .Skip((completionNumber - 1) * plan.Plan.SampleSize)
                .Take(plan.Plan.SampleSize)
                .Select(measurement => measurement.Id))
            .ToArray();
    }

    private IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> PlansForPhase(
        string partNum,
        string processCode,
        int operationSeq,
        string inspectionPhase)
    {
        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase));
        var process = repository.Processes.FirstOrDefault(item => item.ProcessCode.Equals(processCode, StringComparison.OrdinalIgnoreCase));
        if (part is null || process is null)
        {
            return [];
        }

        var operation = repository.Operations.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.ProcessId == process.Id &&
            item.OperationSeq == operationSeq);
        if (operation is null)
        {
            return [];
        }

        return (from characteristic in repository.Characteristics
                join plan in repository.InspectionPlans on characteristic.Id equals plan.CharacteristicId
                where characteristic.OperationId == operation.Id &&
                    plan.SampleSize > 0 &&
                    NormalizeInspectionPhase(plan.InspectionPhase).Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase)
                orderby plan.DisplayOrder, characteristic.Name
                select (plan, characteristic))
            .ToArray();
    }

    private IReadOnlyList<InspectionMeasurement> MeasurementsForPlan(
        string jobNum,
        string partNum,
        string processCode,
        int operationSeq,
        string resourceId,
        string inspectionPhase,
        string characteristicName)
    {
        return repository.Measurements
            .Where(measurement =>
                measurement.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.ProcessCode.Equals(processCode, StringComparison.OrdinalIgnoreCase) &&
                measurement.OperationSeq == operationSeq &&
                measurement.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase) &&
                measurement.CharacteristicName.Equals(characteristicName, StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(measurement.InspectionPhase).Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase))
            .OrderBy(measurement => measurement.Timestamp)
            .ToArray();
    }

    private static string CompletionKey(string jobNum, string partNum, string processCode, int operationSeq, string resourceId, string inspectionPhase, int completionNumber)
    {
        return string.Join("|", jobNum, partNum, processCode, operationSeq, resourceId, NormalizeInspectionPhase(inspectionPhase), completionNumber);
    }

    private static string NormalizeInspectionPhase(string value)
    {
        var phase = value.Trim();
        if (phase.Equals("Startup", StringComparison.OrdinalIgnoreCase))
        {
            return "Startup";
        }

        if (phase.Equals("Spool", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Spool Start", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Spool End", StringComparison.OrdinalIgnoreCase))
        {
            return "Spool";
        }

        return phase.Equals("Set Up", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Setup", StringComparison.OrdinalIgnoreCase)
            ? "Setup"
            : phase.Equals("Coil Change", StringComparison.OrdinalIgnoreCase) ||
                phase.Equals("CoilChange", StringComparison.OrdinalIgnoreCase)
                ? "Coil Change"
                : phase.Equals("In Process", StringComparison.OrdinalIgnoreCase) ||
                    phase.Equals("InProcess", StringComparison.OrdinalIgnoreCase)
                    ? "In Process"
                    : phase;
    }

    private static Guid DeterministicGuid(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return new Guid(bytes);
    }
}

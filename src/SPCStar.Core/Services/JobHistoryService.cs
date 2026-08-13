using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace SPCStar.Core.Services;

public sealed record JobHistoryJobDataDto(
    Guid Id,
    string TagName,
    string TagValue,
    string OperatorUserId,
    string OperatorShift,
    DateTimeOffset Timestamp);

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
    IReadOnlyList<Guid>? MeasurementIds = null,
    string? TagName = null,
    string? TagValue = null,
    IReadOnlyList<JobHistoryJobDataDto>? JobDataEntries = null);

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
        var jobTags = repository.JobTags
            .Where(tag => tag.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var phaseCompletionsWithJobData = phaseCompletions
            .Select(completion => completion with
            {
                JobDataEntries = jobTags
                    .Where(tag => JobTagBelongsToCompletion(tag, completion, phaseCompletions))
                    .OrderBy(tag => tag.TagName)
                    .Select(JobTagDetail)
                    .ToArray()
            })
            .ToArray();
        var standaloneJobTags = jobTags
            .Where(tag => !phaseCompletions.Any(completion => JobTagBelongsToCompletion(tag, completion, phaseCompletions)))
            .Select(JobTagHistoryEntry);

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
            .Concat(standaloneJobTags)
            .Concat(phaseCompletionsWithJobData)
            .Concat(edits)
            .OrderByDescending(entry => entry.Timestamp)
            .ToArray();
    }

    private JobHistoryEntryDto JobTagHistoryEntry(JobTag tag)
    {
        return new JobHistoryEntryDto(
            tag.Id,
            "JobData",
            tag.JobNum,
            tag.PartNum,
            tag.ResourceId,
            tag.OperatorUserId,
            UserShift(tag.OperatorUserId),
            tag.UpdatedAt,
            TagName: tag.TagName,
            TagValue: tag.TagValue);
    }

    private JobHistoryJobDataDto JobTagDetail(JobTag tag)
    {
        return new JobHistoryJobDataDto(
            tag.Id,
            tag.TagName,
            tag.TagValue,
            tag.OperatorUserId,
            UserShift(tag.OperatorUserId),
            tag.UpdatedAt);
    }

    private static bool JobTagBelongsToCompletion(JobTag tag, JobHistoryEntryDto completion, IReadOnlyList<JobHistoryEntryDto> completions)
    {
        if (completion.EntryType != "PhaseComplete" ||
            !tag.JobNum.Equals(completion.JobNum, StringComparison.OrdinalIgnoreCase) ||
            !tag.PartNum.Equals(completion.PartNum, StringComparison.OrdinalIgnoreCase) ||
            !tag.ResourceId.Equals(completion.ResourceId, StringComparison.OrdinalIgnoreCase) ||
            tag.UpdatedAt.Date != completion.Timestamp.Date)
        {
            return false;
        }

        var closest = completions
            .Where(candidate =>
                candidate.EntryType == "PhaseComplete" &&
                candidate.JobNum.Equals(tag.JobNum, StringComparison.OrdinalIgnoreCase) &&
                candidate.PartNum.Equals(tag.PartNum, StringComparison.OrdinalIgnoreCase) &&
                candidate.ResourceId.Equals(tag.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                candidate.Timestamp.Date == tag.UpdatedAt.Date &&
                candidate.Timestamp >= tag.UpdatedAt.AddMinutes(-2))
            .OrderBy(candidate => Math.Abs((candidate.Timestamp - tag.UpdatedAt).TotalMilliseconds))
            .FirstOrDefault();

        return closest?.Id == completion.Id;
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
        var persistedCompletions = repository.JobPhaseCompletions
            .Where(completion => completion.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var persisted = persistedCompletions
            .Select(completion =>
            {
                var previousCompletionAt = persistedCompletions
                    .Where(item =>
                        item.JobNum.Equals(completion.JobNum, StringComparison.OrdinalIgnoreCase) &&
                        item.PartNum.Equals(completion.PartNum, StringComparison.OrdinalIgnoreCase) &&
                        item.ProcessCode.Equals(completion.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                        item.OperationSeq == completion.OperationSeq &&
                        item.ResourceId.Equals(completion.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                        item.InspectionPhase.Equals(completion.InspectionPhase, StringComparison.OrdinalIgnoreCase) &&
                        item.CompletedAt < completion.CompletedAt)
                    .OrderByDescending(item => item.CompletedAt)
                    .FirstOrDefault()
                    ?.CompletedAt;
                var measurementIds = MeasurementIdsForCompletionWindow(
                        completion.JobNum,
                        completion.PartNum,
                        completion.ProcessCode,
                        completion.OperationSeq,
                        completion.ResourceId,
                        completion.InspectionPhase,
                        previousCompletionAt,
                        completion.CompletedAt);
                if (measurementIds.Count == 0 && completion.MeasurementIds.Count > 0)
                {
                    measurementIds = [.. completion.MeasurementIds];
                }

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
            .Select(entry => CompletionKey(entry.JobNum, entry.PartNum, entry.ProcessCode ?? "", entry.OperationSeq ?? 0, entry.ResourceId, entry.InspectionPhase ?? "", entry.Timestamp.Date, entry.CompletionNumber ?? 1))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var calculated = CalculatedPhaseCompletions(jobNum)
            .Where(entry => !existingKeys.Contains(CompletionKey(entry.JobNum, entry.PartNum, entry.ProcessCode ?? "", entry.OperationSeq ?? 0, entry.ResourceId, entry.InspectionPhase ?? "", entry.Timestamp.Date, entry.CompletionNumber ?? 1)));

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
                InspectionPhase = NormalizeInspectionPhase(measurement.InspectionPhase),
                InspectionDate = measurement.Timestamp.Date
            })
            .SelectMany(group => CalculatedPhaseCompletionsForGroup(group.Key.JobNum, group.Key.PartNum, group.Key.ProcessCode, group.Key.OperationSeq, group.Key.ResourceId, group.Key.InspectionPhase, group.Key.InspectionDate))
            .ToArray();
    }

    private IReadOnlyList<JobHistoryEntryDto> CalculatedPhaseCompletionsForGroup(
        string jobNum,
        string partNum,
        string processCode,
        int operationSeq,
        string resourceId,
        string inspectionPhase,
        DateTime inspectionDate)
    {
        var plans = PlansForPhase(partNum, processCode, operationSeq, inspectionPhase);
        if (plans.Count == 0)
        {
            return [];
        }

        var completedRuns = CompletedMeasurementRuns(jobNum, partNum, processCode, operationSeq, resourceId, inspectionPhase, inspectionDate, plans);

        var rows = new List<JobHistoryEntryDto>();
        for (var run = 1; run <= completedRuns.Count; run++)
        {
            var runMeasurements = completedRuns[run - 1];
            if (runMeasurements.Length == 0)
            {
                continue;
            }

            var finalMeasurement = runMeasurements.OrderByDescending(measurement => measurement.Timestamp).First();
            rows.Add(new JobHistoryEntryDto(
                DeterministicGuid(CompletionKey(jobNum, partNum, processCode, operationSeq, resourceId, inspectionPhase, inspectionDate, run)),
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
        DateTime inspectionDate,
        int completionNumber)
    {
        var plans = PlansForPhase(partNum, processCode, operationSeq, inspectionPhase);
        var completedRuns = CompletedMeasurementRuns(jobNum, partNum, processCode, operationSeq, resourceId, inspectionPhase, inspectionDate, plans);
        return completedRuns.Count >= completionNumber
            ? completedRuns[completionNumber - 1].Select(measurement => measurement.Id).ToArray()
            : [];
    }

    private IReadOnlyList<Guid> MeasurementIdsForCompletionWindow(
        string jobNum,
        string partNum,
        string processCode,
        int operationSeq,
        string resourceId,
        string inspectionPhase,
        DateTimeOffset? previousCompletionAt,
        DateTimeOffset completedAt)
    {
        var plans = PlansForPhase(partNum, processCode, operationSeq, inspectionPhase);
        if (plans.Count == 0)
        {
            return [];
        }

        var lowerBound = previousCompletionAt ?? new DateTimeOffset(completedAt.Date, completedAt.Offset);
        var measurements = repository.Measurements
            .Where(measurement =>
                measurement.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.ProcessCode.Equals(processCode, StringComparison.OrdinalIgnoreCase) &&
                measurement.OperationSeq == operationSeq &&
                measurement.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(measurement.InspectionPhase).Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase) &&
                measurement.Timestamp > lowerBound &&
                measurement.Timestamp <= completedAt)
            .ToArray();

        return BuildCompletionWindow(plans, measurements);
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

    private IReadOnlyList<InspectionMeasurement[]> CompletedMeasurementRuns(
        string jobNum,
        string partNum,
        string processCode,
        int operationSeq,
        string resourceId,
        string inspectionPhase,
        DateTime inspectionDate,
        IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> plans)
    {
        var measurements = repository.Measurements
            .Where(measurement =>
                measurement.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.ProcessCode.Equals(processCode, StringComparison.OrdinalIgnoreCase) &&
                measurement.OperationSeq == operationSeq &&
                measurement.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(measurement.InspectionPhase).Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase) &&
                measurement.Timestamp.Date == inspectionDate.Date)
            .OrderBy(measurement => measurement.Timestamp)
            .ThenBy(measurement => measurement.SubmittedAt)
            .ThenBy(measurement => measurement.Id)
            .ToArray();

        return BuildCompletedMeasurementRuns(plans, measurements);
    }

    private static IReadOnlyList<InspectionMeasurement[]> BuildCompletedMeasurementRuns(
        IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> plans,
        IReadOnlyList<InspectionMeasurement> measurements)
    {
        if (plans.Count == 0)
        {
            return [];
        }

        var runs = new List<InspectionMeasurement[]>();
        var buffer = new List<InspectionMeasurement>();
        var planIndex = 0;
        var sampleCount = 0;

        foreach (var measurement in measurements)
        {
            var expected = plans[planIndex];
            if (!measurement.CharacteristicName.Equals(expected.Characteristic.Name, StringComparison.OrdinalIgnoreCase))
            {
                if (measurement.CharacteristicName.Equals(plans[0].Characteristic.Name, StringComparison.OrdinalIgnoreCase))
                {
                    buffer.Clear();
                    planIndex = 0;
                    sampleCount = 0;
                }
                else
                {
                    continue;
                }
            }

            expected = plans[planIndex];
            if (!measurement.CharacteristicName.Equals(expected.Characteristic.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            buffer.Add(measurement);
            sampleCount++;

            if (sampleCount < Math.Max(expected.Plan.SampleSize, 1))
            {
                continue;
            }

            planIndex++;
            sampleCount = 0;
            if (planIndex < plans.Count)
            {
                continue;
            }

            runs.Add(buffer.ToArray());
            buffer.Clear();
            planIndex = 0;
        }

        return runs;
    }

    private static IReadOnlyList<Guid> BuildCompletionWindow(
        IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> plans,
        IReadOnlyList<InspectionMeasurement> measurements)
    {
        var selected = new List<InspectionMeasurement>();
        var cursor = DateTimeOffset.MaxValue;

        for (var index = plans.Count - 1; index >= 0; index--)
        {
            var plan = plans[index];
            var matches = measurements
                .Where(measurement =>
                    measurement.Timestamp <= cursor &&
                    measurement.CharacteristicName.Equals(plan.Characteristic.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(measurement => measurement.Timestamp)
                .ThenByDescending(measurement => measurement.SubmittedAt)
                .ThenByDescending(measurement => measurement.Id)
                .Take(Math.Max(plan.Plan.SampleSize, 1))
                .ToArray();
            if (matches.Length < Math.Max(plan.Plan.SampleSize, 1))
            {
                return [];
            }

            selected.AddRange(matches);
            cursor = matches.Min(measurement => measurement.Timestamp);
        }

        return selected
            .OrderBy(measurement => measurement.Timestamp)
            .ThenBy(measurement => measurement.SubmittedAt)
            .ThenBy(measurement => measurement.Id)
            .Select(measurement => measurement.Id)
            .ToArray();
    }

    private static string CompletionKey(string jobNum, string partNum, string processCode, int operationSeq, string resourceId, string inspectionPhase, DateTime inspectionDate, int completionNumber)
    {
        return string.Join("|", jobNum, partNum, processCode, operationSeq, resourceId, NormalizeInspectionPhase(inspectionPhase), inspectionDate.ToString("yyyy-MM-dd"), completionNumber);
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

using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

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
    long? OldMachineCounter = null,
    long? NewMachineCounter = null,
    string? InspectionPhase = null,
    string? ProcessCode = null,
    int? OperationSeq = null,
    int? CompletionNumber = null,
    int? FailureNumber = null,
    IReadOnlyList<Guid>? MeasurementIds = null,
    long? MachineCounter = null,
    string? TagName = null,
    string? TagValue = null,
    IReadOnlyList<JobHistoryJobDataDto>? JobDataEntries = null,
    string? MaterialName = null,
    string? MaterialDescription = null,
    IReadOnlyList<JobNoteReplyDto>? NoteReplies = null);

public sealed class JobHistoryService(ISpcRepository repository)
{
    public IReadOnlyList<JobHistoryEntryDto> GetForJob(string jobNum)
    {
        if (string.IsNullOrWhiteSpace(jobNum))
        {
            return [];
        }

        var normalizedJob = jobNum.Trim();
        var inspectionNumbers = InspectionSequence.Numbers(repository, normalizedJob);
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
                NoteText: note.NoteText,
                NoteReplies: note.Replies.OrderBy(reply => reply.Timestamp).ThenBy(reply => reply.Id).Select(JobNoteReplyDto.From).ToArray()));

        var locks = repository.Alerts
            .Where(alert => alert.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase) &&
                alert.Status != AlertStatus.Warning && alert.Status != AlertStatus.Resolved)
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
                MaterialName: JobMaterialRules.MaterialName(repository, change),
                MaterialDescription: JobMaterialRules.MaterialDescription(repository, change),
                QuantityLoaded: change.QuantityLoaded,
                Reason: change.Reason));

        var phaseCompletions = BuildPhaseCompletions(normalizedJob, inspectionNumbers);
        var jobTags = repository.JobTags
            .Where(tag => tag.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var phaseCompletionsWithJobData = phaseCompletions
            .Select(completion => completion with
            {
                JobDataEntries = JobTagsForCompletion(jobTags, completion, phaseCompletions)
                    .Select(JobTagDetail)
                    .ToArray()
            })
            .ToArray();
        var standaloneJobTags = jobTags
            .Where(tag => !phaseCompletions.Any(completion => JobTagBelongsToCompletion(tag, completion, phaseCompletions)))
            .Select(JobTagHistoryEntry);

        var failedInspections = repository.FailedInspections
            .Where(failure => failure.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase))
            .Select(failure => new JobHistoryEntryDto(
                failure.Id,
                "InspectionFailed",
                failure.JobNum,
                failure.PartNum,
                failure.ResourceId,
                failure.FailedByUserId,
                failure.OperatorShift,
                failure.FailedAt,
                NoteText: failure.Reason,
                CharacteristicName: repository.Alerts.FirstOrDefault(alert => alert.Id == failure.AlertId)?.CharacteristicName,
                RuleTriggered: repository.Alerts.FirstOrDefault(alert => alert.Id == failure.AlertId)?.RuleTriggered,
                Detail: FailureLockDetail(failure),
                CauseCategory: failure.CauseCategory,
                CauseText: failure.CauseText,
                SolutionText: failure.SolutionText,
                Reason: failure.Reason,
                InspectionPhase: failure.InspectionPhase,
                ProcessCode: failure.ProcessCode,
                OperationSeq: failure.OperationSeq,
                FailureNumber: inspectionNumbers[failure.Id],
                MeasurementIds: [.. failure.MeasurementIds],
                MachineCounter: failure.MachineCounter));

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
                NewInspectionPhase: edit.NewInspectionPhase,
                Reason: edit.Reason));

        var machineCounterEdits = repository.MachineCounterEditAudits
            .Where(edit => edit.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase))
            .Select(edit => new JobHistoryEntryDto(
                edit.Id,
                "MachineCounterEdit",
                edit.JobNum,
                edit.PartNum,
                edit.ResourceId,
                edit.EditedByUserId,
                UserShift(edit.EditedByUserId),
                edit.EditedAt,
                Reason: edit.Reason,
                OldMachineCounter: edit.OldMachineCounter,
                NewMachineCounter: edit.NewMachineCounter,
                InspectionPhase: edit.InspectionPhase,
                ProcessCode: edit.ProcessCode,
                OperationSeq: edit.OperationSeq));

        return notes
            .Concat(locks)
            .Concat(materialChanges)
            .Concat(standaloneJobTags)
            .Concat(phaseCompletionsWithJobData)
            .Concat(failedInspections)
            .Concat(edits)
            .Concat(machineCounterEdits)
            .OrderByDescending(entry => entry.Timestamp)
            .ToArray();
    }

    private string FailureLockDetail(FailedInspection failure)
    {
        var alert = repository.Alerts.FirstOrDefault(item => item.Id == failure.AlertId);
        return alert is null ? failure.FailureDetail : $"{alert.CharacteristicName}: {alert.Detail}";
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
            !BlankOrEqual(tag.PartNum, completion.PartNum) ||
            !BlankOrEqual(tag.ResourceId, completion.ResourceId) ||
            tag.UpdatedAt.Date != completion.Timestamp.Date)
        {
            return false;
        }

        var closest = completions
            .Where(candidate =>
                candidate.EntryType == "PhaseComplete" &&
                candidate.JobNum.Equals(tag.JobNum, StringComparison.OrdinalIgnoreCase) &&
                BlankOrEqual(tag.PartNum, candidate.PartNum) &&
                BlankOrEqual(tag.ResourceId, candidate.ResourceId) &&
                candidate.Timestamp.Date == tag.UpdatedAt.Date &&
                candidate.Timestamp >= tag.UpdatedAt.AddMinutes(-2))
            .OrderBy(candidate => Math.Abs((candidate.Timestamp - tag.UpdatedAt).TotalMilliseconds))
            .FirstOrDefault();

        return closest?.Id == completion.Id;
    }

    private static IReadOnlyList<JobTag> JobTagsForCompletion(IReadOnlyList<JobTag> jobTags, JobHistoryEntryDto completion, IReadOnlyList<JobHistoryEntryDto> completions)
    {
        if (completion.EntryType != "PhaseComplete")
        {
            return [];
        }

        var persistentTags = jobTags
            .Where(tag =>
                !IsPerInspectionJobTag(tag.TagName) &&
                !IsMaterialLotJobTag(tag.TagName) &&
                tag.JobNum.Equals(completion.JobNum, StringComparison.OrdinalIgnoreCase) &&
                BlankOrEqual(tag.PartNum, completion.PartNum) &&
                BlankOrEqual(tag.ResourceId, completion.ResourceId) &&
                tag.UpdatedAt <= completion.Timestamp)
            .GroupBy(tag => tag.TagName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(tag => tag.UpdatedAt)
                .ThenByDescending(tag => tag.Id)
                .First())
            .ToArray();
        var perInspectionTags = jobTags
            .Where(tag => IsPerInspectionJobTag(tag.TagName) && JobTagBelongsToCompletion(tag, completion, completions))
            .ToArray();

        return persistentTags
            .Concat(perInspectionTags)
            .OrderBy(tag => tag.TagName)
            .ToArray();
    }

    private static bool IsPerInspectionJobTag(string tagName)
    {
        var normalized = tagName.Trim().ToLowerInvariant();
        return normalized is "box" or "box #" or "box number";
    }

    private static bool IsMaterialLotJobTag(string tagName)
    {
        var normalized = tagName.Trim().ToLowerInvariant();
        return normalized.Contains("bimetal lot") ||
            normalized.Contains("material lot") ||
            normalized.Contains("raw material lot");
    }

    private static bool BlankOrEqual(string? value, string? expected)
    {
        return string.IsNullOrWhiteSpace(value) ||
            value.Equals(expected ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private string UserShift(string userName)
    {
        return repository.Users
            .FirstOrDefault(user => user.UserName.Equals(userName.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.Shift
            .Trim() ?? string.Empty;
    }

    private IReadOnlyList<JobHistoryEntryDto> BuildPhaseCompletions(string jobNum, IReadOnlyDictionary<Guid, int> inspectionNumbers)
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
                IReadOnlyList<Guid> measurementIds = completion.MeasurementIds.Count > 0
                    ? [.. completion.MeasurementIds]
                    : MeasurementIdsForCompletionWindow(
                        completion.JobNum,
                        completion.PartNum,
                        completion.ProcessCode,
                        completion.OperationSeq,
                        completion.ResourceId,
                        completion.InspectionPhase,
                        previousCompletionAt,
                        completion.CompletedAt);

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
                    CompletionNumber: inspectionNumbers[completion.Id],
                    MeasurementIds: measurementIds,
                    MachineCounter: completion.MachineCounter);
            })
            .ToList();

        return persisted;
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

    private static string NormalizeInspectionPhase(string value)
    {
        var phase = value.Trim();
        if (phase.Equals("Startup", StringComparison.OrdinalIgnoreCase))
        {
            return "Startup";
        }

        if (phase.Equals("Spool", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Spool Start", StringComparison.OrdinalIgnoreCase))
        {
            return "Spool";
        }
        if (phase.Equals("End of Spool", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("EndOfSpool", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Spool End", StringComparison.OrdinalIgnoreCase))
        {
            return "End of Spool";
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
}

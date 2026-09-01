using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record InspectionMeasurementEntry(
    string JobNum,
    string PartNum,
    string ProcessCode,
    int OperationSeq,
    string ResourceId,
    string CharacteristicName,
    decimal Value,
    DateTimeOffset Timestamp,
    string OperatorUserId,
    string? DeviceId = null,
    string? ClientRecordId = null,
    DateTimeOffset? SubmittedAt = null,
    string InspectionPhase = "In Process");

public sealed record CompleteInspectionRequest(
    string JobNum,
    string PartNum,
    string ProcessCode,
    int OperationSeq,
    string ResourceId,
    string InspectionPhase,
    long? MachineCounter);

public sealed class InspectionMeasurementService(
    ISpcRepository repository,
    WesternElectricRuleService westernElectricRuleService)
{
    private const int MaxMeasurementDecimalPlaces = 5;

    public ServiceResult<InspectionMeasurement> EnterMeasurement(InspectionMeasurementEntry entry)
    {
        var errors = Validate(entry);
        if (errors.Count > 0)
        {
            return ServiceResult<InspectionMeasurement>.Fail(errors);
        }

        var duplicate = FindDuplicate(entry.DeviceId, entry.ClientRecordId);
        if (duplicate is not null)
        {
            return UpdateDuplicateMeasurement(duplicate, entry);
        }

        if (!InspectionTargetExists(entry))
        {
            return ServiceResult<InspectionMeasurement>.Fail("No configured inspection characteristic was found for the submitted part/process/operation/characteristic.");
        }

        if (!CanEnterInspections(entry.OperatorUserId, entry.PartNum))
        {
            return ServiceResult<InspectionMeasurement>.Fail("User is not authorized to enter inspections for this product group.");
        }

        var activeLock = FindActiveLock(entry);
        if (activeLock is not null)
        {
            return ServiceResult<InspectionMeasurement>.Fail(ActiveLockMessage(activeLock));
        }

        var jobResult = UpsertJob(entry);
        if (!jobResult.Succeeded)
        {
            return ServiceResult<InspectionMeasurement>.Fail(jobResult.Errors);
        }

        var operatorShift = OperatorShift(entry.OperatorUserId);
        var measurement = new InspectionMeasurement
        {
            ClientRecordId = CleanOptional(entry.ClientRecordId),
            DeviceId = CleanOptional(entry.DeviceId),
            JobNum = entry.JobNum.Trim(),
            PartNum = entry.PartNum.Trim(),
            ProcessCode = entry.ProcessCode.Trim(),
            OperationSeq = entry.OperationSeq,
            ResourceId = entry.ResourceId.Trim(),
            CharacteristicName = entry.CharacteristicName.Trim(),
            InspectionPhase = NormalizeInspectionPhase(entry.InspectionPhase),
            Value = NormalizeMeasurementValue(entry.Value),
            Timestamp = entry.Timestamp,
            OperatorUserId = entry.OperatorUserId.Trim(),
            OperatorShift = operatorShift,
            SubmittedAt = entry.SubmittedAt ?? entry.Timestamp,
            SyncedAt = DateTimeOffset.UtcNow
        };

        repository.Measurements.Add(measurement);
        CreateAlertsForViolations(measurement, entry);
        return ServiceResult<InspectionMeasurement>.Ok(measurement);
    }

    private ServiceResult<InspectionMeasurement> UpdateDuplicateMeasurement(InspectionMeasurement measurement, InspectionMeasurementEntry entry)
    {
        if (!MatchesMeasurementSlot(measurement, entry))
        {
            return ServiceResult<InspectionMeasurement>.Fail("Client measurement record is already assigned to a different inspection sample.");
        }

        var normalizedValue = NormalizeMeasurementValue(entry.Value);
        if (measurement.Value == normalizedValue)
        {
            return ServiceResult<InspectionMeasurement>.Ok(measurement);
        }

        if (HasActiveAlertForMeasurement(measurement.Id) && !ClearDraftAlertsForMeasurement(measurement.Id))
        {
            return ServiceResult<InspectionMeasurement>.Fail("This sample has an active lock. Clear the lock before changing the measurement.");
        }

        var activeLock = FindActiveLock(entry);
        if (activeLock is not null)
        {
            return ServiceResult<InspectionMeasurement>.Fail(ActiveLockMessage(activeLock));
        }

        measurement.Value = normalizedValue;
        measurement.Timestamp = entry.Timestamp;
        measurement.OperatorUserId = entry.OperatorUserId.Trim();
        measurement.OperatorShift = OperatorShift(entry.OperatorUserId);
        measurement.SubmittedAt = entry.SubmittedAt ?? entry.Timestamp;
        measurement.SyncedAt = DateTimeOffset.UtcNow;
        CreateAlertsForViolations(measurement, entry);
        return ServiceResult<InspectionMeasurement>.Ok(measurement);
    }

    private void TryRecordPhaseCompletion(InspectionMeasurement measurement)
    {
        var phase = NormalizeInspectionPhase(measurement.InspectionPhase);
        var plans = PlansForMeasurementPhase(measurement, phase);
        if (plans.Count == 0)
        {
            return;
        }

        var recordedRuns = repository.JobPhaseCompletions
            .Where(item =>
                item.JobNum.Equals(measurement.JobNum, StringComparison.OrdinalIgnoreCase) &&
                item.PartNum.Equals(measurement.PartNum, StringComparison.OrdinalIgnoreCase) &&
                item.ProcessCode.Equals(measurement.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                item.OperationSeq == measurement.OperationSeq &&
                item.ResourceId.Equals(measurement.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                item.InspectionPhase.Equals(phase, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CompletedAt)
            .ToArray();
        var recordedRunCount = recordedRuns
            .Select(item => Math.Max(item.CompletionNumber, 1))
            .DefaultIfEmpty(0)
            .Max();
        var previousCompletionAt = recordedRuns.FirstOrDefault()?.CompletedAt;
        var completionMeasurementIds = MeasurementIdsForCompletionWindow(measurement, phase, plans, previousCompletionAt, measurement.Timestamp);

        if (completionMeasurementIds.Count == 0)
        {
            return;
        }

        var completion = new JobPhaseCompletion
        {
            JobNum = measurement.JobNum,
            PartNum = measurement.PartNum,
            ProcessCode = measurement.ProcessCode,
            OperationSeq = measurement.OperationSeq,
            ResourceId = measurement.ResourceId,
            InspectionPhase = phase,
            CompletionNumber = recordedRunCount + 1,
            CompletedByUserId = measurement.OperatorUserId,
            OperatorShift = measurement.OperatorShift,
            CompletedAt = measurement.Timestamp
        };

        completion.MeasurementIds.AddRange(completionMeasurementIds);
        repository.JobPhaseCompletions.Add(completion);
    }

    public ServiceResult<JobPhaseCompletion> CompleteInspection(CompleteInspectionRequest request)
    {
        var errors = ValidateCompletion(request);
        if (errors.Count > 0)
        {
            return ServiceResult<JobPhaseCompletion>.Fail(errors);
        }

        var phase = NormalizeInspectionPhase(request.InspectionPhase);
        var completion = repository.JobPhaseCompletions
            .Where(item =>
                item.JobNum.Equals(request.JobNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                item.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                item.ProcessCode.Equals(request.ProcessCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                item.OperationSeq == request.OperationSeq &&
                item.ResourceId.Equals(request.ResourceId.Trim(), StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(item.InspectionPhase).Equals(phase, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CompletedAt)
            .FirstOrDefault();
        if (completion is null)
        {
            completion = TryCreateCompletionFromSavedMeasurements(request, phase);
            if (completion is null)
            {
                return ServiceResult<JobPhaseCompletion>.Fail("No completed inspection was found for this job, part, machine, operation, and phase.");
            }
        }

        completion.MachineCounter = request.MachineCounter!.Value;
        return ServiceResult<JobPhaseCompletion>.Ok(completion);
    }

    private JobPhaseCompletion? TryCreateCompletionFromSavedMeasurements(CompleteInspectionRequest request, string phase)
    {
        var jobNum = request.JobNum.Trim();
        var partNum = request.PartNum.Trim();
        var processCode = request.ProcessCode.Trim();
        var resourceId = request.ResourceId.Trim();
        var phasePlans = PlansForPhase(partNum, processCode, request.OperationSeq, phase);
        if (phasePlans.Count == 0)
        {
            return null;
        }

        var latestMeasurement = repository.Measurements
            .Where(item =>
                item.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
                item.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
                item.ProcessCode.Equals(processCode, StringComparison.OrdinalIgnoreCase) &&
                item.OperationSeq == request.OperationSeq &&
                item.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(item.InspectionPhase).Equals(phase, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Timestamp)
            .ThenByDescending(item => item.SubmittedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();
        if (latestMeasurement is null)
        {
            return null;
        }

        var recordedRuns = repository.JobPhaseCompletions
            .Where(item =>
                item.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
                item.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
                item.ProcessCode.Equals(processCode, StringComparison.OrdinalIgnoreCase) &&
                item.OperationSeq == request.OperationSeq &&
                item.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(item.InspectionPhase).Equals(phase, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CompletedAt)
            .ToArray();
        var previousCompletionAt = recordedRuns.FirstOrDefault()?.CompletedAt;
        var candidates = repository.Measurements
            .Where(item =>
                item.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
                item.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
                item.ProcessCode.Equals(processCode, StringComparison.OrdinalIgnoreCase) &&
                item.OperationSeq == request.OperationSeq &&
                item.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(item.InspectionPhase).Equals(phase, StringComparison.OrdinalIgnoreCase) &&
                item.Timestamp > (previousCompletionAt ?? new DateTimeOffset(latestMeasurement.Timestamp.Date, latestMeasurement.Timestamp.Offset)) &&
                item.Timestamp <= latestMeasurement.Timestamp)
            .ToArray();
        var plans = PlansRequiredOrEnteredForCompletion(phasePlans, candidates, request.MachineCounter);
        if (plans.Count == 0)
        {
            return null;
        }

        var completionMeasurementIds = BuildLatestCompletionSet(plans, candidates);
        if (completionMeasurementIds.Count == 0)
        {
            return null;
        }

        var completion = new JobPhaseCompletion
        {
            JobNum = jobNum,
            PartNum = partNum,
            ProcessCode = processCode,
            OperationSeq = request.OperationSeq,
            ResourceId = resourceId,
            InspectionPhase = phase,
            CompletionNumber = recordedRuns.Select(item => Math.Max(item.CompletionNumber, 1)).DefaultIfEmpty(0).Max() + 1,
            CompletedByUserId = latestMeasurement.OperatorUserId,
            OperatorShift = latestMeasurement.OperatorShift,
            CompletedAt = latestMeasurement.Timestamp
        };
        completion.MeasurementIds.AddRange(completionMeasurementIds);
        repository.JobPhaseCompletions.Add(completion);
        return completion;
    }

    private IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> PlansForMeasurementPhase(InspectionMeasurement measurement, string phase)
    {
        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(measurement.PartNum, StringComparison.OrdinalIgnoreCase));
        var process = repository.Processes.FirstOrDefault(item => item.ProcessCode.Equals(measurement.ProcessCode, StringComparison.OrdinalIgnoreCase));
        if (part is null || process is null)
        {
            return [];
        }

        var operation = repository.Operations.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.ProcessId == process.Id &&
            item.OperationSeq == measurement.OperationSeq);
        if (operation is null)
        {
            return [];
        }

        return (from characteristic in repository.Characteristics
                join plan in repository.InspectionPlans on characteristic.Id equals plan.CharacteristicId
                where characteristic.OperationId == operation.Id &&
                    plan.SampleSize > 0 &&
                    NormalizeInspectionPhase(plan.InspectionPhase).Equals(phase, StringComparison.OrdinalIgnoreCase)
                orderby plan.DisplayOrder, characteristic.Name
                select (plan, characteristic))
            .ToArray();
    }

    private IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> PlansForPhase(
        string partNum,
        string processCode,
        int operationSeq,
        string phase)
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
                    NormalizeInspectionPhase(plan.InspectionPhase).Equals(phase, StringComparison.OrdinalIgnoreCase)
                orderby plan.DisplayOrder, characteristic.Name
                select (plan, characteristic))
            .ToArray();
    }

    private static IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> DuePlansForCompletion(
        IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> plans,
        long? machineCounter)
    {
        return plans
            .Where(item => IsPlanDueAtMachineCounter(item.Plan, machineCounter))
            .ToArray();
    }

    private static bool IsPlanDueAtMachineCounter(InspectionPlan plan, long? machineCounter)
    {
        if (plan.Frequency.Type != FrequencyType.Quantity)
        {
            return true;
        }

        if (machineCounter is null)
        {
            return true;
        }

        var every = Math.Max(plan.Frequency.Value, 1);
        var firstDue = Math.Max(plan.Frequency.FirstDueValue ?? every, 1);
        return machineCounter.Value >= firstDue;
    }

    private static IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> PlansRequiredOrEnteredForCompletion(
        IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> plans,
        IReadOnlyList<InspectionMeasurement> candidates,
        long? machineCounter)
    {
        return plans
            .Where(item =>
                IsPlanDueAtMachineCounter(item.Plan, machineCounter) ||
                HasEnoughMeasurementsForPlan(item, candidates))
            .ToArray();
    }

    private static bool HasEnoughMeasurementsForPlan(
        (InspectionPlan Plan, Characteristic Characteristic) plan,
        IReadOnlyList<InspectionMeasurement> candidates)
    {
        var required = Math.Max(plan.Plan.SampleSize, 1);
        return candidates.Count(measurement =>
            measurement.CharacteristicName.Equals(plan.Characteristic.Name, StringComparison.OrdinalIgnoreCase)) >= required;
    }

    private IReadOnlyList<Guid> MeasurementIdsForCompletionWindow(
        InspectionMeasurement measurement,
        string phase,
        IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> plans,
        DateTimeOffset? previousCompletionAt,
        DateTimeOffset completedAt,
        bool requireFinalPlanMeasurement = true)
    {
        if (plans.Count == 0)
        {
            return [];
        }

        var finalPlan = plans[^1];
        if (requireFinalPlanMeasurement && !measurement.CharacteristicName.Equals(finalPlan.Characteristic.Name, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var lowerBound = previousCompletionAt ?? new DateTimeOffset(completedAt.Date, completedAt.Offset);
        var candidates = repository.Measurements
            .Where(item =>
                item.JobNum.Equals(measurement.JobNum, StringComparison.OrdinalIgnoreCase) &&
                item.PartNum.Equals(measurement.PartNum, StringComparison.OrdinalIgnoreCase) &&
                item.ProcessCode.Equals(measurement.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                item.OperationSeq == measurement.OperationSeq &&
                item.ResourceId.Equals(measurement.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(item.InspectionPhase).Equals(phase, StringComparison.OrdinalIgnoreCase) &&
                item.Timestamp > lowerBound &&
                item.Timestamp <= completedAt)
            .ToArray();

        return BuildCompletionWindow(plans, candidates);
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

    private static IReadOnlyList<Guid> BuildLatestCompletionSet(
        IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> plans,
        IReadOnlyList<InspectionMeasurement> measurements)
    {
        var selected = new List<InspectionMeasurement>();
        foreach (var plan in plans)
        {
            var required = Math.Max(plan.Plan.SampleSize, 1);
            var matches = measurements
                .Where(measurement => measurement.CharacteristicName.Equals(plan.Characteristic.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(measurement => measurement.Timestamp)
                .ThenByDescending(measurement => measurement.SubmittedAt)
                .ThenByDescending(measurement => measurement.Id)
                .Take(required)
                .ToArray();
            if (matches.Length < required)
            {
                return [];
            }

            selected.AddRange(matches);
        }

        return selected
            .DistinctBy(measurement => measurement.Id)
            .OrderBy(measurement => measurement.Timestamp)
            .ThenBy(measurement => measurement.SubmittedAt)
            .ThenBy(measurement => measurement.Id)
            .Select(measurement => measurement.Id)
            .ToArray();
    }

    private ServiceResult UpsertJob(InspectionMeasurementEntry entry)
    {
        var jobNum = entry.JobNum.Trim();
        var partNum = entry.PartNum.Trim();
        var existing = repository.Jobs.FirstOrDefault(job => job.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            repository.Jobs.Add(new Job { JobNum = jobNum, PartNum = partNum });
            return ServiceResult.Ok();
        }

        if (!existing.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult.Fail($"Job {jobNum} is already assigned to part {existing.PartNum}.");
        }

        return ServiceResult.Ok();
    }

    private InspectionMeasurement? FindDuplicate(string? deviceId, string? clientRecordId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(clientRecordId))
        {
            return null;
        }

        return repository.Measurements.FirstOrDefault(item =>
            item.DeviceId?.Equals(deviceId.Trim(), StringComparison.OrdinalIgnoreCase) == true &&
            item.ClientRecordId?.Equals(clientRecordId.Trim(), StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool MatchesMeasurementSlot(InspectionMeasurement measurement, InspectionMeasurementEntry entry)
    {
        return measurement.JobNum.Equals(entry.JobNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
            measurement.PartNum.Equals(entry.PartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
            measurement.ProcessCode.Equals(entry.ProcessCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
            measurement.OperationSeq == entry.OperationSeq &&
            measurement.ResourceId.Equals(entry.ResourceId.Trim(), StringComparison.OrdinalIgnoreCase) &&
            measurement.CharacteristicName.Equals(entry.CharacteristicName.Trim(), StringComparison.OrdinalIgnoreCase) &&
            NormalizeInspectionPhase(measurement.InspectionPhase).Equals(NormalizeInspectionPhase(entry.InspectionPhase), StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ValidateCompletion(CompleteInspectionRequest request)
    {
        var errors = new List<string>();
        Required(request.JobNum, nameof(request.JobNum), errors);
        Required(request.PartNum, nameof(request.PartNum), errors);
        Required(request.ProcessCode, nameof(request.ProcessCode), errors);
        Required(request.ResourceId, nameof(request.ResourceId), errors);
        Required(request.InspectionPhase, nameof(request.InspectionPhase), errors);
        if (request.OperationSeq <= 0)
        {
            errors.Add($"{nameof(request.OperationSeq)} is required.");
        }

        if (request.MachineCounter is null)
        {
            errors.Add("Machine Counter is required.");
        }
        else if (request.MachineCounter < 0)
        {
            errors.Add("Machine Counter cannot be negative.");
        }

        return errors;
    }

    private bool HasActiveAlertForMeasurement(Guid measurementId)
    {
        return repository.RuleViolations
            .Where(violation => violation.MeasurementIds.Contains(measurementId))
            .Join(
                repository.Alerts.Where(alert => alert.Status == AlertStatus.Active),
                violation => violation.AlertId,
                alert => alert.Id,
                (_, _) => true)
            .Any();
    }

    private bool ClearDraftAlertsForMeasurement(Guid measurementId)
    {
        var alertIds = repository.RuleViolations
            .Where(violation => violation.MeasurementIds.Contains(measurementId))
            .Select(violation => violation.AlertId)
            .ToHashSet();
        if (alertIds.Count == 0)
        {
            return true;
        }

        var affectedMeasurementIds = repository.RuleViolations
            .Where(violation => alertIds.Contains(violation.AlertId))
            .SelectMany(violation => violation.MeasurementIds)
            .ToHashSet();
        if (affectedMeasurementIds.Any(IsCompletedMeasurement))
        {
            return false;
        }

        repository.RuleViolations.RemoveAll(violation => alertIds.Contains(violation.AlertId));
        repository.Alerts.RemoveAll(alert => alertIds.Contains(alert.Id) && alert.Status == AlertStatus.Active);
        return true;
    }

    private bool IsCompletedMeasurement(Guid measurementId)
    {
        return repository.JobPhaseCompletions.Any(completion => completion.MeasurementIds.Contains(measurementId));
    }

    private ProcessAlert? FindActiveLock(InspectionMeasurementEntry entry)
    {
        return repository.Alerts
            .Where(alert =>
            alert.Status == AlertStatus.Active &&
            alert.JobNum.Equals(entry.JobNum, StringComparison.OrdinalIgnoreCase) &&
            alert.PartNum.Equals(entry.PartNum, StringComparison.OrdinalIgnoreCase) &&
            alert.ResourceId.Equals(entry.ResourceId, StringComparison.OrdinalIgnoreCase) &&
            alert.CharacteristicName.Equals(entry.CharacteristicName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(alert => alert.LockedAt)
            .FirstOrDefault();
    }

    private bool InspectionTargetExists(InspectionMeasurementEntry entry)
    {
        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(entry.PartNum, StringComparison.OrdinalIgnoreCase));
        var process = repository.Processes.FirstOrDefault(item => item.ProcessCode.Equals(entry.ProcessCode, StringComparison.OrdinalIgnoreCase));
        if (part is null || process is null)
        {
            return false;
        }

        var operation = repository.Operations.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.ProcessId == process.Id &&
            item.OperationSeq == entry.OperationSeq);
        if (operation is null)
        {
            return false;
        }

        return repository.Characteristics.Any(item =>
            item.OperationId == operation.Id &&
            item.Name.Equals(entry.CharacteristicName, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanEnterInspections(string userName, string partNum)
    {
        var user = repository.Users.FirstOrDefault(user => user.UserName.Equals(userName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user?.Roles.Any(role => role.Permissions.Contains(PermissionNames.CanEnterInspections)) != true)
        {
            return false;
        }

        if (user.Roles.Any(role =>
            role.Name.Equals(RoleNames.QA, StringComparison.OrdinalIgnoreCase) ||
            role.Name.Equals(RoleNames.GOD, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var productGroup = repository.Parts
            .FirstOrDefault(part => part.PartNum.Equals(partNum.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.ProductGroup;
        productGroup = string.IsNullOrWhiteSpace(productGroup) ? "General Production" : productGroup.Trim();
        if (productGroup.Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            productGroup = "General Production";
        }
        return user.ProductGroups.Any(group => group.Equals(productGroup, StringComparison.OrdinalIgnoreCase));
    }

    private void CreateAlertsForViolations(InspectionMeasurement measurement, InspectionMeasurementEntry entry)
    {
        var characteristic = FindCharacteristic(entry);
        if (characteristic?.Type == CharacteristicType.Attribute)
        {
            CreateAttributeRejectAlert(measurement);
            return;
        }

        var plan = FindInspectionPlan(characteristic);
        var ruleSet = ResolveRuleSet(plan);
        if (plan is null || string.Equals(ruleSet, "None", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var limits = repository.ControlLimits.FirstOrDefault(limit =>
            limit.PartNum.Equals(measurement.PartNum, StringComparison.OrdinalIgnoreCase) &&
            limit.ProcessCode.Equals(measurement.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
            limit.OperationSeq == measurement.OperationSeq &&
            limit.CharacteristicName.Equals(measurement.CharacteristicName, StringComparison.OrdinalIgnoreCase));

        if (limits is null)
        {
            return;
        }

        if (string.Equals(ruleSet, "SpecLimitOnly", StringComparison.OrdinalIgnoreCase))
        {
            CreateSpecLimitAlert(measurement);
            return;
        }

        if (!CanEvaluateDriftRules(limits))
        {
            return;
        }

        var points = repository.Measurements
            .Where(item =>
                item.JobNum.Equals(measurement.JobNum, StringComparison.OrdinalIgnoreCase) &&
                item.PartNum.Equals(measurement.PartNum, StringComparison.OrdinalIgnoreCase) &&
                item.ProcessCode.Equals(measurement.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                item.OperationSeq == measurement.OperationSeq &&
                item.ResourceId.Equals(measurement.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                item.CharacteristicName.Equals(measurement.CharacteristicName, StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(item.InspectionPhase).Equals(NormalizeInspectionPhase(measurement.InspectionPhase), StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Timestamp)
            .Select(item => new WesternElectricPoint(item.Id, item.Value, item.Timestamp))
            .ToArray();

        var violations = DetectRuleViolations(ruleSet, points, limits.CenterLine, limits.Lcl, limits.Ucl);
        foreach (var violation in violations.Where(violation => violation.MeasurementIds.Contains(measurement.Id)))
        {
            var alertExists = repository.RuleViolations.Any(existing =>
                existing.RuleTriggered == violation.RuleTriggered &&
                existing.MeasurementIds.SequenceEqual(violation.MeasurementIds));
            if (alertExists)
            {
                continue;
            }

            var alert = new ProcessAlert
            {
                JobNum = measurement.JobNum,
                PartNum = measurement.PartNum,
                ResourceId = measurement.ResourceId,
                CharacteristicName = measurement.CharacteristicName,
                OperatorUserId = measurement.OperatorUserId,
                OperatorShift = measurement.OperatorShift,
                RuleTriggered = violation.RuleTriggered,
                Detail = DriftViolationDetail(violation, points, limits),
                LockedAt = violation.DetectedAt
            };

            repository.Alerts.Add(alert);
            var ruleViolation = new RuleViolation
            {
                AlertId = alert.Id,
                RuleTriggered = violation.RuleTriggered,
                DetectedAt = violation.DetectedAt
            };
            ruleViolation.MeasurementIds.AddRange(violation.MeasurementIds);
            repository.RuleViolations.Add(ruleViolation);
        }
    }

    private static string DriftViolationDetail(WesternElectricViolation violation, IReadOnlyList<WesternElectricPoint> points, ControlLimitSet limits)
    {
        var values = points
            .Where(point => violation.MeasurementIds.Contains(point.MeasurementId))
            .OrderBy(point => point.Timestamp)
            .Select(point => point.Value.ToString("0.#####"))
            .ToArray();
        var valueText = values.Length == 0 ? "No values listed" : string.Join(", ", values);
        return $"{RuleText(violation.RuleTriggered)} was detected using prior measurements for this same job, machine, operation, phase, and inspection item. Values: {valueText}. Control limits: LCL {limits.Lcl:0.#####}, center {limits.CenterLine:0.#####}, UCL {limits.Ucl:0.#####}.";
    }

    private IReadOnlyList<WesternElectricViolation> DetectRuleViolations(
        string ruleSet,
        IReadOnlyList<WesternElectricPoint> points,
        decimal centerLine,
        decimal lcl,
        decimal ucl)
    {
        if (string.Equals(ruleSet, "WesternElectric", StringComparison.OrdinalIgnoreCase))
        {
            return westernElectricRuleService.Detect(points, centerLine, lcl, ucl);
        }

        var sigma = Sigma(centerLine, lcl, ucl);
        if (string.Equals(ruleSet, "NelsonRules", StringComparison.OrdinalIgnoreCase))
        {
            return DetectNelson(points, centerLine, lcl, ucl);
        }

        if (string.Equals(ruleSet, "Cusum", StringComparison.OrdinalIgnoreCase))
        {
            return DetectCusum(points, centerLine, sigma);
        }

        if (string.Equals(ruleSet, "Ewma", StringComparison.OrdinalIgnoreCase))
        {
            return DetectEwma(points, centerLine, sigma);
        }

        if (string.Equals(ruleSet, "MovingAverageTrend", StringComparison.OrdinalIgnoreCase))
        {
            return DetectMovingAverageTrend(points, centerLine, sigma);
        }

        if (string.Equals(ruleSet, "LinearTrendSlope", StringComparison.OrdinalIgnoreCase))
        {
            return DetectLinearTrendSlope(points, sigma);
        }

        if (string.Equals(ruleSet, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            return DetectCustom(points, centerLine, lcl, ucl, sigma, repository.Settings.CustomDriftRule);
        }

        return [];
    }

    private IReadOnlyList<WesternElectricViolation> DetectNelson(IReadOnlyList<WesternElectricPoint> points, decimal centerLine, decimal lcl, decimal ucl)
    {
        var violations = westernElectricRuleService.Detect(points, centerLine, lcl, ucl).ToList();
        for (var i = 0; i <= points.Count - 6; i++)
        {
            var window = points.Skip(i).Take(6).ToArray();
            var increasing = window.Zip(window.Skip(1), (a, b) => b.Value > a.Value).All(BooleanIdentity);
            var decreasing = window.Zip(window.Skip(1), (a, b) => b.Value < a.Value).All(BooleanIdentity);
            if (increasing || decreasing)
            {
                violations.Add(new WesternElectricViolation(
                    RuleTriggered.NelsonTrend,
                    window.Select(point => point.MeasurementId).ToArray(),
                    window[^1].Timestamp));
            }
        }

        return violations;
    }

    private static IReadOnlyList<WesternElectricViolation> DetectCusum(IReadOnlyList<WesternElectricPoint> points, decimal centerLine, decimal sigma)
    {
        var positive = 0m;
        var negative = 0m;
        var reference = 0.5m * sigma;
        var limit = 5m * sigma;
        for (var i = 0; i < points.Count; i++)
        {
            positive = Math.Max(0m, positive + points[i].Value - centerLine - reference);
            negative = Math.Min(0m, negative + points[i].Value - centerLine + reference);
            if (positive > limit || Math.Abs(negative) > limit)
            {
                return [new WesternElectricViolation(
                    RuleTriggered.CusumShift,
                    points.Take(i + 1).TakeLast(Math.Min(i + 1, 10)).Select(point => point.MeasurementId).ToArray(),
                    points[i].Timestamp)];
            }
        }

        return [];
    }

    private static IReadOnlyList<WesternElectricViolation> DetectEwma(IReadOnlyList<WesternElectricPoint> points, decimal centerLine, decimal sigma)
    {
        if (points.Count < 3)
        {
            return [];
        }

        const decimal lambda = 0.2m;
        var ewma = centerLine;
        var limit = 3m * sigma * (decimal)Math.Sqrt((double)(lambda / (2m - lambda)));
        foreach (var point in points)
        {
            ewma = lambda * point.Value + (1m - lambda) * ewma;
            if (Math.Abs(ewma - centerLine) > limit)
            {
                return [new WesternElectricViolation(
                    RuleTriggered.EwmaShift,
                    [point.MeasurementId],
                    point.Timestamp)];
            }
        }

        return [];
    }

    private static IReadOnlyList<WesternElectricViolation> DetectMovingAverageTrend(IReadOnlyList<WesternElectricPoint> points, decimal centerLine, decimal sigma)
    {
        if (points.Count < 5)
        {
            return [];
        }

        var window = points.TakeLast(5).ToArray();
        var average = window.Average(point => point.Value);
        if (Math.Abs(average - centerLine) >= sigma)
        {
            return [new WesternElectricViolation(
                RuleTriggered.MovingAverageTrend,
                window.Select(point => point.MeasurementId).ToArray(),
                window[^1].Timestamp)];
        }

        return [];
    }

    private static IReadOnlyList<WesternElectricViolation> DetectLinearTrendSlope(IReadOnlyList<WesternElectricPoint> points, decimal sigma)
    {
        if (points.Count < 6)
        {
            return [];
        }

        var window = points.TakeLast(6).ToArray();
        var n = window.Length;
        var meanX = (n - 1) / 2m;
        var meanY = window.Average(point => point.Value);
        var numerator = window.Select((point, index) => ((decimal)index - meanX) * (point.Value - meanY)).Sum();
        var denominator = window.Select((_, index) => ((decimal)index - meanX) * ((decimal)index - meanX)).Sum();
        var slope = denominator == 0m ? 0m : numerator / denominator;
        var netChange = Math.Abs(window[^1].Value - window[0].Value);
        if (Math.Abs(slope) >= sigma / 3m && netChange >= sigma)
        {
            return [new WesternElectricViolation(
                RuleTriggered.LinearTrendSlope,
                window.Select(point => point.MeasurementId).ToArray(),
                window[^1].Timestamp)];
        }

        return [];
    }

    private IReadOnlyList<WesternElectricViolation> DetectCustom(IReadOnlyList<WesternElectricPoint> points, decimal centerLine, decimal lcl, decimal ucl, decimal sigma, CustomDriftRuleSettings custom)
    {
        var violations = custom.IncludeWesternElectric
            ? westernElectricRuleService.Detect(points, centerLine, lcl, ucl).ToList()
            : [];
        var windowSize = Math.Clamp(custom.WindowSize, 2, 25);
        if (points.Count < windowSize)
        {
            return violations;
        }

        var window = points.TakeLast(windowSize).ToArray();
        var sigmaThreshold = custom.SigmaThreshold <= 0 ? 1m : custom.SigmaThreshold;
        var minimumPoints = Math.Clamp(custom.MinimumPointsBeyondThreshold, 1, windowSize);
        var upperThreshold = centerLine + sigma * sigmaThreshold;
        var lowerThreshold = centerLine - sigma * sigmaThreshold;
        var aboveCount = window.Count(point => point.Value > upperThreshold);
        var belowCount = window.Count(point => point.Value < lowerThreshold);
        var triggered = custom.Direction switch
        {
            "Above" => aboveCount >= minimumPoints,
            "Below" => belowCount >= minimumPoints,
            "EitherSide" => aboveCount + belowCount >= minimumPoints,
            _ => aboveCount >= minimumPoints || belowCount >= minimumPoints
        };

        if (triggered)
        {
            violations.Add(new WesternElectricViolation(
                RuleTriggered.CustomRuleTriggered,
                window.Select(point => point.MeasurementId).ToArray(),
                window[^1].Timestamp));
        }

        return violations;
    }

    private static decimal Sigma(decimal centerLine, decimal lcl, decimal ucl)
    {
        var sigma = (ucl - centerLine) / 3m;
        if (sigma <= 0 || centerLine <= lcl || centerLine >= ucl)
        {
            throw new ArgumentException("Control limits must surround the centerline and imply a positive sigma.");
        }

        return sigma;
    }

    private static bool BooleanIdentity(bool value) => value;

    private static bool CanEvaluateDriftRules(ControlLimitSet limits)
    {
        return limits.Lcl < limits.CenterLine && limits.CenterLine < limits.Ucl;
    }

    private InspectionPlan? FindInspectionPlan(Characteristic? characteristic)
    {
        return characteristic is null
            ? null
            : repository.InspectionPlans.FirstOrDefault(plan => plan.CharacteristicId == characteristic.Id);
    }

    private string ResolveRuleSet(InspectionPlan? plan)
    {
        return string.Equals(plan?.AlertRuleSet, "GlobalDefault", StringComparison.OrdinalIgnoreCase)
            ? repository.Settings.GlobalAlertRuleSet
            : plan?.AlertRuleSet ?? "None";
    }

    private void CreateSpecLimitAlert(InspectionMeasurement measurement)
    {
        var spec = FindSpecLimit(measurement);
        if (spec is null || measurement.Value >= spec.Lsl && measurement.Value <= spec.Usl)
        {
            return;
        }

        var alert = new ProcessAlert
        {
            JobNum = measurement.JobNum,
            PartNum = measurement.PartNum,
            ResourceId = measurement.ResourceId,
            CharacteristicName = measurement.CharacteristicName,
            OperatorUserId = measurement.OperatorUserId,
            OperatorShift = measurement.OperatorShift,
            RuleTriggered = RuleTriggered.SpecLimitViolation,
            Detail = SpecLimitDetail(measurement, spec),
            LockedAt = measurement.Timestamp
        };

        repository.Alerts.Add(alert);
        var ruleViolation = new RuleViolation
        {
            AlertId = alert.Id,
            RuleTriggered = RuleTriggered.SpecLimitViolation,
            DetectedAt = measurement.Timestamp
        };
        ruleViolation.MeasurementIds.Add(measurement.Id);
        repository.RuleViolations.Add(ruleViolation);
    }

    private SpecLimit? FindSpecLimit(InspectionMeasurement measurement)
    {
        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(measurement.PartNum, StringComparison.OrdinalIgnoreCase));
        var process = repository.Processes.FirstOrDefault(item => item.ProcessCode.Equals(measurement.ProcessCode, StringComparison.OrdinalIgnoreCase));
        if (part is null || process is null)
        {
            return null;
        }

        var operation = repository.Operations.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.ProcessId == process.Id &&
            item.OperationSeq == measurement.OperationSeq);
        if (operation is null)
        {
            return null;
        }

        var characteristic = repository.Characteristics.FirstOrDefault(item =>
            item.OperationId == operation.Id &&
            item.Name.Equals(measurement.CharacteristicName, StringComparison.OrdinalIgnoreCase));

        return characteristic is null
            ? null
            : repository.SpecLimits.FirstOrDefault(item => item.CharacteristicId == characteristic.Id);
    }

    private Characteristic? FindCharacteristic(InspectionMeasurementEntry entry)
    {
        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(entry.PartNum, StringComparison.OrdinalIgnoreCase));
        var process = repository.Processes.FirstOrDefault(item => item.ProcessCode.Equals(entry.ProcessCode, StringComparison.OrdinalIgnoreCase));
        if (part is null || process is null)
        {
            return null;
        }

        var operation = repository.Operations.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.ProcessId == process.Id &&
            item.OperationSeq == entry.OperationSeq);

        return operation is null
            ? null
            : repository.Characteristics.FirstOrDefault(item =>
                item.OperationId == operation.Id &&
                item.Name.Equals(entry.CharacteristicName, StringComparison.OrdinalIgnoreCase));
    }

    private void CreateAttributeRejectAlert(InspectionMeasurement measurement)
    {
        if (measurement.Value != 0m)
        {
            return;
        }

        var alert = new ProcessAlert
        {
            JobNum = measurement.JobNum,
            PartNum = measurement.PartNum,
            ResourceId = measurement.ResourceId,
            CharacteristicName = measurement.CharacteristicName,
            OperatorUserId = measurement.OperatorUserId,
            OperatorShift = measurement.OperatorShift,
            RuleTriggered = RuleTriggered.AttributeRejected,
            Detail = "Attribute was rejected.",
            LockedAt = measurement.Timestamp
        };

        repository.Alerts.Add(alert);
        var ruleViolation = new RuleViolation
        {
            AlertId = alert.Id,
            RuleTriggered = RuleTriggered.AttributeRejected,
            DetectedAt = measurement.Timestamp
        };
        ruleViolation.MeasurementIds.Add(measurement.Id);
        repository.RuleViolations.Add(ruleViolation);
    }

    private static List<string> Validate(InspectionMeasurementEntry entry)
    {
        var errors = new List<string>();
        Required(entry.JobNum, nameof(entry.JobNum), errors);
        Required(entry.PartNum, nameof(entry.PartNum), errors);
        Required(entry.ProcessCode, nameof(entry.ProcessCode), errors);
        Required(entry.ResourceId, nameof(entry.ResourceId), errors);
        Required(entry.CharacteristicName, nameof(entry.CharacteristicName), errors);
        Required(entry.OperatorUserId, nameof(entry.OperatorUserId), errors);
        if (!IsValidInspectionPhase(entry.InspectionPhase))
        {
            errors.Add("InspectionPhase must be Startup, Setup, In Process, Coil Change, Spool, or End of Spool.");
        }

        if (entry.OperationSeq <= 0)
        {
            errors.Add("OperationSeq must be greater than zero.");
        }

        return errors;
    }

    private static decimal NormalizeMeasurementValue(decimal value)
    {
        return decimal.Round(value, MaxMeasurementDecimalPlaces, MidpointRounding.AwayFromZero);
    }

    private static void Required(string value, string field, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} is required.");
        }
    }

    private static string? CleanOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private string OperatorShift(string userName)
    {
        return repository.Users
            .FirstOrDefault(user => user.UserName.Equals(userName.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.Shift
            .Trim() ?? string.Empty;
    }

    private static string NormalizeInspectionPhase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "In Process";
        }

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

    private static string ActiveLockMessage(ProcessAlert alert)
    {
        var detail = string.IsNullOrWhiteSpace(alert.Detail) ? string.Empty : $" {alert.Detail}";
        return $"{alert.CharacteristicName} is locked for job {alert.JobNum} on {alert.ResourceId} due to {RuleText(alert.RuleTriggered)} at {alert.LockedAt:MM/dd/yyyy HH:mm}.{detail} Clear that lock before entering more {alert.CharacteristicName} measurements.";
    }

    private static string SpecLimitDetail(InspectionMeasurement measurement, SpecLimit spec)
    {
        if (measurement.Value > spec.Usl)
        {
            return $"Entered value {measurement.Value:0.#####} is above the upper specification limit {spec.Usl:0.#####}.";
        }

        if (measurement.Value < spec.Lsl)
        {
            return $"Entered value {measurement.Value:0.#####} is below the lower specification limit {spec.Lsl:0.#####}.";
        }

        return string.Empty;
    }

    private static string RuleText(RuleTriggered rule)
    {
        return rule switch
        {
            RuleTriggered.OnePointBeyondControlLimit => "one point beyond the control limit",
            RuleTriggered.TwoOfThreeNearControlLimit => "two of three points near the control limit",
            RuleTriggered.FourOfFiveApproachingLimit => "four of five points approaching the limit",
            RuleTriggered.EightConsecutiveOneSideOfCenterline => "eight consecutive points on one side of center",
            RuleTriggered.SpecLimitViolation => "a spec limit violation",
            RuleTriggered.NelsonTrend => "a Nelson trend signal",
            RuleTriggered.CusumShift => "a CUSUM shift",
            RuleTriggered.EwmaShift => "an EWMA shift",
            RuleTriggered.MovingAverageTrend => "a moving average trend",
            RuleTriggered.LinearTrendSlope => "a linear trend/slope signal",
            RuleTriggered.CustomRuleTriggered => "a custom rule trigger",
            RuleTriggered.AttributeRejected => "an accept/reject failure",
            _ => rule.ToString()
        };
    }

    private static bool IsValidInspectionPhase(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ||
            value.Trim().Equals("Startup", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Set Up", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Setup", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Coil Change", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("CoilChange", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Spool", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Spool Start", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("End of Spool", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("EndOfSpool", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Spool End", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("In Process", StringComparison.OrdinalIgnoreCase);
    }
}




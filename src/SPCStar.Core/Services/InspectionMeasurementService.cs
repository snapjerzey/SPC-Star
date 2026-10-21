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
    string InspectionPhase = "In Process",
    int? GagePinNumber = null);

public sealed record CompleteInspectionRequest(
    string JobNum,
    string PartNum,
    string ProcessCode,
    int OperationSeq,
    string ResourceId,
    string InspectionPhase,
    long? MachineCounter,
    IReadOnlyList<string>? MeasurementClientRecordIds = null);

public sealed record FailInspectionRequest(
    string JobNum,
    string PartNum,
    string ProcessCode,
    int OperationSeq,
    string ResourceId,
    string InspectionPhase,
    string FailedByUserId,
    long? MachineCounter,
    IReadOnlyList<string>? MeasurementClientRecordIds = null,
    Guid? ActiveAlertId = null,
    string? Reason = null,
    string? CauseCategory = null,
    string? CauseText = null,
    string? SolutionText = null,
    string? SignOffUserName = null,
    string? SignOffPassword = null,
    IReadOnlyList<string>? RequiredCharacteristicNames = null);

internal sealed record ResolvedSpecLimit(decimal? Nominal, decimal? Lsl, decimal? Usl);

public sealed class InspectionMeasurementService(
    ISpcRepository repository,
    WesternElectricRuleService westernElectricRuleService)
{
    private const int MaxMeasurementDecimalPlaces = 5;

    public ServiceResult<InspectionMeasurement> EnterMeasurement(InspectionMeasurementEntry entry)
    {
        var errors = Validate(entry);
        if (entry.Value < 0 && SchneiderContactLocationRules.IsDirectionCheck(entry.PartNum, entry.ProcessCode, entry.CharacteristicName))
            errors.Add("Inside/Outside measurements must be entered as a non-negative magnitude in the matching direction field.");
        if (entry.GagePinNumber is not null)
        {
            var characteristic = FindCharacteristic(entry);
            var part = repository.Parts.FirstOrDefault(p => p.PartNum.Equals(entry.PartNum, StringComparison.OrdinalIgnoreCase));
            var diameter = GagePinBlockRules.TryMapPinToDiameter(part?.ProductGroup, characteristic?.Name,
                characteristic?.InspectionMethod, part?.HoleSize, entry.GagePinNumber.Value);
            if (diameter is null || diameter != entry.Value) errors.Add("Gage pin number does not match the configured block diameter.");
        }
        if (errors.Count > 0)
        {
            return ServiceResult<InspectionMeasurement>.Fail(errors);
        }

        if (SchneiderShearNoteRules.IsWithdrawnEntry(entry.PartNum, entry.ProcessCode, entry.CharacteristicName,
            NormalizeInspectionPhase(entry.InspectionPhase)))
        {
            var characteristic = FindCharacteristic(entry);
            if (characteristic is not null && !repository.InspectionPlans.Any(p => p.CharacteristicId == characteristic.Id &&
                p.InspectionPhase.Equals(NormalizeInspectionPhase(entry.InspectionPhase), StringComparison.OrdinalIgnoreCase)))
                return ServiceResult<InspectionMeasurement>.Fail("This contact-tape change entry is not a scheduled inspection. Reload the inspection to use the current inspection plan.");
        }

        if (SchneiderContactLocationRules.IsCombinedCheck(entry.PartNum, entry.ProcessCode, entry.CharacteristicName))
        {
            var old = FindCharacteristic(entry);
            var replacements = repository.Characteristics.Where(c => c.OperationId == old?.OperationId &&
                SchneiderContactLocationRules.DirectionChecks.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (replacements.Length == 4 && replacements.All(c => repository.InspectionPlans.Any(p => p.CharacteristicId == c.Id &&
                p.InspectionPhase.Equals(NormalizeInspectionPhase(entry.InspectionPhase), StringComparison.OrdinalIgnoreCase))))
                return ServiceResult<InspectionMeasurement>.Fail("The combined Inside/Outside check has been replaced. Reload the inspection and use the separate Contact A/B Inside and Outside fields.");
        }

        var materialIssueMessage = JobMaterialRules.RequiredIssueMessage(repository, entry.JobNum, entry.PartNum);
        if (materialIssueMessage is not null) return ServiceResult<InspectionMeasurement>.Fail(materialIssueMessage);

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
            GagePinNumber = entry.GagePinNumber,
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
        if (measurement.Value == normalizedValue && measurement.GagePinNumber == entry.GagePinNumber)
        {
            return ServiceResult<InspectionMeasurement>.Ok(measurement);
        }

        var draftLockAlertIds = DraftLockAlertIdsForMeasurement(measurement.Id);
        if (draftLockAlertIds.Count > 0)
        {
            return ServiceResult<InspectionMeasurement>.Fail("This sample has an active lock. Clear the lock before changing the measurement.");
        }

        var activeLock = FindActiveLock(entry);
        if (activeLock is not null && !draftLockAlertIds.Contains(activeLock.Id))
        {
            return ServiceResult<InspectionMeasurement>.Fail(ActiveLockMessage(activeLock));
        }

        measurement.Value = normalizedValue;
        measurement.GagePinNumber = entry.GagePinNumber;
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
            CompletionNumber = NextCompletionNumber(measurement.JobNum, measurement.PartNum, measurement.ProcessCode, measurement.OperationSeq, phase),
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
        var counterError = JobMachineCounterRules.Validate(repository, request.JobNum, request.MachineCounter);
        if (counterError is not null) errors.Add(counterError);
        if (errors.Count > 0)
        {
            return ServiceResult<JobPhaseCompletion>.Fail(errors);
        }

        var materialIssueMessage = JobMaterialRules.RequiredIssueMessage(repository, request.JobNum, request.PartNum);
        if (materialIssueMessage is not null) return ServiceResult<JobPhaseCompletion>.Fail(materialIssueMessage);

        var phase = NormalizeInspectionPhase(request.InspectionPhase);
        var hasClientRecordIds = request.MeasurementClientRecordIds?.Any(id => !string.IsNullOrWhiteSpace(id)) == true;
        var completion = FindCompletionForClientRecords(request, phase);
        if (completion is null && !hasClientRecordIds)
        {
            completion = repository.JobPhaseCompletions
                .Where(item =>
                    item.JobNum.Equals(request.JobNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    item.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    item.ProcessCode.Equals(request.ProcessCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    item.OperationSeq == request.OperationSeq &&
                    item.ResourceId.Equals(request.ResourceId.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    NormalizeInspectionPhase(item.InspectionPhase).Equals(phase, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.CompletedAt)
                .FirstOrDefault();
        }

        if (completion is null)
        {
            completion = TryCreateCompletionFromClientRecords(request, phase) ?? TryCreateCompletionFromSavedMeasurements(request, phase);
            if (completion is null)
            {
                var ids = MeasurementIdsForClientRecords(request, phase);
                var readings = repository.Measurements.Where(item => ids.Contains(item.Id)).ToArray();
                var missing = PlansRequiredOrEnteredForCompletion(
                    PlansForPhase(request.PartNum.Trim(), request.ProcessCode.Trim(), request.OperationSeq, phase), readings, request)
                    .Select(item => new { item.Characteristic.Name, Remaining = RequiredMeasurementCount(item) - readings.Count(reading =>
                        reading.CharacteristicName.Equals(item.Characteristic.Name, StringComparison.OrdinalIgnoreCase)) })
                    .Where(item => item.Remaining > 0)
                    .Select(item => $"{item.Name} ({item.Remaining} more sample{(item.Remaining == 1 ? "" : "s")})");
                var details = hasClientRecordIds ? string.Join(", ", missing) : "";
                return ServiceResult<JobPhaseCompletion>.Fail("No completed inspection was found for this job, part, machine, operation, and phase." +
                    (details.Length > 0 ? $" Complete and save: {details}." : ""));
            }
        }

        completion.MachineCounter = request.MachineCounter!.Value;
        if (!completion.WatchEvaluated)
        {
            EvaluateCompletedCustomDrift(completion);
            completion.WatchEvaluated = true;
        }
        return ServiceResult<JobPhaseCompletion>.Ok(completion);
    }

    public ServiceResult<FailedInspection> FailInspectionWithSignoff(FailInspectionRequest request)
    {
        var signer = request.SignOffUserName?.Trim() ?? "";
        if (!new PermissionService(repository).UserHasPermission(signer, PermissionNames.CanOverrideDriftLock))
            return ServiceResult<FailedInspection>.Fail("A Line Tech or QA must sign off the failed inspection.");
        if (!new CredentialService(repository).ValidateCredential(signer, request.SignOffPassword ?? ""))
            return ServiceResult<FailedInspection>.Fail("Invalid failure signoff credentials.");
        return FailInspection(request with { FailedByUserId = signer });
    }

    public ServiceResult<FailedInspection> FailInspection(FailInspectionRequest request)
    {
        var errors = ValidateFailure(request);
        var counterError = JobMachineCounterRules.Validate(repository, request.JobNum, request.MachineCounter);
        if (counterError is not null) errors.Add(counterError);
        if (errors.Count > 0)
        {
            return ServiceResult<FailedInspection>.Fail(errors);
        }

        var phase = NormalizeInspectionPhase(request.InspectionPhase);
        var measurementIds = MeasurementIdsForFailure(request, phase);
        var failedAt = DateTimeOffset.UtcNow;
        var alert = FindFailureAlert(request);
        var failureReason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Inspection ended after lockout."
            : request.Reason.Trim();
        var causeCategory = CleanOptional(request.CauseCategory) ?? "Failed Inspection";
        var causeText = CleanOptional(request.CauseText) ?? alert?.Detail ?? failureReason;
        var solutionText = CleanOptional(request.SolutionText) ?? failureReason;
        var failureDetail = alert is not null
            ? $"{alert.CharacteristicName}: {alert.Detail}"
            : FailureDetailFromMeasurements(measurementIds);
        var failure = new FailedInspection
        {
            JobNum = request.JobNum.Trim(),
            PartNum = request.PartNum.Trim(),
            ProcessCode = request.ProcessCode.Trim(),
            OperationSeq = request.OperationSeq,
            ResourceId = request.ResourceId.Trim(),
            InspectionPhase = phase,
            FailureNumber = NextCompletionNumber(request.JobNum.Trim(), request.PartNum.Trim(), request.ProcessCode.Trim(), request.OperationSeq, phase),
            FailedByUserId = request.FailedByUserId.Trim(),
            OperatorShift = OperatorShift(request.FailedByUserId),
            FailedAt = failedAt,
            MachineCounter = request.MachineCounter,
            Reason = failureReason,
            CauseCategory = causeCategory,
            CauseText = causeText,
            SolutionText = solutionText,
            FailureDetail = failureDetail,
            AlertId = alert?.Id
        };
        failure.MeasurementIds.AddRange(measurementIds);
        var phaseNames = PlansForPhase(request.PartNum, request.ProcessCode, request.OperationSeq, phase)
            .Select(item => item.Characteristic.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        failure.RequiredCharacteristicNames.AddRange((request.RequiredCharacteristicNames ?? [])
            .Where(phaseNames.Contains).Distinct(StringComparer.OrdinalIgnoreCase));
        repository.FailedInspections.Add(failure);

        if (alert is not null && alert.Status == AlertStatus.Active)
        {
            alert.Status = AlertStatus.Overridden;
            repository.AlertOverrides.Add(new AlertOverride
            {
                AlertId = alert.Id,
                OperatorUserId = alert.OperatorUserId,
                OverrideUserId = request.FailedByUserId.Trim(),
                OverrideRole = "Failed Inspection",
                JobNum = alert.JobNum,
                PartNum = alert.PartNum,
                ResourceId = alert.ResourceId,
                CharacteristicName = alert.CharacteristicName,
                RuleTriggered = alert.RuleTriggered,
                CauseCategory = causeCategory,
                CauseText = causeText,
                SolutionText = solutionText,
                LockedAt = alert.LockedAt,
                UnlockedAt = failedAt,
                SubmittedAt = failedAt,
                SyncedAt = DateTimeOffset.UtcNow
            });
        }

        return ServiceResult<FailedInspection>.Ok(failure);
    }

    private JobPhaseCompletion? FindCompletionForClientRecords(CompleteInspectionRequest request, string phase)
    {
        var measurementIds = MeasurementIdsForClientRecords(request, phase);
        if (measurementIds.Count == 0)
        {
            return null;
        }

        return repository.JobPhaseCompletions
            .Where(item =>
                item.JobNum.Equals(request.JobNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                item.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                item.ProcessCode.Equals(request.ProcessCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                item.OperationSeq == request.OperationSeq &&
                item.ResourceId.Equals(request.ResourceId.Trim(), StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(item.InspectionPhase).Equals(phase, StringComparison.OrdinalIgnoreCase) &&
                item.MeasurementIds.Any(measurementIds.Contains))
            .OrderByDescending(item => item.MeasurementIds.Count(measurementIds.Contains))
            .ThenByDescending(item => item.CompletedAt)
            .FirstOrDefault();
    }

    private JobPhaseCompletion? TryCreateCompletionFromClientRecords(CompleteInspectionRequest request, string phase)
    {
        var measurementIds = MeasurementIdsForClientRecords(request, phase);
        if (measurementIds.Count == 0)
        {
            return null;
        }

        var measurements = repository.Measurements
            .Where(item => measurementIds.Contains(item.Id))
            .OrderBy(item => item.Timestamp)
            .ToArray();
        if (measurements.Length == 0)
        {
            return null;
        }

        var phasePlans = PlansForPhase(request.PartNum.Trim(), request.ProcessCode.Trim(), request.OperationSeq, phase);
        var plans = PlansRequiredOrEnteredForCompletion(phasePlans, measurements, request);
        var completionMeasurementIds = BuildClientRecordCompletionSet(plans, measurements);
        if (completionMeasurementIds.Count == 0)
        {
            return null;
        }

        var latestMeasurement = measurements.OrderByDescending(item => item.Timestamp).First();
        var completion = new JobPhaseCompletion
        {
            JobNum = request.JobNum.Trim(),
            PartNum = request.PartNum.Trim(),
            ProcessCode = request.ProcessCode.Trim(),
            OperationSeq = request.OperationSeq,
            ResourceId = request.ResourceId.Trim(),
            InspectionPhase = phase,
            CompletionNumber = NextCompletionNumber(request.JobNum.Trim(), request.PartNum.Trim(), request.ProcessCode.Trim(), request.OperationSeq, phase),
            CompletedByUserId = latestMeasurement.OperatorUserId,
            OperatorShift = latestMeasurement.OperatorShift,
            CompletedAt = latestMeasurement.Timestamp
        };
        completion.MeasurementIds.AddRange(completionMeasurementIds);
        repository.JobPhaseCompletions.Add(completion);
        return completion;
    }

    private HashSet<Guid> MeasurementIdsForClientRecords(CompleteInspectionRequest request, string phase)
    {
        var clientRecordIds = (request.MeasurementClientRecordIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (clientRecordIds.Count == 0)
        {
            return [];
        }

        return repository.Measurements
            .Where(item =>
                item.JobNum.Equals(request.JobNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                item.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                item.ProcessCode.Equals(request.ProcessCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                item.OperationSeq == request.OperationSeq &&
                item.ResourceId.Equals(request.ResourceId.Trim(), StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(item.InspectionPhase).Equals(phase, StringComparison.OrdinalIgnoreCase) &&
                item.ClientRecordId is not null &&
                clientRecordIds.Contains(item.ClientRecordId))
            .Select(item => item.Id)
            .ToHashSet();
    }

    private HashSet<Guid> MeasurementIdsForFailure(FailInspectionRequest request, string phase)
    {
        var clientRecordIds = (request.MeasurementClientRecordIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var measurementIds = repository.Measurements
            .Where(item =>
                item.JobNum.Equals(request.JobNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                item.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                item.ProcessCode.Equals(request.ProcessCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                item.OperationSeq == request.OperationSeq &&
                item.ResourceId.Equals(request.ResourceId.Trim(), StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(item.InspectionPhase).Equals(phase, StringComparison.OrdinalIgnoreCase) &&
                item.ClientRecordId is not null &&
                clientRecordIds.Contains(item.ClientRecordId))
            .Select(item => item.Id)
            .ToHashSet();

        if (request.ActiveAlertId.HasValue)
        {
            foreach (var id in repository.RuleViolations
                .Where(violation => violation.AlertId == request.ActiveAlertId.Value)
                .SelectMany(violation => violation.MeasurementIds))
            {
                measurementIds.Add(id);
            }
        }

        return measurementIds;
    }

    private ProcessAlert? FindFailureAlert(FailInspectionRequest request)
    {
        if (request.ActiveAlertId.HasValue)
        {
            return repository.Alerts.FirstOrDefault(alert => alert.Id == request.ActiveAlertId.Value);
        }

        return repository.Alerts
            .Where(alert =>
                alert.Status == AlertStatus.Active &&
                alert.JobNum.Equals(request.JobNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                alert.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                alert.ResourceId.Equals(request.ResourceId.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(alert => alert.LockedAt)
            .FirstOrDefault();
    }

    private string FailureDetailFromMeasurements(HashSet<Guid> measurementIds)
    {
        if (measurementIds.Count == 0)
        {
            return "";
        }

        return string.Join("; ", repository.Measurements
            .Where(measurement => measurementIds.Contains(measurement.Id))
            .OrderBy(measurement => measurement.Timestamp)
            .Select(measurement => $"{measurement.CharacteristicName}: {measurement.Value:0.#####}")
            .Distinct(StringComparer.OrdinalIgnoreCase));
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
        var plans = PlansRequiredOrEnteredForCompletion(phasePlans, candidates, request);
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
            CompletionNumber = NextCompletionNumber(jobNum, partNum, processCode, request.OperationSeq, phase),
            CompletedByUserId = latestMeasurement.OperatorUserId,
            OperatorShift = latestMeasurement.OperatorShift,
            CompletedAt = latestMeasurement.Timestamp
        };
        completion.MeasurementIds.AddRange(completionMeasurementIds);
        repository.JobPhaseCompletions.Add(completion);
        return completion;
    }

    private int NextCompletionNumber(string jobNum, string partNum, string processCode, int operationSeq, string phase)
    {
        return InspectionSequence.Next(repository, jobNum, partNum, processCode, operationSeq, phase);
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

    private IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> PlansRequiredOrEnteredForCompletion(
        IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> plans,
        IReadOnlyList<InspectionMeasurement> candidates,
        CompleteInspectionRequest request)
    {
        var schedule = new MachineCounterSchedule(repository);
        var frequency = new InspectionFrequencyService(repository);
        return plans.Where(item =>
        {
            if (candidates.Any(reading => reading.CharacteristicName.Equals(item.Characteristic.Name, StringComparison.OrdinalIgnoreCase))) return true;
            if (item.Plan.Frequency.Type != FrequencyType.Quantity || request.MachineCounter is null) return true;
            if (frequency.RequiresReinspection(new(request.JobNum, request.PartNum, request.ProcessCode, request.OperationSeq,
                item.Characteristic.Name, request.ResourceId, DateTimeOffset.UtcNow, null, null, [], request.InspectionPhase), item.Plan)) return true;
            var nextDue = schedule.NextDue(request.JobNum, request.PartNum, request.ProcessCode, request.OperationSeq,
                item.Characteristic.Name, item.Plan.Frequency.Value, item.Plan.Frequency.FirstDueValue);
            return request.MachineCounter.Value >= nextDue;
        }).ToArray();
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
                .Take(RequiredMeasurementCount(plan))
                .ToArray();
            if (matches.Length < RequiredMeasurementCount(plan))
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

    private static IReadOnlyList<Guid> BuildClientRecordCompletionSet(
        IReadOnlyList<(InspectionPlan Plan, Characteristic Characteristic)> plans,
        IReadOnlyList<InspectionMeasurement> measurements)
    {
        var selected = new List<InspectionMeasurement>();
        foreach (var plan in plans)
        {
            var matches = measurements
                .Where(measurement => measurement.CharacteristicName.Equals(plan.Characteristic.Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(measurement => measurement.Timestamp)
                .ThenBy(measurement => measurement.SubmittedAt)
                .ThenBy(measurement => measurement.Id)
                .Take(RequiredMeasurementCount(plan))
                .ToArray();
            if (matches.Length < RequiredMeasurementCount(plan))
            {
                return [];
            }

            selected.AddRange(matches);
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
            var required = RequiredMeasurementCount(plan);
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

    private static int RequiredMeasurementCount((InspectionPlan Plan, Characteristic Characteristic) plan)
    {
        return plan.Characteristic.Type == CharacteristicType.Attribute
            ? 1
            : Math.Max(plan.Plan.SampleSize, 1);
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

    private static List<string> ValidateFailure(FailInspectionRequest request)
    {
        var errors = new List<string>();
        Required(request.JobNum, nameof(request.JobNum), errors);
        Required(request.PartNum, nameof(request.PartNum), errors);
        Required(request.ProcessCode, nameof(request.ProcessCode), errors);
        Required(request.ResourceId, nameof(request.ResourceId), errors);
        Required(request.InspectionPhase, nameof(request.InspectionPhase), errors);
        Required(request.FailedByUserId, nameof(request.FailedByUserId), errors);
        if (request.CauseCategory?.Trim() is not ("Machine" or "Tooling" or "Material"))
        {
            errors.Add("Failed inspections require a Machine, Tooling, or Material cause. User Error must clear the lock for correction.");
        }
        Required(request.CauseText ?? "", nameof(request.CauseText), errors);
        Required(request.SolutionText ?? "", nameof(request.SolutionText), errors);
        if (request.OperationSeq <= 0)
        {
            errors.Add($"{nameof(request.OperationSeq)} is required.");
        }

        if (request.MachineCounter < 0)
        {
            errors.Add("Machine Counter cannot be negative.");
        }

        return errors;
    }

    private HashSet<Guid> DraftLockAlertIdsForMeasurement(Guid measurementId)
    {
        return repository.RuleViolations
            .Where(violation => violation.MeasurementIds.Contains(measurementId))
            .Join(
                repository.Alerts.Where(alert => alert.Status == AlertStatus.Active),
                violation => violation.AlertId,
                alert => alert.Id,
                (_, alert) => alert.Id)
            .ToHashSet();
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

        var plan = FindInspectionPlan(characteristic, entry.InspectionPhase);
        if (plan is null)
        {
            return;
        }

        if (CreateSpecLimitAlert(measurement))
        {
            return;
        }

        var ruleSet = ResolveRuleSet(plan);
        // Custom drift uses completed inspection averages, never individual entry events.
        if (string.Equals(ruleSet, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            var spec = FindSpecLimit(measurement);
            if (spec is not null)
            {
                var reasons = CustomWatchEvaluator.Evaluate([], [measurement.Value], spec.Lsl, spec.Usl, repository.Settings.CustomDriftRule, false);
                if (reasons.Count > 0)
                {
                    measurement.WatchReason = string.Join(" ", reasons);
                    ApplyCustomWatch(measurement, measurement.WatchReason, [measurement.Id], null);
                }
            }
            return;
        }
        if (string.Equals(ruleSet, "None", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ruleSet, "SpecLimitOnly", StringComparison.OrdinalIgnoreCase))
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

        var hasCompletedHistory = HasCompletedHistoryForDrift(measurement);
        var violations = DetectRuleViolations(ruleSet, points, limits.CenterLine, limits.Lcl, limits.Ucl);
        foreach (var violation in violations.Where(violation =>
            violation.MeasurementIds.Contains(measurement.Id) &&
            (violation.RuleTriggered == RuleTriggered.OnePointBeyondControlLimit || hasCompletedHistory)))
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
                LockedAt = violation.DetectedAt,
                Status = AlertStatus.Warning
            };

            repository.Alerts.Add(alert);
            measurement.WatchReason = alert.Detail ?? "Process drift";
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

    private void EvaluateCompletedCustomDrift(JobPhaseCompletion completion)
    {
        var measurements = repository.Measurements.Where(item =>
            item.JobNum.Equals(completion.JobNum, StringComparison.OrdinalIgnoreCase) &&
            item.PartNum.Equals(completion.PartNum, StringComparison.OrdinalIgnoreCase) &&
            item.ProcessCode.Equals(completion.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
            item.OperationSeq == completion.OperationSeq && item.ResourceId.Equals(completion.ResourceId, StringComparison.OrdinalIgnoreCase) &&
            NormalizeInspectionPhase(item.InspectionPhase).Equals(NormalizeInspectionPhase(completion.InspectionPhase), StringComparison.OrdinalIgnoreCase)).ToDictionary(item => item.Id);
        var failedIds = repository.FailedInspections.SelectMany(item => item.MeasurementIds).ToHashSet();
        var history = repository.JobPhaseCompletions.Where(item => item.CompletedAt <= completion.CompletedAt &&
            item.JobNum.Equals(completion.JobNum, StringComparison.OrdinalIgnoreCase) &&
            item.PartNum.Equals(completion.PartNum, StringComparison.OrdinalIgnoreCase) &&
            item.ProcessCode.Equals(completion.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
            item.OperationSeq == completion.OperationSeq &&
            item.ResourceId.Equals(completion.ResourceId, StringComparison.OrdinalIgnoreCase) &&
            NormalizeInspectionPhase(item.InspectionPhase).Equals(NormalizeInspectionPhase(completion.InspectionPhase), StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CompletedAt).ThenBy(item => item.CompletionNumber).ToArray();
        var custom = repository.Settings.CustomDriftRule;
        foreach (var group in completion.MeasurementIds.Where(measurements.ContainsKey).Select(id => measurements[id])
            .GroupBy(item => item.CharacteristicName, StringComparer.OrdinalIgnoreCase))
        {
            var current = group.First();
            var characteristic = FindCharacteristic(new(current.JobNum, current.PartNum, current.ProcessCode,
                current.OperationSeq, current.ResourceId, current.CharacteristicName, current.Value, current.Timestamp,
                current.OperatorUserId, InspectionPhase: current.InspectionPhase));
            var plan = FindInspectionPlan(characteristic, current.InspectionPhase);
            if (characteristic?.Type != CharacteristicType.Variable || !string.Equals(ResolveRuleSet(plan), "Custom", StringComparison.OrdinalIgnoreCase)) continue;
            var spec = FindSpecLimit(current);
            if (spec is null || (spec.Lsl is null && spec.Usl is null)) continue;

            // Paired samples are interleaved in the entry form; do not average the two sides/nubs together.
            var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(current.PartNum, StringComparison.OrdinalIgnoreCase));
            var pairedL3 = part?.ProductGroup.Contains("Cutting Edge", StringComparison.OrdinalIgnoreCase) == true &&
                !part.ProductGroup.Contains("Drilled", StringComparison.OrdinalIgnoreCase) &&
                current.CharacteristicName.Contains("L3", StringComparison.OrdinalIgnoreCase) && plan?.SampleSize == 6;
            var pairedNubs = current.PartNum == "70306" && current.CharacteristicName.Equals("Weld Nub Dia. (2x)", StringComparison.OrdinalIgnoreCase) && plan?.SampleSize == 6;
            var lanes = pairedL3 || pairedNubs ? 2 : 1;
            var sets = history.Select(run => run.MeasurementIds.Where(id => measurements.ContainsKey(id) && !failedIds.Contains(id)).Select(id => measurements[id])
                .Where(item => item.CharacteristicName.Equals(current.CharacteristicName, StringComparison.OrdinalIgnoreCase)).ToArray()).ToArray();
            var details = new List<string>();
            var normal = true;
            var warningIds = new HashSet<Guid>();
            for (var lane = 0; lane < lanes; lane++)
            {
                var values = sets.Select(set => set.Length != plan!.SampleSize || set.Any(item =>
                    (spec.Lsl.HasValue && item.Value < spec.Lsl) || (spec.Usl.HasValue && item.Value > spec.Usl))
                    ? null : set.Where((_, index) => index % lanes == lane).Select(item => item.Value).ToArray()).ToArray();
                var currentValues = group.Where((_, index) => index % lanes == lane).Select(item => item.Value).ToArray();
                var reasons = CustomWatchEvaluator.Evaluate(values, currentValues, spec.Lsl, spec.Usl, custom, true);
                normal &= values.LastOrDefault() is not null &&
                    CustomWatchEvaluator.Evaluate(values, currentValues, spec.Lsl, spec.Usl, custom, true, true).Count == 0;
                if (reasons.Count == 0) continue;
                var laneName = pairedL3 ? (lane == 0 ? "Left: " : "Right: ") : pairedNubs ? $"Weld nub {lane + 1}: " : "";
                details.Add(laneName + string.Join(" ", reasons));
                foreach (var item in group.Where((_, index) => index % lanes == lane))
                    item.WatchReason = details[^1];
                foreach (var item in sets.TakeLast(Math.Max(custom.WindowSize, custom.Options.RepeatedWindow)).SelectMany(set => set.Where((_, index) => index % lanes == lane))) warningIds.Add(item.Id);
            }
            var detail = string.Join(" ", details);
            ApplyCustomWatch(current, detail, warningIds, completion.Id, normal);
            var stillWatching = repository.Alerts.LastOrDefault(alert => alert.Status == AlertStatus.Warning &&
                alert.RuleTriggered == RuleTriggered.CustomRuleTriggered && alert.WatchScope == CustomWatchScope(current) &&
                alert.JobNum == current.JobNum && alert.PartNum == current.PartNum && alert.ResourceId == current.ResourceId && alert.CharacteristicName == current.CharacteristicName);
            if (stillWatching is not null && details.Count == 0)
                foreach (var item in group) item.WatchReason = $"{stillWatching.Detail} Recovery: {stillWatching.NormalWatchInspections} of {custom.Options.RecoveryInspections} normal inspections.";
        }
    }

    private static string CustomWatchScope(InspectionMeasurement measurement) =>
        $"{measurement.ProcessCode}|{measurement.OperationSeq}|{NormalizeInspectionPhase(measurement.InspectionPhase)}".ToLowerInvariant();

    private void ApplyCustomWatch(InspectionMeasurement current, string reason, IEnumerable<Guid> evidence, Guid? completionId, bool normal = true)
    {
        var scope = CustomWatchScope(current);
        var alert = repository.Alerts.LastOrDefault(item => item.Status == AlertStatus.Warning && item.RuleTriggered == RuleTriggered.CustomRuleTriggered &&
            item.JobNum.Equals(current.JobNum, StringComparison.OrdinalIgnoreCase) && item.PartNum.Equals(current.PartNum, StringComparison.OrdinalIgnoreCase) &&
            item.ResourceId.Equals(current.ResourceId, StringComparison.OrdinalIgnoreCase) && item.CharacteristicName.Equals(current.CharacteristicName, StringComparison.OrdinalIgnoreCase) && item.WatchScope == scope);
        if (string.IsNullOrEmpty(reason))
        {
            if (alert is null || !completionId.HasValue || alert.LastWatchCompletionId == completionId) return;
            alert.LastWatchCompletionId = completionId;
            if (!normal) { alert.NormalWatchInspections = 0; return; }
            alert.NormalWatchInspections++;
            if (!repository.Settings.CustomDriftRule.Options.Enabled || alert.NormalWatchInspections >= repository.Settings.CustomDriftRule.Options.RecoveryInspections)
                alert.Status = AlertStatus.Resolved;
            return;
        }
        if (alert is null)
        {
            alert = new ProcessAlert { JobNum = current.JobNum, PartNum = current.PartNum, ResourceId = current.ResourceId,
                CharacteristicName = current.CharacteristicName, OperatorUserId = current.OperatorUserId, OperatorShift = current.OperatorShift,
                RuleTriggered = RuleTriggered.CustomRuleTriggered, LockedAt = current.Timestamp, Status = AlertStatus.Warning, WatchScope = scope };
            repository.Alerts.Add(alert);
            repository.RuleViolations.Add(new RuleViolation { AlertId = alert.Id, RuleTriggered = RuleTriggered.CustomRuleTriggered, DetectedAt = current.Timestamp });
        }
        alert.Detail = reason;
        alert.NormalWatchInspections = 0;
        if (completionId.HasValue) alert.LastWatchCompletionId = completionId;
        var violation = repository.RuleViolations.Last(item => item.AlertId == alert.Id);
        foreach (var id in evidence.Except(violation.MeasurementIds)) violation.MeasurementIds.Add(id);
    }

    private bool HasCompletedHistoryForDrift(InspectionMeasurement measurement)
    {
        var phase = NormalizeInspectionPhase(measurement.InspectionPhase);
        var completedMeasurementIds = repository.JobPhaseCompletions
            .Where(completion =>
                completion.JobNum.Equals(measurement.JobNum, StringComparison.OrdinalIgnoreCase) &&
                completion.PartNum.Equals(measurement.PartNum, StringComparison.OrdinalIgnoreCase) &&
                completion.ProcessCode.Equals(measurement.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                completion.OperationSeq == measurement.OperationSeq &&
                completion.ResourceId.Equals(measurement.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                NormalizeInspectionPhase(completion.InspectionPhase).Equals(phase, StringComparison.OrdinalIgnoreCase) &&
                completion.CompletedAt < measurement.Timestamp)
            .SelectMany(completion => completion.MeasurementIds)
            .ToHashSet();

        return completedMeasurementIds.Count > 0 &&
            repository.Measurements.Any(item =>
                completedMeasurementIds.Contains(item.Id) &&
                item.CharacteristicName.Equals(measurement.CharacteristicName, StringComparison.OrdinalIgnoreCase));
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

    private InspectionPlan? FindInspectionPlan(Characteristic? characteristic, string? inspectionPhase)
    {
        return characteristic is null
            ? null
            : repository.InspectionPlans.FirstOrDefault(plan =>
                plan.CharacteristicId == characteristic.Id &&
                NormalizeInspectionPhase(plan.InspectionPhase).Equals(NormalizeInspectionPhase(inspectionPhase), StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveRuleSet(InspectionPlan? plan)
    {
        return string.Equals(plan?.AlertRuleSet, "GlobalDefault", StringComparison.OrdinalIgnoreCase)
            ? repository.Settings.GlobalAlertRuleSet
            : plan?.AlertRuleSet ?? "None";
    }

    private bool CreateSpecLimitAlert(InspectionMeasurement measurement)
    {
        var spec = FindSpecLimit(measurement);
        var rejectedPin = measurement.GagePinNumber == 7;
        if (!rejectedPin && (spec is null ||
            (!spec.Lsl.HasValue && !spec.Usl.HasValue) ||
            (!IsBelowLowerSpec(measurement.Value, spec) && !IsAboveUpperSpec(measurement.Value, spec))))
        {
            return false;
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
            Detail = rejectedPin ? $"Gage pin 7 rejected (auto-fail). Recorded diameter {measurement.Value}." : SpecLimitDetail(measurement, spec!),
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
        return true;
    }

    private ResolvedSpecLimit? FindSpecLimit(InspectionMeasurement measurement)
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

        if (characteristic is null)
        {
            return null;
        }

        var inspectionPhase = NormalizeInspectionPhase(measurement.InspectionPhase);
        var plan = repository.InspectionPlans.FirstOrDefault(item =>
            item.CharacteristicId == characteristic.Id &&
            NormalizeInspectionPhase(item.InspectionPhase).Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase));
        if (plan is not null && (plan.Lsl is not null || plan.Usl is not null))
        {
            return new ResolvedSpecLimit(plan.Nominal, plan.Lsl, plan.Usl);
        }

        var spec = repository.SpecLimits.FirstOrDefault(item => item.CharacteristicId == characteristic.Id);
        return spec is null ? null : new ResolvedSpecLimit(spec.Nominal, spec.Lsl, spec.Usl);
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

    private static string SpecLimitDetail(InspectionMeasurement measurement, ResolvedSpecLimit spec)
    {
        if (IsAboveUpperSpec(measurement.Value, spec))
        {
            return $"Entered value {measurement.Value:0.#####} is above the upper specification limit {spec.Usl!.Value:0.#####}.";
        }

        if (IsBelowLowerSpec(measurement.Value, spec))
        {
            return $"Entered value {measurement.Value:0.#####} is below the lower specification limit {spec.Lsl!.Value:0.#####}.";
        }

        return string.Empty;
    }

    private static bool IsAboveUpperSpec(decimal value, ResolvedSpecLimit spec)
    {
        return spec.Usl.HasValue && value > spec.Usl.Value;
    }

    private static bool IsBelowLowerSpec(decimal value, ResolvedSpecLimit spec)
    {
        return spec.Lsl.HasValue && value < spec.Lsl.Value;
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




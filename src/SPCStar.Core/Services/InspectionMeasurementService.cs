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
    DateTimeOffset? SubmittedAt = null);

public sealed class InspectionMeasurementService(
    ISpcRepository repository,
    WesternElectricRuleService westernElectricRuleService)
{
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
            return ServiceResult<InspectionMeasurement>.Ok(duplicate);
        }

        if (!InspectionTargetExists(entry))
        {
            return ServiceResult<InspectionMeasurement>.Fail("No configured inspection characteristic was found for the submitted part/process/operation/characteristic.");
        }

        if (!CanEnterInspections(entry.OperatorUserId))
        {
            return ServiceResult<InspectionMeasurement>.Fail("User is not authorized to enter inspections.");
        }

        if (HasActiveLock(entry))
        {
            return ServiceResult<InspectionMeasurement>.Fail("Inspection entry is locked for this job/resource/characteristic due to an active drift alert.");
        }

        var jobResult = UpsertJob(entry);
        if (!jobResult.Succeeded)
        {
            return ServiceResult<InspectionMeasurement>.Fail(jobResult.Errors);
        }

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
            Value = entry.Value,
            Timestamp = entry.Timestamp,
            OperatorUserId = entry.OperatorUserId.Trim(),
            SubmittedAt = entry.SubmittedAt ?? entry.Timestamp,
            SyncedAt = DateTimeOffset.UtcNow
        };

        repository.Measurements.Add(measurement);
        CreateAlertsForViolations(measurement);
        return ServiceResult<InspectionMeasurement>.Ok(measurement);
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

    private bool HasActiveLock(InspectionMeasurementEntry entry)
    {
        return repository.Alerts.Any(alert =>
            alert.Status == AlertStatus.Active &&
            alert.JobNum.Equals(entry.JobNum, StringComparison.OrdinalIgnoreCase) &&
            alert.PartNum.Equals(entry.PartNum, StringComparison.OrdinalIgnoreCase) &&
            alert.ResourceId.Equals(entry.ResourceId, StringComparison.OrdinalIgnoreCase) &&
            alert.CharacteristicName.Equals(entry.CharacteristicName, StringComparison.OrdinalIgnoreCase));
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

    private bool CanEnterInspections(string userName)
    {
        return repository.Users
            .FirstOrDefault(user => user.UserName.Equals(userName.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.Roles.Any(role => role.Permissions.Contains(PermissionNames.CanEnterInspections)) == true;
    }

    private void CreateAlertsForViolations(InspectionMeasurement measurement)
    {
        var limits = repository.ControlLimits.FirstOrDefault(limit =>
            limit.PartNum.Equals(measurement.PartNum, StringComparison.OrdinalIgnoreCase) &&
            limit.ProcessCode.Equals(measurement.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
            limit.OperationSeq == measurement.OperationSeq &&
            limit.CharacteristicName.Equals(measurement.CharacteristicName, StringComparison.OrdinalIgnoreCase));

        if (limits is null)
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
                item.CharacteristicName.Equals(measurement.CharacteristicName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Timestamp)
            .Select(item => new WesternElectricPoint(item.Id, item.Value, item.Timestamp))
            .ToArray();

        var violations = westernElectricRuleService.Detect(points, limits.CenterLine, limits.Lcl, limits.Ucl);
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
                RuleTriggered = violation.RuleTriggered,
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

    private static List<string> Validate(InspectionMeasurementEntry entry)
    {
        var errors = new List<string>();
        Required(entry.JobNum, nameof(entry.JobNum), errors);
        Required(entry.PartNum, nameof(entry.PartNum), errors);
        Required(entry.ProcessCode, nameof(entry.ProcessCode), errors);
        Required(entry.ResourceId, nameof(entry.ResourceId), errors);
        Required(entry.CharacteristicName, nameof(entry.CharacteristicName), errors);
        Required(entry.OperatorUserId, nameof(entry.OperatorUserId), errors);
        if (entry.OperationSeq <= 0)
        {
            errors.Add("OperationSeq must be greater than zero.");
        }

        return errors;
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
}

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
            Value = entry.Value,
            Timestamp = entry.Timestamp,
            OperatorUserId = entry.OperatorUserId.Trim(),
            SubmittedAt = entry.SubmittedAt ?? entry.Timestamp,
            SyncedAt = DateTimeOffset.UtcNow
        };

        repository.Measurements.Add(measurement);
        CreateAlertsForViolations(measurement, entry);
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

    private bool CanEnterInspections(string userName)
    {
        return repository.Users
            .FirstOrDefault(user => user.UserName.Equals(userName.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.Roles.Any(role => role.Permissions.Contains(PermissionNames.CanEnterInspections)) == true;
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
            return DetectCustomDefault(points, centerLine, sigma);
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

    private static IReadOnlyList<WesternElectricViolation> DetectCustomDefault(IReadOnlyList<WesternElectricPoint> points, decimal centerLine, decimal sigma)
    {
        if (points.Count < 4)
        {
            return [];
        }

        var window = points.TakeLast(4).ToArray();
        if (window.All(point => point.Value > centerLine + sigma) || window.All(point => point.Value < centerLine - sigma))
        {
            return [new WesternElectricViolation(
                RuleTriggered.CustomRuleTriggered,
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
            RuleTriggered = RuleTriggered.SpecLimitViolation,
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
        if (part is null)
        {
            return null;
        }

        var operation = repository.Operations.FirstOrDefault(item => item.PartId == part.Id && item.OperationSeq == measurement.OperationSeq);
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
            RuleTriggered = RuleTriggered.AttributeRejected,
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
            errors.Add("InspectionPhase must be Startup, Setup, or In Process.");
        }

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

        return phase.Equals("Set Up", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Setup", StringComparison.OrdinalIgnoreCase)
            ? "Setup"
            : "In Process";
    }

    private static string ActiveLockMessage(ProcessAlert alert)
    {
        return $"{alert.CharacteristicName} is locked for job {alert.JobNum} on {alert.ResourceId} due to {RuleText(alert.RuleTriggered)} at {alert.LockedAt:MM/dd/yyyy HH:mm}. Clear that lock before entering more {alert.CharacteristicName} measurements.";
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
            value.Trim().Equals("In Process", StringComparison.OrdinalIgnoreCase);
    }
}

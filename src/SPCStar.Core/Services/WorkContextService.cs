using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record WorkContextRequest(
    string JobNum,
    string PartNum,
    string ProcessCode,
    int OperationSeq,
    string ResourceId,
    string CharacteristicName,
    DateTimeOffset Now,
    string InspectionPhase = "In Process");

public sealed record WorkContextBatchRequest(
    string JobNum,
    string PartNum,
    string ProcessCode,
    int OperationSeq,
    string ResourceId,
    IReadOnlyList<string> CharacteristicNames,
    string InspectionPhase = "In Process",
    DateTimeOffset? Now = null);

public sealed record ActiveLockDto(
    Guid AlertId,
    string CharacteristicName,
    RuleTriggered RuleTriggered,
    DateTimeOffset LockedAt,
    string OperatorUserId,
    string? Detail);

public sealed record ProcessWarningDto(
    Guid AlertId,
    string CharacteristicName,
    RuleTriggered RuleTriggered,
    DateTimeOffset DetectedAt,
    string OperatorUserId,
    string? Detail);

public sealed record WorkContextDto(
    WorkContextRequest Request,
    InspectionPlanSetupDto? InspectionPlan,
    decimal? LowerSpecLimit,
    decimal? UpperSpecLimit,
    decimal? LowerControlLimit,
    decimal? UpperControlLimit,
    InspectionFrequencyStatus FrequencyStatus,
    ActiveLockDto? ActiveLock,
    ProcessWarningDto? ProcessWarning,
    WorkCapabilityDto Capability,
    IReadOnlyList<ChartPoint> RecentMeasurements,
    long MinimumMachineCounter = 0,
    long? NextMachineCounterDue = null);

public sealed record WorkCapabilityDto(
    decimal? Mean,
    decimal? Min,
    decimal? Max,
    decimal? StdDev,
    decimal? Cp,
    decimal? Cpk,
    decimal? Pp,
    decimal? Ppk,
    int Count,
    int OutOfSpecExcludedCount,
    int OutOfSpecCount = 0,
    decimal? WithinStdDev = null,
    int SubgroupCount = 0,
    string CalculationMethod = "");

public sealed class WorkContextService(
    ISpcRepository repository,
    SetupQueryService setupQueryService,
    InspectionFrequencyService inspectionFrequencyService,
    ChartDataService chartDataService)
{
    public IReadOnlyList<WorkContextDto> BuildBatch(WorkContextBatchRequest request)
    {
        var now = request.Now ?? DateTimeOffset.UtcNow;
        var schedule = new MachineCounterSchedule(repository);
        return request.CharacteristicNames
            .Where(characteristicName => !string.IsNullOrWhiteSpace(characteristicName))
            .Select(characteristicName => Build(new WorkContextRequest(
                request.JobNum,
                request.PartNum,
                request.ProcessCode,
                request.OperationSeq,
                request.ResourceId,
                characteristicName.Trim(),
                now,
                request.InspectionPhase), schedule))
            .ToArray();
    }

    public WorkContextDto Build(WorkContextRequest request)
        => Build(request, new MachineCounterSchedule(repository));

    private WorkContextDto Build(WorkContextRequest request, MachineCounterSchedule schedule)
    {
        var plan = setupQueryService
            .GetInspectionPlans(request.PartNum)
            .FirstOrDefault(item =>
                item.ProcessCode.Equals(request.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                item.OperationSeq == request.OperationSeq &&
                item.InspectionPhase.Equals(NormalizeInspectionPhase(request.InspectionPhase), StringComparison.OrdinalIgnoreCase) &&
                item.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase));
        var chart = chartDataService.Build(new ChartDataRequest(
            ChartType.IndividualsMovingRange,
            request.JobNum,
            request.PartNum,
            request.ResourceId,
            request.CharacteristicName,
            null,
            null,
            NormalizeInspectionPhase(request.InspectionPhase),
            request.ProcessCode,
            request.OperationSeq));
        var controlLimits = repository.ControlLimits.FirstOrDefault(limit =>
            limit.PartNum.Equals(request.PartNum, StringComparison.OrdinalIgnoreCase) &&
            limit.ProcessCode.Equals(request.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
            limit.OperationSeq == request.OperationSeq &&
            limit.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase));
        var frequency = inspectionFrequencyService.Evaluate(new InspectionFrequencyCheckRequest(
            request.JobNum,
            request.PartNum,
            request.ProcessCode,
            request.OperationSeq,
            request.CharacteristicName,
            request.ResourceId,
            request.Now,
            null,
            null,
            [],
            NormalizeInspectionPhase(request.InspectionPhase)));
        var activeLock = repository.Alerts
            .Where(alert =>
                alert.Status == AlertStatus.Active &&
                alert.JobNum.Equals(request.JobNum, StringComparison.OrdinalIgnoreCase) &&
                alert.PartNum.Equals(request.PartNum, StringComparison.OrdinalIgnoreCase) &&
                alert.ResourceId.Equals(request.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                alert.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(alert => alert.LockedAt)
            .Select(alert => new ActiveLockDto(alert.Id, alert.CharacteristicName, alert.RuleTriggered, alert.LockedAt, alert.OperatorUserId, alert.Detail))
            .FirstOrDefault();
        var processWarning = repository.Alerts
            .Where(alert =>
                alert.Status == AlertStatus.Warning &&
                (alert.RuleTriggered != RuleTriggered.CustomRuleTriggered || repository.Settings.CustomDriftRule.Options.Enabled) &&
                (alert.RuleTriggered != RuleTriggered.CustomRuleTriggered || (alert.WatchScope.Length > 0
                    ? alert.WatchScope == $"{request.ProcessCode}|{request.OperationSeq}|{NormalizeInspectionPhase(request.InspectionPhase)}".ToLowerInvariant()
                    : repository.RuleViolations.Any(violation =>
                    violation.AlertId == alert.Id && repository.Measurements.Any(measurement =>
                        violation.MeasurementIds.Contains(measurement.Id) &&
                        measurement.ProcessCode.Equals(request.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                        measurement.OperationSeq == request.OperationSeq &&
                        NormalizeInspectionPhase(measurement.InspectionPhase).Equals(NormalizeInspectionPhase(request.InspectionPhase), StringComparison.OrdinalIgnoreCase))))) &&
                alert.JobNum.Equals(request.JobNum, StringComparison.OrdinalIgnoreCase) &&
                alert.PartNum.Equals(request.PartNum, StringComparison.OrdinalIgnoreCase) &&
                alert.ResourceId.Equals(request.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                alert.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(alert => alert.LockedAt)
            .Select(alert => new ProcessWarningDto(alert.Id, alert.CharacteristicName, alert.RuleTriggered, alert.LockedAt, alert.OperatorUserId,
                alert.NormalWatchInspections > 0 ? $"{alert.Detail} Recovery: {alert.NormalWatchInspections} of {repository.Settings.CustomDriftRule.Options.RecoveryInspections} normal inspections." : alert.Detail))
            .FirstOrDefault();

        return new WorkContextDto(
            request,
            plan,
            plan?.Lsl,
            plan?.Usl,
            controlLimits?.Lcl,
            controlLimits?.Ucl,
            frequency,
            activeLock,
            processWarning,
            BuildCapability(request, plan),
            chart.Points.TakeLast(10).ToArray(),
            JobMachineCounterRules.Minimum(repository, request.JobNum),
            plan?.FrequencyType == FrequencyType.Quantity && plan.FrequencyUnit is FrequencyUnit.Pieces or FrequencyUnit.Box
                ? schedule.NextDue(request.JobNum, request.PartNum, request.ProcessCode, request.OperationSeq,
                    request.CharacteristicName, plan.FrequencyValue, plan.FirstDueValue)
                : null);
    }

    private WorkCapabilityDto BuildCapability(WorkContextRequest request, InspectionPlanSetupDto? plan)
    {
        if (plan is null || plan.CharacteristicType == CharacteristicType.Attribute)
        {
            return new WorkCapabilityDto(null, null, null, null, null, null, null, null, 0, 0);
        }

        var measurements = repository.Measurements
            .Where(measurement =>
                measurement.JobNum.Equals(request.JobNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.PartNum.Equals(request.PartNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.ProcessCode.Equals(request.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                measurement.OperationSeq == request.OperationSeq &&
                measurement.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var specifications = setupQueryService.GetInspectionPlans(request.PartNum)
            .Where(item => item.ProcessCode.Equals(request.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                item.OperationSeq == request.OperationSeq &&
                item.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase) &&
                (measurements.Length == 0 || measurements.Any(measurement =>
                    measurement.InspectionPhase.Equals(item.InspectionPhase, StringComparison.OrdinalIgnoreCase))))
            .Select(item => (item.Lsl, item.Usl)).Distinct().ToArray();
        var lower = specifications.Length == 1 ? specifications[0].Lsl : null;
        var upper = specifications.Length == 1 ? specifications[0].Usl : null;
        var result = new CapabilityCalculationService(repository).Calculate(measurements,lower,upper);
        return new(result.Mean,result.Min,result.Max,result.OverallStdDev,result.Cp,result.Cpk,result.Pp,result.Ppk,result.Count,0,
            result.OutOfSpecCount,result.WithinStdDev,result.SubgroupCount,
            specifications.Length > 1 ? "Capability unavailable across mixed phase specification limits" : result.WithinMethod);
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
}



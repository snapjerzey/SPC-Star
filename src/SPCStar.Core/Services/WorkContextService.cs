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

public sealed record ActiveLockDto(
    Guid AlertId,
    string CharacteristicName,
    RuleTriggered RuleTriggered,
    DateTimeOffset LockedAt,
    string OperatorUserId);

public sealed record WorkContextDto(
    WorkContextRequest Request,
    InspectionPlanSetupDto? InspectionPlan,
    decimal? LowerSpecLimit,
    decimal? UpperSpecLimit,
    decimal? LowerControlLimit,
    decimal? UpperControlLimit,
    InspectionFrequencyStatus FrequencyStatus,
    ActiveLockDto? ActiveLock,
    WorkCapabilityDto Capability,
    IReadOnlyList<ChartPoint> RecentMeasurements);

public sealed record WorkCapabilityDto(
    decimal? Cp,
    decimal? Cpk,
    decimal? Pp,
    decimal? Ppk,
    int Count);

public sealed class WorkContextService(
    ISpcRepository repository,
    SetupQueryService setupQueryService,
    InspectionFrequencyService inspectionFrequencyService,
    ChartDataService chartDataService)
{
    public WorkContextDto Build(WorkContextRequest request)
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
            NormalizeInspectionPhase(request.InspectionPhase)));
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
            .Select(alert => new ActiveLockDto(alert.Id, alert.CharacteristicName, alert.RuleTriggered, alert.LockedAt, alert.OperatorUserId))
            .FirstOrDefault();

        return new WorkContextDto(
            request,
            plan,
            plan?.Lsl ?? chart.LowerSpecLimit,
            plan?.Usl ?? chart.UpperSpecLimit,
            controlLimits?.Lcl ?? chart.LowerControlLimit,
            controlLimits?.Ucl ?? chart.UpperControlLimit,
            frequency,
            activeLock,
            BuildCapability(request, plan),
            chart.Points.TakeLast(10).ToArray());
    }

    private WorkCapabilityDto BuildCapability(WorkContextRequest request, InspectionPlanSetupDto? plan)
    {
        if (plan is null || plan.CharacteristicType == CharacteristicType.Attribute)
        {
            return new WorkCapabilityDto(null, null, null, null, 0);
        }

        var values = repository.Measurements
            .Where(measurement =>
                measurement.JobNum.Equals(request.JobNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.PartNum.Equals(request.PartNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.ResourceId.Equals(request.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                measurement.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase) &&
                measurement.InspectionPhase.Equals(NormalizeInspectionPhase(request.InspectionPhase), StringComparison.OrdinalIgnoreCase) &&
                measurement.Value >= plan.Lsl &&
                measurement.Value <= plan.Usl)
            .Select(measurement => measurement.Value)
            .ToArray();
        var stdDev = StandardDeviation(values);
        if (!stdDev.HasValue || stdDev.Value <= 0 || values.Length < 2)
        {
            return new WorkCapabilityDto(null, null, null, null, values.Length);
        }

        var mean = values.Average();
        var cp = (plan.Usl - plan.Lsl) / (6 * stdDev.Value);
        var cpk = Math.Min((mean - plan.Lsl) / (3 * stdDev.Value), (plan.Usl - mean) / (3 * stdDev.Value));
        return new WorkCapabilityDto(cp, cpk, cp, cpk, values.Length);
    }

    private static decimal? StandardDeviation(IReadOnlyCollection<decimal> values)
    {
        if (values.Count < 2)
        {
            return null;
        }

        var mean = values.Average();
        return (decimal)Math.Sqrt(values.Select(value => Math.Pow((double)(value - mean), 2)).Sum() / (values.Count - 1));
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
            phase.Equals("Spool Start", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Spool End", StringComparison.OrdinalIgnoreCase))
        {
            return "Spool";
        }

        return phase.Equals("Set Up", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Setup", StringComparison.OrdinalIgnoreCase)
            ? "Setup"
            : "In Process";
    }
}



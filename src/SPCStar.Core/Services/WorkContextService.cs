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
    DateTimeOffset Now);

public sealed record ActiveLockDto(
    Guid AlertId,
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
    IReadOnlyList<ChartPoint> RecentMeasurements);

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
                item.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase));
        var chart = chartDataService.Build(new ChartDataRequest(
            ChartType.IndividualsMovingRange,
            request.JobNum,
            request.PartNum,
            request.ResourceId,
            request.CharacteristicName,
            null,
            null));
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
            []));
        var activeLock = repository.Alerts
            .Where(alert =>
                alert.Status == AlertStatus.Active &&
                alert.JobNum.Equals(request.JobNum, StringComparison.OrdinalIgnoreCase) &&
                alert.PartNum.Equals(request.PartNum, StringComparison.OrdinalIgnoreCase) &&
                alert.ResourceId.Equals(request.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                alert.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(alert => alert.LockedAt)
            .Select(alert => new ActiveLockDto(alert.Id, alert.RuleTriggered, alert.LockedAt, alert.OperatorUserId))
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
            chart.Points.TakeLast(10).ToArray());
    }
}

using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record ChartDataRequest(
    ChartType ChartType,
    string? JobNum,
    string? PartNum,
    string? ResourceId,
    string? CharacteristicName,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? InspectionPhase = null,
    string? ProcessCode = null,
    int? OperationSeq = null);

public sealed record ChartPoint(
    Guid MeasurementId,
    DateTimeOffset Timestamp,
    decimal Value,
    decimal? MovingRange,
    bool HasRuleViolation,
    IReadOnlyList<RuleTriggered> RuleViolations);

public sealed record ChartDataSet(
    ChartType ChartType,
    IReadOnlyList<ChartPoint> Points,
    decimal? Mean,
    decimal? LowerControlLimit,
    decimal? UpperControlLimit,
    decimal? LowerSpecLimit,
    decimal? UpperSpecLimit);

public sealed class ChartDataService(ISpcRepository repository)
{
    public ChartDataSet Build(ChartDataRequest request)
    {
        var measurements = repository.Measurements
            .Where(measurement =>
                Matches(request.JobNum, measurement.JobNum) &&
                Matches(request.PartNum, measurement.PartNum) &&
                Matches(request.ProcessCode, measurement.ProcessCode) &&
                (!request.OperationSeq.HasValue || measurement.OperationSeq == request.OperationSeq.Value) &&
                Matches(request.ResourceId, measurement.ResourceId) &&
                Matches(request.CharacteristicName, measurement.CharacteristicName) &&
                Matches(request.InspectionPhase, measurement.InspectionPhase) &&
                (!request.From.HasValue || measurement.Timestamp >= request.From.Value) &&
                (!request.To.HasValue || measurement.Timestamp <= request.To.Value))
            .OrderBy(measurement => measurement.Timestamp)
            .ToArray();

        var violationLookup = repository.RuleViolations
            .SelectMany(violation => violation.MeasurementIds.Select(id => new { MeasurementId = id, violation.RuleTriggered }))
            .GroupBy(item => item.MeasurementId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RuleTriggered>)group.Select(item => item.RuleTriggered).Distinct().ToArray());

        var points = new List<ChartPoint>();
        decimal? previousValue = null;
        foreach (var measurement in measurements)
        {
            var violations = violationLookup.GetValueOrDefault(measurement.Id) ?? [];
            var movingRange = previousValue.HasValue ? Math.Abs(measurement.Value - previousValue.Value) : (decimal?)null;
            points.Add(new ChartPoint(measurement.Id, measurement.Timestamp, measurement.Value, movingRange, violations.Count > 0, violations));
            previousValue = measurement.Value;
        }

        var first = measurements.FirstOrDefault();
        var limits = first is null ? null : FindControlLimits(first);
        var specs = first is null ? null : FindSpecLimits(first);
        var mean = measurements.Length == 0 ? null : (decimal?)measurements.Average(measurement => measurement.Value);

        return new ChartDataSet(
            request.ChartType,
            points,
            mean,
            limits?.Lcl,
            limits?.Ucl,
            specs?.Lsl,
            specs?.Usl);
    }

    private ControlLimitSet? FindControlLimits(InspectionMeasurement measurement)
    {
        return repository.ControlLimits.FirstOrDefault(limit =>
            limit.PartNum.Equals(measurement.PartNum, StringComparison.OrdinalIgnoreCase) &&
            limit.ProcessCode.Equals(measurement.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
            limit.OperationSeq == measurement.OperationSeq &&
            limit.CharacteristicName.Equals(measurement.CharacteristicName, StringComparison.OrdinalIgnoreCase));
    }

    private SpecLimit? FindSpecLimits(InspectionMeasurement measurement)
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

    private static bool Matches(string? filter, string value)
    {
        return string.IsNullOrWhiteSpace(filter) || value.Equals(filter, StringComparison.OrdinalIgnoreCase);
    }
}

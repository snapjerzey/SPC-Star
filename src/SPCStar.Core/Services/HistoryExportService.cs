using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record InspectionHistoryExportRequest(
    IReadOnlyCollection<string> PartNums,
    IReadOnlyCollection<string> JobNums,
    IReadOnlyCollection<string> ResourceIds,
    IReadOnlyCollection<string> CharacteristicNames,
    DateTimeOffset? From,
    DateTimeOffset? To);

public sealed record AlertHistoryExportRequest(
    IReadOnlyCollection<string> PartNums,
    IReadOnlyCollection<string> JobNums,
    IReadOnlyCollection<string> ResourceIds,
    IReadOnlyCollection<string> CharacteristicNames,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool IncludeOverridden);

public sealed record MaterialHistoryExportRequest(
    IReadOnlyCollection<string> PartNums,
    IReadOnlyCollection<string> JobNums,
    IReadOnlyCollection<string> ResourceIds,
    DateTimeOffset? From,
    DateTimeOffset? To);

public sealed class HistoryExportService(ISpcRepository repository)
{
    private static readonly string[] InspectionHeaders =
    [
        "JobNum",
        "PartNum",
        "ProcessCode",
        "OperationSeq",
        "ResourceID",
        "CharacteristicName",
        "MeasurementValue",
        "Timestamp",
        "OperatorUserID",
        "OperatorShift",
        "InspectionPhase"
    ];

    private static readonly string[] AlertHeaders =
    [
        "AlertID",
        "JobNum",
        "PartNum",
        "ResourceID",
        "CharacteristicName",
        "OperatorUserID",
        "OperatorShift",
        "RuleTriggered",
        "LockedAt",
        "Status"
    ];

    private static readonly string[] MaterialHeaders =
    [
        "JobNum",
        "PartNum",
        "MaterialPartNum",
        "OldLotNum",
        "NewLotNum",
        "QuantityLoaded",
        "ResourceID",
        "OperatorUserID",
        "Timestamp",
        "Reason"
    ];

    public string ExportInspectionCsv(InspectionHistoryExportRequest request)
    {
        var rows = FilterMeasurements(request)
            .OrderBy(item => item.Timestamp)
            .Select(item => new Dictionary<string, string>
            {
                ["JobNum"] = item.JobNum,
                ["PartNum"] = item.PartNum,
                ["ProcessCode"] = item.ProcessCode,
                ["OperationSeq"] = item.OperationSeq.ToString(),
                ["ResourceID"] = item.ResourceId,
                ["CharacteristicName"] = item.CharacteristicName,
                ["MeasurementValue"] = item.Value.ToString("0.#####"),
                ["Timestamp"] = item.Timestamp.ToString("O"),
                ["OperatorUserID"] = item.OperatorUserId,
                ["OperatorShift"] = item.OperatorShift,
                ["InspectionPhase"] = item.InspectionPhase
            });

        return CsvSupport.WriteRows(InspectionHeaders, rows);
    }

    public string ExportJobInspectionHistoryCsv(string jobNum)
    {
        return ExportInspectionCsv(new InspectionHistoryExportRequest([], [jobNum], [], [], null, null));
    }

    public string ExportAlertHistoryCsv(AlertHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resourceIds = request.ResourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var characteristics = request.CharacteristicNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = repository.Alerts
            .Where(alert =>
                (request.IncludeOverridden || alert.Status == AlertStatus.Active) &&
                Matches(partNums, alert.PartNum) &&
                Matches(jobNums, alert.JobNum) &&
                Matches(resourceIds, alert.ResourceId) &&
                Matches(characteristics, alert.CharacteristicName) &&
                (!request.From.HasValue || alert.LockedAt >= request.From.Value) &&
                (!request.To.HasValue || alert.LockedAt <= request.To.Value))
            .OrderBy(alert => alert.LockedAt)
            .Select(alert => new Dictionary<string, string>
            {
                ["AlertID"] = alert.Id.ToString(),
                ["JobNum"] = alert.JobNum,
                ["PartNum"] = alert.PartNum,
                ["ResourceID"] = alert.ResourceId,
                ["CharacteristicName"] = alert.CharacteristicName,
                ["OperatorUserID"] = alert.OperatorUserId,
                ["OperatorShift"] = alert.OperatorShift,
                ["RuleTriggered"] = alert.RuleTriggered.ToString(),
                ["LockedAt"] = alert.LockedAt.ToString("O"),
                ["Status"] = alert.Status.ToString()
            });

        return CsvSupport.WriteRows(AlertHeaders, rows);
    }

    public string ExportMaterialChangeHistoryCsv(MaterialHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resourceIds = request.ResourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = repository.MaterialChanges
            .Where(log =>
                Matches(partNums, log.PartNum) &&
                Matches(jobNums, log.JobNum) &&
                Matches(resourceIds, log.ResourceId) &&
                (!request.From.HasValue || log.Timestamp >= request.From.Value) &&
                (!request.To.HasValue || log.Timestamp <= request.To.Value))
            .OrderBy(log => log.Timestamp)
            .Select(log => new Dictionary<string, string>
            {
                ["JobNum"] = log.JobNum,
                ["PartNum"] = log.PartNum,
                ["MaterialPartNum"] = log.MaterialPartNum,
                ["OldLotNum"] = log.OldLotNum,
                ["NewLotNum"] = log.NewLotNum,
                ["QuantityLoaded"] = log.QuantityLoaded?.ToString("0.#####") ?? string.Empty,
                ["ResourceID"] = log.ResourceId,
                ["OperatorUserID"] = log.OperatorUserId,
                ["Timestamp"] = log.Timestamp.ToString("O"),
                ["Reason"] = log.Reason
            });

        return CsvSupport.WriteRows(MaterialHeaders, rows);
    }

    private IEnumerable<InspectionMeasurement> FilterMeasurements(InspectionHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resourceIds = request.ResourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var characteristics = request.CharacteristicNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return repository.Measurements.Where(item =>
            Matches(partNums, item.PartNum) &&
            Matches(jobNums, item.JobNum) &&
            Matches(resourceIds, item.ResourceId) &&
            Matches(characteristics, item.CharacteristicName) &&
            (!request.From.HasValue || item.Timestamp >= request.From.Value) &&
            (!request.To.HasValue || item.Timestamp <= request.To.Value));
    }

    private static bool Matches(IReadOnlySet<string> filters, string value)
    {
        return filters.Count == 0 || filters.Contains(value);
    }
}

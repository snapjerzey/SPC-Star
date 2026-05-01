using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record QaSummaryExportRequest(
    IReadOnlyCollection<string> PartNums,
    IReadOnlyCollection<string> JobNums,
    IReadOnlyCollection<string> CharacteristicNames,
    DateTimeOffset? From,
    DateTimeOffset? To);

public sealed record QaSummaryRow(
    string PartNum,
    string JobNum,
    string CharacteristicName,
    decimal Mean,
    decimal Min,
    decimal Max,
    decimal StdDev,
    int Count,
    decimal? Lsl,
    decimal? Usl,
    PassFailStatus PassFailStatus);

public sealed class QaSummaryExportService(InMemorySpcRepository repository)
{
    private static readonly string[] Headers =
    [
        "PartNum",
        "JobNum",
        "CharacteristicName",
        "Mean",
        "Min",
        "Max",
        "StdDev",
        "Count",
        "LSL",
        "USL",
        "PassFailStatus"
    ];

    public ServiceResult<IReadOnlyList<QaSummaryRow>> BuildSummary(QaSummaryExportRequest request)
    {
        if (request.CharacteristicNames.Count == 0)
        {
            return ServiceResult<IReadOnlyList<QaSummaryRow>>.Fail("At least one characteristic must be selected.");
        }

        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var characteristics = request.CharacteristicNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var query = repository.Measurements.Where(measurement =>
            (partNums.Count == 0 || partNums.Contains(measurement.PartNum)) &&
            (jobNums.Count == 0 || jobNums.Contains(measurement.JobNum)) &&
            characteristics.Contains(measurement.CharacteristicName) &&
            (!request.From.HasValue || measurement.Timestamp >= request.From.Value) &&
            (!request.To.HasValue || measurement.Timestamp <= request.To.Value));

        var rows = query
            .GroupBy(measurement => new { measurement.PartNum, measurement.JobNum, measurement.CharacteristicName })
            .Select(group => BuildRow(group.Key.PartNum, group.Key.JobNum, group.Key.CharacteristicName, group.Select(item => item.Value).ToArray()))
            .OrderBy(row => row.PartNum)
            .ThenBy(row => row.JobNum)
            .ThenBy(row => row.CharacteristicName)
            .ToArray();

        return ServiceResult<IReadOnlyList<QaSummaryRow>>.Ok(rows);
    }

    public ServiceResult<string> ExportCsv(QaSummaryExportRequest request)
    {
        var summary = BuildSummary(request);
        if (!summary.Succeeded || summary.Value is null)
        {
            return ServiceResult<string>.Fail(summary.Errors);
        }

        var rows = summary.Value.Select(row => new Dictionary<string, string>
        {
            ["PartNum"] = row.PartNum,
            ["JobNum"] = row.JobNum,
            ["CharacteristicName"] = row.CharacteristicName,
            ["Mean"] = row.Mean.ToString("0.#####"),
            ["Min"] = row.Min.ToString("0.#####"),
            ["Max"] = row.Max.ToString("0.#####"),
            ["StdDev"] = row.StdDev.ToString("0.#####"),
            ["Count"] = row.Count.ToString(),
            ["LSL"] = row.Lsl?.ToString("0.#####") ?? string.Empty,
            ["USL"] = row.Usl?.ToString("0.#####") ?? string.Empty,
            ["PassFailStatus"] = row.PassFailStatus.ToString()
        });

        return ServiceResult<string>.Ok(CsvSupport.WriteRows(Headers, rows));
    }

    private QaSummaryRow BuildRow(string partNum, string jobNum, string characteristicName, IReadOnlyList<decimal> values)
    {
        var spec = FindSpecLimit(partNum, characteristicName);
        var mean = values.Average();
        var min = values.Min();
        var max = values.Max();
        var stdDev = values.Count <= 1
            ? 0m
            : (decimal)Math.Sqrt(values.Select(value => Math.Pow((double)(value - mean), 2)).Sum() / (values.Count - 1));

        var passed = spec is null || values.All(value => value >= spec.Lsl && value <= spec.Usl);
        return new QaSummaryRow(
            partNum,
            jobNum,
            characteristicName,
            mean,
            min,
            max,
            stdDev,
            values.Count,
            spec?.Lsl,
            spec?.Usl,
            passed ? PassFailStatus.Pass : PassFailStatus.Fail);
    }

    private SpecLimit? FindSpecLimit(string partNum, string characteristicName)
    {
        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase));
        if (part is null)
        {
            return null;
        }

        var operationIds = repository.Operations
            .Where(operation => operation.PartId == part.Id)
            .Select(operation => operation.Id)
            .ToHashSet();
        var characteristic = repository.Characteristics.FirstOrDefault(item =>
            operationIds.Contains(item.OperationId) &&
            item.Name.Equals(characteristicName, StringComparison.OrdinalIgnoreCase));

        return characteristic is null
            ? null
            : repository.SpecLimits.FirstOrDefault(item => item.CharacteristicId == characteristic.Id);
    }
}

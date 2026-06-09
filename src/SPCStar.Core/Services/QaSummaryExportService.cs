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
    CoaStatisticType CoaStatisticType,
    decimal? CoaValue,
    decimal? Mean,
    decimal? Min,
    decimal? Max,
    decimal? StdDev,
    int Count,
    int OutOfSpecExcludedCount,
    decimal? Lsl,
    decimal? Usl,
    PassFailStatus PassFailStatus);

public sealed record JobVariableMeanRow(
    string JobNum,
    string PartNum,
    string CharacteristicName,
    CharacteristicType CharacteristicType,
    string UnitOfMeasure,
    bool IsRequiredForCoa,
    CoaStatisticType CoaStatisticType,
    decimal? CoaValue,
    decimal Nominal,
    decimal Lsl,
    decimal Usl,
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
    string Status);

public sealed class QaSummaryExportService(ISpcRepository repository)
{
    private static readonly string[] Headers =
    [
        "PartNum",
        "JobNum",
        "CharacteristicName",
        "COAStatistic",
        "COAValue",
        "Mean",
        "Min",
        "Max",
        "StdDev",
        "Count",
        "OutOfSpecExcluded",
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

    public ServiceResult<IReadOnlyList<JobVariableMeanRow>> BuildJobVariableMeans(string jobNum, bool requiredOnly = true)
    {
        return BuildJobVariableMeans([jobNum], requiredOnly);
    }

    public ServiceResult<IReadOnlyList<JobVariableMeanRow>> BuildPartCapability(string partNum)
    {
        if (string.IsNullOrWhiteSpace(partNum))
        {
            return ServiceResult<IReadOnlyList<JobVariableMeanRow>>.Fail("PartNum is required.");
        }

        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(partNum.Trim(), StringComparison.OrdinalIgnoreCase));
        if (part is null)
        {
            return ServiceResult<IReadOnlyList<JobVariableMeanRow>>.Fail($"Part was not found: {partNum}.");
        }

        var plans =
            from operation in repository.Operations
            join characteristic in repository.Characteristics on operation.Id equals characteristic.OperationId
            join spec in repository.SpecLimits on characteristic.Id equals spec.CharacteristicId
            where operation.PartId == part.Id && characteristic.Type == CharacteristicType.Variable
            orderby operation.OperationSeq, characteristic.Name
            select new { part.PartNum, characteristic.Name, characteristic.Type, characteristic.UnitOfMeasure, characteristic.IsRequiredForCoa, characteristic.CoaStatisticType, spec.Nominal, spec.Lsl, spec.Usl };

        var rows = plans
            .Select(plan => BuildCapabilityRow("All Jobs", plan.PartNum, plan.Name, plan.Type, plan.UnitOfMeasure, plan.IsRequiredForCoa, plan.CoaStatisticType, plan.Nominal, plan.Lsl, plan.Usl,
                repository.Measurements
                    .Where(measurement =>
                        measurement.PartNum.Equals(plan.PartNum, StringComparison.OrdinalIgnoreCase) &&
                        measurement.CharacteristicName.Equals(plan.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(measurement => measurement.Value)
                    .ToArray()))
            .ToArray();

        return ServiceResult<IReadOnlyList<JobVariableMeanRow>>.Ok(rows);
    }

    public ServiceResult<IReadOnlyList<JobVariableMeanRow>> BuildJobVariableMeans(IReadOnlyCollection<string> jobNums, bool requiredOnly = true)
    {
        var requestedJobs = jobNums
            .Where(jobNum => !string.IsNullOrWhiteSpace(jobNum))
            .Select(jobNum => jobNum.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedJobs.Length == 0)
        {
            return ServiceResult<IReadOnlyList<JobVariableMeanRow>>.Fail("JobNum is required.");
        }

        var jobs = repository.Jobs
            .Where(item => requestedJobs.Contains(item.JobNum, StringComparer.OrdinalIgnoreCase))
            .OrderBy(item => item.JobNum)
            .ToArray();
        var missingJobs = requestedJobs
            .Where(jobNum => jobs.All(job => !job.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (missingJobs.Length > 0)
        {
            return ServiceResult<IReadOnlyList<JobVariableMeanRow>>.Fail($"Job was not found: {string.Join(", ", missingJobs)}.");
        }

        var rows = jobs
            .SelectMany(job => BuildSingleJobVariableMeans(job, requiredOnly))
            .ToArray();

        return ServiceResult<IReadOnlyList<JobVariableMeanRow>>.Ok(rows);
    }

    public ServiceResult<string> ExportJobVariableMeansCsv(string jobNum, bool requiredOnly = true)
    {
        return ExportJobVariableMeansCsv([jobNum], requiredOnly);
    }

    public ServiceResult<string> ExportJobVariableMeansCsv(IReadOnlyCollection<string> jobNums, bool requiredOnly = true)
    {
        var summary = BuildJobVariableMeans(jobNums, requiredOnly);
        if (!summary.Succeeded || summary.Value is null)
        {
            return ServiceResult<string>.Fail(summary.Errors);
        }

        var headers = new[]
        {
            "JobNum",
            "PartNum",
            "CharacteristicName",
            "CharacteristicType",
            "UnitOfMeasure",
            "IsRequiredForCOA",
            "COAStatistic",
            "COAValue",
            "Nominal",
            "LSL",
            "USL",
            "Mean",
            "Min",
            "Max",
            "StdDev",
            "Cp",
            "Cpk",
            "Pp",
            "Ppk",
            "Count",
            "OutOfSpecExcluded",
            "Status"
        };
        var rows = summary.Value.Select(row => new Dictionary<string, string>
        {
            ["JobNum"] = row.JobNum,
            ["PartNum"] = row.PartNum,
            ["CharacteristicName"] = row.CharacteristicName,
            ["CharacteristicType"] = row.CharacteristicType.ToString(),
            ["UnitOfMeasure"] = row.UnitOfMeasure,
            ["IsRequiredForCOA"] = row.IsRequiredForCoa.ToString(),
            ["COAStatistic"] = row.CoaStatisticType.ToString(),
            ["COAValue"] = row.CoaValue?.ToString("0.#####") ?? string.Empty,
            ["Nominal"] = row.Nominal.ToString("0.#####"),
            ["LSL"] = row.Lsl.ToString("0.#####"),
            ["USL"] = row.Usl.ToString("0.#####"),
            ["Mean"] = row.Mean?.ToString("0.#####") ?? string.Empty,
            ["Min"] = row.Min?.ToString("0.#####") ?? string.Empty,
            ["Max"] = row.Max?.ToString("0.#####") ?? string.Empty,
            ["StdDev"] = row.StdDev?.ToString("0.#####") ?? string.Empty,
            ["Cp"] = row.Cp?.ToString("0.#####") ?? string.Empty,
            ["Cpk"] = row.Cpk?.ToString("0.#####") ?? string.Empty,
            ["Pp"] = row.Pp?.ToString("0.#####") ?? string.Empty,
            ["Ppk"] = row.Ppk?.ToString("0.#####") ?? string.Empty,
            ["Count"] = row.Count.ToString(),
            ["OutOfSpecExcluded"] = row.OutOfSpecExcludedCount.ToString(),
            ["Status"] = row.Status
        });

        return ServiceResult<string>.Ok(CsvSupport.WriteRows(headers, rows));
    }

    private IEnumerable<JobVariableMeanRow> BuildSingleJobVariableMeans(Job job, bool requiredOnly)
    {
        var plans =
            from part in repository.Parts
            join operation in repository.Operations on part.Id equals operation.PartId
            join characteristic in repository.Characteristics on operation.Id equals characteristic.OperationId
            join spec in repository.SpecLimits on characteristic.Id equals spec.CharacteristicId
            where part.PartNum.Equals(job.PartNum, StringComparison.OrdinalIgnoreCase) &&
                (!requiredOnly || characteristic.IsRequiredForCoa)
            orderby operation.OperationSeq, characteristic.Name
            select new { part.PartNum, characteristic.Name, characteristic.Type, characteristic.UnitOfMeasure, characteristic.IsRequiredForCoa, characteristic.CoaStatisticType, spec.Nominal, spec.Lsl, spec.Usl };

        return plans.Select(plan =>
            BuildCapabilityRow(job.JobNum, plan.PartNum, plan.Name, plan.Type, plan.UnitOfMeasure, plan.IsRequiredForCoa, plan.CoaStatisticType, plan.Nominal, plan.Lsl, plan.Usl,
                repository.Measurements
                .Where(measurement =>
                    measurement.JobNum.Equals(job.JobNum, StringComparison.OrdinalIgnoreCase) &&
                    measurement.PartNum.Equals(plan.PartNum, StringComparison.OrdinalIgnoreCase) &&
                    measurement.CharacteristicName.Equals(plan.Name, StringComparison.OrdinalIgnoreCase))
                .Select(measurement => measurement.Value)
                .ToArray()));
    }

    private static JobVariableMeanRow BuildCapabilityRow(
        string jobNum,
        string partNum,
        string characteristicName,
        CharacteristicType characteristicType,
        string unitOfMeasure,
        bool isRequiredForCoa,
        CoaStatisticType coaStatisticType,
        decimal nominal,
        decimal lsl,
        decimal usl,
        IReadOnlyCollection<decimal> values)
    {
        var acceptedValues = values
            .Where(value => value >= lsl && value <= usl)
            .ToArray();
        var outOfSpecCount = values.Count - acceptedValues.Length;
        var mean = acceptedValues.Length == 0 ? (decimal?)null : acceptedValues.Average();
        var stdDev = StandardDeviation(acceptedValues);
        var coaValue = CoaValue(coaStatisticType, mean, stdDev);
        var capability = characteristicType == CharacteristicType.Variable
            ? Capability(acceptedValues, lsl, usl)
            : new CapabilityMetrics(null, null, null, null, null);

        return new JobVariableMeanRow(
            jobNum,
            partNum,
            characteristicName,
            characteristicType,
            unitOfMeasure,
            isRequiredForCoa,
            coaStatisticType,
            coaValue,
            nominal,
            lsl,
            usl,
            mean,
            acceptedValues.Length == 0 ? null : acceptedValues.Min(),
            acceptedValues.Length == 0 ? null : acceptedValues.Max(),
            stdDev,
            capability.Cp,
            capability.Cpk,
            capability.Pp,
            capability.Ppk,
            acceptedValues.Length,
            outOfSpecCount,
            values.Count == 0 ? "NoData" : acceptedValues.Length == 0 ? PassFailStatus.Fail.ToString() : PassFailStatus.Pass.ToString());
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
            ["COAStatistic"] = row.CoaStatisticType.ToString(),
            ["COAValue"] = row.CoaValue?.ToString("0.#####") ?? string.Empty,
            ["Mean"] = row.Mean?.ToString("0.#####") ?? string.Empty,
            ["Min"] = row.Min?.ToString("0.#####") ?? string.Empty,
            ["Max"] = row.Max?.ToString("0.#####") ?? string.Empty,
            ["StdDev"] = row.StdDev?.ToString("0.#####") ?? string.Empty,
            ["Count"] = row.Count.ToString(),
            ["OutOfSpecExcluded"] = row.OutOfSpecExcludedCount.ToString(),
            ["LSL"] = row.Lsl?.ToString("0.#####") ?? string.Empty,
            ["USL"] = row.Usl?.ToString("0.#####") ?? string.Empty,
            ["PassFailStatus"] = row.PassFailStatus.ToString()
        });

        return ServiceResult<string>.Ok(CsvSupport.WriteRows(Headers, rows));
    }

    private QaSummaryRow BuildRow(string partNum, string jobNum, string characteristicName, IReadOnlyList<decimal> values)
    {
        var spec = FindSpecLimit(partNum, characteristicName);
        var coaStatisticType = FindCharacteristic(partNum, characteristicName)?.CoaStatisticType ?? CoaStatisticType.Mean;
        var acceptedValues = spec is null
            ? values.ToArray()
            : values.Where(value => value >= spec.Lsl && value <= spec.Usl).ToArray();
        var outOfSpecCount = values.Count - acceptedValues.Length;
        var mean = acceptedValues.Length == 0 ? (decimal?)null : acceptedValues.Average();
        var min = acceptedValues.Length == 0 ? (decimal?)null : acceptedValues.Min();
        var max = acceptedValues.Length == 0 ? (decimal?)null : acceptedValues.Max();
        var stdDev = StandardDeviation(acceptedValues);
        var coaValue = CoaValue(coaStatisticType, mean, stdDev);

        return new QaSummaryRow(
            partNum,
            jobNum,
            characteristicName,
            coaStatisticType,
            coaValue,
            mean,
            min,
            max,
            stdDev,
            acceptedValues.Length,
            outOfSpecCount,
            spec?.Lsl,
            spec?.Usl,
            acceptedValues.Length > 0 ? PassFailStatus.Pass : PassFailStatus.Fail);
    }

    private SpecLimit? FindSpecLimit(string partNum, string characteristicName)
    {
        var characteristic = FindCharacteristic(partNum, characteristicName);
        return characteristic is null
            ? null
            : repository.SpecLimits.FirstOrDefault(item => item.CharacteristicId == characteristic.Id);
    }

    private Characteristic? FindCharacteristic(string partNum, string characteristicName)
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
        return repository.Characteristics.FirstOrDefault(item =>
            operationIds.Contains(item.OperationId) &&
            item.Name.Equals(characteristicName, StringComparison.OrdinalIgnoreCase));
    }

    private static decimal? StandardDeviation(IReadOnlyCollection<decimal> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        if (values.Count == 1)
        {
            return 0m;
        }

        var mean = values.Average();
        return (decimal)Math.Sqrt(values.Select(value => Math.Pow((double)(value - mean), 2)).Sum() / (values.Count - 1));
    }

    private static decimal? CoaValue(CoaStatisticType statisticType, decimal? mean, decimal? standardDeviation)
    {
        return statisticType switch
        {
            CoaStatisticType.StandardDeviation => standardDeviation,
            _ => mean
        };
    }

    private sealed record CapabilityMetrics(decimal? StdDev, decimal? Cp, decimal? Cpk, decimal? Pp, decimal? Ppk);

    private static CapabilityMetrics Capability(IReadOnlyCollection<decimal> values, decimal lsl, decimal usl)
    {
        var stdDev = StandardDeviation(values);
        if (!stdDev.HasValue || stdDev.Value <= 0 || values.Count < 2 || lsl == usl)
        {
            return new CapabilityMetrics(stdDev, null, null, null, null);
        }

        var mean = values.Average();
        var cp = (usl - lsl) / (6 * stdDev.Value);
        var lowerCapability = (mean - lsl) / (3 * stdDev.Value);
        var upperCapability = (usl - mean) / (3 * stdDev.Value);
        var cpk = Math.Min(lowerCapability, upperCapability);

        return new CapabilityMetrics(stdDev, cp, cpk, cp, cpk);
    }
}

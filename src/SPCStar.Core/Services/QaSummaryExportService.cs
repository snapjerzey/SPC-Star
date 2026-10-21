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
    string ProcessCode,
    int OperationSeq,
    string InspectionPhases,
    string CharacteristicName,
    CharacteristicType CharacteristicType,
    string UnitOfMeasure,
    decimal? Nominal,
    decimal? Lsl,
    decimal? Usl,
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
    string Status,
    int OutOfSpecCount = 0,
    decimal? WithinStdDev = null,
    string CalculationMethod = "");

public sealed class QaSummaryExportService(ISpcRepository repository)
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
            .GroupBy(measurement => new { measurement.PartNum, measurement.JobNum, measurement.ProcessCode, measurement.OperationSeq, measurement.CharacteristicName })
            .Select(group => BuildRow(
                group.Key.PartNum,
                group.Key.JobNum,
                group.Key.ProcessCode,
                group.Key.OperationSeq,
                group.Key.CharacteristicName,
                group.Select(item => item.Value).ToArray()))
            .OrderBy(row => row.PartNum)
            .ThenBy(row => row.JobNum)
            .ThenBy(row => row.CharacteristicName)
            .ToArray();

        return ServiceResult<IReadOnlyList<QaSummaryRow>>.Ok(rows);
    }

    public ServiceResult<IReadOnlyList<JobVariableMeanRow>> BuildJobVariableMeans(string jobNum)
    {
        return BuildJobVariableMeans([jobNum]);
    }

    public ServiceResult<IReadOnlyList<JobVariableMeanRow>> BuildPartCapability(string partNum, string? shift = null, DataReviewFilter? filter = null)
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
            join process in repository.Processes on operation.ProcessId equals process.Id
            join characteristic in repository.Characteristics on operation.Id equals characteristic.OperationId
            join spec in repository.SpecLimits on characteristic.Id equals spec.CharacteristicId
            where operation.PartId == part.Id && characteristic.Type == CharacteristicType.Variable &&
                (filter == null || (DataReviewFilter.Text(filter.ProcessCode, process.ProcessCode) &&
                    (!filter.OperationSeq.HasValue || operation.OperationSeq == filter.OperationSeq) &&
                    DataReviewFilter.Text(filter.CharacteristicName, characteristic.Name)))
            orderby operation.OperationSeq, characteristic.Name
            select new { part.PartNum, process.ProcessCode, operation.OperationSeq, characteristic.Id, characteristic.Name, characteristic.Type, characteristic.UnitOfMeasure, spec.Nominal, spec.Lsl, spec.Usl };

        var rows = plans
            .Select(plan => BuildCapabilityRow("All Jobs", plan.PartNum, plan.ProcessCode, plan.OperationSeq, filter?.InspectionPhase ?? PhaseText(plan.Id), plan.Name, plan.Type, plan.UnitOfMeasure, plan.Nominal, plan.Lsl, plan.Usl,
                repository.Measurements
                    .Where(measurement =>
                        (string.IsNullOrWhiteSpace(shift) || measurement.OperatorShift.Equals(shift.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                        (filter == null || filter.Measurement(measurement)) &&
                        measurement.PartNum.Equals(plan.PartNum, StringComparison.OrdinalIgnoreCase) &&
                        measurement.ProcessCode.Equals(plan.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                        measurement.OperationSeq == plan.OperationSeq &&
                        measurement.CharacteristicName.Equals(plan.Name, StringComparison.OrdinalIgnoreCase))
                    .ToArray()))
            .ToArray();

        return ServiceResult<IReadOnlyList<JobVariableMeanRow>>.Ok(rows);
    }

    public ServiceResult<IReadOnlyList<JobVariableMeanRow>> BuildJobVariableMeans(IReadOnlyCollection<string> jobNums, string? shift = null, DataReviewFilter? filter = null)
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
            .SelectMany(job => BuildSingleJobVariableMeans(job, shift, filter))
            .ToArray();

        return ServiceResult<IReadOnlyList<JobVariableMeanRow>>.Ok(rows);
    }

    public ServiceResult<string> ExportJobVariableMeansCsv(string jobNum)
    {
        return ExportJobVariableMeansCsv([jobNum]);
    }

    public ServiceResult<string> ExportJobVariableMeansCsv(IReadOnlyCollection<string> jobNums)
    {
        var summary = BuildJobVariableMeans(jobNums);
        if (!summary.Succeeded || summary.Value is null)
        {
            return ServiceResult<string>.Fail(summary.Errors);
        }

        var headers = new[]
        {
            "JobNum",
            "PartNum",
            "ProcessCode",
            "OperationSeq",
            "InspectionPhases",
            "CharacteristicName",
            "CharacteristicType",
            "UnitOfMeasure",
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
            "OutOfSpecCount",
            "Status"
        };
        var rows = summary.Value.Select(row => new Dictionary<string, string>
        {
            ["JobNum"] = row.JobNum,
            ["PartNum"] = row.PartNum,
            ["ProcessCode"] = row.ProcessCode,
            ["OperationSeq"] = row.OperationSeq.ToString(),
            ["InspectionPhases"] = row.InspectionPhases,
            ["CharacteristicName"] = row.CharacteristicName,
            ["CharacteristicType"] = row.CharacteristicType.ToString(),
            ["UnitOfMeasure"] = row.UnitOfMeasure,
            ["Nominal"] = row.Nominal?.ToString("0.#####") ?? "",
            ["LSL"] = row.Lsl?.ToString("0.#####") ?? "",
            ["USL"] = row.Usl?.ToString("0.#####") ?? "",
            ["Mean"] = row.Mean?.ToString("0.#####") ?? string.Empty,
            ["Min"] = row.Min?.ToString("0.#####") ?? string.Empty,
            ["Max"] = row.Max?.ToString("0.#####") ?? string.Empty,
            ["StdDev"] = row.StdDev?.ToString("0.#####") ?? string.Empty,
            ["Cp"] = row.Cp?.ToString("0.#####") ?? string.Empty,
            ["Cpk"] = row.Cpk?.ToString("0.#####") ?? string.Empty,
            ["Pp"] = row.Pp?.ToString("0.#####") ?? string.Empty,
            ["Ppk"] = row.Ppk?.ToString("0.#####") ?? string.Empty,
            ["Count"] = row.Count.ToString(),
            ["OutOfSpecCount"] = row.OutOfSpecCount.ToString(),
            ["Status"] = row.Status
        });

        return ServiceResult<string>.Ok(CsvSupport.WriteRows(headers, rows));
    }

    private IEnumerable<JobVariableMeanRow> BuildSingleJobVariableMeans(Job job, string? shift = null, DataReviewFilter? filter = null)
    {
        var plans =
            from part in repository.Parts
            join operation in repository.Operations on part.Id equals operation.PartId
            join process in repository.Processes on operation.ProcessId equals process.Id
            join characteristic in repository.Characteristics on operation.Id equals characteristic.OperationId
            join spec in repository.SpecLimits on characteristic.Id equals spec.CharacteristicId
            where part.PartNum.Equals(job.PartNum, StringComparison.OrdinalIgnoreCase) &&
                (filter == null || (DataReviewFilter.Text(filter.ProcessCode, process.ProcessCode) &&
                    (!filter.OperationSeq.HasValue || operation.OperationSeq == filter.OperationSeq) &&
                    DataReviewFilter.Text(filter.CharacteristicName, characteristic.Name)))
            orderby operation.OperationSeq, characteristic.Name
            select new { part.PartNum, process.ProcessCode, operation.OperationSeq, characteristic.Id, characteristic.Name, characteristic.Type, characteristic.UnitOfMeasure, spec.Nominal, spec.Lsl, spec.Usl };

        return plans.Select(plan =>
            BuildCapabilityRow(job.JobNum, plan.PartNum, plan.ProcessCode, plan.OperationSeq, filter?.InspectionPhase ?? PhaseText(plan.Id), plan.Name, plan.Type, plan.UnitOfMeasure, plan.Nominal, plan.Lsl, plan.Usl,
                repository.Measurements
                .Where(measurement =>
                    (string.IsNullOrWhiteSpace(shift) || measurement.OperatorShift.Equals(shift.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                    (filter == null || filter.Measurement(measurement)) &&
                    measurement.JobNum.Equals(job.JobNum, StringComparison.OrdinalIgnoreCase) &&
                    measurement.PartNum.Equals(plan.PartNum, StringComparison.OrdinalIgnoreCase) &&
                    measurement.ProcessCode.Equals(plan.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                    measurement.OperationSeq == plan.OperationSeq &&
                    measurement.CharacteristicName.Equals(plan.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray()));
    }

    private JobVariableMeanRow BuildCapabilityRow(
        string jobNum,
        string partNum,
        string processCode,
        int operationSeq,
        string inspectionPhases,
        string characteristicName,
        CharacteristicType characteristicType,
        string unitOfMeasure,
        decimal nominal,
        decimal lsl,
        decimal usl,
        IReadOnlyCollection<InspectionMeasurement> measurements)
    {
        var characteristic = FindCharacteristic(partNum,processCode,operationSeq,characteristicName);
        var plans = repository.InspectionPlans.Where(plan => plan.CharacteristicId == characteristic?.Id).ToArray();
        var applicablePlans = plans.Where(plan => measurements.Count == 0 || measurements.Any(m => m.InspectionPhase.Equals(plan.InspectionPhase,StringComparison.OrdinalIgnoreCase))).ToArray();
        var specifications = applicablePlans.Select(plan => (plan.Lsl,plan.Usl)).Distinct().ToArray();
        decimal? lower = plans.Length == 0 ? lsl : specifications.Length == 1 ? specifications[0].Lsl : null;
        decimal? upper = plans.Length == 0 ? usl : specifications.Length == 1 ? specifications[0].Usl : null;
        var targets = applicablePlans.Select(plan => plan.Nominal).Distinct().ToArray();
        decimal? target = plans.Length == 0 ? nominal : targets.Length == 1 ? targets[0] : null;
        var result = new CapabilityCalculationService(repository).Calculate(measurements,lower,upper);
        var measured = characteristicType == CharacteristicType.Variable;

        return new JobVariableMeanRow(
            jobNum,
            partNum,
            processCode,
            operationSeq,
            inspectionPhases,
            characteristicName,
            characteristicType,
            unitOfMeasure,
            target, lower, upper, result.Mean,result.Min,result.Max,result.OverallStdDev,
            measured ? result.Cp : null,measured ? result.Cpk : null,measured ? result.Pp : null,measured ? result.Ppk : null,
            result.Count,0,result.Count == 0 ? "NoData" : specifications.Length > 1 ? "Mixed specification limits" : "Calculated; no acceptance requirement",
            result.OutOfSpecCount,result.WithinStdDev,specifications.Length > 1 ? "Capability unavailable across mixed phase specification limits" : result.WithinMethod);
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

    private QaSummaryRow BuildRow(string partNum, string jobNum, string processCode, int operationSeq, string characteristicName, IReadOnlyList<decimal> values)
    {
        var spec = FindSpecLimit(partNum, processCode, operationSeq, characteristicName);
        var acceptedValues = spec is null
            ? values.ToArray()
            : values.Where(value => value >= spec.Lsl && value <= spec.Usl).ToArray();
        var outOfSpecCount = values.Count - acceptedValues.Length;
        var mean = acceptedValues.Length == 0 ? (decimal?)null : acceptedValues.Average();
        var min = acceptedValues.Length == 0 ? (decimal?)null : acceptedValues.Min();
        var max = acceptedValues.Length == 0 ? (decimal?)null : acceptedValues.Max();
        var stdDev = StandardDeviation(acceptedValues);

        return new QaSummaryRow(
            partNum,
            jobNum,
            characteristicName,
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

    private SpecLimit? FindSpecLimit(string partNum, string processCode, int operationSeq, string characteristicName)
    {
        var characteristic = FindCharacteristic(partNum, processCode, operationSeq, characteristicName);
        return characteristic is null
            ? null
            : repository.SpecLimits.FirstOrDefault(item => item.CharacteristicId == characteristic.Id);
    }

    private Characteristic? FindCharacteristic(string partNum, string processCode, int operationSeq, string characteristicName)
    {
        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase));
        var process = repository.Processes.FirstOrDefault(item => item.ProcessCode.Equals(processCode, StringComparison.OrdinalIgnoreCase));
        if (part is null || process is null)
        {
            return null;
        }

        var operation = repository.Operations.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.ProcessId == process.Id &&
            item.OperationSeq == operationSeq);
        if (operation is null)
        {
            return null;
        }

        return repository.Characteristics.FirstOrDefault(item =>
            item.OperationId == operation.Id &&
            item.Name.Equals(characteristicName, StringComparison.OrdinalIgnoreCase));
    }

    private string PhaseText(Guid characteristicId)
    {
        var phases = repository.InspectionPlans
            .Where(plan => plan.CharacteristicId == characteristicId)
            .Select(plan => plan.InspectionPhase)
            .Where(phase => !string.IsNullOrWhiteSpace(phase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(PhaseSort)
            .ThenBy(phase => phase)
            .ToArray();

        return phases.Length == 0 ? "All phases" : string.Join(", ", phases);
    }

    private static int PhaseSort(string phase)
    {
        return phase.Trim().ToUpperInvariant() switch
        {
            "STARTUP" => 0,
            "SETUP" => 1,
            "IN PROCESS" => 2,
            "SPOOL" => 3,
            "COIL CHANGE" => 4,
            _ => 99
        };
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

}

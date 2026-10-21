using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record PartQualityRequest(string PartNum, string? JobNum = null, string? ResourceId = null,
    string? OperatorShift = null, string? ProcessCode = null, int? OperationSeq = null,
    string? InspectionPhase = null, DateTimeOffset? From = null, DateTimeOffset? To = null,
    string? VariableKey = null, int Page = 1, int PageSize = 20, string? CharacteristicName = null);
public sealed record QualityReading(string Variable, decimal Value, string Unit);
public sealed record QualityFailure(Guid Id, string JobNum, string ProcessCode, int OperationSeq,
    string InspectionPhase, int Number, string ResourceId, string Operator, string Shift, DateTimeOffset At,
    long? MachineCounter, string Category, string Cause, string Action, string Reason, string Detail,
    IReadOnlyList<string> VariableKeys, IReadOnlyList<QualityReading> FailedReadings, string RecordedDetail = "");
public sealed record QualityVariable(string Key, string Name, string ProcessCode, int OperationSeq, string Unit,
    int Checked, int Failed, decimal? FailureRate, DateTimeOffset? LastFailure);
public sealed record PartQualityResult(string PartNum, string Description, int Inspections, int Passed, int Failed,
    decimal? PassRate, int MissingReadingLinks, int UnassignedFailures,
    IReadOnlyList<QualityVariable> Variables, IReadOnlyList<QualityFailure> Failures,
    int MatchingFailures = 0, int FailurePage = 1, int FailurePageSize = 20);

public sealed class PartQualityService(ISpcRepository repository)
{
    private sealed class Variable(string name, string process, int operation, string unit, int order)
    {
        public string Name = name, Process = process, Unit = unit;
        public int Operation = operation, Order = order;
        public HashSet<Guid> Checked = [], Failed = [];
        public DateTimeOffset? LastFailure;
    }
    private static string Key(string process, int operation, string name) =>
        $"{process.Trim()}|{operation}|{name.Trim()}".ToLowerInvariant();
    private static string Phase(string phase) => phase.Trim().ToLowerInvariant() switch
    {
        "set up" => "setup", "coilchange" => "coil change", "spool end" or "endofspool" => "end of spool",
        "spool start" => "spool", var value => value
    };
    private static bool Matches(string? filter, string value) => string.IsNullOrWhiteSpace(filter) ||
        filter.Trim().Equals(value, StringComparison.OrdinalIgnoreCase);
    private static int PhasePosition(string phase) => Phase(phase) switch
    {
        "setup" => 0, "startup" => 1, "in process" => 2, "coil change" => 3,
        "spool" => 4, "end of spool" => 5, _ => 6
    };
    private static decimal? Rate(int numerator, int denominator) => denominator == 0 ? null : 100m * numerator / denominator;

    public ServiceResult<PartQualityResult> Build(PartQualityRequest request)
    {
        var part = repository.Parts.FirstOrDefault(item => Matches(request.PartNum, item.PartNum));
        if (string.IsNullOrWhiteSpace(request.PartNum) || part is null)
            return ServiceResult<PartQualityResult>.Fail("Select an existing part number.");
        if (request.From > request.To) return ServiceResult<PartQualityResult>.Fail("From date must be before To date.");
        var job = repository.Jobs.FirstOrDefault(item => Matches(request.JobNum, item.JobNum));
        if (!string.IsNullOrWhiteSpace(request.JobNum) && job is not null && !Matches(part.PartNum, job.PartNum))
            return ServiceResult<PartQualityResult>.Fail($"Job {job.JobNum} belongs to part {job.PartNum}.");

        bool Scope(string partNum, string jobNum, string resource, string shift, string process, int operation, string phase, DateTimeOffset at) =>
            Matches(part.PartNum, partNum) && Matches(request.JobNum, jobNum) && Matches(request.ResourceId, resource) &&
            Matches(request.OperatorShift, shift) && Matches(request.ProcessCode, process) &&
            (!request.OperationSeq.HasValue || request.OperationSeq == operation) &&
            (string.IsNullOrWhiteSpace(request.InspectionPhase) || Phase(request.InspectionPhase) == Phase(phase)) &&
            (!request.From.HasValue || at >= request.From) && (!request.To.HasValue || at <= request.To);

        var definitions = (from operation in repository.Operations
            join process in repository.Processes on operation.ProcessId equals process.Id
            join characteristic in repository.Characteristics on operation.Id equals characteristic.OperationId
            join plan in repository.InspectionPlans on characteristic.Id equals plan.CharacteristicId
            where operation.PartId == part.Id && plan.SampleSize > 0 && Matches(request.ProcessCode, process.ProcessCode) &&
                (!request.OperationSeq.HasValue || operation.OperationSeq == request.OperationSeq) &&
                (string.IsNullOrWhiteSpace(request.InspectionPhase) || Phase(plan.InspectionPhase) == Phase(request.InspectionPhase))
            orderby operation.OperationSeq, process.ProcessCode, PhasePosition(plan.InspectionPhase), plan.DisplayOrder
            select new { Process = process.ProcessCode, Operation = operation.OperationSeq, Characteristic = characteristic, Plan = plan }).ToArray();
        var variables = new Dictionary<string, Variable>();
        foreach (var definition in definitions)
            variables.TryAdd(Key(definition.Process, definition.Operation, definition.Characteristic.Name),
                new(definition.Characteristic.Name, definition.Process, definition.Operation, definition.Characteristic.UnitOfMeasure, variables.Count));
        var readings = repository.Measurements.ToDictionary(item => item.Id);
        var alerts = repository.Alerts.ToDictionary(item => item.Id);
        var violationIds = repository.RuleViolations.Where(item => item.RuleTriggered is RuleTriggered.SpecLimitViolation or RuleTriggered.AttributeRejected)
            .SelectMany(item => item.MeasurementIds).ToHashSet();
        var numbers = InspectionSequence.Numbers(repository);
        var missingLinks = 0;
        Variable GetVariable(string name, string process, int operation)
        {
            var key = Key(process, operation, name);
            if (!variables.TryGetValue(key, out var variable))
            {
                variable = new(name, process, operation, "", variables.Count);
                variables.Add(key, variable);
            }
            return variable;
        }
        InspectionMeasurement[] Linked(IEnumerable<Guid> ids, string jobNum, string process, int operation, string resource, string phase) =>
            ids.Distinct().Where(readings.ContainsKey).Select(id => readings[id]).Where(item =>
                Matches(part.PartNum, item.PartNum) && Matches(jobNum, item.JobNum) && Matches(process, item.ProcessCode) &&
                item.OperationSeq == operation && Matches(resource, item.ResourceId) && Phase(phase) == Phase(item.InspectionPhase)).ToArray();
        bool IsFailedReading(InspectionMeasurement reading)
        {
            if (violationIds.Contains(reading.Id)) return true;
            var definition = definitions.FirstOrDefault(item => Matches(item.Process, reading.ProcessCode) && item.Operation == reading.OperationSeq &&
                Matches(item.Characteristic.Name, reading.CharacteristicName) && Phase(item.Plan.InspectionPhase) == Phase(reading.InspectionPhase));
            if (definition is null) return false;
            if (definition.Characteristic.Type == CharacteristicType.Attribute) return reading.Value == 0;
            return (definition.Plan.Lsl.HasValue && reading.Value < definition.Plan.Lsl) ||
                (definition.Plan.Usl.HasValue && reading.Value > definition.Plan.Usl);
        }
        var completed = repository.JobPhaseCompletions.Where(item => Scope(item.PartNum, item.JobNum, item.ResourceId, item.OperatorShift,
            item.ProcessCode, item.OperationSeq, item.InspectionPhase, item.CompletedAt)).ToArray();
        foreach (var inspection in completed)
        {
            var linked = Linked(inspection.MeasurementIds, inspection.JobNum, inspection.ProcessCode, inspection.OperationSeq, inspection.ResourceId, inspection.InspectionPhase);
            if (linked.Length == 0 || linked.Length != inspection.MeasurementIds.Distinct().Count()) missingLinks++;
            foreach (var reading in linked)
                GetVariable(reading.CharacteristicName, reading.ProcessCode, reading.OperationSeq).Checked.Add(inspection.Id);
        }
        var failures = new List<QualityFailure>();
        foreach (var failure in repository.FailedInspections.Where(item => !Matches("User Error", item.CauseCategory) &&
            Scope(item.PartNum, item.JobNum, item.ResourceId, item.OperatorShift, item.ProcessCode, item.OperationSeq, item.InspectionPhase, item.FailedAt)))
        {
            var linked = Linked(failure.MeasurementIds, failure.JobNum, failure.ProcessCode, failure.OperationSeq, failure.ResourceId, failure.InspectionPhase);
            if (linked.Length == 0 || linked.Length != failure.MeasurementIds.Distinct().Count()) missingLinks++;
            foreach (var reading in linked)
                GetVariable(reading.CharacteristicName, reading.ProcessCode, reading.OperationSeq).Checked.Add(failure.Id);
            var bad = linked.Where(IsFailedReading).ToArray();
            var keys = bad.Select(item => Key(item.ProcessCode, item.OperationSeq, item.CharacteristicName)).ToHashSet();
            var alert = failure.AlertId.HasValue ? alerts.GetValueOrDefault(failure.AlertId.Value) : null;
            if (alert is not null && Matches(failure.JobNum, alert.JobNum) && Matches(failure.PartNum, alert.PartNum) &&
                Matches(failure.ResourceId, alert.ResourceId) && alert.RuleTriggered is RuleTriggered.SpecLimitViolation or RuleTriggered.AttributeRejected)
            {
                keys.Add(Key(failure.ProcessCode, failure.OperationSeq, alert.CharacteristicName));
                GetVariable(alert.CharacteristicName, failure.ProcessCode, failure.OperationSeq).Checked.Add(failure.Id);
            }
            else alert = null;
            foreach (var key in keys)
            {
                var variable = variables[key];
                variable.Failed.Add(failure.Id);
                if (!variable.LastFailure.HasValue || failure.FailedAt > variable.LastFailure) variable.LastFailure = failure.FailedAt;
            }
            failures.Add(new(failure.Id, failure.JobNum, failure.ProcessCode, failure.OperationSeq, failure.InspectionPhase,
                numbers[failure.Id], failure.ResourceId, failure.FailedByUserId, failure.OperatorShift, failure.FailedAt, failure.MachineCounter,
                failure.CauseCategory, failure.CauseText, failure.SolutionText, failure.Reason,
                alert is null ? failure.FailureDetail : $"{alert.CharacteristicName}: {alert.Detail}", keys.ToArray(),
                bad.Select(item => new QualityReading(item.CharacteristicName, item.Value, GetVariable(item.CharacteristicName, item.ProcessCode, item.OperationSeq).Unit)).ToArray(), failure.FailureDetail));
        }
        var selectedVariables = variables.Where(pair => Matches(request.CharacteristicName, pair.Value.Name)).ToArray();
        var selectedChecked = selectedVariables.SelectMany(pair => pair.Value.Checked).ToHashSet();
        var selectedFailed = selectedVariables.SelectMany(pair => pair.Value.Failed).ToHashSet();
        var variableFiltered = !string.IsNullOrWhiteSpace(request.CharacteristicName);
        var total = variableFiltered ? selectedChecked.Count : completed.Length + failures.Count;
        var failedCount = variableFiltered ? selectedFailed.Count : failures.Count;
        var matching = string.IsNullOrWhiteSpace(request.VariableKey) ? [] : failures
            .Where(item => selectedVariables.Any(variable => variable.Key == request.VariableKey.Trim().ToLowerInvariant()) &&
                item.VariableKeys.Contains(request.VariableKey.Trim().ToLowerInvariant()))
            .OrderByDescending(item => item.At).ThenBy(item => item.Id).ToArray();
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var page = Math.Clamp(request.Page, 1, Math.Max(1, (int)Math.Ceiling((decimal)matching.Length / pageSize)));
        return ServiceResult<PartQualityResult>.Ok(new(part.PartNum, part.Description, total, total - failedCount, failedCount,
            Rate(total - failedCount, total), missingLinks, failures.Count(item => item.VariableKeys.Count == 0),
            selectedVariables.OrderBy(pair => pair.Value.Order).Select(pair => new QualityVariable(pair.Key, pair.Value.Name, pair.Value.Process, pair.Value.Operation, pair.Value.Unit,
                pair.Value.Checked.Count, pair.Value.Failed.Count, Rate(pair.Value.Failed.Count, pair.Value.Checked.Count), pair.Value.LastFailure))
                .ToArray(), matching.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), matching.Length, page, pageSize));
    }
}

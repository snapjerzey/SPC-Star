using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record InspectionFrequencyEvent(FrequencyUnit EventType, DateTimeOffset Timestamp);

public sealed record InspectionFrequencyCheckRequest(
    string JobNum,
    string PartNum,
    string ProcessCode,
    int OperationSeq,
    string CharacteristicName,
    string ResourceId,
    DateTimeOffset Now,
    int? CurrentQuantity,
    int? QuantityAtLastInspection,
    IReadOnlyCollection<InspectionFrequencyEvent> Events,
    string InspectionPhase = "In Process");

public sealed record InspectionFrequencyStatus(
    InspectionDueStatus Status,
    DateTimeOffset? LastInspectionAt,
    DateTimeOffset? NextInspectionDueAt,
    int? NextInspectionDueQuantity,
    IReadOnlyList<string> Reasons);

public sealed class InspectionFrequencyService(ISpcRepository repository)
{
    public InspectionFrequencyStatus Evaluate(InspectionFrequencyCheckRequest request)
    {
        var plan = FindPlan(request);
        if (plan is null)
        {
            return new InspectionFrequencyStatus(InspectionDueStatus.NotConfigured, null, null, null, ["No inspection plan found."]);
        }

        var lastInspectionAt = repository.Measurements
            .Where(measurement =>
                measurement.JobNum.Equals(request.JobNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.PartNum.Equals(request.PartNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.ProcessCode.Equals(request.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                measurement.OperationSeq == request.OperationSeq &&
                measurement.ResourceId.Equals(request.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                measurement.InspectionPhase.Equals(NormalizeInspectionPhase(request.InspectionPhase), StringComparison.OrdinalIgnoreCase) &&
                measurement.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(measurement => measurement.Timestamp)
            .Select(measurement => (DateTimeOffset?)measurement.Timestamp)
            .FirstOrDefault();

        return plan.Frequency.Type switch
        {
            FrequencyType.Time => EvaluateTime(plan.Frequency, request.Now, lastInspectionAt),
            FrequencyType.Quantity => EvaluateQuantity(plan.Frequency, request.CurrentQuantity, request.QuantityAtLastInspection, lastInspectionAt),
            FrequencyType.Event => EvaluateEvent(plan.Frequency, request.Events, lastInspectionAt),
            _ => new InspectionFrequencyStatus(InspectionDueStatus.NotConfigured, lastInspectionAt, null, null, ["Unsupported frequency type."])
        };
    }

    private InspectionPlan? FindPlan(InspectionFrequencyCheckRequest request)
    {
        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(request.PartNum, StringComparison.OrdinalIgnoreCase));
        var process = repository.Processes.FirstOrDefault(item => item.ProcessCode.Equals(request.ProcessCode, StringComparison.OrdinalIgnoreCase));
        if (part is null || process is null)
        {
            return null;
        }

        var operation = repository.Operations.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.ProcessId == process.Id &&
            item.OperationSeq == request.OperationSeq);
        if (operation is null)
        {
            return null;
        }

        var characteristic = repository.Characteristics.FirstOrDefault(item =>
            item.OperationId == operation.Id &&
            item.Name.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase));

        return characteristic is null
            ? null
            : repository.InspectionPlans.FirstOrDefault(item =>
                item.CharacteristicId == characteristic.Id &&
                item.InspectionPhase.Equals(NormalizeInspectionPhase(request.InspectionPhase), StringComparison.OrdinalIgnoreCase));
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

    private static InspectionFrequencyStatus EvaluateTime(
        InspectionFrequency frequency,
        DateTimeOffset now,
        DateTimeOffset? lastInspectionAt)
    {
        if (!lastInspectionAt.HasValue)
        {
            return new InspectionFrequencyStatus(InspectionDueStatus.DueNow, null, now, null, ["No completed inspection found."]);
        }

        var interval = frequency.Unit switch
        {
            FrequencyUnit.Minutes => TimeSpan.FromMinutes(frequency.Value),
            FrequencyUnit.Hours => TimeSpan.FromHours(frequency.Value),
            _ => TimeSpan.Zero
        };

        var dueAt = lastInspectionAt.Value.Add(interval);
        if (now > dueAt)
        {
            return new InspectionFrequencyStatus(InspectionDueStatus.Overdue, lastInspectionAt, dueAt, null, ["Time-based inspection is overdue."]);
        }

        if (now == dueAt)
        {
            return new InspectionFrequencyStatus(InspectionDueStatus.DueNow, lastInspectionAt, dueAt, null, ["Time-based inspection is due now."]);
        }

        return new InspectionFrequencyStatus(InspectionDueStatus.NotDue, lastInspectionAt, dueAt, null, []);
    }

    private static InspectionFrequencyStatus EvaluateQuantity(
        InspectionFrequency frequency,
        int? currentQuantity,
        int? quantityAtLastInspection,
        DateTimeOffset? lastInspectionAt)
    {
        if (!currentQuantity.HasValue)
        {
            return new InspectionFrequencyStatus(InspectionDueStatus.NotConfigured, lastInspectionAt, null, null, ["Current quantity is required for quantity-based frequency."]);
        }

        var baseline = quantityAtLastInspection ?? 0;
        var dueQuantity = baseline + frequency.Value;
        if (currentQuantity.Value >= dueQuantity)
        {
            return new InspectionFrequencyStatus(InspectionDueStatus.DueNow, lastInspectionAt, null, dueQuantity, ["Quantity-based inspection is due."]);
        }

        return new InspectionFrequencyStatus(InspectionDueStatus.NotDue, lastInspectionAt, null, dueQuantity, []);
    }

    private static InspectionFrequencyStatus EvaluateEvent(
        InspectionFrequency frequency,
        IReadOnlyCollection<InspectionFrequencyEvent> events,
        DateTimeOffset? lastInspectionAt)
    {
        var matchingEvents = events
            .Where(item => item.EventType == frequency.Unit)
            .OrderByDescending(item => item.Timestamp)
            .ToArray();
        var latestEvent = matchingEvents.FirstOrDefault();

        if (latestEvent is null)
        {
            return new InspectionFrequencyStatus(InspectionDueStatus.NotDue, lastInspectionAt, null, null, []);
        }

        if (!lastInspectionAt.HasValue || latestEvent.Timestamp > lastInspectionAt.Value)
        {
            return new InspectionFrequencyStatus(InspectionDueStatus.DueNow, lastInspectionAt, null, null, [$"Event-based inspection is due for {frequency.Unit}."]);
        }

        return new InspectionFrequencyStatus(InspectionDueStatus.Completed, lastInspectionAt, null, null, ["Required event inspection was completed."]);
    }
}

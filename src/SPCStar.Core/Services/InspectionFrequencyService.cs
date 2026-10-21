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
    long? NextInspectionDueQuantity,
    IReadOnlyList<string> Reasons,
    bool RequiresReinspection = false);

public sealed class InspectionFrequencyService(ISpcRepository repository)
{
    public InspectionFrequencyStatus Evaluate(InspectionFrequencyCheckRequest request)
    {
        var plan = FindPlan(request);
        if (plan is null)
        {
            return new InspectionFrequencyStatus(InspectionDueStatus.NotConfigured, null, null, null, ["No inspection plan found."]);
        }

        if (RequiresReinspection(request, plan))
        {
            return new InspectionFrequencyStatus(InspectionDueStatus.DueNow, null, null, null,
                ["Repeat the failed inspection now. No additional machine count is required."], true);
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
            FrequencyType.Quantity when plan.Frequency.Unit is FrequencyUnit.Pieces or FrequencyUnit.Box => EvaluateMachineCounter(request, plan, lastInspectionAt),
            FrequencyType.Quantity => EvaluateQuantity(plan.Frequency, request.CurrentQuantity, request.QuantityAtLastInspection, lastInspectionAt),
            FrequencyType.Event => EvaluateEvent(plan.Frequency, request.Events, lastInspectionAt),
            _ => new InspectionFrequencyStatus(InspectionDueStatus.NotConfigured, lastInspectionAt, null, null, ["Unsupported frequency type."])
        };
    }

    private InspectionFrequencyStatus EvaluateMachineCounter(InspectionFrequencyCheckRequest request, InspectionPlan plan, DateTimeOffset? lastInspectionAt)
    {
        var nextDue = new MachineCounterSchedule(repository).NextDue(request.JobNum, request.PartNum, request.ProcessCode,
            request.OperationSeq, request.CharacteristicName, plan.Frequency.Value, plan.Frequency.FirstDueValue);
        if (!request.CurrentQuantity.HasValue)
            return new(InspectionDueStatus.NotConfigured, lastInspectionAt, null, nextDue,
                ["Current quantity is required for quantity-based frequency."]);
        var due = request.CurrentQuantity.Value >= nextDue;
        return new(due ? InspectionDueStatus.DueNow : InspectionDueStatus.NotDue, lastInspectionAt, null, nextDue,
            due ? ["Quantity-based inspection is due."] : []);
    }

    internal bool RequiresReinspection(InspectionFrequencyCheckRequest request, InspectionPlan plan)
    {
        bool Matches(string job, string part, string process, int operation, string resource, string phase) =>
            job.Equals(request.JobNum, StringComparison.OrdinalIgnoreCase) &&
            part.Equals(request.PartNum, StringComparison.OrdinalIgnoreCase) &&
            process.Equals(request.ProcessCode, StringComparison.OrdinalIgnoreCase) && operation == request.OperationSeq &&
            resource.Equals(request.ResourceId, StringComparison.OrdinalIgnoreCase) &&
            NormalizeInspectionPhase(phase).Equals(NormalizeInspectionPhase(request.InspectionPhase), StringComparison.OrdinalIgnoreCase);

        var failure = repository.FailedInspections.Where(item =>
                !item.CauseCategory.Equals("User Error", StringComparison.OrdinalIgnoreCase) &&
                Matches(item.JobNum, item.PartNum, item.ProcessCode, item.OperationSeq, item.ResourceId, item.InspectionPhase))
            .OrderByDescending(item => item.FailedAt).FirstOrDefault();
        if (failure is null) return false;
        var completions = repository.JobPhaseCompletions.Where(item =>
            Matches(item.JobNum, item.PartNum, item.ProcessCode, item.OperationSeq, item.ResourceId, item.InspectionPhase)).ToArray();
        if (completions.Any(item => item.CompletedAt >= failure.FailedAt)) return false;

        if (failure.RequiredCharacteristicNames.Count > 0)
            return failure.RequiredCharacteristicNames.Contains(request.CharacteristicName, StringComparer.OrdinalIgnoreCase);

        // Repeat the due or entered checks, not unrelated items with a later frequency.
        if (plan.Frequency.Type != FrequencyType.Quantity) return true;
        var nextDue = new MachineCounterSchedule(repository).NextDue(request.JobNum, request.PartNum, request.ProcessCode,
            request.OperationSeq, request.CharacteristicName, plan.Frequency.Value, plan.Frequency.FirstDueValue);
        if (failure.MachineCounter >= nextDue) return true;
        var readingIds = failure.MeasurementIds.ToHashSet();
        return repository.Measurements.Any(item => readingIds.Contains(item.Id) &&
            item.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase));
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



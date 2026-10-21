using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class InspectionFrequencyServiceTests
{
    [Fact]
    public void FailedInspectionRequiresImmediateRetryAtTheSameCounterUntilItPasses()
    {
        var repository = RepositoryWithPlan(FrequencyType.Quantity, 1500, FrequencyUnit.Pieces);
        var now = DateTimeOffset.Parse("2026-01-01T08:00:00Z");
        repository.JobPhaseCompletions.Add(Completion(now.AddMinutes(-10), 1500));
        repository.FailedInspections.Add(Failure(now, 3000));
        var request = Request(now: now.AddMinutes(1), currentQuantity: 3000, quantityAtLastInspection: 1500);
        var retry = new InspectionFrequencyService(repository).Evaluate(request);
        Assert.Equal(InspectionDueStatus.DueNow, retry.Status);
        Assert.True(retry.RequiresReinspection);
        Assert.True(new InspectionFrequencyService(repository).Evaluate(request).RequiresReinspection);
        var reading = Measurement(now.AddMinutes(2)); repository.Measurements.Add(reading);
        var completion = Completion(now.AddMinutes(2), 3000); completion.MeasurementIds.Add(reading.Id);
        repository.JobPhaseCompletions.Add(completion);
        var passed = new InspectionFrequencyService(repository).Evaluate(request with { QuantityAtLastInspection = 3000 });
        Assert.False(passed.RequiresReinspection);
        Assert.Equal(InspectionDueStatus.NotDue, passed.Status);
        Assert.Equal(4500, passed.NextInspectionDueQuantity);
    }

    [Theory]
    [InlineData("job")]
    [InlineData("part")]
    [InlineData("operation")]
    [InlineData("machine")]
    [InlineData("phase")]
    public void FailedInspectionDoesNotMakeAnotherContextDue(string changed)
    {
        var repository = RepositoryWithPlan(FrequencyType.Quantity, 4500, FrequencyUnit.Pieces);
        var failure = Failure(DateTimeOffset.Parse("2026-01-01T08:00:00Z"), 4500);
        if (changed == "job") failure.JobNum = "OTHER";
        if (changed == "part") failure.PartNum = "OTHER";
        if (changed == "operation") failure.OperationSeq = 20;
        if (changed == "machine") failure.ResourceId = "OTHER";
        if (changed == "phase") failure.InspectionPhase = "Setup";
        repository.FailedInspections.Add(failure);
        Assert.False(new InspectionFrequencyService(repository).Evaluate(Request(currentQuantity: 3000)).RequiresReinspection);
    }

    [Fact]
    public void RetryDoesNotPullInLaterFrequencyUnlessThatCheckWasEntered()
    {
        var repository = RepositoryWithPlan(FrequencyType.Quantity, 4500, FrequencyUnit.Pieces);
        var failure = Failure(DateTimeOffset.Parse("2026-01-01T08:00:00Z"), 3000);
        repository.FailedInspections.Add(failure);
        var service = new InspectionFrequencyService(repository);
        Assert.False(service.Evaluate(Request(currentQuantity: 3000)).RequiresReinspection);
        var reading = Measurement(failure.FailedAt);
        repository.Measurements.Add(reading);
        failure.MeasurementIds.Add(reading.Id);
        Assert.True(service.Evaluate(Request(currentQuantity: 3000)).RequiresReinspection);
    }

    [Fact]
    public void UserErrorAndDraftReadingsDoNotCreateRetryRequirements()
    {
        var repository = RepositoryWithPlan(FrequencyType.Quantity, 4500, FrequencyUnit.Pieces);
        var failure = Failure(DateTimeOffset.Parse("2026-01-01T08:00:00Z"), 4500);
        failure.CauseCategory = "User Error";
        repository.FailedInspections.Add(failure);
        repository.Measurements.Add(Measurement(failure.FailedAt));
        Assert.False(new InspectionFrequencyService(repository).Evaluate(Request(currentQuantity: 3000)).RequiresReinspection);
    }

    private static FailedInspection Failure(DateTimeOffset at, long counter) => new()
    {
        JobNum = "J100", PartNum = "P100", ProcessCode = "MOLD", OperationSeq = 10,
        ResourceId = "PRESS1", InspectionPhase = "In Process", FailedByUserId = "line1",
        FailedAt = at, MachineCounter = counter, CauseCategory = "Machine"
    };

    [Fact]
    public void ExplicitFailedChecklistRetriesUnreachedChecksEvenBeforeTheirNextScheduledCount()
    {
        var repository = RepositoryWithPlan(FrequencyType.Quantity, 4500, FrequencyUnit.Pieces);
        var failure = Failure(DateTimeOffset.Parse("2026-01-01T08:00:00Z"), 3000);
        failure.RequiredCharacteristicNames.Add("Diameter");
        repository.FailedInspections.Add(failure);
        var result = new InspectionFrequencyService(repository).Evaluate(Request(currentQuantity: 3000));
        Assert.True(result.RequiresReinspection);
        Assert.Empty(failure.MeasurementIds);
    }

    private static JobPhaseCompletion Completion(DateTimeOffset at, long counter) => new()
    {
        JobNum = "J100", PartNum = "P100", ProcessCode = "MOLD", OperationSeq = 10,
        ResourceId = "PRESS1", InspectionPhase = "In Process", CompletedByUserId = "operator1",
        CompletedAt = at, MachineCounter = counter
    };

    [Fact]
    public void Evaluate_ReturnsNotConfigured_WhenPlanMissing()
    {
        var service = new InspectionFrequencyService(new InMemorySpcRepository());

        var result = service.Evaluate(Request());

        Assert.Equal(InspectionDueStatus.NotConfigured, result.Status);
    }

    [Fact]
    public void Evaluate_TimeBasedPlanIsOverdue_WhenLastInspectionIsPastInterval()
    {
        var repository = RepositoryWithPlan(FrequencyType.Time, 30, FrequencyUnit.Minutes);
        repository.Measurements.Add(Measurement(DateTimeOffset.Parse("2026-01-01T08:00:00Z")));
        var service = new InspectionFrequencyService(repository);

        var result = service.Evaluate(Request(now: DateTimeOffset.Parse("2026-01-01T08:45:00Z")));

        Assert.Equal(InspectionDueStatus.Overdue, result.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T08:30:00Z"), result.NextInspectionDueAt);
    }

    [Fact]
    public void Evaluate_QuantityBasedPlanUsesTheFixedScheduleInsteadOfAnUnverifiedClientBaseline()
    {
        var repository = RepositoryWithPlan(FrequencyType.Quantity, 5000, FrequencyUnit.Pieces);
        var service = new InspectionFrequencyService(repository);

        var result = service.Evaluate(Request(currentQuantity: 12000, quantityAtLastInspection: 7000));

        Assert.Equal(InspectionDueStatus.DueNow, result.Status);
        Assert.Equal(5000, result.NextInspectionDueQuantity);
    }

    [Fact]
    public void Evaluate_EventBasedPlanIsDue_WhenEventOccurredAfterLastInspection()
    {
        var repository = RepositoryWithPlan(FrequencyType.Event, 1, FrequencyUnit.MaterialChange);
        repository.Measurements.Add(Measurement(DateTimeOffset.Parse("2026-01-01T08:00:00Z")));
        var service = new InspectionFrequencyService(repository);

        var result = service.Evaluate(Request(
            events:
            [
                new InspectionFrequencyEvent(FrequencyUnit.MaterialChange, DateTimeOffset.Parse("2026-01-01T08:10:00Z"))
            ]));

        Assert.Equal(InspectionDueStatus.DueNow, result.Status);
    }

    private static InMemorySpcRepository RepositoryWithPlan(FrequencyType type, int value, FrequencyUnit unit)
    {
        var repository = new InMemorySpcRepository();
        var part = new Part { PartNum = "P100", Description = "Widget" };
        var process = new ManufacturingProcess { ProcessCode = "MOLD", Description = "Molding" };
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        var characteristic = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Diameter",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "mm",
        };

        repository.Parts.Add(part);
        repository.Processes.Add(process);
        repository.Operations.Add(operation);
        repository.Characteristics.Add(characteristic);
        repository.InspectionPlans.Add(new InspectionPlan
        {
            CharacteristicId = characteristic.Id,
            SampleSize = 1,
            AlertRuleSet = "WesternElectric",
            Frequency = new InspectionFrequency { Type = type, Value = value, Unit = unit }
        });
        return repository;
    }

    private static InspectionFrequencyCheckRequest Request(
        DateTimeOffset? now = null,
        int? currentQuantity = null,
        int? quantityAtLastInspection = null,
        IReadOnlyCollection<InspectionFrequencyEvent>? events = null)
    {
        return new InspectionFrequencyCheckRequest(
            "J100",
            "P100",
            "MOLD",
            10,
            "Diameter",
            "PRESS1",
            now ?? DateTimeOffset.Parse("2026-01-01T08:00:00Z"),
            currentQuantity,
            quantityAtLastInspection,
            events ?? []);
    }

    private static InspectionMeasurement Measurement(DateTimeOffset timestamp)
    {
        return new InspectionMeasurement
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            Value = 5m,
            Timestamp = timestamp,
            OperatorUserId = "operator1"
        };
    }
}

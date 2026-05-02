using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class InspectionFrequencyServiceTests
{
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
    public void Evaluate_QuantityBasedPlanIsDue_WhenCurrentQuantityPassesDueQuantity()
    {
        var repository = RepositoryWithPlan(FrequencyType.Quantity, 5000, FrequencyUnit.Pieces);
        var service = new InspectionFrequencyService(repository);

        var result = service.Evaluate(Request(currentQuantity: 12000, quantityAtLastInspection: 7000));

        Assert.Equal(InspectionDueStatus.DueNow, result.Status);
        Assert.Equal(12000, result.NextInspectionDueQuantity);
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
            IsRequiredForCoa = true
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

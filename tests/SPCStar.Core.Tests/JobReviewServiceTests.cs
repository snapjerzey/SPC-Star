using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class JobReviewServiceTests
{
    [Fact]
    public void Build_CombinedFiltersSelectOnlyMatchingFinalizedInspection()
    {
        var r = RepositoryWithMeasurement(); r.Jobs.Add(new Job { JobNum="J100", PartNum="P100" });
        var reading = r.Measurements.Single();
        var completion = Completion(r, reading); completion.OperatorShift = "5 Day"; r.JobPhaseCompletions.Add(completion);
        var filter = new DataReviewFilter(completion.ProcessCode, completion.OperationSeq, completion.InspectionPhase,
            completion.ResourceId, reading.CharacteristicName, completion.CompletedAt, completion.CompletedAt);
        var service = new JobReviewService(r,new QaSummaryExportService(r),new JobHistoryService(r));
        Assert.Single(service.Build("P100","J100","5 Day",filter).Value!.History);
        Assert.Empty(service.Build("P100","J100","5 Day",filter with { ResourceId="Other" }).Value!.History);
        Assert.Empty(service.Build("P100","J100","5 Day",filter with { CharacteristicName="Other" }).Value!.History);
        Assert.Empty(service.Build("P100","J100","5 Day",filter with { OperationSeq=100 }).Value!.History);
    }

    [Fact]
    public void Build_ShiftSelectsWholeInspectionsAndTheirLinkedReadings()
    {
        var repository = RepositoryWithMeasurement();
        repository.Jobs.Add(new Job { JobNum = "J100", PartNum = "P100" });
        var reading = repository.Measurements.Single();
        var day = Completion(repository, reading);
        day.OperatorShift = "1st Half Days";
        repository.JobPhaseCompletions.Add(day);
        var nightReading = Measurement(5.1m);
        repository.Measurements.Add(nightReading);
        var night = Completion(repository, nightReading);
        night.OperatorShift = "1st Half Nights";
        repository.JobPhaseCompletions.Add(night);
        var service = new JobReviewService(repository, new QaSummaryExportService(repository), new JobHistoryService(repository));
        var result = service.Build("P100", "J100", "1st Half Days");
        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(reading.Id, Assert.Single(result.Value!.Measurements).Id);
        Assert.Equal(day.Id, Assert.Single(result.Value.History).Id);
        Assert.Equal(2, service.Build("P100", "J100").Value!.History.Count);
    }

    [Fact]
    public void Build_PreservesAllRecordedReadingsAfterChecklistAndSampleSizeChanges()
    {
        var repository = RepositoryWithMeasurement();
        var part = new Part { PartNum = "P100", Description = "Widget" };
        var process = new ManufacturingProcess { ProcessCode = "MOLD", Description = "Molding" };
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        var characteristic = new Characteristic { OperationId = operation.Id, Name = "Renamed Diameter", Type = CharacteristicType.Variable, UnitOfMeasure = "mm" };
        repository.Parts.Add(part); repository.Processes.Add(process); repository.Operations.Add(operation);
        repository.Characteristics.Add(characteristic);
        repository.InspectionPlans.Add(new InspectionPlan { CharacteristicId = characteristic.Id, InspectionPhase = "In Process", SampleSize = 6, AlertRuleSet = "SpecLimitOnly" });
        repository.Jobs.Add(new Job { JobNum = "J100", PartNum = "P100" });
        var first = repository.Measurements.Single();
        var second = Measurement(5.1m); repository.Measurements.Add(second);
        var completion = Completion(repository, second); completion.MeasurementIds.Add(first.Id);
        repository.JobPhaseCompletions.Add(completion);
        var result = new JobReviewService(repository, new QaSummaryExportService(repository), new JobHistoryService(repository)).Build("P100", "J100");
        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(2, result.Value!.Measurements.Count);
        var history = Assert.Single(result.Value.History, row => row.EntryType == "PhaseComplete");
        Assert.Equal(completion.MeasurementIds, history.MeasurementIds);
        Assert.All(result.Value.Measurements, reading => Assert.Equal("Diameter", reading.CharacteristicName));
    }

    [Fact]
    public void Build_IncludesActualReadingsReferencedByHistoryForExistingCompletion()
    {
        var repository = RepositoryWithMeasurement();
        var part = new Part { PartNum = "P100", Description = "Widget" };
        var process = new ManufacturingProcess { ProcessCode = "MOLD", Description = "Molding" };
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        var characteristic = new Characteristic { OperationId = operation.Id, Name = "Diameter", Type = CharacteristicType.Variable, UnitOfMeasure = "mm" };
        repository.Parts.Add(part); repository.Processes.Add(process); repository.Operations.Add(operation);
        repository.Characteristics.Add(characteristic);
        repository.InspectionPlans.Add(new InspectionPlan { CharacteristicId = characteristic.Id, InspectionPhase = "In Process", SampleSize = 1, AlertRuleSet = "SpecLimitOnly" });
        repository.Jobs.Add(new Job { JobNum = "J100", PartNum = "P100" });
        var completion = Completion(repository);
        completion.CompletedAt = completion.CompletedAt.AddMinutes(1);
        completion.MeasurementIds.Clear();
        repository.JobPhaseCompletions.Add(completion);
        var draft = Measurement(6m); repository.Measurements.Add(draft);
        var result = new JobReviewService(repository, new QaSummaryExportService(repository), new JobHistoryService(repository)).Build("P100", "J100");
        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var row = Assert.Single(result.Value!.Measurements);
        var history = Assert.Single(result.Value.History, item => item.EntryType == "PhaseComplete");
        Assert.Contains(row.Id, history.MeasurementIds!);
        Assert.NotEqual(draft.Id, row.Id);
        Assert.Empty(completion.MeasurementIds);
        Assert.Single(repository.JobPhaseCompletions);
    }

    [Fact]
    public void MeasurementDto_PreservesRecordedWatchReasonWhenHistoryIsEdited()
    {
        var repository = RepositoryWithMeasurement();
        var measurement = repository.Measurements.Single();
        measurement.WatchReason = "Near upper specification limit";
        var result = new JobReviewService(repository, new QaSummaryExportService(repository), new JobHistoryService(repository))
            .UpdateMeasurement(measurement.Id, new(5m, Reason: "Reviewed reading"));
        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsWatch);
        Assert.Equal(measurement.WatchReason, result.Value.WatchReason);
    }

    [Fact]
    public void UpdateMeasurement_ChangesValueAndPhase()
    {
        var repository = RepositoryWithMeasurement();
        var measurement = repository.Measurements.Single();
        var service = new JobReviewService(
            repository,
            new QaSummaryExportService(repository),
            new JobHistoryService(repository));

        var result = service.UpdateMeasurement(measurement.Id, new UpdateInspectionMeasurementRequest(5.25m, InspectionPhase: "Setup", Reason: "Corrected data entry error"));

        Assert.True(result.Succeeded);
        Assert.Equal(5.25m, measurement.Value);
        Assert.Equal("Setup", measurement.InspectionPhase);
        Assert.Equal(5.25m, result.Value!.Value);
        var audit = Assert.Single(repository.MeasurementEditAudits);
        Assert.Equal(5m, audit.OldValue);
        Assert.Equal(5.25m, audit.NewValue);
        Assert.Equal("In Process", audit.OldInspectionPhase);
        Assert.Equal("Setup", audit.NewInspectionPhase);
        Assert.Equal("Corrected data entry error", audit.Reason);
    }

    [Fact]
    public void UpdateMeasurement_RequiresReason()
    {
        var repository = RepositoryWithMeasurement();
        var measurement = repository.Measurements.Single();
        var service = new JobReviewService(
            repository,
            new QaSummaryExportService(repository),
            new JobHistoryService(repository));

        var result = service.UpdateMeasurement(measurement.Id, new UpdateInspectionMeasurementRequest(5.25m, InspectionPhase: "Setup"));

        Assert.False(result.Succeeded);
        Assert.Contains("A reason is required when editing inspection history.", result.Errors);
        Assert.Equal(5m, measurement.Value);
        Assert.Equal("In Process", measurement.InspectionPhase);
        Assert.Empty(repository.MeasurementEditAudits);
    }

    [Fact]
    public void UpdateMachineCounter_ChangesCounterAndAudits()
    {
        var repository = RepositoryWithMeasurement();
        var completion = Completion(repository);
        repository.JobPhaseCompletions.Add(completion);
        var service = new JobReviewService(
            repository,
            new QaSummaryExportService(repository),
            new JobHistoryService(repository));

        var result = service.UpdateMachineCounter(completion.Id, new UpdateMachineCounterRequest(23456, "Archon", "Corrected machine counter entry"));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(23456, completion.MachineCounter);
        var audit = Assert.Single(repository.MachineCounterEditAudits);
        Assert.Equal(completion.Id, audit.CompletionId);
        Assert.Equal(12345, audit.OldMachineCounter);
        Assert.Equal(23456, audit.NewMachineCounter);
        Assert.Equal("Archon", audit.EditedByUserId);
        Assert.Equal("Corrected machine counter entry", audit.Reason);
    }

    [Fact]
    public void UpdateMachineCounter_RequiresReason()
    {
        var repository = RepositoryWithMeasurement();
        var completion = Completion(repository);
        repository.JobPhaseCompletions.Add(completion);
        var service = new JobReviewService(
            repository,
            new QaSummaryExportService(repository),
            new JobHistoryService(repository));

        var result = service.UpdateMachineCounter(completion.Id, new UpdateMachineCounterRequest(23456));

        Assert.False(result.Succeeded);
        Assert.Contains("A reason is required when editing inspection history.", result.Errors);
        Assert.Equal(12345, completion.MachineCounter);
        Assert.Empty(repository.MachineCounterEditAudits);
    }

    [Fact]
    public void Build_FlagsOutOfSpecAndOutOfControlMeasurements()
    {
        var repository = RepositoryWithMeasurement();
        repository.Parts.Add(new Part { PartNum = "P100", Description = "Widget" });
        var process = new ManufacturingProcess { ProcessCode = "MOLD", Description = "Molding" };
        var part = repository.Parts.Single();
        repository.Processes.Add(process);
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        repository.Operations.Add(operation);
        var characteristic = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Diameter",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "mm",
        };
        repository.Characteristics.Add(characteristic);
        repository.SpecLimits.Add(new SpecLimit { CharacteristicId = characteristic.Id, Nominal = 5m, Lsl = 4.5m, Usl = 5.5m });
        repository.ControlLimits.Add(new ControlLimitSet { PartNum = "P100", ProcessCode = "MOLD", OperationSeq = 10, CharacteristicName = "Diameter", CenterLine = 5m, Lcl = 4.8m, Ucl = 5.2m });
        repository.Jobs.Add(new Job { JobNum = "J100", PartNum = "P100" });
        var completedMeasurement = repository.Measurements.Single();
        completedMeasurement.Value = 5.3m;
        repository.JobPhaseCompletions.Add(Completion(repository, completedMeasurement));
        var failedMeasurement = Measurement(5.8m);
        repository.Measurements.Add(failedMeasurement);
        var failedInspection = new FailedInspection
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            InspectionPhase = "In Process",
            FailureNumber = 1,
            FailedByUserId = "operator1",
            FailedAt = failedMeasurement.Timestamp
        };
        failedInspection.MeasurementIds.Add(failedMeasurement.Id);
        repository.FailedInspections.Add(failedInspection);
        var service = new JobReviewService(
            repository,
            new QaSummaryExportService(repository),
            new JobHistoryService(repository));

        var result = service.Build("P100", "J100");

        Assert.True(result.Succeeded);
        Assert.Contains(result.Value!.Measurements, measurement => measurement.Value == 5.3m && measurement.IsOutOfControl && !measurement.IsOutOfSpec);
        Assert.Contains(result.Value.Measurements, measurement => measurement.Value == 5.8m && measurement.IsOutOfSpec);
    }

    [Fact]
    public void Build_ExcludesMeasurementsNotAttachedToCompletedOrFailedInspection()
    {
        var repository = RepositoryWithMeasurement();
        repository.Parts.Add(new Part { PartNum = "P100", Description = "Widget" });
        var process = new ManufacturingProcess { ProcessCode = "MOLD", Description = "Molding" };
        repository.Processes.Add(process);
        var part = repository.Parts.Single();
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        repository.Operations.Add(operation);
        repository.Characteristics.Add(new Characteristic
        {
            OperationId = operation.Id,
            Name = "Diameter",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "mm"
        });
        repository.Jobs.Add(new Job { JobNum = "J100", PartNum = "P100" });
        var submittedMeasurement = repository.Measurements.Single();
        var rogueMeasurement = Measurement(6m);
        repository.Measurements.Add(rogueMeasurement);
        repository.JobPhaseCompletions.Add(Completion(repository, submittedMeasurement));
        var service = new JobReviewService(
            repository,
            new QaSummaryExportService(repository),
            new JobHistoryService(repository));

        var result = service.Build("P100", "J100");

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Contains(result.Value!.Measurements, measurement => measurement.Id == submittedMeasurement.Id);
        Assert.DoesNotContain(result.Value.Measurements, measurement => measurement.Id == rogueMeasurement.Id);
    }

    private static InMemorySpcRepository RepositoryWithMeasurement()
    {
        var repository = new InMemorySpcRepository();
        repository.Measurements.Add(new InspectionMeasurement
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            Value = 5m,
            Timestamp = DateTimeOffset.Parse("2026-01-01T08:00:00Z"),
            OperatorUserId = "operator1"
        });
        return repository;
    }

    private static InspectionMeasurement Measurement(decimal value)
    {
        return new InspectionMeasurement
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            Value = value,
            Timestamp = DateTimeOffset.Parse("2026-01-01T08:05:00Z"),
            OperatorUserId = "operator1"
        };
    }

    private static JobPhaseCompletion Completion(InMemorySpcRepository repository, InspectionMeasurement? measurement = null)
    {
        measurement ??= repository.Measurements.Single();
        var completion = new JobPhaseCompletion
        {
            JobNum = measurement.JobNum,
            PartNum = measurement.PartNum,
            ProcessCode = measurement.ProcessCode,
            OperationSeq = measurement.OperationSeq,
            ResourceId = measurement.ResourceId,
            InspectionPhase = measurement.InspectionPhase,
            CompletionNumber = 1,
            CompletedByUserId = measurement.OperatorUserId,
            CompletedAt = measurement.Timestamp,
            MachineCounter = 12345
        };
        completion.MeasurementIds.Add(measurement.Id);
        return completion;
    }
}

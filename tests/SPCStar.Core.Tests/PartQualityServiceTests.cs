using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class PartQualityServiceTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-18T12:00:00Z");
    [Fact]
    public void VariableFilter_LimitsVariableRowsAndTotalsToInspectionsThatCheckedIt()
    {
        var r = Repository();
        Complete(r, 1, Reading(r, "Width", 15, 0));
        Fail(r, 3, Reading(r, "Width", 21, 2));
        Complete(r, 5, Reading(r, "Visual", 1, 4));
        var result = Build(r, new("P", JobNum:"J", ResourceId:"M", OperatorShift:"5 Day", ProcessCode:"PRESS",
            OperationSeq:10, InspectionPhase:"In Process", From:At, To:At.AddMinutes(10), CharacteristicName:"Width", VariableKey:"press|10|width"));
        Assert.Equal(2, result.Inspections); Assert.Equal(1, result.Passed); Assert.Equal(1, result.Failed);
        Assert.Equal("Width", Assert.Single(result.Variables).Name); Assert.Single(result.Failures);
        Assert.Equal(0, Build(r, new("P", CharacteristicName:"Unknown")).Inspections);
    }

    private static InMemorySpcRepository Repository()
    {
        var repository = new InMemorySpcRepository();
        var part = new Part { PartNum = "P", Description = "Test Part" };
        var process = new ManufacturingProcess { ProcessCode = "PRESS", Description = "Press" };
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        repository.Parts.Add(part); repository.Processes.Add(process); repository.Operations.Add(operation);
        repository.Jobs.Add(new Job { JobNum = "J", PartNum = "P" });
        foreach (var (name, type, order) in new[] { ("Width", CharacteristicType.Variable, 1), ("Visual", CharacteristicType.Attribute, 2), ("Uninspected", CharacteristicType.Variable, 3) })
        {
            var characteristic = new Characteristic { Name = name, Type = type, OperationId = operation.Id, UnitOfMeasure = "mm" };
            repository.Characteristics.Add(characteristic);
            repository.InspectionPlans.Add(new InspectionPlan { CharacteristicId = characteristic.Id, InspectionPhase = "In Process", SampleSize = 2,
                DisplayOrder = order, Lsl = 10, Usl = 20, AlertRuleSet = "Custom" });
        }
        return repository;
    }
    private static InspectionMeasurement Reading(InMemorySpcRepository repository, string name, decimal value, int minute)
    {
        var reading = new InspectionMeasurement { JobNum = "J", PartNum = "P", ResourceId = "M", ProcessCode = "PRESS", OperationSeq = 10,
            CharacteristicName = name, InspectionPhase = "In Process", Value = value, Timestamp = At.AddMinutes(minute), OperatorUserId = "op", OperatorShift = "5 Day" };
        repository.Measurements.Add(reading); return reading;
    }
    private static JobPhaseCompletion Complete(InMemorySpcRepository repository, int minute, params InspectionMeasurement[] readings)
    {
        var complete = new JobPhaseCompletion { JobNum = "J", PartNum = "P", ResourceId = "M", ProcessCode = "PRESS", OperationSeq = 10,
            InspectionPhase = "In Process", CompletedByUserId = "op", OperatorShift = "5 Day", CompletedAt = At.AddMinutes(minute), CompletionNumber = minute };
        complete.MeasurementIds.AddRange(readings.Select(item => item.Id)); repository.JobPhaseCompletions.Add(complete); return complete;
    }
    private static FailedInspection Fail(InMemorySpcRepository repository, int minute, params InspectionMeasurement[] readings)
    {
        var failure = new FailedInspection { JobNum = "J", PartNum = "P", ResourceId = "M", ProcessCode = "PRESS", OperationSeq = 10,
            InspectionPhase = "In Process", FailedByUserId = "tech", OperatorShift = "5 Day", FailedAt = At.AddMinutes(minute), FailureNumber = minute,
            CauseCategory = "Tooling", CauseText = "Worn tool", SolutionText = "Replaced insert", Reason = "Stopped inspection", FailureDetail = "Width above upper limit" };
        failure.MeasurementIds.AddRange(readings.Select(item => item.Id)); repository.FailedInspections.Add(failure); return failure;
    }
    private static PartQualityResult Build(InMemorySpcRepository repository, PartQualityRequest? request = null)
    {
        var result = new PartQualityService(repository).Build(request ?? new("P", VariableKey:"press|10|width"));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors)); return result.Value!;
    }

    [Fact]
    public void PartAndVariables_CountInspectionsNotSamples_AndRetainExactNotes()
    {
        var r = Repository();
        Complete(r, 1, Reading(r, "Width", 10, 1), Reading(r, "Width", 20, 1), Reading(r, "Visual", 1, 1));
        Complete(r, 2, Reading(r, "Width", 16, 2));
        var failure = Fail(r, 3, Reading(r, "Width", 21, 3), Reading(r, "Width", 22, 3), Reading(r, "Visual", 0, 3));
        failure.MeasurementIds.Add(failure.MeasurementIds[0]);
        var result = Build(r);
        Assert.Equal(3, result.Inspections); Assert.Equal(2, result.Passed); Assert.Equal(1, result.Failed);
        Assert.Equal(200m / 3, result.PassRate);
        var width = result.Variables.Single(item => item.Name == "Width");
        Assert.Equal(3, width.Checked); Assert.Equal(1, width.Failed); Assert.Equal(100m / 3, width.FailureRate);
        var visual = result.Variables.Single(item => item.Name == "Visual");
        Assert.Equal(2, visual.Checked); Assert.Equal(1, visual.Failed); Assert.Equal(50m, visual.FailureRate);
        Assert.Null(result.Variables.Single(item => item.Name == "Uninspected").FailureRate);
        Assert.Equal(new[] { "Width", "Visual", "Uninspected" }, result.Variables.Select(item => item.Name));
        var detail = Assert.Single(result.Failures);
        Assert.Equal(failure.Id, detail.Id); Assert.Equal("Worn tool", detail.Cause); Assert.Equal("Replaced insert", detail.Action);
        Assert.Equal("Stopped inspection", detail.Reason); Assert.Equal(3, detail.FailedReadings.Count);
        Assert.Equal(2, detail.VariableKeys.Count); Assert.Equal(0, result.MissingReadingLinks);
    }

    [Fact]
    public void DraftsWatchAndUserError_DoNotCountAsFailedInspections()
    {
        var r = Repository(); var reading = Reading(r, "Width", 15, 1); reading.WatchReason = "Near limit";
        Complete(r, 1, reading);
        Reading(r, "Width", 25, 2);
        var mistake = Fail(r, 3, Reading(r, "Width", 99, 3)); mistake.CauseCategory = "User Error";
        r.Alerts.Add(new ProcessAlert { JobNum = "J", PartNum = "P", ResourceId = "M", CharacteristicName = "Width",
            OperatorUserId = "op", RuleTriggered = RuleTriggered.CustomRuleTriggered, Status = AlertStatus.Warning });
        var result = Build(r);
        Assert.Equal(1, result.Inspections); Assert.Equal(0, result.Failed); Assert.Equal(100m, result.PassRate);
        Assert.Equal(1, result.Variables.Single(item => item.Name == "Width").Checked); Assert.Empty(result.Failures);
    }

    [Fact]
    public void LegacyMissingLinks_AreFlaggedWithoutReconstruction_AndManualFailuresRemainUnassigned()
    {
        var r = Repository(); Reading(r, "Width", 25, 1); Complete(r, 2); Fail(r, 3);
        var result = Build(r);
        Assert.Equal(2, result.Inspections); Assert.Equal(2, result.MissingReadingLinks); Assert.Equal(1, result.UnassignedFailures);
        Assert.All(result.Variables, item => Assert.Equal(0, item.Checked));
        Assert.Empty(r.JobPhaseCompletions.Single().MeasurementIds); Assert.Empty(r.FailedInspections.Single().MeasurementIds);
    }

    [Fact]
    public void LinkedRecordedViolation_AssociatesFailureWithoutGuessingNearbyReadings()
    {
        var r = Repository(); var failure = Fail(r, 1);
        var alert = new ProcessAlert { JobNum = "J", PartNum = "P", ResourceId = "M", CharacteristicName = "Width",
            OperatorUserId = "op", RuleTriggered = RuleTriggered.SpecLimitViolation, Detail = "Entered 21 above USL 20" };
        r.Alerts.Add(alert); failure.AlertId = alert.Id;
        var result = Build(r); var width = result.Variables.Single(item => item.Name == "Width");
        Assert.Equal(1, width.Checked); Assert.Equal(1, width.Failed); Assert.Equal(100m, width.FailureRate);
        Assert.Equal(0, result.UnassignedFailures); Assert.Contains("Entered 21", result.Failures.Single().Detail);
    }

    [Theory]
    [InlineData("job")][InlineData("machine")][InlineData("shift")][InlineData("operation")][InlineData("phase")][InlineData("date")]
    public void FiltersApplyToBothTotalsAndVariableCounts(string filter)
    {
        var r = Repository(); Complete(r, 1, Reading(r, "Width", 15, 1)); Fail(r, 2, Reading(r, "Width", 21, 2));
        var request = filter switch
        {
            "job" => new PartQualityRequest("P", JobNum:"OTHER"),
            "machine" => new("P", ResourceId:"OTHER"), "shift" => new("P", OperatorShift:"OTHER"),
            "operation" => new("P", OperationSeq:20), "phase" => new("P", InspectionPhase:"Setup"),
            _ => new("P", From:At.AddDays(1))
        };
        var result = Build(r, request); Assert.Equal(0, result.Inspections); Assert.Null(result.PassRate);
        Assert.All(result.Variables, row => Assert.Equal(0, row.Checked));
    }

    [Fact]
    public void InvalidPartJobAndDateRange_AreRejected()
    {
        var r = Repository(); var service = new PartQualityService(r);
        Assert.False(service.Build(new("")).Succeeded);
        Assert.False(service.Build(new("P", From:At.AddDays(1), To:At)).Succeeded);
        r.Jobs.Add(new Job { JobNum="OTHER", PartNum="OTHER" }); Assert.False(service.Build(new("P", JobNum:"OTHER")).Succeeded);
    }

    [Fact]
    public void VariablesFollowPhaseChecklistOrder_NotAlphabetOrFailureCounts()
    {
        var r = Repository();
        var width = r.Characteristics.Single(item => item.Name == "Width");
        var visual = r.Characteristics.Single(item => item.Name == "Visual");
        r.InspectionPlans.Add(new InspectionPlan { CharacteristicId = visual.Id, InspectionPhase = "Setup", SampleSize = 1, DisplayOrder = 1, AlertRuleSet = "Custom" });
        r.InspectionPlans.Add(new InspectionPlan { CharacteristicId = width.Id, InspectionPhase = "Setup", SampleSize = 1, DisplayOrder = 2, AlertRuleSet = "Custom" });
        Fail(r, 1, Reading(r, "Width", 21, 1));
        Assert.Equal(new[] { "Visual", "Width", "Uninspected" }, Build(r).Variables.Select(item => item.Name));
        Assert.Equal(new[] { "Width", "Visual", "Uninspected" }, Build(r, new("P", InspectionPhase:"In Process")).Variables.Select(item => item.Name));
        Assert.Equal(new[] { "Visual", "Width" }, Build(r, new("P", InspectionPhase:"Setup")).Variables.Select(item => item.Name));
    }

    [Fact]
    public void SummaryDoesNotReturnAllFailureNotes_AndVariableFailuresArePagedWithoutChangingTotals()
    {
        var r = Repository();
        for (var i = 1; i <= 43; i++) Fail(r, i, Reading(r, "Width", 21, i));
        Fail(r, 44, Reading(r, "Visual", 0, 44));
        var service = new PartQualityService(r);
        var summary = service.Build(new("P")).Value!;
        Assert.Equal(44, summary.Failed); Assert.Empty(summary.Failures); Assert.Equal(0, summary.MatchingFailures);
        var ids = new HashSet<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = service.Build(new("P", VariableKey:"press|10|width", Page:page)).Value!;
            Assert.Equal(44, result.Failed); Assert.Equal(43, result.MatchingFailures);
            Assert.Equal(page, result.FailurePage); Assert.Equal(20, result.FailurePageSize);
            Assert.Equal(page == 3 ? 3 : 20, result.Failures.Count);
            Assert.All(result.Failures, failure => { Assert.Contains("press|10|width", failure.VariableKeys); Assert.True(ids.Add(failure.Id)); });
        }
        Assert.Equal(43, ids.Count);
        var bounded = service.Build(new("P", VariableKey:"press|10|width", Page:int.MaxValue, PageSize:int.MaxValue)).Value!;
        Assert.Equal(50, bounded.FailurePageSize); Assert.Equal(1, bounded.FailurePage); Assert.Equal(43, bounded.Failures.Count);
        var empty = service.Build(new("P", VariableKey:"unknown")).Value!;
        Assert.Empty(empty.Failures); Assert.Equal(0, empty.MatchingFailures);
    }
}

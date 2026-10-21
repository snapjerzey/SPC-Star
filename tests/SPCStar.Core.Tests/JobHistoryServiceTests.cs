using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class JobHistoryServiceTests
{
    [Fact]
    public void FailedInspection_IncludesItsLinkedLockViolationWithoutBorrowingOtherLocks()
    {
        var repository = new InMemorySpcRepository();
        var alert = new ProcessAlert
        {
            JobNum = "J100", PartNum = "P100", ResourceId = "PRESS1",
            CharacteristicName = "Overall Width - Upper", OperatorUserId = "operator1",
            RuleTriggered = RuleTriggered.SpecLimitViolation,
            Detail = "Entered value 0.210 is above the upper specification limit 0.205.",
            Status = AlertStatus.Overridden
        };
        repository.Alerts.Add(alert);
        var failure = new FailedInspection
        {
            JobNum = "J100", PartNum = "P100", ResourceId = "PRESS1", ProcessCode = "MOLD",
            OperationSeq = 10, InspectionPhase = "In Process", FailedByUserId = "operator1",
            AlertId = alert.Id, CauseCategory = "Tooling", CauseText = "Worn tool",
            SolutionText = "Replaced tool", FailureDetail = "Old incomplete detail"
        };
        repository.FailedInspections.Add(failure);
        var row = Assert.Single(new JobHistoryService(repository).GetForJob("J100"), item => item.EntryType == "InspectionFailed");
        Assert.Equal("Overall Width - Upper", row.CharacteristicName);
        Assert.Equal(RuleTriggered.SpecLimitViolation, row.RuleTriggered);
        Assert.Equal($"Overall Width - Upper: {alert.Detail}", row.Detail);
        Assert.Equal("Tooling", row.CauseCategory);
        Assert.Equal("Worn tool", row.CauseText);
        Assert.Equal("Replaced tool", row.SolutionText);
        Assert.Single(repository.Alerts);
        failure.AlertId = null;
        row = Assert.Single(new JobHistoryService(repository).GetForJob("J100"), item => item.EntryType == "InspectionFailed");
        Assert.Equal("Old incomplete detail", row.Detail);
        Assert.Null(row.CharacteristicName);
    }

    [Fact]
    public void GetForJob_ReturnsNotesAndLockHistory()
    {
        var repository = new InMemorySpcRepository();
        var alert = new ProcessAlert
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            OperatorUserId = "operator1",
            RuleTriggered = RuleTriggered.SpecLimitViolation,
            LockedAt = DateTimeOffset.Parse("2026-05-12T08:00:00Z"),
            Status = AlertStatus.Overridden
        };
        repository.Alerts.Add(alert);
        repository.AlertOverrides.Add(new AlertOverride
        {
            AlertId = alert.Id,
            OperatorUserId = "operator1",
            OverrideUserId = "linetech1",
            OverrideRole = RoleNames.LineTech,
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            RuleTriggered = RuleTriggered.SpecLimitViolation,
            CauseCategory = "Tooling",
            CauseText = "Tool wear",
            SolutionText = "Changed tool insert",
            LockedAt = alert.LockedAt,
            UnlockedAt = DateTimeOffset.Parse("2026-05-12T08:15:00Z"),
            SubmittedAt = DateTimeOffset.Parse("2026-05-12T08:16:00Z")
        });
        repository.JobNotes.Add(new JobNote
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            OperatorUserId = "operator1",
            NoteText = "Watch cavity side for flash.",
            Timestamp = DateTimeOffset.Parse("2026-05-12T08:20:00Z")
        });
        repository.MaterialChanges.Add(new MaterialChangeLog
        {
            JobNum = "J100",
            PartNum = "P100",
            MaterialPartNum = "RESIN-A",
            OldLotNum = string.Empty,
            NewLotNum = "LOT-2",
            QuantityLoaded = 250m,
            ResourceId = "PRESS1",
            OperatorUserId = "operator1",
            Timestamp = DateTimeOffset.Parse("2026-05-12T08:10:00Z"),
            Reason = "Lot Change",
            SubmittedAt = DateTimeOffset.Parse("2026-05-12T08:11:00Z")
        });
        repository.JobTags.Add(new JobTag
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            TagName = "Bimetal Lot",
            TagValue = "LOT-BI-1",
            OperatorUserId = "operator1",
            UpdatedAt = DateTimeOffset.Parse("2026-05-12T08:18:00Z")
        });

        var history = new JobHistoryService(repository).GetForJob("J100");

        Assert.Equal(4, history.Count);
        Assert.Equal("Note", history[0].EntryType);
        Assert.Equal("JobData", history[1].EntryType);
        Assert.Equal("Lock", history[2].EntryType);
        Assert.Equal("Material", history[3].EntryType);
        Assert.Equal("Bimetal Lot", history[1].TagName);
        Assert.Equal("LOT-BI-1", history[1].TagValue);
        Assert.Equal(DateTimeOffset.Parse("2026-05-12T08:15:00Z"), history[2].Timestamp);
        Assert.Equal("linetech1", history[2].OverrideUserId);
        Assert.Equal("Tooling", history[2].CauseCategory);
        Assert.Equal("Changed tool insert", history[2].SolutionText);
        Assert.Equal("LOT-2", history[3].NewLotNum);
    }

    [Fact]
    public void GetForJob_GroupsJobDataUnderClosestCompletedInspection()
    {
        var repository = new InMemorySpcRepository();
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            ProcessCode = "General Production",
            OperationSeq = 10,
            InspectionPhase = "In Process",
            CompletionNumber = 1,
            CompletedByUserId = "operator1",
            CompletedAt = DateTimeOffset.Parse("2026-05-12T08:10:00Z"),
            MachineCounter = 12345
        });
        repository.JobTags.Add(new JobTag
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            TagName = "Box #",
            TagValue = "45",
            OperatorUserId = "operator1",
            UpdatedAt = DateTimeOffset.Parse("2026-05-12T08:09:30Z")
        });

        var history = new JobHistoryService(repository).GetForJob("J100");

        var completion = Assert.Single(history);
        Assert.Equal("PhaseComplete", completion.EntryType);
        Assert.Equal(12345, completion.MachineCounter);
        var jobData = Assert.Single(completion.JobDataEntries!);
        Assert.Equal("Box #", jobData.TagName);
        Assert.Equal("45", jobData.TagValue);
    }

    [Fact]
    public void GetForJob_ReturnsMachineCounterEditHistory()
    {
        var repository = new InMemorySpcRepository();
        var completion = new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            ProcessCode = "General Production",
            OperationSeq = 10,
            InspectionPhase = "In Process",
            CompletionNumber = 1,
            CompletedByUserId = "operator1",
            CompletedAt = DateTimeOffset.Parse("2026-05-12T08:10:00Z"),
            MachineCounter = 23456
        };
        repository.JobPhaseCompletions.Add(completion);
        repository.MachineCounterEditAudits.Add(new MachineCounterEditAudit
        {
            CompletionId = completion.Id,
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            ProcessCode = "General Production",
            OperationSeq = 10,
            InspectionPhase = "In Process",
            OldMachineCounter = 12345,
            NewMachineCounter = 23456,
            EditedByUserId = "Archon",
            EditedAt = DateTimeOffset.Parse("2026-05-12T08:15:00Z"),
            Reason = "Corrected machine counter entry"
        });

        var history = new JobHistoryService(repository).GetForJob("J100");

        var edit = Assert.Single(history, entry => entry.EntryType == "MachineCounterEdit");
        Assert.Equal(12345, edit.OldMachineCounter);
        Assert.Equal(23456, edit.NewMachineCounter);
        Assert.Equal("Corrected machine counter entry", edit.Reason);
    }

    [Fact]
    public void GetForJob_CarriesPersistentJobDataButNotBoxNumberForwardToLaterCompletedInspections()
    {
        var repository = new InMemorySpcRepository();
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            ProcessCode = "General Production",
            OperationSeq = 10,
            InspectionPhase = "In Process",
            CompletionNumber = 1,
            CompletedByUserId = "operator1",
            CompletedAt = DateTimeOffset.Parse("2026-05-12T08:10:00Z")
        });
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            ProcessCode = "General Production",
            OperationSeq = 10,
            InspectionPhase = "In Process",
            CompletionNumber = 2,
            CompletedByUserId = "operator1",
            CompletedAt = DateTimeOffset.Parse("2026-05-12T11:00:00Z")
        });
        repository.JobTags.Add(new JobTag
        {
            JobNum = "J100",
            PartNum = "",
            ResourceId = "",
            TagName = "Vendor Coil #",
            TagValue = "COIL-1",
            OperatorUserId = "operator1",
            UpdatedAt = DateTimeOffset.Parse("2026-05-12T08:09:30Z")
        });
        repository.JobTags.Add(new JobTag
        {
            JobNum = "J100",
            PartNum = "",
            ResourceId = "",
            TagName = "Bimetal Lot #",
            TagValue = "LOT-BI-1",
            OperatorUserId = "operator1",
            UpdatedAt = DateTimeOffset.Parse("2026-05-12T08:09:30Z")
        });
        repository.JobTags.Add(new JobTag
        {
            JobNum = "J100",
            PartNum = "",
            ResourceId = "",
            TagName = "Box #",
            TagValue = "45",
            OperatorUserId = "operator1",
            UpdatedAt = DateTimeOffset.Parse("2026-05-12T08:09:30Z")
        });

        var history = new JobHistoryService(repository).GetForJob("J100");

        var latestCompletion = history.First(entry => entry.EntryType == "PhaseComplete" && entry.CompletionNumber == 2);
        var jobData = Assert.Single(latestCompletion.JobDataEntries!);
        Assert.Equal("Vendor Coil #", jobData.TagName);
        Assert.Equal("COIL-1", jobData.TagValue);
    }

    [Fact]
    public void GetForJob_PreservesRecordedMeasurementLinksInsteadOfReplacingThemWithChecklistReconstruction()
    {
        var repository = new InMemorySpcRepository();
        var part = new Part { PartNum = "P100", Description = "Test part" };
        var process = new ManufacturingProcess { ProcessCode = "General Production", Description = "General Production" };
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        repository.Parts.Add(part);
        repository.Processes.Add(process);
        repository.Operations.Add(operation);

        AddCharacteristic(repository, operation.Id, "First", 1);
        AddCharacteristic(repository, operation.Id, "Second", 2);
        AddCharacteristic(repository, operation.Id, "Third", 3);
        var orphanFirst = AddMeasurement(repository, "First", "2026-05-12T08:00:00Z");
        var validFirst = AddMeasurement(repository, "First", "2026-05-12T08:01:00Z");
        var validSecond = AddMeasurement(repository, "Second", "2026-05-12T08:02:00Z");
        var validThird = AddMeasurement(repository, "Third", "2026-05-12T08:03:00Z");
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            ProcessCode = "General Production",
            OperationSeq = 10,
            InspectionPhase = "In Process",
            CompletionNumber = 1,
            CompletedByUserId = "operator1",
            CompletedAt = DateTimeOffset.Parse("2026-05-12T08:03:00Z")
        });
        repository.JobPhaseCompletions.Single().MeasurementIds.AddRange([orphanFirst.Id, validSecond.Id, validThird.Id]);

        var history = new JobHistoryService(repository).GetForJob("J100");

        var completion = Assert.Single(history, entry => entry.EntryType == "PhaseComplete");
        var measurementIds = Assert.IsAssignableFrom<IReadOnlyList<Guid>>(completion.MeasurementIds);
        Assert.Equal([orphanFirst.Id, validSecond.Id, validThird.Id], measurementIds);
        Assert.DoesNotContain(validFirst.Id, measurementIds);

    }

    [Fact]
    public void GetForJob_DoesNotCreateCompletionRowsFromMeasurementsOnly()
    {
        var repository = new InMemorySpcRepository();
        var part = new Part { PartNum = "P100", Description = "Test part" };
        var process = new ManufacturingProcess { ProcessCode = "General Production", Description = "General Production" };
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        repository.Parts.Add(part);
        repository.Processes.Add(process);
        repository.Operations.Add(operation);

        AddCharacteristic(repository, operation.Id, "First", 1);
        AddCharacteristic(repository, operation.Id, "Second", 2);
        AddMeasurement(repository, "First", "2026-05-12T08:01:00Z");
        AddMeasurement(repository, "Second", "2026-05-12T08:02:00Z");
        AddMeasurement(repository, "First", "2026-05-13T08:01:00Z");
        AddMeasurement(repository, "Second", "2026-05-13T08:02:00Z");

        var history = new JobHistoryService(repository).GetForJob("J100");

        Assert.DoesNotContain(history, entry => entry.EntryType == "PhaseComplete");
    }

    [Fact]
    public void GetForJob_OnlyShowsPersistedCompletionRows()
    {
        var repository = new InMemorySpcRepository();
        var part = new Part { PartNum = "P100", Description = "Test part" };
        var process = new ManufacturingProcess { ProcessCode = "General Production", Description = "General Production" };
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        repository.Parts.Add(part);
        repository.Processes.Add(process);
        repository.Operations.Add(operation);

        AddCharacteristic(repository, operation.Id, "First", 1);
        AddCharacteristic(repository, operation.Id, "Second", 2);
        AddMeasurement(repository, "First", "2026-05-12T08:01:00Z");
        AddMeasurement(repository, "Second", "2026-05-12T08:02:00Z");
        var secondDayFirst = AddMeasurement(repository, "First", "2026-05-13T08:01:00Z");
        var secondDaySecond = AddMeasurement(repository, "Second", "2026-05-13T08:02:00Z");
        var completion = new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            ProcessCode = "General Production",
            OperationSeq = 10,
            InspectionPhase = "In Process",
            CompletionNumber = 1,
            CompletedByUserId = "operator1",
            CompletedAt = secondDaySecond.Timestamp,
            MachineCounter = 4500
        };
        completion.MeasurementIds.AddRange([secondDayFirst.Id, secondDaySecond.Id]);
        repository.JobPhaseCompletions.Add(completion);

        var history = new JobHistoryService(repository).GetForJob("J100");

        var completions = history
            .Where(entry => entry.EntryType == "PhaseComplete")
            .OrderBy(entry => entry.Timestamp)
            .ToArray();
        var persisted = Assert.Single(completions);
        Assert.Equal(1, persisted.CompletionNumber);
        Assert.Equal(4500, persisted.MachineCounter);
        Assert.Equal([secondDayFirst.Id, secondDaySecond.Id], persisted.MeasurementIds);
    }

    [Fact]
    public void GetForJob_DisplaysMonotonicPersistedCompletionNumbersWithinOperationAndPhase()
    {
        var repository = new InMemorySpcRepository();
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            ProcessCode = "Needlemaker",
            OperationSeq = 10,
            InspectionPhase = "In Process",
            CompletionNumber = 1,
            CompletedByUserId = "operator1",
            CompletedAt = DateTimeOffset.Parse("2026-05-12T08:10:00Z")
        });
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            ProcessCode = "Needlemaker",
            OperationSeq = 10,
            InspectionPhase = "In Process",
            CompletionNumber = 1,
            CompletedByUserId = "operator1",
            CompletedAt = DateTimeOffset.Parse("2026-05-13T08:10:00Z")
        });
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            ProcessCode = "Test Polish",
            OperationSeq = 20,
            InspectionPhase = "In Process",
            CompletionNumber = 1,
            CompletedByUserId = "operator1",
            CompletedAt = DateTimeOffset.Parse("2026-05-13T08:20:00Z")
        });

        var history = new JobHistoryService(repository).GetForJob("J100");

        var needlemaker = history
            .Where(entry => entry.EntryType == "PhaseComplete" && entry.ProcessCode == "Needlemaker")
            .OrderBy(entry => entry.Timestamp)
            .ToArray();
        var testPolish = Assert.Single(history, entry => entry.EntryType == "PhaseComplete" && entry.ProcessCode == "Test Polish");
        Assert.Equal(1, needlemaker[0].CompletionNumber);
        Assert.Equal(2, needlemaker[1].CompletionNumber);
        Assert.Equal(1, testPolish.CompletionNumber);
    }

    private static Characteristic AddCharacteristic(InMemorySpcRepository repository, Guid operationId, string name, int displayOrder)
    {
        var characteristic = new Characteristic
        {
            OperationId = operationId,
            Name = name,
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "mm"
        };
        repository.Characteristics.Add(characteristic);
        repository.InspectionPlans.Add(new InspectionPlan
        {
            CharacteristicId = characteristic.Id,
            InspectionPhase = "In Process",
            SampleSize = 1,
            DisplayOrder = displayOrder,
            AlertRuleSet = "SpecLimitOnly",
            Frequency = new InspectionFrequency
            {
                Type = FrequencyType.Quantity,
                Value = 1,
                Unit = FrequencyUnit.Pieces
            }
        });

        return characteristic;
    }

    private static InspectionMeasurement AddMeasurement(InMemorySpcRepository repository, string characteristicName, string timestamp)
    {
        var measurement = new InspectionMeasurement
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "General Production",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            CharacteristicName = characteristicName,
            InspectionPhase = "In Process",
            Value = 1m,
            Timestamp = DateTimeOffset.Parse(timestamp),
            SubmittedAt = DateTimeOffset.Parse(timestamp),
            OperatorUserId = "operator1"
        };
        repository.Measurements.Add(measurement);
        return measurement;
    }
}

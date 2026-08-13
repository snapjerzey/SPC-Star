using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class JobHistoryServiceTests
{
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
            CompletedAt = DateTimeOffset.Parse("2026-05-12T08:10:00Z")
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
        var jobData = Assert.Single(completion.JobDataEntries!);
        Assert.Equal("Box #", jobData.TagName);
        Assert.Equal("45", jobData.TagValue);
    }
}

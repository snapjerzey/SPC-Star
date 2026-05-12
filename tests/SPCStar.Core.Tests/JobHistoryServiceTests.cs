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

        var history = new JobHistoryService(repository).GetForJob("J100");

        Assert.Equal(2, history.Count);
        Assert.Equal("Note", history[0].EntryType);
        Assert.Equal("Lock", history[1].EntryType);
        Assert.Equal("Tooling", history[1].CauseCategory);
        Assert.Equal("Changed tool insert", history[1].SolutionText);
    }
}

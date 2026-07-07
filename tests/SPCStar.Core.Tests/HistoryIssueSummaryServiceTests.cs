using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class HistoryIssueSummaryServiceTests
{
    [Fact]
    public void TopIssues_GroupsRepeatAlertsByPartAndCharacteristic()
    {
        var repository = new InMemorySpcRepository();
        var firstAlert = Alert("J100", "P100", "PRESS1", "Diameter", RuleTriggered.SpecLimitViolation, "2026-01-01T08:00:00Z", "1st Half Days");
        var secondAlert = Alert("J101", "P100", "PRESS2", "Diameter", RuleTriggered.OnePointBeyondControlLimit, "2026-01-02T08:00:00Z", "2nd Half Nights");
        var lengthAlert = Alert("J100", "P100", "PRESS1", "Length", RuleTriggered.NelsonTrend, "2026-01-03T08:00:00Z", "1st Half Days");
        repository.Alerts.AddRange([firstAlert, secondAlert, lengthAlert]);
        repository.AlertOverrides.AddRange([
            Override(firstAlert, "Tooling", "Changed punch"),
            Override(secondAlert, "Tooling", "Adjusted die"),
            Override(lengthAlert, "Machine", "Checked feed")
        ]);

        var rows = new HistoryIssueSummaryService(repository).TopIssues(new HistoryIssueSummaryRequest("P100", null, null, null, null, null, null));

        Assert.Equal(2, rows.Count);
        Assert.Equal("Diameter", rows[0].CharacteristicName);
        Assert.Equal("Tooling", rows[0].CauseCategory);
        Assert.Equal(2, rows[0].EventCount);
        Assert.Equal("One point beyond control limit", rows[0].SignalSummary);
        Assert.Collection(rows[0].SignalBreakdown,
            item =>
            {
                Assert.Equal("One point beyond control limit", item.Label);
                Assert.Equal(1, item.Count);
            },
            item =>
            {
                Assert.Equal("Spec limit violation", item.Label);
                Assert.Equal(1, item.Count);
            });
        Assert.Equal(2, rows[0].DistinctJobCount);
        Assert.Equal(2, rows[0].DistinctMachineCount);
        Assert.Equal("J101", rows[0].LatestJobNum);
        Assert.Equal("2nd Half Nights", rows[0].LatestOperatorShift);
        Assert.Equal("Adjusted die", rows[0].LatestSolution);
        Assert.Equal("Length", rows[1].CharacteristicName);
    }

    [Fact]
    public void TopIssues_DoesNotSplitRepeatIssueByCauseOrSolution()
    {
        var repository = new InMemorySpcRepository();
        var firstAlert = Alert("J100", "P100", "PRESS1", "Caliper Width", RuleTriggered.SpecLimitViolation, "2026-01-01T08:00:00Z", "1st Half Days");
        var secondAlert = Alert("J101", "P100", "PRESS1", "Caliper Width", RuleTriggered.OnePointBeyondControlLimit, "2026-01-02T08:00:00Z", "1st Half Days");
        var thirdAlert = Alert("J102", "P100", "PRESS2", "Caliper Width", RuleTriggered.SpecLimitViolation, "2026-01-03T08:00:00Z", "1st Half Days");
        repository.Alerts.AddRange([firstAlert, secondAlert, thirdAlert]);
        repository.AlertOverrides.AddRange([
            Override(firstAlert, "Tooling", "Adjusted fixture"),
            Override(secondAlert, "Machine", "Reset offset"),
            Override(thirdAlert, "Tooling", "Changed insert")
        ]);

        var rows = new HistoryIssueSummaryService(repository).TopIssues(new HistoryIssueSummaryRequest("P100", null, null, null, "Caliper Width", null, null));

        var row = Assert.Single(rows);
        Assert.Equal("Caliper Width", row.CharacteristicName);
        Assert.Equal(3, row.EventCount);
        Assert.Equal("Spec limit violation", row.SignalSummary);
        Assert.Equal("Tooling", row.CauseCategory);
        Assert.Collection(row.SignalBreakdown,
            item =>
            {
                Assert.Equal("Spec limit violation", item.Label);
                Assert.Equal(2, item.Count);
            },
            item =>
            {
                Assert.Equal("One point beyond control limit", item.Label);
                Assert.Equal(1, item.Count);
            });
        Assert.Collection(row.CauseBreakdown,
            item =>
            {
                Assert.Equal("Tooling", item.Label);
                Assert.Equal(2, item.Count);
            },
            item =>
            {
                Assert.Equal("Machine", item.Label);
                Assert.Equal(1, item.Count);
            });
        Assert.Collection(row.SolutionBreakdown,
            item =>
            {
                Assert.Equal("Adjusted fixture", item.Label);
                Assert.Equal(1, item.Count);
            },
            item =>
            {
                Assert.Equal("Changed insert", item.Label);
                Assert.Equal(1, item.Count);
            },
            item =>
            {
                Assert.Equal("Reset offset", item.Label);
                Assert.Equal(1, item.Count);
            });
        Assert.Equal("Changed insert", row.LatestSolution);
    }

    [Fact]
    public void TopIssues_FiltersByShiftAndTimeframe()
    {
        var repository = new InMemorySpcRepository();
        repository.Alerts.AddRange([
            Alert("J100", "P100", "PRESS1", "Diameter", RuleTriggered.SpecLimitViolation, "2026-01-01T08:00:00Z", "1st Half Days"),
            Alert("J101", "P100", "PRESS1", "Diameter", RuleTriggered.SpecLimitViolation, "2026-01-02T08:00:00Z", "1st Half Nights"),
            Alert("J102", "P100", "PRESS1", "Diameter", RuleTriggered.SpecLimitViolation, "2026-01-03T08:00:00Z", "1st Half Days")
        ]);

        var rows = new HistoryIssueSummaryService(repository).TopIssues(new HistoryIssueSummaryRequest(
            "P100",
            null,
            null,
            "1st Half Days",
            null,
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-04T00:00:00Z")));

        var row = Assert.Single(rows);
        Assert.Equal(1, row.EventCount);
        Assert.Equal("J102", row.LatestJobNum);
        Assert.Equal("1st Half Days", row.LatestOperatorShift);
    }

    private static ProcessAlert Alert(string jobNum, string partNum, string resourceId, string characteristicName, RuleTriggered rule, string lockedAt, string operatorShift)
    {
        return new ProcessAlert
        {
            JobNum = jobNum,
            PartNum = partNum,
            ResourceId = resourceId,
            CharacteristicName = characteristicName,
            OperatorUserId = "operator1",
            OperatorShift = operatorShift,
            RuleTriggered = rule,
            LockedAt = DateTimeOffset.Parse(lockedAt),
            Status = AlertStatus.Overridden,
            Detail = "Outside configured limit."
        };
    }

    private static AlertOverride Override(ProcessAlert alert, string causeCategory, string solution)
    {
        return new AlertOverride
        {
            AlertId = alert.Id,
            OperatorUserId = alert.OperatorUserId,
            OverrideUserId = "linetech1",
            OverrideRole = "LineTech",
            JobNum = alert.JobNum,
            PartNum = alert.PartNum,
            ResourceId = alert.ResourceId,
            CharacteristicName = alert.CharacteristicName,
            RuleTriggered = alert.RuleTriggered,
            CauseCategory = causeCategory,
            CauseText = causeCategory,
            SolutionText = solution,
            LockedAt = alert.LockedAt,
            UnlockedAt = alert.LockedAt.AddMinutes(5),
            SubmittedAt = alert.LockedAt.AddMinutes(5)
        };
    }
}

using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class HistoryIssueSummaryServiceTests
{
    [Fact]
    public void TopIssues_GroupsRepeatAlertsByPartCharacteristicRuleAndCause()
    {
        var repository = new InMemorySpcRepository();
        var firstAlert = Alert("J100", "P100", "PRESS1", "Diameter", RuleTriggered.SpecLimitViolation, "2026-01-01T08:00:00Z");
        var secondAlert = Alert("J101", "P100", "PRESS2", "Diameter", RuleTriggered.SpecLimitViolation, "2026-01-02T08:00:00Z");
        var lengthAlert = Alert("J100", "P100", "PRESS1", "Length", RuleTriggered.NelsonTrend, "2026-01-03T08:00:00Z");
        repository.Alerts.AddRange([firstAlert, secondAlert, lengthAlert]);
        repository.AlertOverrides.AddRange([
            Override(firstAlert, "Tooling", "Changed punch"),
            Override(secondAlert, "Tooling", "Adjusted die"),
            Override(lengthAlert, "Machine", "Checked feed")
        ]);

        var rows = new HistoryIssueSummaryService(repository).TopIssues(new HistoryIssueSummaryRequest("P100", null, null, null, null, null));

        Assert.Equal(2, rows.Count);
        Assert.Equal("Diameter", rows[0].CharacteristicName);
        Assert.Equal("Tooling", rows[0].CauseCategory);
        Assert.Equal(2, rows[0].EventCount);
        Assert.Equal(2, rows[0].DistinctJobCount);
        Assert.Equal(2, rows[0].DistinctMachineCount);
        Assert.Equal("J101", rows[0].LatestJobNum);
        Assert.Equal("Adjusted die", rows[0].LatestSolution);
        Assert.Equal("Length", rows[1].CharacteristicName);
    }

    private static ProcessAlert Alert(string jobNum, string partNum, string resourceId, string characteristicName, RuleTriggered rule, string lockedAt)
    {
        return new ProcessAlert
        {
            JobNum = jobNum,
            PartNum = partNum,
            ResourceId = resourceId,
            CharacteristicName = characteristicName,
            OperatorUserId = "operator1",
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

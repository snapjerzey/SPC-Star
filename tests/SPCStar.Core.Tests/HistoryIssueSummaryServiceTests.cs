using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class HistoryIssueSummaryServiceTests
{
    [Fact]
    public void TopIssues_UsesRecordedLinksForOperationAndPhase_NotGuessedContext()
    {
        var r = new InMemorySpcRepository();
        var alert = Alert("J100", "P100", "PRESS1", "Diameter", RuleTriggered.SpecLimitViolation, "2026-10-19T12:00:00Z", "5 Day");
        r.Alerts.Add(alert);
        var reading = new InspectionMeasurement { JobNum="J100", PartNum="P100", ResourceId="PRESS1", CharacteristicName="Diameter",
            ProcessCode="PRESS", OperationSeq=10, InspectionPhase="Coil Change", OperatorUserId="op", Timestamp=alert.LockedAt };
        r.Measurements.Add(reading);
        var query = new HistoryIssueSummaryRequest("P100","J100","PRESS1","5 Day","Diameter",null,null,
            ProcessCode:"PRESS",OperationSeq:10,InspectionPhase:"Coil Change");
        var service = new HistoryIssueSummaryService(r);
        Assert.Empty(service.TopIssues(query));
        var violation = new RuleViolation {AlertId=alert.Id,RuleTriggered=alert.RuleTriggered,DetectedAt=alert.LockedAt};
        violation.MeasurementIds.Add(reading.Id); r.RuleViolations.Add(violation);
        Assert.Single(service.TopIssues(query));
        Assert.Empty(service.TopIssues(query with { InspectionPhase="Startup" }));
        Assert.Empty(service.TopIssues(query with { OperationSeq=20 }));
    }

    [Fact]
    public void TopIssues_OnlyCountsSpecificationViolationsAndRejectedAttributes_NotWatchOrControlRules()
    {
        var repository = new InMemorySpcRepository();
        var at = DateTimeOffset.Parse("2026-10-17T13:00:00Z");
        foreach (var rule in Enum.GetValues<RuleTriggered>())
        {
            var alert = Alert("J100", "P100", "PRESS1", "Diameter", rule, at.ToString("O"), "1st Half Days");
            alert.Detail = rule.ToString();
            repository.Alerts.Add(alert);
            at = at.AddMinutes(1);
        }
        foreach (var status in new[] { AlertStatus.Warning, AlertStatus.Resolved, AlertStatus.Active, AlertStatus.Overridden })
        {
            var watch = Alert("J100", "P100", "PRESS1", "Watch-only variable", RuleTriggered.CustomRuleTriggered, at.ToString("O"), "1st Half Days");
            watch.Status = status; repository.Alerts.Add(watch);
        }
        var rows = new HistoryIssueSummaryService(repository).TopIssues(new("P100", "J100", null, null, null, null, null));
        var row = Assert.Single(rows);
        Assert.Equal("Diameter", row.CharacteristicName);
        Assert.Equal(2, row.EventCount);
        Assert.Equal(2, row.SignalBreakdown.Count);
        Assert.All(row.SignalBreakdown, item => Assert.Contains(item.Label, new[] { "SpecLimitViolation", "AttributeRejected" }));
        Assert.Equal(Enum.GetValues<RuleTriggered>().Length + 4, repository.Alerts.Count);
    }

    [Fact]
    public void TopIssues_GroupsRepeatAlertsByPartAndCharacteristic()
    {
        var repository = new InMemorySpcRepository();
        var firstAlert = Alert("J100", "P100", "PRESS1", "Diameter", RuleTriggered.SpecLimitViolation, "2026-01-01T08:00:00Z", "1st Half Days");
        var secondAlert = Alert("J101", "P100", "PRESS2", "Diameter", RuleTriggered.SpecLimitViolation, "2026-01-02T08:00:00Z", "2nd Half Nights");
        var lengthAlert = Alert("J100", "P100", "PRESS1", "Length", RuleTriggered.AttributeRejected, "2026-01-03T08:00:00Z", "1st Half Days");
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
        Assert.Equal("Outside configured limit.", rows[0].SignalSummary);
        Assert.Collection(rows[0].SignalBreakdown,
            item =>
            {
                Assert.Equal("Outside configured limit.", item.Label);
                Assert.Equal(2, item.Count);
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
        var secondAlert = Alert("J101", "P100", "PRESS1", "Caliper Width", RuleTriggered.SpecLimitViolation, "2026-01-02T08:00:00Z", "1st Half Days");
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
        Assert.Equal("Outside configured limit.", row.SignalSummary);
        Assert.Equal("Tooling", row.CauseCategory);
        Assert.Collection(row.SignalBreakdown,
            item =>
            {
                Assert.Equal("Outside configured limit.", item.Label);
                Assert.Equal(3, item.Count);
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

    [Fact]
    public void TopIssues_UsesFailedInspectionReasonForFailedInspectionOverride()
    {
        var repository = new InMemorySpcRepository();
        var alert = Alert("J100", "P100", "PRESS1", "Diameter", RuleTriggered.SpecLimitViolation, "2026-01-01T08:00:00Z", "1st Half Days");
        repository.Alerts.Add(alert);
        repository.AlertOverrides.Add(new AlertOverride
        {
            AlertId = alert.Id,
            OperatorUserId = alert.OperatorUserId,
            OverrideUserId = "operator1",
            OverrideRole = "Failed Inspection",
            JobNum = alert.JobNum,
            PartNum = alert.PartNum,
            ResourceId = alert.ResourceId,
            CharacteristicName = alert.CharacteristicName,
            RuleTriggered = alert.RuleTriggered,
            CauseCategory = "Failed Inspection",
            CauseText = "Outside configured limit.",
            SolutionText = "Current inspection ended as failed. New inspection started.",
            LockedAt = alert.LockedAt,
            UnlockedAt = alert.LockedAt.AddMinutes(5),
            SubmittedAt = alert.LockedAt.AddMinutes(5)
        });
        repository.FailedInspections.Add(new FailedInspection
        {
            AlertId = alert.Id,
            JobNum = alert.JobNum,
            PartNum = alert.PartNum,
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = alert.ResourceId,
            InspectionPhase = "In Process",
            FailureNumber = 1,
            FailedByUserId = "operator1",
            FailedAt = alert.LockedAt.AddMinutes(5),
            Reason = "Operator wrote that the jaw was visibly bent."
        });

        var row = Assert.Single(new HistoryIssueSummaryService(repository).TopIssues(new HistoryIssueSummaryRequest("P100", null, null, null, "Diameter", null, null)));

        Assert.Equal("Operator wrote that the jaw was visibly bent.", row.LatestSolution);
        Assert.Null(row.CauseCategory);
        Assert.Equal("Operator wrote that the jaw was visibly bent.", row.CauseDetailSummary);
        Assert.Empty(row.CauseBreakdown);
        var causeDetail = Assert.Single(row.CauseDetailBreakdown);
        Assert.Equal("Operator wrote that the jaw was visibly bent.", causeDetail.Label);
        var solution = Assert.Single(row.SolutionBreakdown);
        Assert.Equal("Operator wrote that the jaw was visibly bent.", solution.Label);
    }

    [Fact]
    public void TopIssues_UsesBypassReasonAsCauseForSystemManagerBypass()
    {
        var repository = new InMemorySpcRepository();
        var alert = Alert("J100", "P100", "PRESS1", "Diameter", RuleTriggered.SpecLimitViolation, "2026-01-01T08:00:00Z", "1st Half Days");
        repository.Alerts.Add(alert);
        repository.AlertOverrides.Add(new AlertOverride
        {
            AlertId = alert.Id,
            OperatorUserId = alert.OperatorUserId,
            OverrideUserId = "Archon",
            OverrideRole = "System Manager",
            JobNum = alert.JobNum,
            PartNum = alert.PartNum,
            ResourceId = alert.ResourceId,
            CharacteristicName = alert.CharacteristicName,
            RuleTriggered = alert.RuleTriggered,
            CauseCategory = "System Manager Bypass",
            CauseText = "Standard correction workflow bypassed by System Manager access.",
            SolutionText = "Bypass approved. See bypass reason.",
            WhyStandardProcessWasBypassed = "Operator entered the wrong number during test.",
            LockedAt = alert.LockedAt,
            UnlockedAt = alert.LockedAt.AddMinutes(5),
            SubmittedAt = alert.LockedAt.AddMinutes(5)
        });

        var row = Assert.Single(new HistoryIssueSummaryService(repository).TopIssues(new HistoryIssueSummaryRequest("P100", null, null, null, "Diameter", null, null)));

        Assert.Null(row.CauseCategory);
        Assert.Empty(row.CauseBreakdown);
        Assert.Equal("Operator entered the wrong number during test.", row.CauseDetailSummary);
        var causeDetail = Assert.Single(row.CauseDetailBreakdown);
        Assert.Equal("Operator entered the wrong number during test.", causeDetail.Label);
    }

    [Fact]
    public void TopIssues_UsesSpecificViolationDetailForSpecSignals()
    {
        var repository = new InMemorySpcRepository();
        var alert = Alert("J100", "P100", "PRESS1", "Diameter", RuleTriggered.SpecLimitViolation, "2026-01-01T08:00:00Z", "1st Half Days");
        alert.Detail = "Entered value 0.099 is above the upper specification limit 0.067.";
        repository.Alerts.Add(alert);

        var row = Assert.Single(new HistoryIssueSummaryService(repository).TopIssues(new HistoryIssueSummaryRequest("P100", null, null, null, "Diameter", null, null)));

        Assert.Equal("Entered value 0.099 is above the upper specification limit 0.067.", row.SignalSummary);
        var signal = Assert.Single(row.SignalBreakdown);
        Assert.Equal("Entered value 0.099 is above the upper specification limit 0.067.", signal.Label);
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

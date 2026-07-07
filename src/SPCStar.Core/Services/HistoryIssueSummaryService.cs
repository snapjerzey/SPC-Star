using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record HistoryIssueSummaryRequest(
    string? PartNum,
    string? JobNum,
    string? ResourceId,
    string? OperatorShift,
    string? CharacteristicName,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit = 25);

public sealed record HistoryIssueSummaryRow(
    string PartNum,
    string CharacteristicName,
    RuleTriggered RuleTriggered,
    string SignalSummary,
    string? CauseCategory,
    IReadOnlyList<HistoryIssueBreakdownItem> SignalBreakdown,
    IReadOnlyList<HistoryIssueBreakdownItem> CauseBreakdown,
    IReadOnlyList<HistoryIssueBreakdownItem> SolutionBreakdown,
    int EventCount,
    int ActiveCount,
    int DistinctJobCount,
    int DistinctMachineCount,
    DateTimeOffset LatestEventAt,
    string LatestJobNum,
    string LatestResourceId,
    string? LatestOperatorShift,
    string? LatestDetail,
    string? LatestSolution);

public sealed record HistoryIssueBreakdownItem(string Label, int Count);

public sealed class HistoryIssueSummaryService(ISpcRepository repository)
{
    public IReadOnlyList<HistoryIssueSummaryRow> TopIssues(HistoryIssueSummaryRequest request)
    {
        var alerts = repository.Alerts
            .Where(alert =>
                Matches(request.PartNum, alert.PartNum) &&
                Matches(request.JobNum, alert.JobNum) &&
                Matches(request.ResourceId, alert.ResourceId) &&
                Matches(request.OperatorShift, alert.OperatorShift) &&
                Matches(request.CharacteristicName, alert.CharacteristicName) &&
                (!request.From.HasValue || alert.LockedAt >= request.From.Value) &&
                (!request.To.HasValue || alert.LockedAt <= request.To.Value))
            .ToArray();

        var overridesByAlert = repository.AlertOverrides
            .GroupBy(overrideEntry => overrideEntry.AlertId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(overrideEntry => overrideEntry.SubmittedAt).First());

        return alerts
            .GroupBy(alert => new
            {
                alert.PartNum,
                alert.CharacteristicName
            })
            .Select(group =>
            {
                var latest = group.OrderByDescending(alert => alert.LockedAt).First();
                overridesByAlert.TryGetValue(latest.Id, out var latestOverride);
                var signalBreakdown = SignalBreakdown(group);
                var causeBreakdown = CauseBreakdown(group, overridesByAlert);
                var solutionBreakdown = SolutionBreakdown(group, overridesByAlert);
                return new HistoryIssueSummaryRow(
                    group.Key.PartNum,
                    group.Key.CharacteristicName,
                    latest.RuleTriggered,
                    TopLabel(signalBreakdown) ?? "Unspecified",
                    TopLabel(causeBreakdown),
                    signalBreakdown,
                    causeBreakdown,
                    solutionBreakdown,
                    group.Count(),
                    group.Count(alert => alert.Status == AlertStatus.Active),
                    group.Select(alert => alert.JobNum).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    group.Select(alert => alert.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    latest.LockedAt,
                    latest.JobNum,
                    latest.ResourceId,
                    string.IsNullOrWhiteSpace(latest.OperatorShift) ? null : latest.OperatorShift,
                    latest.Detail,
                    latestOverride?.SolutionText);
            })
            .OrderByDescending(row => row.EventCount)
            .ThenByDescending(row => row.ActiveCount)
            .ThenByDescending(row => row.LatestEventAt)
            .Take(Math.Clamp(request.Limit, 1, 100))
            .ToArray();
    }

    private static IReadOnlyList<HistoryIssueBreakdownItem> SignalBreakdown(IEnumerable<ProcessAlert> alerts)
    {
        return alerts
            .GroupBy(alert => alert.RuleTriggered)
            .Select(group => new HistoryIssueBreakdownItem(RuleLabel(group.Key), group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string RuleLabel(RuleTriggered rule) => rule switch
    {
        RuleTriggered.SpecLimitViolation => "Spec limit violation",
        RuleTriggered.OnePointBeyondControlLimit => "One point beyond control limit",
        RuleTriggered.TwoOfThreeNearControlLimit => "Two of three near control limit",
        RuleTriggered.FourOfFiveApproachingLimit => "Four of five approaching limit",
        RuleTriggered.EightConsecutiveOneSideOfCenterline => "Eight consecutive one side of centerline",
        RuleTriggered.NelsonTrend => "Nelson trend",
        RuleTriggered.CusumShift => "CUSUM shift",
        RuleTriggered.EwmaShift => "EWMA shift",
        RuleTriggered.MovingAverageTrend => "Moving average trend",
        RuleTriggered.LinearTrendSlope => "Linear trend slope",
        RuleTriggered.CustomRuleTriggered => "Custom rule triggered",
        RuleTriggered.AttributeRejected => "Attribute rejected",
        _ => rule.ToString()
    };

    private static IReadOnlyList<HistoryIssueBreakdownItem> CauseBreakdown(
        IEnumerable<ProcessAlert> alerts,
        IReadOnlyDictionary<Guid, AlertOverride> overridesByAlert)
    {
        return alerts
            .Select(alert =>
            {
                overridesByAlert.TryGetValue(alert.Id, out var overrideEntry);
                return string.IsNullOrWhiteSpace(overrideEntry?.CauseCategory)
                    ? "Unspecified"
                    : overrideEntry.CauseCategory.Trim();
            })
            .GroupBy(cause => cause, StringComparer.OrdinalIgnoreCase)
            .Select(group => new HistoryIssueBreakdownItem(group.First(), group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<HistoryIssueBreakdownItem> SolutionBreakdown(
        IEnumerable<ProcessAlert> alerts,
        IReadOnlyDictionary<Guid, AlertOverride> overridesByAlert)
    {
        return alerts
            .Select(alert =>
            {
                overridesByAlert.TryGetValue(alert.Id, out var overrideEntry);
                return string.IsNullOrWhiteSpace(overrideEntry?.SolutionText)
                    ? "No solution entered"
                    : overrideEntry.SolutionText.Trim();
            })
            .GroupBy(solution => solution, StringComparer.OrdinalIgnoreCase)
            .Select(group => new HistoryIssueBreakdownItem(group.First(), group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? TopLabel(IReadOnlyList<HistoryIssueBreakdownItem> items) =>
        items.Count == 0 ? null : items[0].Label;

    private static bool Matches(string? filter, string value)
    {
        return string.IsNullOrWhiteSpace(filter) ||
            value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

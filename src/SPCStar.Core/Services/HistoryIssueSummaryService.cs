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
    int Limit = 25,
    string? ProcessCode = null,
    int? OperationSeq = null,
    string? InspectionPhase = null);

public sealed record HistoryIssueSummaryRow(
    string PartNum,
    string CharacteristicName,
    RuleTriggered RuleTriggered,
    string SignalSummary,
    string? CauseCategory,
    string? CauseDetailSummary,
    IReadOnlyList<HistoryIssueBreakdownItem> SignalBreakdown,
    IReadOnlyList<HistoryIssueBreakdownItem> CauseBreakdown,
    IReadOnlyList<HistoryIssueBreakdownItem> CauseDetailBreakdown,
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
                (alert.RuleTriggered == RuleTriggered.SpecLimitViolation || alert.RuleTriggered == RuleTriggered.AttributeRejected) &&
                Matches(request.PartNum, alert.PartNum) &&
                Matches(request.JobNum, alert.JobNum) &&
                Matches(request.ResourceId, alert.ResourceId) &&
                Matches(request.OperatorShift, alert.OperatorShift) &&
                Matches(request.CharacteristicName, alert.CharacteristicName) &&
                AlertMatchesInspectionScope(alert, request) &&
                (!request.From.HasValue || alert.LockedAt >= request.From.Value) &&
                (!request.To.HasValue || alert.LockedAt <= request.To.Value))
            .ToArray();

        var overridesByAlert = repository.AlertOverrides
            .GroupBy(overrideEntry => overrideEntry.AlertId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(overrideEntry => overrideEntry.SubmittedAt).First());

        var rows = alerts
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
                var causeDetailBreakdown = CauseDetailBreakdown(group, overridesByAlert);
                var solutionBreakdown = SolutionBreakdown(group, overridesByAlert);
                return new HistoryIssueSummaryRow(
                    group.Key.PartNum,
                    group.Key.CharacteristicName,
                    latest.RuleTriggered,
                    TopLabel(signalBreakdown) ?? "Unspecified",
                    TopLabel(causeBreakdown),
                    TopLabel(causeDetailBreakdown),
                    signalBreakdown,
                    causeBreakdown,
                    causeDetailBreakdown,
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
                    DisplaySolution(latestOverride));
            })
            .OrderByDescending(row => row.EventCount)
            .ThenByDescending(row => row.ActiveCount)
            .ThenByDescending(row => row.LatestEventAt);

        return request.Limit <= 0
            ? rows.ToArray()
            : rows.Take(Math.Clamp(request.Limit, 1, 100)).ToArray();
    }

    private bool AlertMatchesInspectionScope(ProcessAlert alert, HistoryIssueSummaryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProcessCode) && !request.OperationSeq.HasValue &&
            string.IsNullOrWhiteSpace(request.InspectionPhase)) return true;
        var scope = new DataReviewFilter(request.ProcessCode, request.OperationSeq, request.InspectionPhase);
        var ids = repository.RuleViolations.Where(item => item.AlertId == alert.Id)
            .SelectMany(item => item.MeasurementIds).ToHashSet();
        return repository.Measurements.Any(item => ids.Contains(item.Id) &&
            Matches(alert.PartNum, item.PartNum) && Matches(alert.JobNum, item.JobNum) &&
            scope.Context(item.ProcessCode, item.OperationSeq, item.InspectionPhase, item.ResourceId, item.Timestamp)) ||
            repository.FailedInspections.Any(item => item.AlertId == alert.Id &&
                scope.Context(item.ProcessCode, item.OperationSeq, item.InspectionPhase, item.ResourceId, item.FailedAt));
    }

    private static IReadOnlyList<HistoryIssueBreakdownItem> SignalBreakdown(IEnumerable<ProcessAlert> alerts)
    {
        return alerts
            .GroupBy(DisplaySignal, StringComparer.OrdinalIgnoreCase)
            .Select(group => new HistoryIssueBreakdownItem(group.Key, group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string DisplaySignal(ProcessAlert alert)
    {
        if ((alert.RuleTriggered == RuleTriggered.SpecLimitViolation ||
                alert.RuleTriggered == RuleTriggered.AttributeRejected) &&
            !string.IsNullOrWhiteSpace(alert.Detail))
        {
            return alert.Detail.Trim();
        }

        return RuleLabel(alert.RuleTriggered);
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

    private IReadOnlyList<HistoryIssueBreakdownItem> CauseBreakdown(
        IEnumerable<ProcessAlert> alerts,
        IReadOnlyDictionary<Guid, AlertOverride> overridesByAlert)
    {
        return alerts
            .Select(alert =>
            {
                overridesByAlert.TryGetValue(alert.Id, out var overrideEntry);
                return DisplayCauseCategory(overrideEntry);
            })
            .Where(cause => !string.IsNullOrWhiteSpace(cause))
            .GroupBy(cause => cause, StringComparer.OrdinalIgnoreCase)
            .Select(group => new HistoryIssueBreakdownItem(group.First()!, group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<HistoryIssueBreakdownItem> CauseDetailBreakdown(
        IEnumerable<ProcessAlert> alerts,
        IReadOnlyDictionary<Guid, AlertOverride> overridesByAlert)
    {
        return alerts
            .Select(alert =>
            {
                overridesByAlert.TryGetValue(alert.Id, out var overrideEntry);
                var cause = DisplayCause(overrideEntry);
                return string.IsNullOrWhiteSpace(cause)
                    ? "No cause detail entered"
                    : cause;
            })
            .GroupBy(cause => cause, StringComparer.OrdinalIgnoreCase)
            .Select(group => new HistoryIssueBreakdownItem(group.First(), group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<HistoryIssueBreakdownItem> SolutionBreakdown(
        IEnumerable<ProcessAlert> alerts,
        IReadOnlyDictionary<Guid, AlertOverride> overridesByAlert)
    {
        return alerts
            .Select(alert =>
            {
                overridesByAlert.TryGetValue(alert.Id, out var overrideEntry);
                var solution = DisplaySolution(overrideEntry);
                return string.IsNullOrWhiteSpace(solution)
                    ? "No solution entered"
                    : solution;
            })
            .GroupBy(solution => solution, StringComparer.OrdinalIgnoreCase)
            .Select(group => new HistoryIssueBreakdownItem(group.First(), group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? TopLabel(IReadOnlyList<HistoryIssueBreakdownItem> items) =>
        items.Count == 0 ? null : items[0].Label;

    private string? DisplaySolution(AlertOverride? overrideEntry)
    {
        if (overrideEntry?.OverrideRole.Equals("Failed Inspection", StringComparison.OrdinalIgnoreCase) == true)
        {
            var failedReason = repository.FailedInspections
                .Where(failure => failure.AlertId == overrideEntry.AlertId)
                .OrderByDescending(failure => failure.FailedAt)
                .Select(failure => failure.Reason)
                .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
            if (!string.IsNullOrWhiteSpace(failedReason))
            {
                return failedReason.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(overrideEntry?.WhyStandardProcessWasBypassed))
        {
            return overrideEntry.WhyStandardProcessWasBypassed.Trim();
        }

        return string.IsNullOrWhiteSpace(overrideEntry?.SolutionText)
            ? null
            : overrideEntry.SolutionText.Trim();
    }

    private string? DisplayCause(AlertOverride? overrideEntry)
    {
        if (overrideEntry?.OverrideRole.Equals("Failed Inspection", StringComparison.OrdinalIgnoreCase) == true)
        {
            var failedReason = repository.FailedInspections
                .Where(failure => failure.AlertId == overrideEntry.AlertId)
                .OrderByDescending(failure => failure.FailedAt)
                .Select(failure => failure.Reason)
                .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
            if (!string.IsNullOrWhiteSpace(failedReason))
            {
                return failedReason.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(overrideEntry?.WhyStandardProcessWasBypassed))
        {
            return overrideEntry.WhyStandardProcessWasBypassed.Trim();
        }

        if (!string.IsNullOrWhiteSpace(overrideEntry?.CauseText) &&
            !overrideEntry.CauseText.Equals("Standard correction workflow bypassed by System Manager access.", StringComparison.OrdinalIgnoreCase))
        {
            return overrideEntry.CauseText.Trim();
        }

        return null;
    }

    private static string? DisplayCauseCategory(AlertOverride? overrideEntry)
    {
        if (string.IsNullOrWhiteSpace(overrideEntry?.CauseCategory))
        {
            return null;
        }

        var category = overrideEntry.CauseCategory.Trim();
        return category.Equals("System Manager Bypass", StringComparison.OrdinalIgnoreCase) ||
            category.Equals("Failed Inspection", StringComparison.OrdinalIgnoreCase) ||
            category.Equals("Unspecified", StringComparison.OrdinalIgnoreCase)
                ? null
                : category;
    }

    private static bool Matches(string? filter, string value)
    {
        return string.IsNullOrWhiteSpace(filter) ||
            value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

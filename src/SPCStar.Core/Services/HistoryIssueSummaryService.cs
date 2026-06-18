using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record HistoryIssueSummaryRequest(
    string? PartNum,
    string? JobNum,
    string? ResourceId,
    string? CharacteristicName,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit = 25);

public sealed record HistoryIssueSummaryRow(
    string PartNum,
    string CharacteristicName,
    RuleTriggered RuleTriggered,
    string? CauseCategory,
    int EventCount,
    int ActiveCount,
    int DistinctJobCount,
    int DistinctMachineCount,
    DateTimeOffset LatestEventAt,
    string LatestJobNum,
    string LatestResourceId,
    string? LatestDetail,
    string? LatestSolution);

public sealed class HistoryIssueSummaryService(ISpcRepository repository)
{
    public IReadOnlyList<HistoryIssueSummaryRow> TopIssues(HistoryIssueSummaryRequest request)
    {
        var alerts = repository.Alerts
            .Where(alert =>
                Matches(request.PartNum, alert.PartNum) &&
                Matches(request.JobNum, alert.JobNum) &&
                Matches(request.ResourceId, alert.ResourceId) &&
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
            .GroupBy(alert =>
            {
                overridesByAlert.TryGetValue(alert.Id, out var overrideEntry);
                return new
                {
                    alert.PartNum,
                    alert.CharacteristicName,
                    alert.RuleTriggered,
                    CauseCategory = string.IsNullOrWhiteSpace(overrideEntry?.CauseCategory)
                        ? null
                        : overrideEntry.CauseCategory.Trim()
                };
            })
            .Select(group =>
            {
                var latest = group.OrderByDescending(alert => alert.LockedAt).First();
                overridesByAlert.TryGetValue(latest.Id, out var latestOverride);
                return new HistoryIssueSummaryRow(
                    group.Key.PartNum,
                    group.Key.CharacteristicName,
                    group.Key.RuleTriggered,
                    group.Key.CauseCategory,
                    group.Count(),
                    group.Count(alert => alert.Status == AlertStatus.Active),
                    group.Select(alert => alert.JobNum).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    group.Select(alert => alert.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    latest.LockedAt,
                    latest.JobNum,
                    latest.ResourceId,
                    latest.Detail,
                    latestOverride?.SolutionText);
            })
            .OrderByDescending(row => row.EventCount)
            .ThenByDescending(row => row.ActiveCount)
            .ThenByDescending(row => row.LatestEventAt)
            .Take(Math.Clamp(request.Limit, 1, 100))
            .ToArray();
    }

    private static bool Matches(string? filter, string value)
    {
        return string.IsNullOrWhiteSpace(filter) ||
            value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

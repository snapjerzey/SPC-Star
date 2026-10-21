using SPCStar.Core.Domain;

namespace SPCStar.Core.Services;

internal static class CustomWatchEvaluator
{
    internal static bool IsNear(decimal value, decimal? lower, decimal? upper, CustomDriftRuleSettings rule, bool high)
    {
        var o = rule.Options;
        if ((lower.HasValue && value < lower) || (upper.HasValue && value > upper)) return false;
        var width = o.ZoneMode == "Distance" ? o.ZoneDistance : lower.HasValue && upper.HasValue && upper > lower
            ? (upper.Value - lower.Value) * o.ZonePercent / 100m : 0m;
        if (width <= 0) return false;
        return high ? upper.HasValue && rule.Direction != "Below" && upper.Value - value <= width
            : lower.HasValue && rule.Direction != "Above" && value - lower.Value <= width;
    }

    internal static IReadOnlyList<string> Evaluate(IReadOnlyList<decimal[]?> completed, decimal[] current,
        decimal? lower, decimal? upper, CustomDriftRuleSettings rule, bool completing, bool anyCondition = false)
    {
        var o = rule.Options;
        if (!o.Enabled || current.Length == 0 || current.Any(v => (lower.HasValue && v < lower) || (upper.HasValue && v > upper))) return [];
        var conditions = new List<(bool Matched, string Reason)>();
        var nearUpper = current.Any(v => IsNear(v, lower, upper, rule, true));
        var nearLower = current.Any(v => IsNear(v, lower, upper, rule, false));
        if (o.TrendEnabled)
        {
            var up = 1; var down = 1; decimal? previous = null;
            foreach (var set in completed)
            {
                if (set is null || set.Length == 0) { up = down = 1; previous = null; continue; }
                var average = set.Average();
                if (previous.HasValue)
                {
                    if (average > previous) { up++; if (o.ResetOnReversal) down = 1; }
                    else if (average < previous) { down++; if (o.ResetOnReversal) up = 1; }
                    else if (o.ResetOnEqual) up = down = 1;
                }
                previous = average;
            }
            var high = completing && upper.HasValue && rule.Direction != "Below" && up >= rule.WindowSize;
            var low = completing && lower.HasValue && rule.Direction != "Above" && down >= rule.WindowSize;
            conditions.Add((high || low, $"Trending toward {(high ? "upper" : "lower")} specification limit ({rule.WindowSize} completed inspection averages)."));
        }
        if (o.NearLimitEnabled)
            conditions.Add((nearUpper || nearLower, $"Near {(nearUpper && nearLower ? "upper and lower" : nearUpper ? "upper" : "lower")} specification limit."));
        if (o.RepeatedNearLimitEnabled)
        {
            var window = completed.TakeLast(o.RepeatedWindow).ToArray();
            var highCount = window.Count(set => set?.Any(v => IsNear(v, lower, upper, rule, true)) == true);
            var lowCount = window.Count(set => set?.Any(v => IsNear(v, lower, upper, rule, false)) == true);
            var matched = completing && window.Length >= o.RepeatedWindow && (highCount >= o.RepeatedCount || lowCount >= o.RepeatedCount);
            conditions.Add((matched, $"Repeated near-{(highCount >= o.RepeatedCount ? "upper" : "lower")}-limit results ({o.RepeatedCount} of {o.RepeatedWindow} completed inspections)."));
        }
        if (conditions.Count == 0 || (!anyCondition && o.Combination == "All" && conditions.Any(c => !c.Matched))) return [];
        return conditions.Where(c => c.Matched).Select(c => c.Reason).ToArray();
    }
}

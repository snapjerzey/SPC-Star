using SPCStar.Core.Domain;

namespace SPCStar.Core.Services;

public sealed record WesternElectricPoint(Guid MeasurementId, decimal Value, DateTimeOffset Timestamp);

public sealed record WesternElectricViolation(
    RuleTriggered RuleTriggered,
    IReadOnlyList<Guid> MeasurementIds,
    DateTimeOffset DetectedAt);

public sealed class WesternElectricRuleService
{
    public IReadOnlyList<WesternElectricViolation> Detect(
        IReadOnlyList<WesternElectricPoint> points,
        decimal centerLine,
        decimal lcl,
        decimal ucl)
    {
        var ordered = points.OrderBy(point => point.Timestamp).ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var sigma = (ucl - centerLine) / 3m;
        if (sigma <= 0 || centerLine <= lcl || centerLine >= ucl)
        {
            throw new ArgumentException("Control limits must surround the centerline and imply a positive sigma.");
        }

        var violations = new List<WesternElectricViolation>();
        DetectOnePointBeyondLimits(ordered, lcl, ucl, violations);
        DetectTwoOfThreeNearLimit(ordered, centerLine, sigma, violations);
        DetectFourOfFiveApproachingLimit(ordered, centerLine, sigma, violations);
        DetectEightConsecutiveSameSide(ordered, centerLine, violations);
        return violations;
    }

    private static void DetectOnePointBeyondLimits(
        IReadOnlyList<WesternElectricPoint> points,
        decimal lcl,
        decimal ucl,
        List<WesternElectricViolation> violations)
    {
        foreach (var point in points.Where(point => point.Value < lcl || point.Value > ucl))
        {
            violations.Add(new WesternElectricViolation(
                RuleTriggered.OnePointBeyondControlLimit,
                [point.MeasurementId],
                point.Timestamp));
        }
    }

    private static void DetectTwoOfThreeNearLimit(
        IReadOnlyList<WesternElectricPoint> points,
        decimal centerLine,
        decimal sigma,
        List<WesternElectricViolation> violations)
    {
        for (var i = 0; i <= points.Count - 3; i++)
        {
            var window = points.Skip(i).Take(3).ToArray();
            var high = window.Where(point => point.Value >= centerLine + 2m * sigma).ToArray();
            var low = window.Where(point => point.Value <= centerLine - 2m * sigma).ToArray();
            var triggered = high.Length >= 2 ? high : low.Length >= 2 ? low : Array.Empty<WesternElectricPoint>();
            if (triggered.Length >= 2)
            {
                violations.Add(new WesternElectricViolation(
                    RuleTriggered.TwoOfThreeNearControlLimit,
                    triggered.Select(point => point.MeasurementId).ToArray(),
                    window[^1].Timestamp));
            }
        }
    }

    private static void DetectFourOfFiveApproachingLimit(
        IReadOnlyList<WesternElectricPoint> points,
        decimal centerLine,
        decimal sigma,
        List<WesternElectricViolation> violations)
    {
        for (var i = 0; i <= points.Count - 5; i++)
        {
            var window = points.Skip(i).Take(5).ToArray();
            var high = window.Where(point => point.Value >= centerLine + sigma).ToArray();
            var low = window.Where(point => point.Value <= centerLine - sigma).ToArray();
            var triggered = high.Length >= 4 ? high : low.Length >= 4 ? low : Array.Empty<WesternElectricPoint>();
            if (triggered.Length >= 4)
            {
                violations.Add(new WesternElectricViolation(
                    RuleTriggered.FourOfFiveApproachingLimit,
                    triggered.Select(point => point.MeasurementId).ToArray(),
                    window[^1].Timestamp));
            }
        }
    }

    private static void DetectEightConsecutiveSameSide(
        IReadOnlyList<WesternElectricPoint> points,
        decimal centerLine,
        List<WesternElectricViolation> violations)
    {
        for (var i = 0; i <= points.Count - 8; i++)
        {
            var window = points.Skip(i).Take(8).ToArray();
            if (window.All(point => point.Value > centerLine) || window.All(point => point.Value < centerLine))
            {
                violations.Add(new WesternElectricViolation(
                    RuleTriggered.EightConsecutiveOneSideOfCenterline,
                    window.Select(point => point.MeasurementId).ToArray(),
                    window[^1].Timestamp));
            }
        }
    }
}

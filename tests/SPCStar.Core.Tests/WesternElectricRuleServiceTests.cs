using SPCStar.Core.Domain;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class WesternElectricRuleServiceTests
{
    [Fact]
    public void Detect_FindsPointBeyondControlLimit()
    {
        var service = new WesternElectricRuleService();
        var points = Points([10m, 10.2m, 13.5m]);

        var result = service.Detect(points, centerLine: 10m, lcl: 7m, ucl: 13m);

        Assert.Contains(result, violation => violation.RuleTriggered == RuleTriggered.OnePointBeyondControlLimit);
    }

    [Fact]
    public void Detect_FindsTwoOfThreeNearControlLimit()
    {
        var service = new WesternElectricRuleService();
        var points = Points([10m, 12.2m, 12.4m]);

        var result = service.Detect(points, centerLine: 10m, lcl: 7m, ucl: 13m);

        Assert.Contains(result, violation => violation.RuleTriggered == RuleTriggered.TwoOfThreeNearControlLimit);
    }

    [Fact]
    public void Detect_FindsEightConsecutiveOnSameSide()
    {
        var service = new WesternElectricRuleService();
        var points = Points([10.1m, 10.2m, 10.3m, 10.1m, 10.2m, 10.4m, 10.2m, 10.1m]);

        var result = service.Detect(points, centerLine: 10m, lcl: 7m, ucl: 13m);

        Assert.Contains(result, violation => violation.RuleTriggered == RuleTriggered.EightConsecutiveOneSideOfCenterline);
    }

    private static IReadOnlyList<WesternElectricPoint> Points(IReadOnlyList<decimal> values)
    {
        return values.Select((value, index) => new WesternElectricPoint(
            Guid.NewGuid(),
            value,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddMinutes(index))).ToArray();
    }
}

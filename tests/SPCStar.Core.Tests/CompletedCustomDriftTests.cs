using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class CompletedCustomDriftTests
{
    [Fact]
    public void NearLimit_WarnsOnIndividualSampleBeforeCompletion_AndRecoversAfterConfiguredNormalInspections()
    {
        var r = Repository(1); var o = r.Settings.CustomDriftRule.Options;
        o.TrendEnabled = false; o.NearLimitEnabled = true; o.RecoveryInspections = 2;
        var service = new InspectionMeasurementService(r, new WesternElectricRuleService());
        Assert.True(service.EnterMeasurement(new("J", "P100", "MOLD", 10, "PRESS1", "Diameter", 9.5m, DateTimeOffset.UtcNow, "operator1")).Succeeded);
        var warning = Assert.Single(r.Alerts); Assert.Equal(AlertStatus.Warning, warning.Status); Assert.Contains("Near upper", warning.Detail);
        var first = AddRun(r, 5m); Complete(r, first); Assert.Equal(AlertStatus.Warning, warning.Status);
        Complete(r, first); Assert.Equal(1, warning.NormalWatchInspections);
        var second = AddRun(r, 5m); Complete(r, second); Assert.Equal(AlertStatus.Resolved, warning.Status);
        Assert.Contains("Near upper", r.Measurements.First().WatchReason);
    }

    [Fact]
    public void RepeatedNearLimit_UsesInspections_NotSampleCount_AndSameSide()
    {
        var r = Repository(3); var o = r.Settings.CustomDriftRule.Options;
        o.TrendEnabled = false; o.NearLimitEnabled = false; o.RepeatedNearLimitEnabled = true; o.RepeatedCount = 2; o.RepeatedWindow = 3;
        Complete(r, AddRun(r, 9.1m, 9.5m, 9.9m)); Assert.Empty(r.Alerts);
        Complete(r, AddRun(r, .1m, .5m, .9m)); Assert.Empty(r.Alerts);
        Complete(r, AddRun(r, 5m, 5m, 5m)); Assert.Empty(r.Alerts);
        Complete(r, AddRun(r, 9.2m, 5m, 5m)); Assert.Empty(r.Alerts);
        Complete(r, AddRun(r, 9.3m, 5m, 5m)); Assert.Equal(AlertStatus.Warning, Assert.Single(r.Alerts).Status);
    }

    [Fact]
    public void AllConditions_RequireTrendAndNearLimit_WhileAnyCanWarnForEither()
    {
        var r = Repository(1); var o = r.Settings.CustomDriftRule.Options;
        o.NearLimitEnabled = true; o.Combination = "All";
        Complete(r, AddRun(r, 4m)); Complete(r, AddRun(r, 5m)); Complete(r, AddRun(r, 6m)); Assert.Empty(r.Alerts);
        Complete(r, AddRun(r, 9.5m)); Assert.Single(r.Alerts);
    }

    [Fact]
    public void OneSidedNearLimit_RequiresDistance_NoInventedSpan()
    {
        var r = Repository(1); var o = r.Settings.CustomDriftRule.Options;
        r.InspectionPlans.First(p => p.AlertRuleSet == "Custom").Lsl = null;
        o.TrendEnabled = false; o.NearLimitEnabled = true;
        Complete(r, AddRun(r, 9.5m)); Assert.Empty(r.Alerts);
        o.ZoneMode = "Distance"; o.ZoneDistance = .5m;
        Complete(r, AddRun(r, 9.5m)); Assert.Equal(AlertStatus.Warning, Assert.Single(r.Alerts).Status);
    }

    [Fact]
    public void ResetToggles_CanIgnoreEqualAndReversingAverages()
    {
        var r = Repository(1); var o = r.Settings.CustomDriftRule.Options;
        o.ResetOnEqual = false; o.ResetOnReversal = false;
        foreach (var v in new[] { 2m, 3m, 3m, 2m, 4m }) Complete(r, AddRun(r, v));
        Assert.Contains(r.Alerts, alert => alert.Status == AlertStatus.Warning);
    }

    [Fact]
    public void DisabledRules_DoNotWarn_ButStillLockOutOfSpec()
    {
        var r = Repository(1); r.Settings.CustomDriftRule.Options.Enabled = false;
        foreach (var v in new[] { 2m, 3m, 4m }) Complete(r, AddRun(r, v)); Assert.Empty(r.Alerts);
        var entered = new InspectionMeasurementService(r, new WesternElectricRuleService()).EnterMeasurement(
            new("J", "P100", "MOLD", 10, "PRESS1", "Diameter", 11m, DateTimeOffset.UtcNow, "operator1"));
        Assert.True(entered.Succeeded); Assert.Equal(AlertStatus.Active, Assert.Single(r.Alerts).Status);
    }

    private static InMemorySpcRepository Repository(int samples = 3, string direction = "SameSide")
    {
        var r = new InMemorySpcRepository();
        SeedData.SeedSecurity(r); TestSeedData.SeedUsers(r); SeedData.SeedSampleInspectionPlans(r);
        var characteristic = r.Characteristics.Single(item => item.Name == "Diameter");
        var plan = r.InspectionPlans.First(item => item.CharacteristicId == characteristic.Id);
        plan.SampleSize = samples; plan.AlertRuleSet = "Custom"; plan.Lsl = 0m; plan.Usl = 10m;
        r.Settings.CustomDriftRule.WindowSize = 3; r.Settings.CustomDriftRule.Direction = direction;
        r.Settings.CustomDriftRule.Options.NearLimitEnabled = false;
        r.Settings.CustomDriftRule.Options.RecoveryInspections = 1;
        r.ControlLimits.Clear();
        return r;
    }

    private static JobPhaseCompletion AddRun(InMemorySpcRepository r, params decimal[] values)
    {
        var n = r.JobPhaseCompletions.Count + 1;
        var run = new JobPhaseCompletion { JobNum = "J", PartNum = "P100", ProcessCode = "MOLD", OperationSeq = 10,
            ResourceId = "PRESS1", InspectionPhase = "In Process", CompletedByUserId = "operator1", CompletionNumber = n,
            CompletedAt = DateTimeOffset.Parse("2026-10-16T12:00:00Z").AddMinutes(n) };
        foreach (var value in values)
        {
            var m = new InspectionMeasurement { JobNum = "J", PartNum = "P100", ProcessCode = "MOLD", OperationSeq = 10,
                ResourceId = "PRESS1", CharacteristicName = "Diameter", Value = value, OperatorUserId = "operator1",
                Timestamp = run.CompletedAt, ClientRecordId = Guid.NewGuid().ToString(), DeviceId = "test" };
            r.Measurements.Add(m); run.MeasurementIds.Add(m.Id);
        }
        r.JobPhaseCompletions.Add(run); return run;
    }

    private static void Complete(InMemorySpcRepository r, JobPhaseCompletion run)
    {
        var ids = r.Measurements.Where(item => run.MeasurementIds.Contains(item.Id)).Select(item => item.ClientRecordId!).ToArray();
        var result = new InspectionMeasurementService(r, new WesternElectricRuleService()).CompleteInspection(
            new(run.JobNum, run.PartNum, run.ProcessCode, run.OperationSeq, run.ResourceId, run.InspectionPhase, 20000, ids));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Averages_CountInspections_NotSamples_WithoutControlLimits_AndRetryDoesNotDuplicate()
    {
        var r = Repository();
        var first = AddRun(r, 1m, 2m, 3m); Complete(r, first); Assert.Empty(r.Alerts);
        var second = AddRun(r, 2m, 3m, 4m); Complete(r, second); Assert.Empty(r.Alerts);
        var third = AddRun(r, 3m, 4m, 5m); Complete(r, third);
        var alert = Assert.Single(r.Alerts); Assert.Equal(AlertStatus.Warning, alert.Status);
        Assert.Contains("3 completed inspection averages", alert.Detail);
        Assert.Equal(9, Assert.Single(r.RuleViolations).MeasurementIds.Count);
        Complete(r, third); Assert.Single(r.Alerts);
        Complete(r, AddRun(r, 2m, 3m, 4m));
        Assert.Equal(AlertStatus.Resolved, alert.Status);
        Assert.All(r.Measurements.Where(item => third.MeasurementIds.Contains(item.Id)), item => Assert.Contains("3 completed", item.WatchReason));
        Assert.All(r.Measurements.Where(item => first.MeasurementIds.Contains(item.Id)), item => Assert.Empty(item.WatchReason));
    }

    [Theory]
    [InlineData("Above", true, false)]
    [InlineData("Below", true, true)]
    [InlineData("SameSide", true, true)]
    [InlineData("Above", false, true)]
    public void SingleSample_AndOneSidedSpecs_RespectDirection(string direction, bool lowerOnly, bool expected)
    {
        var r = Repository(1, direction); var plan = r.InspectionPlans.First(item => item.AlertRuleSet == "Custom");
        if (lowerOnly) plan.Usl = null; else plan.Lsl = null;
        var values = lowerOnly ? new[] { 4m, 3m, 2m } : new[] { 2m, 3m, 4m };
        foreach (var value in values) Complete(r, AddRun(r, value));
        Assert.Equal(expected ? 1 : 0, r.Alerts.Count);
    }

    [Theory]
    [InlineData(3, 3, 4)]
    [InlineData(2, 4, 3)]
    public void EqualOrReversingAverage_BreaksTrend(int a, int b, int c)
    {
        var r = Repository(1); foreach (var value in new[] { a, b, c }) Complete(r, AddRun(r, value)); Assert.Empty(r.Alerts);
    }

    [Fact]
    public void UnsubmittedSamples_DoNotTriggerCustomRule_AndIndividualOutOfSpecStillLocks()
    {
        var r = Repository(1); var service = new InspectionMeasurementService(r, new WesternElectricRuleService());
        for (var i = 1; i <= 4; i++)
            Assert.True(service.EnterMeasurement(new("J", "P100", "MOLD", 10, "PRESS1", "Diameter", i, DateTimeOffset.UtcNow, "operator1")).Succeeded);
        Assert.Empty(r.Alerts);
        Assert.True(service.EnterMeasurement(new("J", "P100", "MOLD", 10, "PRESS1", "Diameter", 11m, DateTimeOffset.UtcNow, "operator1")).Succeeded);
        Assert.Equal(AlertStatus.Active, Assert.Single(r.Alerts).Status);
    }
}

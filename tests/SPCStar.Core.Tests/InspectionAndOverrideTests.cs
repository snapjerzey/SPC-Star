using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class InspectionAndOverrideTests
{
    [Theory]
    [InlineData(193500, 198000)]
    [InlineData(193510, 198000)]
    public void FailedAt193500CanRepeatImmediatelyAndOnlyPassingRepeatAdvancesSchedule(long repeatCounter, int nextDue)
    {
        var repository = RepositoryWithSecurityAndLimits();
        var diameter = repository.Characteristics.Single(item => item.Name == "Diameter");
        repository.InspectionPlans.RemoveAll(item => item.CharacteristicId != diameter.Id);
        var plan = Assert.Single(repository.InspectionPlans);
        plan.SampleSize = 1;
        plan.Frequency = new InspectionFrequency { Type = FrequencyType.Quantity, Value = 4500, Unit = FrequencyUnit.Pieces };
        repository.ControlLimits.Clear();
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100", PartNum = "P100", ProcessCode = "MOLD", OperationSeq = 10,
            ResourceId = "OTHER", InspectionPhase = "Coil Change", CompletedByUserId = "operator1",
            MachineCounter = 189000, CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        Assert.True(service.EnterMeasurement(Entry(6m) with { Timestamp = DateTimeOffset.UtcNow.AddSeconds(-1),
            DeviceId = "test", ClientRecordId = "failed-sample" }).Succeeded);
        var alert = Assert.Single(repository.Alerts);
        var failed = service.FailInspectionWithSignoff(new FailInspectionRequest("J100", "P100", "MOLD", 10,
            "PRESS1", "In Process", "operator1", 193500, ["failed-sample"], alert.Id,
            CauseCategory: "Machine", CauseText: "Fixture shifted", SolutionText: "Fixture repaired",
            SignOffUserName: "linetech1", SignOffPassword: "linetech1", RequiredCharacteristicNames: ["Diameter"]));
        Assert.True(failed.Succeeded, string.Join(" | ", failed.Errors));
        Assert.Single(repository.JobPhaseCompletions);
        var savedFailure = System.Text.Json.JsonSerializer.Deserialize<FailedInspection>(
            System.Text.Json.JsonSerializer.Serialize(Assert.Single(repository.FailedInspections)))!;
        repository.FailedInspections.Clear();
        repository.FailedInspections.Add(savedFailure);
        var request = new InspectionFrequencyCheckRequest("J100", "P100", "MOLD", 10, "Diameter", "PRESS1",
            DateTimeOffset.UtcNow, (int)repeatCounter, 189000, []);
        var retry = new InspectionFrequencyService(repository).Evaluate(request);
        Assert.True(retry.RequiresReinspection);
        Assert.Equal(InspectionDueStatus.DueNow, retry.Status);
        Assert.Null(JobMachineCounterRules.Validate(repository, "J100", repeatCounter));
        Assert.True(service.EnterMeasurement(Entry(5m) with { Timestamp = DateTimeOffset.UtcNow.AddSeconds(1),
            DeviceId = "test", ClientRecordId = "repeat-sample" }).Succeeded);
        var passed = service.CompleteInspection(CompletionRequest(repeatCounter) with { MeasurementClientRecordIds = ["repeat-sample"] });
        Assert.True(passed.Succeeded, string.Join(" | ", passed.Errors));
        Assert.Single(repository.FailedInspections);
        Assert.Equal(2, repository.JobPhaseCompletions.Count);
        var after = new InspectionFrequencyService(repository).Evaluate(request with { QuantityAtLastInspection = (int)repeatCounter });
        Assert.False(after.RequiresReinspection);
        Assert.Equal(InspectionDueStatus.NotDue, after.Status);
        Assert.Equal(nextDue, after.NextInspectionDueQuantity);
    }

    [Fact]
    public void EnterMeasurement_CreatesDriftWarningAndAllowsFurtherEntry_WhenRuleViolationOccurs()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var limit = repository.ControlLimits.First(item =>
            item.PartNum == "P100" &&
            item.ProcessCode == "MOLD" &&
            item.OperationSeq == 10 &&
            item.CharacteristicName == "Diameter");
        limit.CenterLine = 5m;
        limit.Lcl = 4.7m;
        limit.Ucl = 5.3m;
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var warning = service.EnterMeasurement(Entry(5.4m));
        var next = service.EnterMeasurement(Entry(5m, minutes: 1));

        Assert.True(warning.Succeeded, string.Join(" | ", warning.Errors));
        Assert.True(next.Succeeded, string.Join(" | ", next.Errors));
        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(RuleTriggered.OnePointBeyondControlLimit, alert.RuleTriggered);
        Assert.Equal(AlertStatus.Warning, alert.Status);
    }

    [Fact]
    public void EnterMeasurement_CreatesSpecLock_WhenValueIsOutOfSpec()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.Users.Single(user => user.UserName == "operator1").Shift = "1st Shift";
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(6m));
        var locked = service.EnterMeasurement(Entry(5m, minutes: 1));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal("1st Shift", result.Value!.OperatorShift);
        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(RuleTriggered.SpecLimitViolation, alert.RuleTriggered);
        Assert.Equal(AlertStatus.Active, alert.Status);
        Assert.Equal("1st Shift", alert.OperatorShift);
        Assert.Contains("above the upper specification limit", alert.Detail);
        Assert.False(locked.Succeeded);
        Assert.Contains(locked.Errors, error => error.Contains("Diameter is locked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnterMeasurement_CreatesSpecLock_WhenValueIsBelowLowerOnlySpec()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var characteristic = repository.Characteristics.Single(characteristic => characteristic.Name == "Diameter");
        var plan = repository.InspectionPlans.Single(plan => plan.CharacteristicId == characteristic.Id);
        plan.Nominal = null;
        plan.Lsl = 280m;
        plan.Usl = null;
        SetRuleSet(repository, "Diameter", "SpecLimitOnly");
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(279m));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(RuleTriggered.SpecLimitViolation, alert.RuleTriggered);
        Assert.Equal(AlertStatus.Active, alert.Status);
        Assert.Contains("below the lower specification limit 280", alert.Detail);
    }

    [Theory]
    [InlineData(324, true)]
    [InlineData(325, false)]
    [InlineData(326, false)]
    [InlineData(400, false)]
    [InlineData(10000, false)]
    public void EnterMeasurement_MinimumShear325_OnlyLocksBelowMinimum(int value, bool shouldLock)
    {
        var repository = RepositoryWithSecurityAndLimits();
        var characteristic = repository.Characteristics.Single(item => item.Name == "Diameter");
        var plan = repository.InspectionPlans.Single(item => item.CharacteristicId == characteristic.Id);
        plan.Nominal = null;
        plan.Lsl = 325m;
        plan.Usl = null;
        SetRuleSet(repository, "Diameter", "SpecLimitOnly");
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(value));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(shouldLock, repository.Alerts.Any(alert => alert.Status == AlertStatus.Active));
        Assert.Empty(repository.FailedInspections);
    }

    [Theory]
    [InlineData(87, true)]
    [InlineData(88, false)]
    [InlineData(90, false)]
    [InlineData(92, false)]
    [InlineData(93, true)]
    public void EnterMeasurement_Angle88To92_OnlyLocksOutsideLimits(int value, bool shouldLock)
    {
        var repository = RepositoryWithSecurityAndLimits();
        var characteristic = repository.Characteristics.Single(item => item.Name == "Diameter");
        var plan = repository.InspectionPlans.Single(item => item.CharacteristicId == characteristic.Id);
        plan.Nominal = 90m;
        plan.Lsl = 88m;
        plan.Usl = 92m;
        SetRuleSet(repository, "Diameter", "SpecLimitOnly");
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(value));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(shouldLock, repository.Alerts.Any(alert => alert.Status == AlertStatus.Active));
    }

    [Fact]
    public void EnterMeasurement_CreatesSpecLock_WhenNoControlLimitsExist()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.ControlLimits.Clear();
        SetRuleSet(repository, "Diameter", "SpecLimitOnly");
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(6m));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(RuleTriggered.SpecLimitViolation, alert.RuleTriggered);
        Assert.Equal(AlertStatus.Active, alert.Status);
        Assert.Contains("above the upper specification limit", alert.Detail);
    }

    [Fact]
    public void FailInspection_UsesOperatorReasonAsAlertSolution()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        var measurement = service.EnterMeasurement(Entry(6m));
        var alert = Assert.Single(repository.Alerts);

        var failed = service.FailInspection(new FailInspectionRequest(
            "J100",
            "P100",
            "MOLD",
            10,
            "PRESS1",
            "In Process",
            "operator1",
            null,
            [measurement.Value!.ClientRecordId ?? ""],
            alert.Id,
            "Operator stopped inspection after jaw height failed.",
            "Machine", "Jaw height failed.", "Operator stopped inspection after jaw height failed."));

        Assert.True(failed.Succeeded, string.Join(" | ", failed.Errors));
        var audit = Assert.Single(repository.AlertOverrides);
        Assert.Equal("Operator stopped inspection after jaw height failed.", audit.SolutionText);
        var topIssue = Assert.Single(new HistoryIssueSummaryService(repository).TopIssues(new HistoryIssueSummaryRequest("P100", null, null, null, "Diameter", null, null)));
        Assert.Equal("Operator stopped inspection after jaw height failed.", topIssue.LatestSolution);
    }

    [Fact]
    public void FailInspection_StoresCauseDetailsAndFailureDetail()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        var measurement = service.EnterMeasurement(Entry(6m) with { ClientRecordId = "sample-1", DeviceId = "browser-dev" });
        var alert = Assert.Single(repository.Alerts);

        var failed = service.FailInspection(new FailInspectionRequest(
            "J100",
            "P100",
            "MOLD",
            10,
            "PRESS1",
            "In Process",
            "operator1",
            123,
            ["sample-1"],
            alert.Id,
            "Changed cutoff setup.",
            "Tooling",
            "Fixture moved during inspection.",
            "Reset fixture and started a fresh inspection."));

        Assert.True(failed.Succeeded, string.Join(" | ", failed.Errors));
        Assert.Equal("Tooling", failed.Value!.CauseCategory);
        Assert.Equal("Fixture moved during inspection.", failed.Value.CauseText);
        Assert.Equal("Reset fixture and started a fresh inspection.", failed.Value.SolutionText);
        Assert.Contains("above the upper specification limit", failed.Value.FailureDetail);
        var audit = Assert.Single(repository.AlertOverrides);
        Assert.Equal("Tooling", audit.CauseCategory);
        Assert.Equal("Fixture moved during inspection.", audit.CauseText);
        Assert.Equal("Reset fixture and started a fresh inspection.", audit.SolutionText);
    }

    [Fact]
    public void EnterMeasurement_UsesSelectedPhasePlanSpecs_ForSpecLimitValidation()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var characteristic = repository.Characteristics.Single(item => item.Name == "Diameter");
        repository.InspectionPlans.Add(new InspectionPlan
        {
            CharacteristicId = characteristic.Id,
            InspectionPhase = "Setup",
            Nominal = 2m,
            Lsl = 1m,
            Usl = 3m,
            SampleSize = 1,
            DisplayOrder = 1,
            AlertRuleSet = "SpecLimitOnly",
            Frequency = new InspectionFrequency { Type = FrequencyType.Event, Value = 1, Unit = FrequencyUnit.StartOfJob }
        });
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(2m) with { InspectionPhase = "Setup" });

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Empty(repository.Alerts);
        Assert.Empty(repository.RuleViolations);
    }

    [Fact]
    public void EnterMeasurement_RequiresUserErrorClearBeforeDraftCorrection()
    {
        var repository = RepositoryWithSecurityAndLimits();
        SetRuleSet(repository, "Diameter", "SpecLimitOnly");
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var bad = service.EnterMeasurement(Entry(6m) with { DeviceId = "browser-dev", ClientRecordId = "slot-1" });
        var blocked = service.EnterMeasurement(Entry(5m, minutes: 1) with { DeviceId = "browser-dev", ClientRecordId = "slot-1" });
        Assert.False(blocked.Succeeded);
        var clear = OverrideService(repository).Override(new AlertOverrideRequest(repository.Alerts.Single().Id, "linetech1", "linetech1", "", "", null, DateTimeOffset.UtcNow, CauseCategory: "User Error"));
        Assert.True(clear.Succeeded);
        var corrected = service.EnterMeasurement(Entry(5m, minutes: 1) with { DeviceId = "browser-dev", ClientRecordId = "slot-1" });

        Assert.True(bad.Succeeded, string.Join(" | ", bad.Errors));
        Assert.True(corrected.Succeeded, string.Join(" | ", corrected.Errors));
        Assert.Single(repository.Measurements);
        Assert.Equal(5m, repository.Measurements.Single().Value);
        Assert.Empty(repository.Alerts);
        Assert.DoesNotContain(repository.Alerts, alert => alert.Status == AlertStatus.Active);
    }

    [Fact]
    public void EnterMeasurement_DoesNotAllowDraftCorrection_WhenMeasurementIsAlreadyCompleted()
    {
        var repository = RepositoryWithSecurityAndLimits();
        SetRuleSet(repository, "Diameter", "SpecLimitOnly");
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var bad = service.EnterMeasurement(Entry(6m) with { DeviceId = "browser-dev", ClientRecordId = "slot-1" });
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            InspectionPhase = "In Process",
            CompletionNumber = 1,
            CompletedByUserId = "operator1",
            CompletedAt = DateTimeOffset.Parse("2026-01-01T00:02:00Z")
        });
        repository.JobPhaseCompletions.Single().MeasurementIds.Add(bad.Value!.Id);

        var corrected = service.EnterMeasurement(Entry(5m, minutes: 1) with { DeviceId = "browser-dev", ClientRecordId = "slot-1" });

        Assert.False(corrected.Succeeded);
        Assert.Contains(corrected.Errors, error => error.Contains("active lock", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(6m, repository.Measurements.Single().Value);
        Assert.Single(repository.Alerts);
    }

    [Fact]
    public void EnterMeasurement_NormalizesMoreThanFiveDecimalPlaces()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(5.123456m));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(5.12346m, result.Value!.Value);
    }

    [Fact]
    public void EnterMeasurement_Succeeds_WhenDriftRuleHasNoControlSpread()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var limit = repository.ControlLimits.First(item =>
            item.PartNum == "P100" &&
            item.ProcessCode == "MOLD" &&
            item.OperationSeq == 10 &&
            item.CharacteristicName == "Diameter");
        limit.CenterLine = 5m;
        limit.Lcl = 5m;
        limit.Ucl = 5m;
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(5m));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Empty(repository.Alerts);
        Assert.Empty(repository.RuleViolations);
    }

    [Fact]
    public void EnterMeasurement_UsesGlobalRuleSet_WhenPlanUsesGlobalDefault()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.Settings.GlobalAlertRuleSet = "SpecLimitOnly";
        SetRuleSet(repository, "Diameter", "GlobalDefault");
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(6m));

        Assert.True(result.Succeeded);
        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(RuleTriggered.SpecLimitViolation, alert.RuleTriggered);
    }

    [Fact]
    public void EnterMeasurement_DoesNotCreateMeasuredAlert_WhenNoAutomaticRuleIsSelected()
    {
        var repository = RepositoryWithSecurityAndLimits();
        SetRuleSet(repository, "Diameter", "None");
        var limit = repository.ControlLimits.First(item =>
            item.PartNum == "P100" &&
            item.ProcessCode == "MOLD" &&
            item.OperationSeq == 10 &&
            item.CharacteristicName == "Diameter");
        limit.CenterLine = 5m;
        limit.Lcl = 4.7m;
        limit.Ucl = 5.3m;
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(5.4m));

        Assert.True(result.Succeeded);
        Assert.Empty(repository.Alerts);
    }

    [Fact]
    public void EnterMeasurement_CreatesSpecLock_WhenNoAutomaticRuleIsSelected()
    {
        var repository = RepositoryWithSecurityAndLimits();
        SetRuleSet(repository, "Diameter", "None");
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(6m));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(RuleTriggered.SpecLimitViolation, alert.RuleTriggered);
        Assert.Equal(AlertStatus.Active, alert.Status);
        Assert.Contains("above the upper specification limit", alert.Detail);
    }

    [Fact]
    public void EnterMeasurement_CreatesNelsonTrendAlert_WhenNelsonRulesAreSelected()
    {
        var repository = RepositoryWithSecurityAndLimits();
        SetRuleSet(repository, "Diameter", "NelsonRules");
        AddCompletedMeasurement(repository, "Diameter", 5m, -10);
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        foreach (var (value, index) in new[] { 5.01m, 5.02m, 5.03m, 5.04m, 5.05m, 5.06m }.Select((value, index) => (value, index)))
        {
            service.EnterMeasurement(Entry(value, index));
        }

        var alerts = repository.Alerts.Where(alert => alert.RuleTriggered == RuleTriggered.NelsonTrend).ToArray();
        Assert.NotEmpty(alerts);
        Assert.All(alerts, alert => Assert.Equal(AlertStatus.Warning, alert.Status));
    }

    [Fact]
    public void EnterMeasurement_AddsDetailToOutOfControlDriftAlert()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var limit = repository.ControlLimits.First(item =>
            item.PartNum == "P100" &&
            item.ProcessCode == "MOLD" &&
            item.OperationSeq == 10 &&
            item.CharacteristicName == "Diameter");
        limit.CenterLine = 5m;
        limit.Lcl = 4.7m;
        limit.Ucl = 5.3m;
        AddCompletedMeasurement(repository, "Diameter", 5m, -10);
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        service.EnterMeasurement(Entry(5.4m));

        var alert = repository.Alerts.Single(alert => alert.RuleTriggered == RuleTriggered.OnePointBeyondControlLimit);
        Assert.Equal(AlertStatus.Warning, alert.Status);
        Assert.Contains("prior measurements", alert.Detail);
        Assert.Contains("Control limits", alert.Detail);
        Assert.Contains("5.4", alert.Detail);
    }

    [Fact]
    public void EnterMeasurement_DoesNotCreateMultiPointDriftAlert_DuringFirstInspectionRun()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var limit = repository.ControlLimits.First(item =>
            item.PartNum == "P100" &&
            item.ProcessCode == "MOLD" &&
            item.OperationSeq == 10 &&
            item.CharacteristicName == "Diameter");
        limit.CenterLine = 5m;
        limit.Lcl = 4.7m;
        limit.Ucl = 5.3m;
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var first = service.EnterMeasurement(Entry(5.24m));
        var second = service.EnterMeasurement(Entry(5.24m, 1));
        var third = service.EnterMeasurement(Entry(5.24m, 2));

        Assert.True(first.Succeeded, string.Join(" | ", first.Errors));
        Assert.True(second.Succeeded, string.Join(" | ", second.Errors));
        Assert.True(third.Succeeded, string.Join(" | ", third.Errors));
        Assert.Empty(repository.Alerts);
        Assert.Empty(repository.RuleViolations);
    }

    [Theory]
    [InlineData("Cusum", RuleTriggered.CusumShift, new[] { "5.30", "5.30", "5.30", "5.30", "5.30", "5.30", "5.30", "5.30", "5.30", "5.30", "5.30", "5.30", "5.30" })]
    [InlineData("Ewma", RuleTriggered.EwmaShift, new[] { "5.80", "5.80", "5.80" })]
    [InlineData("MovingAverageTrend", RuleTriggered.MovingAverageTrend, new[] { "5.40", "5.40", "5.40", "5.40", "5.40" })]
    [InlineData("LinearTrendSlope", RuleTriggered.LinearTrendSlope, new[] { "4.80", "5.00", "5.20", "5.40", "5.60", "5.80" })]
    public void EnterMeasurement_CreatesExpectedAlert_ForSelectedDriftRule(string ruleSet, RuleTriggered expectedRule, string[] values)
    {
        var repository = RepositoryWithSecurityAndLimits();
        SetRuleSet(repository, "Diameter", ruleSet);
        WidenSpecLimits(repository, "Diameter", 0m, 10m);
        AddCompletedMeasurement(repository, "Diameter", 5m, -10);
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        foreach (var (text, index) in values.Select((text, index) => (text, index)))
        {
            service.EnterMeasurement(Entry(decimal.Parse(text), index));
        }

        var alerts = repository.Alerts.Where(alert => alert.RuleTriggered == expectedRule).ToArray();
        Assert.NotEmpty(alerts);
        Assert.All(alerts, alert => Assert.Equal(AlertStatus.Warning, alert.Status));
    }

    [Fact]
    public void EnterMeasurement_RejectsUnknownInspectionTarget()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(5m) with { CharacteristicName = "Unknown" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("No configured inspection characteristic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnterMeasurement_ReturnsExistingMeasurement_WhenOfflineRecordIsRetried()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        var entry = Entry(10m) with
        {
            DeviceId = "tablet-press1",
            ClientRecordId = "measurement-001",
            SubmittedAt = DateTimeOffset.Parse("2026-01-01T00:05:00Z")
        };

        var first = service.EnterMeasurement(entry);
        var retry = service.EnterMeasurement(entry);

        Assert.True(first.Succeeded);
        Assert.True(retry.Succeeded);
        Assert.Single(repository.Measurements);
        Assert.Equal(first.Value!.Id, retry.Value!.Id);
        Assert.Equal("tablet-press1", retry.Value.DeviceId);
        Assert.Equal("measurement-001", retry.Value.ClientRecordId);
    }

    [Fact]
    public void EnterMeasurement_UpdatesExistingMeasurement_WhenSameSampleSlotChanges()
    {
        var repository = RepositoryWithSecurityAndLimits();
        SetRuleSet(repository, "Diameter", "None");
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        var entry = Entry(5m) with
        {
            DeviceId = "tablet-press1",
            ClientRecordId = "measurement-001",
            SubmittedAt = DateTimeOffset.Parse("2026-01-01T00:05:00Z")
        };

        var first = service.EnterMeasurement(entry);
        var update = service.EnterMeasurement(entry with
        {
            Value = 5.25m,
            Timestamp = DateTimeOffset.Parse("2026-01-01T00:06:00Z"),
            SubmittedAt = DateTimeOffset.Parse("2026-01-01T00:06:00Z")
        });

        Assert.True(first.Succeeded);
        Assert.True(update.Succeeded, string.Join(" | ", update.Errors));
        Assert.Single(repository.Measurements);
        Assert.Equal(first.Value!.Id, update.Value!.Id);
        Assert.Equal(5.25m, repository.Measurements.Single().Value);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:06:00Z"), repository.Measurements.Single().Timestamp);
    }

    [Fact]
    public void CompleteInspection_SavesMachineCounterOnLatestCompletion()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        AddCompletedInspection(repository);

        var result = service.CompleteInspection(CompletionRequest(12345));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(12345, repository.JobPhaseCompletions.Single().MachineCounter);
    }

    [Fact]
    public void CompleteInspection_CreatesCompletionFromSavedMeasurements_WhenAutoCompletionWasMissed()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.JobPhaseCompletions.Clear();
        repository.Measurements.Add(SavedMeasurement("Diameter", 10m, 1));
        repository.Measurements.Add(SavedMeasurement("Length", 42m, 2));
        repository.Measurements.Add(SavedMeasurement("Weight", 18m, 3));
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.CompleteInspection(CompletionRequest(12345));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var completion = Assert.Single(repository.JobPhaseCompletions);
        Assert.Equal(12345, completion.MachineCounter);
        Assert.Equal(3, completion.MeasurementIds.Count);
        Assert.Equal("operator1", completion.CompletedByUserId);
    }

    [Fact]
    public void CompleteInspection_RequiresOneAttributeDisposition_WhenTemplateSampleSizeIsHigher()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.JobPhaseCompletions.Clear();
        AddAttributeCharacteristic(repository);
        var attribute = repository.Characteristics.Single(characteristic => characteristic.Name == "Comparator profile");
        repository.InspectionPlans.Single(plan => plan.CharacteristicId == attribute.Id).SampleSize = 4;
        repository.Measurements.Add(SavedMeasurement("Diameter", 10m, 1));
        repository.Measurements.Add(SavedMeasurement("Length", 42m, 2));
        repository.Measurements.Add(SavedMeasurement("Weight", 18m, 3));
        repository.Measurements.Add(SavedMeasurement("Comparator profile", 1m, 4));
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.CompleteInspection(CompletionRequest(12345));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var completion = Assert.Single(repository.JobPhaseCompletions);
        Assert.Equal(4, completion.MeasurementIds.Count);
        Assert.Contains(repository.Measurements.Single(measurement => measurement.CharacteristicName == "Comparator profile").Id, completion.MeasurementIds);
    }

    [Fact]
    public void CompleteInspection_UsesMeasurementClientRecordIds_WhenQueuedCompletionSyncsLater()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.JobPhaseCompletions.Clear();
        var firstMeasurements = new[]
        {
            SavedMeasurementWithClientId("Diameter", 10m, 1, "first-diameter"),
            SavedMeasurementWithClientId("Length", 42m, 2, "first-length"),
            SavedMeasurementWithClientId("Weight", 18m, 3, "first-weight")
        };
        var secondMeasurements = new[]
        {
            SavedMeasurementWithClientId("Diameter", 10m, 11, "second-diameter"),
            SavedMeasurementWithClientId("Length", 42m, 12, "second-length"),
            SavedMeasurementWithClientId("Weight", 18m, 13, "second-weight")
        };
        repository.Measurements.AddRange(firstMeasurements);
        repository.Measurements.AddRange(secondMeasurements);
        var firstCompletion = AddCompletion(repository, firstMeasurements, 1);
        var secondCompletion = AddCompletion(repository, secondMeasurements, 2);
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.CompleteInspection(CompletionRequest(11111) with
        {
            MeasurementClientRecordIds = ["first-diameter", "first-length", "first-weight"]
        });

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(firstCompletion.Id, result.Value!.Id);
        Assert.Equal(11111, firstCompletion.MachineCounter);
        Assert.Null(secondCompletion.MachineCounter);
    }

    [Fact]
    public void CompleteInspection_CreatesNextCompletion_WhenClientRecordIdsAreNew()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.JobPhaseCompletions.Clear();
        var firstMeasurements = new[]
        {
            SavedMeasurementWithClientId("Diameter", 10m, 1, "first-diameter"),
            SavedMeasurementWithClientId("Length", 42m, 2, "first-length"),
            SavedMeasurementWithClientId("Weight", 18m, 3, "first-weight")
        };
        var secondMeasurements = new[]
        {
            SavedMeasurementWithClientId("Diameter", 10m, 11, "second-diameter"),
            SavedMeasurementWithClientId("Length", 42m, 12, "second-length"),
            SavedMeasurementWithClientId("Weight", 18m, 13, "second-weight")
        };
        repository.Measurements.AddRange(firstMeasurements);
        repository.Measurements.AddRange(secondMeasurements);
        var firstCompletion = AddCompletion(repository, firstMeasurements, 1);
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.CompleteInspection(CompletionRequest(22222) with
        {
            MeasurementClientRecordIds = ["second-diameter", "second-length", "second-weight"]
        });

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(2, repository.JobPhaseCompletions.Count);
        Assert.Equal(1, firstCompletion.CompletionNumber);
        Assert.Null(firstCompletion.MachineCounter);
        Assert.Equal(2, result.Value!.CompletionNumber);
        Assert.Equal(22222, result.Value.MachineCounter);
        Assert.All(secondMeasurements, measurement => Assert.Contains(measurement.Id, result.Value.MeasurementIds));
    }

    [Theory]
    [InlineData("Setup")]
    [InlineData("Startup")]
    [InlineData("In Process")]
    [InlineData("Coil Change")]
    [InlineData("Spool")]
    [InlineData("End of Spool")]
    public void CompleteInspection_NumbersNextCompletionWithinSameOperationAndPhase_ForEveryInspectionPhase(string phase)
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.JobPhaseCompletions.Clear();
        foreach (var plan in repository.InspectionPlans)
        {
            plan.InspectionPhase = phase;
        }

        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS2",
            InspectionPhase = phase,
            CompletionNumber = 3,
            CompletedByUserId = "operator1",
            CompletedAt = DateTimeOffset.Parse("2026-01-01T00:10:00Z")
        });
        var currentMeasurements = new[]
        {
            SavedMeasurementWithClientId("Diameter", 10m, 21, "current-diameter", phase),
            SavedMeasurementWithClientId("Length", 42m, 22, "current-length", phase),
            SavedMeasurementWithClientId("Weight", 18m, 23, "current-weight", phase)
        };
        repository.Measurements.AddRange(currentMeasurements);
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.CompleteInspection(CompletionRequest(44444) with
        {
            InspectionPhase = phase,
            MeasurementClientRecordIds = ["current-diameter", "current-length", "current-weight"]
        });

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(4, result.Value!.CompletionNumber);
        Assert.Equal(44444, result.Value.MachineCounter);
    }

    [Fact]
    public void CompleteInspection_StartsSeparateCompletionSequenceForDifferentOperation()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.JobPhaseCompletions.Clear();
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "OTHER",
            OperationSeq = 20,
            ResourceId = "PRESS2",
            InspectionPhase = "In Process",
            CompletionNumber = 8,
            CompletedByUserId = "operator1",
            CompletedAt = DateTimeOffset.Parse("2026-01-01T00:10:00Z")
        });
        var currentMeasurements = new[]
        {
            SavedMeasurementWithClientId("Diameter", 10m, 21, "current-diameter"),
            SavedMeasurementWithClientId("Length", 42m, 22, "current-length"),
            SavedMeasurementWithClientId("Weight", 18m, 23, "current-weight")
        };
        repository.Measurements.AddRange(currentMeasurements);
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.CompleteInspection(CompletionRequest(44444) with
        {
            MeasurementClientRecordIds = ["current-diameter", "current-length", "current-weight"]
        });

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(1, result.Value!.CompletionNumber);
    }

    [Theory]
    [InlineData("Setup")]
    [InlineData("Startup")]
    [InlineData("In Process")]
    [InlineData("Coil Change")]
    [InlineData("Spool")]
    [InlineData("End of Spool")]
    public void CompleteInspection_UsesClientRecordIdsEvenWhenEntryOrderChanged_ForEveryInspectionPhase(string phase)
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.JobPhaseCompletions.Clear();
        foreach (var plan in repository.InspectionPlans)
        {
            plan.InspectionPhase = phase;
        }

        var currentMeasurements = new[]
        {
            SavedMeasurementWithClientId("Diameter", 10m, 3, "current-diameter", phase),
            SavedMeasurementWithClientId("Length", 42m, 1, "current-length", phase),
            SavedMeasurementWithClientId("Weight", 18m, 2, "current-weight", phase)
        };
        repository.Measurements.AddRange(currentMeasurements);
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.CompleteInspection(CompletionRequest(33333) with
        {
            InspectionPhase = phase,
            MeasurementClientRecordIds = ["current-diameter", "current-length", "current-weight"]
        });

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var completion = Assert.Single(repository.JobPhaseCompletions);
        Assert.Equal(phase, completion.InspectionPhase);
        Assert.Equal(33333, completion.MachineCounter);
        Assert.All(currentMeasurements, measurement => Assert.Contains(measurement.Id, completion.MeasurementIds));
    }

    [Fact]
    public void CompleteInspection_RequiresSelectedPhasePlansRegardlessOfMachineCounter()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.JobPhaseCompletions.Clear();
        foreach (var plan in repository.InspectionPlans)
        {
            var characteristic = repository.Characteristics.Single(item => item.Id == plan.CharacteristicId);
            plan.Frequency = characteristic.Name == "Diameter"
                ? new InspectionFrequency { Type = FrequencyType.Quantity, Value = 10000, Unit = FrequencyUnit.Pieces, FirstDueValue = 5000 }
                : new InspectionFrequency { Type = FrequencyType.Quantity, Value = 10000, Unit = FrequencyUnit.Pieces };
        }

        repository.Measurements.Add(SavedMeasurement("Diameter", 10m, 1));
        repository.Measurements.Add(SavedMeasurement("Length", 42m, 2));
        repository.Measurements.Add(SavedMeasurement("Weight", 18m, 3));
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.CompleteInspection(CompletionRequest(5125));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var completion = Assert.Single(repository.JobPhaseCompletions);
        Assert.Equal(3, completion.MeasurementIds.Count);
    }

    [Fact]
    public void CompleteInspection_DoesNotCountOtherPhasePlansAsInProcessRequirements()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.JobPhaseCompletions.Clear();
        foreach (var characteristic in repository.Characteristics)
        {
            repository.InspectionPlans.Add(new InspectionPlan
            {
                CharacteristicId = characteristic.Id,
                InspectionPhase = "Coil Change",
                SampleSize = 10,
                AlertRuleSet = "WesternElectric",
                Frequency = new InspectionFrequency { Type = FrequencyType.Event, Value = 1, Unit = FrequencyUnit.Pieces }
            });
        }

        repository.Measurements.Add(SavedMeasurement("Diameter", 10m, 1));
        repository.Measurements.Add(SavedMeasurement("Length", 42m, 2));
        repository.Measurements.Add(SavedMeasurement("Weight", 18m, 3));
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.CompleteInspection(CompletionRequest(12345));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var completion = Assert.Single(repository.JobPhaseCompletions);
        Assert.Equal("In Process", completion.InspectionPhase);
        Assert.Equal(3, completion.MeasurementIds.Count);
    }

    [Fact]
    public void CompleteInspection_FailsWhenSavedMeasurementsAreIncomplete()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.JobPhaseCompletions.Clear();
        repository.Measurements.Add(SavedMeasurement("Diameter", 10m, 1));
        repository.Measurements.Add(SavedMeasurement("Length", 42m, 2));
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.CompleteInspection(CompletionRequest(12345));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("No completed inspection was found", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(repository.JobPhaseCompletions);
    }

    [Fact]
    public void CompleteInspection_RequiresMachineCounter()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        AddCompletedInspection(repository);

        var result = service.CompleteInspection(CompletionRequest(null));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("Machine Counter is required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnterMeasurement_RejectsClientRecordIdReusedForDifferentInspectionSlot()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        var entry = Entry(10m) with
        {
            DeviceId = "tablet-press1",
            ClientRecordId = "measurement-001"
        };

        var first = service.EnterMeasurement(entry);
        var reused = service.EnterMeasurement(entry with { CharacteristicName = "Length" });

        Assert.True(first.Succeeded);
        Assert.False(reused.Succeeded);
        Assert.Contains(reused.Errors, error => error.Contains("different inspection sample", StringComparison.OrdinalIgnoreCase));
        Assert.Single(repository.Measurements);
    }

    [Fact]
    public void EnterMeasurement_CreatesJobForOperatorEnteredJobNumber()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.Jobs.Clear();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m) with { JobNum = "J200" });

        Assert.True(result.Succeeded);
        var job = Assert.Single(repository.Jobs);
        Assert.Equal("J200", job.JobNum);
        Assert.Equal("P100", job.PartNum);
    }

    [Fact]
    public void EnterMeasurement_AllowsCoilChangeInspectionPhase()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m) with { InspectionPhase = "Coil Change" });

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal("Coil Change", Assert.Single(repository.Measurements).InspectionPhase);
    }

    [Fact]
    public void EnterMeasurement_AllowsEndOfSpoolInspectionPhase()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m) with { InspectionPhase = "Spool End" });

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal("End of Spool", Assert.Single(repository.Measurements).InspectionPhase);
    }

    [Fact]
    public void EnterMeasurement_RejectsJobAlreadyAssignedToDifferentPart()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.Jobs.Clear();
        repository.Jobs.Add(new Job { JobNum = "J200", PartNum = "P999" });
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m) with { JobNum = "J200" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("already assigned", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(repository.Measurements);
    }

    [Fact]
    public void EnterMeasurement_AllowsLineTechToInspect()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m) with { OperatorUserId = "linetech1" });

        Assert.True(result.Succeeded);
        Assert.Equal("linetech1", repository.Measurements.Single().OperatorUserId);
    }

    [Fact]
    public void EnterMeasurement_StoresInspectionPhase()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m) with { InspectionPhase = "Startup" });

        Assert.True(result.Succeeded);
        Assert.Equal("Startup", repository.Measurements.Single().InspectionPhase);
    }

    [Fact]
    public void EnterMeasurement_AllowsQaInspectionWithoutProductGroupAssignment()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.Parts.Single(part => part.PartNum == "P100").ProductGroup = "Needles";
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m) with { OperatorUserId = "qa1" });

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Single(repository.Measurements);
    }

    [Fact]
    public void EnterMeasurement_AllowsArchonInspectionAcrossProductGroups()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.Parts.Single(part => part.PartNum == "P100").ProductGroup = "Needles";
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m) with { OperatorUserId = "Archon" });

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Single(repository.Measurements);
    }

    [Fact]
    public void EnterMeasurement_RejectsOperatorOutsideCertifiedProductGroup()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.Parts.Single(part => part.PartNum == "P100").ProductGroup = "Needles";
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(10m));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("product group", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(repository.Measurements);
    }

    [Fact]
    public void EnterMeasurement_CreatesLock_WhenAttributeIsRejected()
    {
        var repository = RepositoryWithSecurityAndLimits();
        AddAttributeCharacteristic(repository);
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var result = service.EnterMeasurement(Entry(0m) with { CharacteristicName = "Comparator profile" });
        var locked = service.EnterMeasurement(Entry(1m, minutes: 1) with { CharacteristicName = "Comparator profile" });

        Assert.True(result.Succeeded);
        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(RuleTriggered.AttributeRejected, alert.RuleTriggered);
        Assert.False(locked.Succeeded);
        Assert.Contains(locked.Errors, error => error.Contains("locked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnterMeasurement_AllowsImmediateCorrectionAfterDraftLockClear()
    {
        var repository = RepositoryWithSecurityAndLimits();
        AddAttributeCharacteristic(repository);
        var measurementService = new InspectionMeasurementService(repository, new WesternElectricRuleService());

        var failed = measurementService.EnterMeasurement(Entry(0m) with
        {
            CharacteristicName = "Comparator profile",
            DeviceId = "browser-dev",
            ClientRecordId = "attribute-sample-1"
        });
        var alert = Assert.Single(repository.Alerts);
        var overrideResult = OverrideService(repository).Override(new AlertOverrideRequest(alert.Id, "linetech1", "linetech1", "User entry error", "Corrected measurement", null, DateTimeOffset.UtcNow, CauseCategory: "User Error"));
        Assert.True(overrideResult.Succeeded, string.Join(" | ", overrideResult.Errors));
        var corrected = measurementService.EnterMeasurement(Entry(1m, minutes: 1) with
        {
            CharacteristicName = "Comparator profile",
            DeviceId = "browser-dev",
            ClientRecordId = "attribute-sample-1"
        });

        Assert.True(failed.Succeeded, string.Join(" | ", failed.Errors));
        Assert.True(corrected.Succeeded, string.Join(" | ", corrected.Errors));
        Assert.Single(repository.Measurements);
        Assert.Equal(1m, repository.Measurements.Single().Value);
        Assert.Empty(repository.Alerts);
        Assert.Empty(repository.AlertOverrides);
    }

    [Fact]
    public void Override_AllowsOperatorToClearTheirOwnUserError()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(alert.Id, "operator1", "operator1", "Cause", "Fix", null, DateTimeOffset.UtcNow, CauseCategory: "User Error"));

        Assert.True(result.Succeeded);
        Assert.Empty(repository.Alerts);
        Assert.Empty(repository.FailedInspections);
        Assert.Empty(repository.AlertOverrides);
    }

    [Fact]
    public void Override_OperatorCannotClearAnotherOperatorsEntryMistake()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        alert.OperatorUserId = "someone-else";
        var result = OverrideService(repository).Override(new AlertOverrideRequest(alert.Id, "operator1", "operator1",
            "", "", null, DateTimeOffset.UtcNow, CauseCategory: "User Error"));
        Assert.False(result.Succeeded);
        Assert.Equal(AlertStatus.Active, alert.Status);
    }

    [Theory]
    [InlineData("operator1", "operator1", false)]
    [InlineData("linetech1", "wrong", false)]
    [InlineData("linetech1", "linetech1", true)]
    [InlineData("qa1", "qa1", true)]
    public void FailureSignoffRequiresAuthorizedCredentials(string user, string password, bool allowed)
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        var reading = service.EnterMeasurement(Entry(6m));
        var alert = Assert.Single(repository.Alerts);
        var result = service.FailInspectionWithSignoff(new FailInspectionRequest("J100", "P100", "MOLD", 10,
            "PRESS1", "In Process", "operator1", 3000, ActiveAlertId: alert.Id, CauseCategory: "Machine",
            CauseText: "Fixture moved", SolutionText: "Repaired fixture", SignOffUserName: user, SignOffPassword: password));
        Assert.Equal(allowed, result.Succeeded);
        Assert.Equal(allowed ? 1 : 0, repository.FailedInspections.Count);
        Assert.Equal("operator1", reading.Value!.OperatorUserId);
        if (allowed) Assert.Equal(user, result.Value!.FailedByUserId);
        else Assert.Equal(AlertStatus.Active, alert.Status);
    }

    [Fact]
    public void OperatorAssignedLineTechAccessCanSignOffFailure()
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.Users.Single(u => u.UserName == "operator1").Roles.Add(repository.Roles.Single(r => r.Name == RoleNames.LineTech));
        var result = new InspectionMeasurementService(repository, new WesternElectricRuleService())
            .FailInspectionWithSignoff(new FailInspectionRequest("J100", "P100", "MOLD", 10, "PRESS1", "In Process",
                "operator1", 3000, CauseCategory: "Machine", CauseText: "Fixture moved", SolutionText: "Repaired fixture",
                SignOffUserName: "operator1", SignOffPassword: "operator1"));
        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void Override_AllowsQaUserErrorWithoutFailureOrClearEvent()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(alert.Id, "qa1", "qa1", "", "", null, DateTimeOffset.UtcNow, CauseCategory: "User Error"));

        Assert.True(result.Succeeded);
        Assert.Empty(repository.Alerts);
        Assert.Empty(repository.AlertOverrides);
        Assert.Empty(repository.FailedInspections);
        Assert.Equal("User Error", result.Value!.CauseCategory);
    }

    [Fact]
    public void Override_AllowsLineTechWithoutBypassReason()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(alert.Id, "linetech1", "linetech1", "", "", null, DateTimeOffset.UtcNow, CauseCategory: "User Error"));

        Assert.True(result.Succeeded);
        Assert.Empty(repository.Alerts);
        Assert.Equal(RoleNames.LineTech, result.Value!.OverrideRole);
    }

    [Fact]
    public void Override_RejectsArchonWithoutUserErrorCategory()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(alert.Id, "Archon", "archon", "", "", null, DateTimeOffset.UtcNow));

        Assert.False(result.Succeeded);
        Assert.Equal(AlertStatus.Active, alert.Status);
    }

    [Fact]
    public void Override_RejectsArchonMachineBypass()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(
            alert.Id,
            "Archon",
            "archon",
            "",
            "",
            "System manager bypass for test entry.",
            DateTimeOffset.UtcNow,
            CauseCategory: "Machine"));

        Assert.False(result.Succeeded);
        Assert.Equal(AlertStatus.Active, alert.Status);
        Assert.Empty(repository.AlertOverrides);
    }

    [Fact]
    public void Override_AllowsArchonSystemManagerNormalWorkflow()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(alert.Id, "Archon", "archon", "", "", null, DateTimeOffset.UtcNow, CauseCategory: "User Error"));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Empty(repository.Alerts);
        Assert.Equal("System Manager", result.Value!.OverrideRole);
        Assert.Equal("User Error", result.Value.CauseCategory);
        Assert.Null(result.Value.WhyStandardProcessWasBypassed);
    }

    [Fact]
    public void Override_RejectsInvalidCredentials()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(alert.Id, "qa1", "wrong", "", "", null, DateTimeOffset.UtcNow, CauseCategory: "User Error"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("Invalid override credentials", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Override_RejectsMachineClearOnEveryRetry()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);
        var request = new AlertOverrideRequest(
            alert.Id,
            "qa1",
            "qa1",
            "Tool wear",
            "Changed tool",
            null,
            DateTimeOffset.Parse("2026-01-01T00:10:00Z"),
            "tablet-qa1",
            "override-001",
            DateTimeOffset.Parse("2026-01-01T00:11:00Z"), CauseCategory: "Machine");

        var first = service.Override(request);
        var retry = service.Override(request);

        Assert.False(first.Succeeded);
        Assert.False(retry.Succeeded);
        Assert.Empty(repository.AlertOverrides);
        Assert.Equal(AlertStatus.Active, alert.Status);
    }

    [Fact]
    public void Override_RejectsToolingClear()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(
            alert.Id,
            "qa1",
            "qa1",
            "Tool wear",
            "Changed tool",
            null,
            DateTimeOffset.UtcNow,
            CauseCategory: "Tooling"));

        Assert.False(result.Succeeded);
        Assert.Equal(AlertStatus.Active, alert.Status);
    }

    [Fact]
    public void Override_UserErrorRemovesDraftLockWithoutWritingClearEvent()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var measurement = SavedMeasurement("Diameter", 6m, 0);
        repository.Measurements.Add(measurement);
        repository.RuleViolations.Add(new RuleViolation
        {
            AlertId = alert.Id,
            RuleTriggered = alert.RuleTriggered,
            DetectedAt = alert.LockedAt,
            MeasurementIds = { measurement.Id }
        });
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(
            alert.Id,
            "linetech1",
            "linetech1",
            "",
            "",
            null,
            DateTimeOffset.UtcNow,
            CauseCategory: "User Error"));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Empty(repository.Alerts);
        Assert.Empty(repository.RuleViolations);
        Assert.Empty(repository.AlertOverrides);
    }

    [Fact]
    public void Override_RejectsUnsupportedCauseCategory()
    {
        var repository = RepositoryWithSecurityAndLimits();
        var alert = AddAlert(repository);
        var service = OverrideService(repository);

        var result = service.Override(new AlertOverrideRequest(
            alert.Id,
            "qa1",
            "qa1",
            "Tool wear",
            "Changed tool",
            null,
            DateTimeOffset.UtcNow,
            CauseCategory: "Bad Category"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("CauseCategory is not supported", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Machine")]
    [InlineData("Tooling")]
    [InlineData("Material")]
    [InlineData("Measurement Method")]
    [InlineData("Process Drift")]
    [InlineData("Other")]
    public void Override_RejectsNonUserErrorForEveryAuthorizedRole(string category)
    {
        foreach (var user in new[] { ("Archon", "archon"), ("qa1", "qa1"), ("linetech1", "linetech1") })
        {
            var repository = RepositoryWithSecurityAndLimits();
            var alert = AddAlert(repository);
            var result = OverrideService(repository).Override(new AlertOverrideRequest(alert.Id, user.Item1, user.Item2,
                "Cause", "Action", "Bypass request", DateTimeOffset.UtcNow, CauseCategory: category));
            Assert.False(result.Succeeded);
            Assert.Equal(AlertStatus.Active, alert.Status);
            Assert.Empty(repository.AlertOverrides);
            Assert.Empty(repository.FailedInspections);
        }
    }

    [Theory]
    [InlineData("Machine", true)]
    [InlineData("Tooling", true)]
    [InlineData("Material", true)]
    [InlineData("User Error", false)]
    [InlineData("Measurement Method", false)]
    [InlineData("Process Drift", false)]
    [InlineData("Other", false)]
    public void FailInspection_OnlyRecordsMachineToolingOrMaterial(string category, bool expectedSuccess)
    {
        var repository = RepositoryWithSecurityAndLimits();
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        service.EnterMeasurement(Entry(6m));
        var alert = Assert.Single(repository.Alerts);
        var result = service.FailInspection(new FailInspectionRequest("J100", "P100", "MOLD", 10, "PRESS1",
            "In Process", "operator1", 4500, ActiveAlertId: alert.Id, CauseCategory: category,
            CauseText: "Fixture moved", SolutionText: "Repaired fixture"));
        Assert.Equal(expectedSuccess, result.Succeeded);
        Assert.Equal(expectedSuccess ? 1 : 0, repository.FailedInspections.Count);
        Assert.Equal(expectedSuccess ? AlertStatus.Overridden : AlertStatus.Active, alert.Status);
        if (expectedSuccess)
        {
            Assert.Equal(category, result.Value!.CauseCategory);
            Assert.Equal("Fixture moved", result.Value.CauseText);
            Assert.Equal("Repaired fixture", result.Value.SolutionText);
            Assert.DoesNotContain(new JobHistoryService(repository).GetForJob("J100"), entry => entry.EntryType == "PhaseComplete");
        }
    }

    [Theory]
    [InlineData("Setup")]
    [InlineData("Startup")]
    [InlineData("In Process")]
    [InlineData("Coil Change")]
    [InlineData("Spool")]
    [InlineData("End of Spool")]
    public void TerminalInspections_ShareSequenceAcrossSuccessFailureAndMachines(string phase)
    {
        var repository = RepositoryWithSecurityAndLimits();
        repository.JobPhaseCompletions.Clear();
        foreach (var plan in repository.InspectionPlans) plan.InspectionPhase = phase;
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100", PartNum = "P100", ProcessCode = "MOLD", OperationSeq = 10,
            ResourceId = "PRESS2", InspectionPhase = phase, CompletionNumber = 5,
            CompletedByUserId = "operator1", CompletedAt = DateTimeOffset.Parse("2025-12-01T00:00:00Z")
        });
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        var failure = service.FailInspection(new FailInspectionRequest("J100", "P100", "MOLD", 10,
            "PRESS1", phase, "operator1", 4500, CauseCategory: "Machine",
            CauseText: "Fixture moved", SolutionText: "Repaired fixture"));
        Assert.True(failure.Succeeded, string.Join(" | ", failure.Errors));
        Assert.Equal(6, failure.Value!.FailureNumber);

        var measurements = new[]
        {
            SavedMeasurementWithClientId("Diameter", 10m, 21, "next-diameter", phase),
            SavedMeasurementWithClientId("Length", 42m, 22, "next-length", phase),
            SavedMeasurementWithClientId("Weight", 18m, 23, "next-weight", phase)
        };
        repository.Measurements.AddRange(measurements);
        var completion = service.CompleteInspection(CompletionRequest(9000) with
        {
            InspectionPhase = phase,
            MeasurementClientRecordIds = ["next-diameter", "next-length", "next-weight"]
        });
        Assert.True(completion.Succeeded, string.Join(" | ", completion.Errors));
        Assert.Equal(7, completion.Value!.CompletionNumber);
        Assert.Equal(2, repository.JobPhaseCompletions.Count);
        Assert.Single(repository.FailedInspections);
    }

    private static InMemorySpcRepository RepositoryWithSecurityAndLimits()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        TestSeedData.SeedUsers(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        repository.ControlLimits.Add(new ControlLimitSet
        {
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            CharacteristicName = "Diameter",
            CenterLine = 10m,
            Lcl = 7m,
            Ucl = 13m
        });
        return repository;
    }

    private static InspectionMeasurementEntry Entry(decimal value, int minutes = 0)
    {
        return new InspectionMeasurementEntry(
            "J100",
            "P100",
            "MOLD",
            10,
            "PRESS1",
            "Diameter",
            value,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddMinutes(minutes),
            "operator1");
    }

    private static CompleteInspectionRequest CompletionRequest(long? machineCounter)
    {
        return new CompleteInspectionRequest(
            "J100",
            "P100",
            "MOLD",
            10,
            "PRESS1",
            "In Process",
            machineCounter);
    }

    private static void AddCompletedInspection(InMemorySpcRepository repository)
    {
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            InspectionPhase = "In Process",
            CompletionNumber = 1,
            CompletedByUserId = "operator1",
            CompletedAt = DateTimeOffset.Parse("2026-01-01T00:02:00Z")
        });
    }

    private static JobPhaseCompletion AddCompletion(InMemorySpcRepository repository, IReadOnlyList<InspectionMeasurement> measurements, int completionNumber)
    {
        var latestMeasurement = measurements.OrderByDescending(item => item.Timestamp).First();
        var completion = new JobPhaseCompletion
        {
            JobNum = latestMeasurement.JobNum,
            PartNum = latestMeasurement.PartNum,
            ProcessCode = latestMeasurement.ProcessCode,
            OperationSeq = latestMeasurement.OperationSeq,
            ResourceId = latestMeasurement.ResourceId,
            InspectionPhase = latestMeasurement.InspectionPhase,
            CompletionNumber = completionNumber,
            CompletedByUserId = latestMeasurement.OperatorUserId,
            OperatorShift = latestMeasurement.OperatorShift,
            CompletedAt = latestMeasurement.Timestamp
        };
        completion.MeasurementIds.AddRange(measurements.Select(item => item.Id));
        repository.JobPhaseCompletions.Add(completion);
        return completion;
    }

    private static void AddCompletedMeasurement(InMemorySpcRepository repository, string characteristicName, decimal value, int minutes)
    {
        var measurement = SavedMeasurement(characteristicName, value, minutes);
        repository.Measurements.Add(measurement);
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion
        {
            JobNum = measurement.JobNum,
            PartNum = measurement.PartNum,
            ProcessCode = measurement.ProcessCode,
            OperationSeq = measurement.OperationSeq,
            ResourceId = measurement.ResourceId,
            InspectionPhase = measurement.InspectionPhase,
            CompletionNumber = 1,
            CompletedByUserId = measurement.OperatorUserId,
            CompletedAt = measurement.Timestamp.AddMinutes(1),
            MeasurementIds = { measurement.Id }
        });
    }

    private static InspectionMeasurement SavedMeasurement(string characteristicName, decimal value, int minutes)
    {
        return new InspectionMeasurement
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            CharacteristicName = characteristicName,
            InspectionPhase = "In Process",
            Value = value,
            Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddMinutes(minutes),
            OperatorUserId = "operator1",
            OperatorShift = "1st Half Days",
            SubmittedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddMinutes(minutes)
        };
    }

    private static InspectionMeasurement SavedMeasurementWithClientId(string characteristicName, decimal value, int minutes, string clientRecordId, string inspectionPhase = "In Process")
    {
        var measurement = SavedMeasurement(characteristicName, value, minutes);
        measurement.DeviceId = "browser-dev";
        measurement.ClientRecordId = clientRecordId;
        measurement.InspectionPhase = inspectionPhase;
        return measurement;
    }

    private static void AddAttributeCharacteristic(InMemorySpcRepository repository)
    {
        var part = repository.Parts.Single(part => part.PartNum == "P100");
        var process = repository.Processes.Single(process => process.ProcessCode == "MOLD");
        var operation = repository.Operations.Single(operation =>
            operation.PartId == part.Id &&
            operation.ProcessId == process.Id &&
            operation.OperationSeq == 10);
        var characteristic = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Comparator profile",
            Type = CharacteristicType.Attribute,
            UnitOfMeasure = "Accept/Reject",
        };
        repository.Characteristics.Add(characteristic);
        repository.SpecLimits.Add(new SpecLimit { CharacteristicId = characteristic.Id, Nominal = 1m, Lsl = 0m, Usl = 1m });
        repository.InspectionPlans.Add(new InspectionPlan
        {
            CharacteristicId = characteristic.Id,
            SampleSize = 1,
            AlertRuleSet = "WesternElectric",
            Frequency = new InspectionFrequency { Type = FrequencyType.Quantity, Value = 10000, Unit = FrequencyUnit.Pieces }
        });
    }

    private static void SetRuleSet(InMemorySpcRepository repository, string characteristicName, string ruleSet)
    {
        var characteristic = repository.Characteristics.Single(characteristic => characteristic.Name == characteristicName);
        var plan = repository.InspectionPlans.Single(plan => plan.CharacteristicId == characteristic.Id);
        plan.AlertRuleSet = ruleSet;
    }

    private static void WidenSpecLimits(InMemorySpcRepository repository, string characteristicName, decimal lsl, decimal usl)
    {
        var characteristic = repository.Characteristics.Single(characteristic => characteristic.Name == characteristicName);
        foreach (var plan in repository.InspectionPlans.Where(plan => plan.CharacteristicId == characteristic.Id))
        {
            plan.Lsl = lsl;
            plan.Usl = usl;
        }

        foreach (var spec in repository.SpecLimits.Where(spec => spec.CharacteristicId == characteristic.Id))
        {
            spec.Lsl = lsl;
            spec.Usl = usl;
        }
    }

    private static ProcessAlert AddAlert(InMemorySpcRepository repository)
    {
        var alert = new ProcessAlert
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            OperatorUserId = "operator1",
            RuleTriggered = RuleTriggered.OnePointBeyondControlLimit,
            LockedAt = DateTimeOffset.UtcNow
        };
        repository.Alerts.Add(alert);
        return alert;
    }

    private static AlertOverrideService OverrideService(InMemorySpcRepository repository)
    {
        return new AlertOverrideService(repository, new PermissionService(repository), new CredentialService(repository));
    }
}

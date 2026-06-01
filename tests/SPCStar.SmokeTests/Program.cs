using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;

var tests = new (string Name, Action Run)[]
{
    ("setup import rejects invalid limits", SetupImportRejectsInvalidLimits),
    ("setup import upserts", SetupImportUpserts),
    ("setup query returns seeded inspection plan", SetupQueryReturnsSeededInspectionPlan),
    ("western electric detects beyond limit", WesternElectricDetectsBeyondLimit),
    ("measurement rejects unknown inspection target", MeasurementRejectsUnknownInspectionTarget),
    ("measurement creates lock alert", MeasurementCreatesLockAlert),
    ("override rejects operator", OverrideRejectsOperator),
    ("override allows QA", OverrideAllowsQa),
    ("override rejects bad credentials", OverrideRejectsBadCredentials),
    ("GOD override requires bypass reason", GodOverrideRequiresBypassReason),
    ("QA export requires characteristic", QaExportRequiresCharacteristic),
    ("QA export calculates summary CSV", QaExportCalculatesSummaryCsv),
    ("material change validates required fields", MaterialChangeValidatesRequiredFields),
    ("material change stores lot change", MaterialChangeStoresLotChange),
    ("frequency service detects overdue time inspection", FrequencyDetectsOverdueTimeInspection),
    ("frequency service detects event due", FrequencyDetectsEventDue),
    ("chart service returns points and limits", ChartServiceReturnsPointsAndLimits),
    ("chart service marks rule violations", ChartServiceMarksRuleViolations),
    ("history export writes inspection csv", HistoryExportWritesInspectionCsv),
    ("history export writes alert csv", HistoryExportWritesAlertCsv),
    ("history export writes material csv", HistoryExportWritesMaterialCsv)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Failures:");
    foreach (var failure in failures)
    {
        Console.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine();
Console.WriteLine($"{tests.Length} smoke tests passed.");
return 0;

static void SetupImportRejectsInvalidLimits()
{
    var repository = new InMemorySpcRepository();
    var result = new SetupImportService(repository).ImportCsv(ValidCsv(lsl: "10", usl: "5"));
    AssertFalse(result.Succeeded);
    AssertTrue(repository.Parts.Count == 0);
}

static void SetupImportUpserts()
{
    var repository = new InMemorySpcRepository();
    var service = new SetupImportService(repository);
    AssertTrue(service.ImportCsv(ValidCsv(description: "Original", sampleSize: "1")).Succeeded);
    AssertTrue(service.ImportCsv(ValidCsv(description: "Updated", sampleSize: "3")).Succeeded);
    AssertTrue(repository.Parts.Count == 1);
    AssertEqual("Updated", repository.Parts.Single().Description);
    AssertEqual(3, repository.InspectionPlans.Single().SampleSize);
}

static void SetupQueryReturnsSeededInspectionPlan()
{
    var repository = new InMemorySpcRepository();
    SeedData.SeedAll(repository);
    var service = new SetupQueryService(repository);

    var parts = service.GetParts();
    var plans = service.GetInspectionPlans("P100");

    AssertTrue(parts.Any(part => part.PartNum == "P100"));
    AssertTrue(plans.Any(plan =>
        plan.PartNum == "P100" &&
        plan.ProcessCode == "MOLD" &&
        plan.OperationSeq == 10 &&
        plan.CharacteristicName == "Diameter"));
}

static void WesternElectricDetectsBeyondLimit()
{
    var points = new[]
    {
        Point(10m, 0),
        Point(13.5m, 1)
    };
    var result = new WesternElectricRuleService().Detect(points, 10m, 7m, 13m);
    AssertTrue(result.Any(item => item.RuleTriggered == RuleTriggered.OnePointBeyondControlLimit));
}

static void MeasurementCreatesLockAlert()
{
    var repository = RepositoryWithSecurityAndLimits();
    var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
    var result = service.EnterMeasurement(Entry(13.5m));
    var locked = service.EnterMeasurement(Entry(10m, 1));
    AssertTrue(result.Succeeded);
    AssertTrue(repository.Alerts.Count == 1);
    AssertFalse(locked.Succeeded);
}

static void MeasurementRejectsUnknownInspectionTarget()
{
    var repository = RepositoryWithSecurityAndLimits();
    var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
    var result = service.EnterMeasurement(Entry(5m) with { CharacteristicName = "Unknown" });
    AssertFalse(result.Succeeded);
}

static void OverrideRejectsOperator()
{
    var repository = RepositoryWithSecurityAndLimits();
    var alert = AddAlert(repository);
    var result = OverrideService(repository)
        .Override(new AlertOverrideRequest(alert.Id, "operator1", "operator1", "Cause", "Fix", null, DateTimeOffset.UtcNow));
    AssertFalse(result.Succeeded);
}

static void OverrideAllowsQa()
{
    var repository = RepositoryWithSecurityAndLimits();
    var alert = AddAlert(repository);
    var result = OverrideService(repository)
        .Override(new AlertOverrideRequest(alert.Id, "qa1", "qa1", "Tool wear", "Changed tool", null, DateTimeOffset.UtcNow));
    AssertTrue(result.Succeeded);
    AssertEqual(AlertStatus.Overridden, alert.Status);
}

static void OverrideRejectsBadCredentials()
{
    var repository = RepositoryWithSecurityAndLimits();
    var alert = AddAlert(repository);
    var result = OverrideService(repository)
        .Override(new AlertOverrideRequest(alert.Id, "qa1", "wrong", "Tool wear", "Changed tool", null, DateTimeOffset.UtcNow));
    AssertFalse(result.Succeeded);
}

static void GodOverrideRequiresBypassReason()
{
    var repository = RepositoryWithSecurityAndLimits();
    var alert = AddAlert(repository);
    var result = OverrideService(repository)
        .Override(new AlertOverrideRequest(alert.Id, "god1", "god1", "Emergency", "Released", null, DateTimeOffset.UtcNow));
    AssertFalse(result.Succeeded);
}

static void QaExportRequiresCharacteristic()
{
    var result = new QaSummaryExportService(new InMemorySpcRepository())
        .BuildSummary(new QaSummaryExportRequest([], [], [], null, null));
    AssertFalse(result.Succeeded);
}

static void QaExportCalculatesSummaryCsv()
{
    var result = new QaSummaryExportService(RepositoryWithMeasurements())
        .ExportCsv(new QaSummaryExportRequest(["P100"], ["J100"], ["Diameter"], null, null));
    AssertTrue(result.Succeeded);
    AssertTrue(result.Value is not null && result.Value.Contains("P100,J100,Diameter,Mean,5,5,4.9,5.1", StringComparison.Ordinal));
}

static void MaterialChangeValidatesRequiredFields()
{
    var result = new MaterialChangeLogService(new InMemorySpcRepository()).Record(new MaterialChangeLogEntry(
        "",
        "P100",
        "RESIN-A",
        "LOT1",
        "LOT2",
        null,
        "PRESS1",
        "operator1",
        DateTimeOffset.UtcNow,
        "Material change"));
    AssertFalse(result.Succeeded);
}

static void MaterialChangeStoresLotChange()
{
    var repository = new InMemorySpcRepository();
    var result = new MaterialChangeLogService(repository).Record(new MaterialChangeLogEntry(
        "J100",
        "P100",
        "RESIN-A",
        "LOT1",
        "LOT2",
        500m,
        "PRESS1",
        "operator1",
        DateTimeOffset.UtcNow,
        "Loaded next lot"));
    AssertTrue(result.Succeeded);
    AssertEqual("LOT2", repository.MaterialChanges.Single().NewLotNum);
}

static void FrequencyDetectsOverdueTimeInspection()
{
    var repository = RepositoryWithPlan(FrequencyType.Time, 30, FrequencyUnit.Minutes);
    repository.Measurements.Add(Measurement(5m, 0));
    var result = new InspectionFrequencyService(repository).Evaluate(FrequencyRequest(now: Now(45)));
    AssertEqual(InspectionDueStatus.Overdue, result.Status);
}

static void FrequencyDetectsEventDue()
{
    var repository = RepositoryWithPlan(FrequencyType.Event, 1, FrequencyUnit.MaterialChange);
    repository.Measurements.Add(Measurement(5m, 0));
    var result = new InspectionFrequencyService(repository).Evaluate(FrequencyRequest(
        events:
        [
            new InspectionFrequencyEvent(FrequencyUnit.MaterialChange, Now(10))
        ]));
    AssertEqual(InspectionDueStatus.DueNow, result.Status);
}

static void ChartServiceReturnsPointsAndLimits()
{
    var repository = RepositoryWithMeasurements();
    repository.ControlLimits.Add(new ControlLimitSet
    {
        PartNum = "P100",
        ProcessCode = "MOLD",
        OperationSeq = 10,
        CharacteristicName = "Diameter",
        CenterLine = 5m,
        Lcl = 4m,
        Ucl = 6m
    });

    var result = new ChartDataService(repository).Build(new ChartDataRequest(
        ChartType.IndividualsMovingRange,
        "J100",
        "P100",
        "PRESS1",
        "Diameter",
        null,
        null));

    AssertEqual(3, result.Points.Count);
    AssertEqual(5m, result.Mean);
    AssertEqual(4m, result.LowerControlLimit);
    AssertEqual(6m, result.UpperControlLimit);
    AssertEqual(4.5m, result.LowerSpecLimit);
    AssertEqual(5.5m, result.UpperSpecLimit);
}

static void ChartServiceMarksRuleViolations()
{
    var repository = RepositoryWithMeasurements();
    var alert = AddAlert(repository);
    var violation = new RuleViolation
    {
        AlertId = alert.Id,
        RuleTriggered = RuleTriggered.OnePointBeyondControlLimit,
        DetectedAt = alert.LockedAt
    };
    violation.MeasurementIds.Add(repository.Measurements.Last().Id);
    repository.RuleViolations.Add(violation);

    var result = new ChartDataService(repository).Build(new ChartDataRequest(
        ChartType.Run,
        "J100",
        "P100",
        "PRESS1",
        "Diameter",
        null,
        null));

    AssertTrue(result.Points.Last().HasRuleViolation);
}

static void HistoryExportWritesInspectionCsv()
{
    var repository = RepositoryWithMeasurements();
    var csv = new HistoryExportService(repository)
        .ExportInspectionCsv(new InspectionHistoryExportRequest([], ["J100"], [], ["Diameter"], null, null));
    AssertTrue(csv.Contains("JobNum,PartNum,ProcessCode,OperationSeq,ResourceID,CharacteristicName,MeasurementValue,Timestamp,OperatorUserID", StringComparison.Ordinal));
    AssertTrue(csv.Contains("J100,P100,MOLD,10,PRESS1,Diameter", StringComparison.Ordinal));
}

static void HistoryExportWritesAlertCsv()
{
    var repository = RepositoryWithMeasurements();
    var alert = AddAlert(repository);
    alert.Status = AlertStatus.Overridden;
    var csv = new HistoryExportService(repository)
        .ExportAlertHistoryCsv(new AlertHistoryExportRequest([], [], [], [], null, null, IncludeOverridden: true));
    AssertTrue(csv.Contains("RuleTriggered", StringComparison.Ordinal));
    AssertTrue(csv.Contains("Overridden", StringComparison.Ordinal));
}

static void HistoryExportWritesMaterialCsv()
{
    var repository = new InMemorySpcRepository();
    repository.MaterialChanges.Add(new MaterialChangeLog
    {
        JobNum = "J100",
        PartNum = "P100",
        MaterialPartNum = "RESIN-A",
        OldLotNum = "LOT1",
        NewLotNum = "LOT2",
        QuantityLoaded = 500m,
        ResourceId = "PRESS1",
        OperatorUserId = "operator1",
        Timestamp = Now(0),
        Reason = "Loaded next lot"
    });
    var csv = new HistoryExportService(repository)
        .ExportMaterialChangeHistoryCsv(new MaterialHistoryExportRequest(["P100"], ["J100"], ["PRESS1"], null, null));
    AssertTrue(csv.Contains("RESIN-A,LOT1,LOT2", StringComparison.Ordinal));
}

static InMemorySpcRepository RepositoryWithSecurityAndLimits()
{
    var repository = new InMemorySpcRepository();
    SeedData.SeedSecurity(repository);
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

static InMemorySpcRepository RepositoryWithMeasurements()
{
    var repository = new InMemorySpcRepository();
    var part = new Part { PartNum = "P100", Description = "Widget" };
    var process = new ManufacturingProcess { ProcessCode = "MOLD", Description = "Molding" };
    var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
    var characteristic = new Characteristic
    {
        OperationId = operation.Id,
        Name = "Diameter",
        Type = CharacteristicType.Variable,
        UnitOfMeasure = "mm",
        IsRequiredForCoa = true
    };
    repository.Parts.Add(part);
    repository.Processes.Add(process);
    repository.Operations.Add(operation);
    repository.Characteristics.Add(characteristic);
    repository.SpecLimits.Add(new SpecLimit { CharacteristicId = characteristic.Id, Nominal = 5m, Lsl = 4.5m, Usl = 5.5m });
    repository.Measurements.AddRange([Measurement(4.9m, 0), Measurement(5.0m, 1), Measurement(5.1m, 2)]);
    return repository;
}

static InMemorySpcRepository RepositoryWithPlan(FrequencyType type, int value, FrequencyUnit unit)
{
    var repository = RepositoryWithMeasurements();
    var characteristic = repository.Characteristics.Single();
    repository.InspectionPlans.Add(new InspectionPlan
    {
        CharacteristicId = characteristic.Id,
        SampleSize = 1,
        AlertRuleSet = "WesternElectric",
        Frequency = new InspectionFrequency { Type = type, Value = value, Unit = unit }
    });
    repository.Measurements.Clear();
    return repository;
}

static InspectionFrequencyCheckRequest FrequencyRequest(
    DateTimeOffset? now = null,
    int? currentQuantity = null,
    int? quantityAtLastInspection = null,
    IReadOnlyCollection<InspectionFrequencyEvent>? events = null)
{
    return new InspectionFrequencyCheckRequest(
        "J100",
        "P100",
        "MOLD",
        10,
        "Diameter",
        "PRESS1",
        now ?? Now(0),
        currentQuantity,
        quantityAtLastInspection,
        events ?? []);
}

static ProcessAlert AddAlert(InMemorySpcRepository repository)
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

static AlertOverrideService OverrideService(InMemorySpcRepository repository)
{
    return new AlertOverrideService(repository, new PermissionService(repository), new CredentialService(repository));
}

static InspectionMeasurementEntry Entry(decimal value, int minutes = 0)
{
    return new InspectionMeasurementEntry("J100", "P100", "MOLD", 10, "PRESS1", "Diameter", value, Now(minutes), "operator1");
}

static InspectionMeasurement Measurement(decimal value, int minutes)
{
    return new InspectionMeasurement
    {
        JobNum = "J100",
        PartNum = "P100",
        ProcessCode = "MOLD",
        OperationSeq = 10,
        ResourceId = "PRESS1",
        CharacteristicName = "Diameter",
        Value = value,
        Timestamp = Now(minutes),
        OperatorUserId = "operator1"
    };
}

static WesternElectricPoint Point(decimal value, int minutes)
{
    return new WesternElectricPoint(Guid.NewGuid(), value, Now(minutes));
}

static DateTimeOffset Now(int minutes)
{
    return DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddMinutes(minutes);
}

static string ValidCsv(string description = "Widget", string lsl = "4.5", string usl = "5.5", string sampleSize = "1")
{
    return string.Join(Environment.NewLine, [
        "RowType,PartNum,PartDescription,ProductGroup,InspectionPhase,Operation,FieldName,MaterialName,MaterialPartNum,CharacteristicName,CharacteristicType,Nominal,LSL,USL,LCL,UCL,UnitOfMeasure,SampleSize,FrequencyType,FrequencyValue,FrequencyUnit,AlertRuleSet,IsRequiredForCOA,COAStatistic,IsRequired,DisplayOrder",
        $"Variable,P100,{description},General,In Process,MOLD,,,,Diameter,Variable,5.0,{lsl},{usl},,,mm,{sampleSize},Time,30,Minutes,WesternElectric,true,Mean,,",
        string.Empty
    ]);
}

static void AssertTrue(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void AssertFalse(bool condition)
{
    if (condition)
    {
        throw new InvalidOperationException("Expected false.");
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;

var tests = new (string Name, Action Run)[]
{
    ("setup import rejects invalid limits", SetupImportRejectsInvalidLimits),
    ("setup import upserts", SetupImportUpserts),
    ("western electric detects beyond limit", WesternElectricDetectsBeyondLimit),
    ("measurement creates lock alert", MeasurementCreatesLockAlert),
    ("override rejects operator", OverrideRejectsOperator),
    ("override allows QA", OverrideAllowsQa),
    ("GOD override requires bypass reason", GodOverrideRequiresBypassReason),
    ("QA export requires characteristic", QaExportRequiresCharacteristic),
    ("QA export calculates summary CSV", QaExportCalculatesSummaryCsv),
    ("material change validates required fields", MaterialChangeValidatesRequiredFields),
    ("material change stores lot change", MaterialChangeStoresLotChange)
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

static void OverrideRejectsOperator()
{
    var repository = RepositoryWithSecurityAndLimits();
    var alert = AddAlert(repository);
    var result = new AlertOverrideService(repository, new PermissionService(repository))
        .Override(new AlertOverrideRequest(alert.Id, "operator1", "Cause", "Fix", null, DateTimeOffset.UtcNow));
    AssertFalse(result.Succeeded);
}

static void OverrideAllowsQa()
{
    var repository = RepositoryWithSecurityAndLimits();
    var alert = AddAlert(repository);
    var result = new AlertOverrideService(repository, new PermissionService(repository))
        .Override(new AlertOverrideRequest(alert.Id, "qa1", "Tool wear", "Changed tool", null, DateTimeOffset.UtcNow));
    AssertTrue(result.Succeeded);
    AssertEqual(AlertStatus.Overridden, alert.Status);
}

static void GodOverrideRequiresBypassReason()
{
    var repository = RepositoryWithSecurityAndLimits();
    var alert = AddAlert(repository);
    var result = new AlertOverrideService(repository, new PermissionService(repository))
        .Override(new AlertOverrideRequest(alert.Id, "god1", "Emergency", "Released", null, DateTimeOffset.UtcNow));
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
    AssertTrue(result.Value is not null && result.Value.Contains("P100,J100,Diameter,5,4.9,5.1", StringComparison.Ordinal));
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

static InMemorySpcRepository RepositoryWithSecurityAndLimits()
{
    var repository = new InMemorySpcRepository();
    SeedData.SeedSecurity(repository);
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
        "PartNum,PartDescription,ProcessCode,ProcessDescription,OperationSeq,CharacteristicName,CharacteristicType,Nominal,LSL,USL,UnitOfMeasure,SampleSize,FrequencyType,FrequencyValue,FrequencyUnit,AlertRuleSet,IsRequiredForCOA",
        $"P100,{description},MOLD,Molding,10,Diameter,Variable,5.0,{lsl},{usl},mm,{sampleSize},Time,30,Minutes,WesternElectric,true",
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

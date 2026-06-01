namespace SPCStar.Core.Domain;

public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string UserName { get; set; }
    public required string PasswordHash { get; set; }
    public required string PasswordSalt { get; set; }
    public List<Role> Roles { get; } = [];
    public List<string> ProductGroups { get; } = [];
}

public sealed class Role
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public HashSet<string> Permissions { get; } = [];
}

public sealed class Part
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PartNum { get; set; }
    public required string Description { get; set; }
    public string ProductGroup { get; set; } = "General";
}

public sealed class ManufacturingProcess
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ProcessCode { get; set; }
    public required string Description { get; set; }
}

public sealed class Operation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PartId { get; set; }
    public Guid ProcessId { get; set; }
    public int OperationSeq { get; set; }
}

public sealed class Characteristic
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid OperationId { get; set; }
    public required string Name { get; set; }
    public CharacteristicType Type { get; set; }
    public required string UnitOfMeasure { get; set; }
    public bool IsRequiredForCoa { get; set; }
    public CoaStatisticType CoaStatisticType { get; set; } = CoaStatisticType.Mean;
}

public sealed class SpecLimit
{
    public Guid CharacteristicId { get; set; }
    public decimal Nominal { get; set; }
    public decimal Lsl { get; set; }
    public decimal Usl { get; set; }
}

public sealed class InspectionPlan
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CharacteristicId { get; set; }
    public string InspectionPhase { get; set; } = "In Process";
    public int SampleSize { get; set; }
    public required string AlertRuleSet { get; set; }
    public InspectionFrequency Frequency { get; set; } = new();
}

public sealed class AppSettings
{
    public string GlobalAlertRuleSet { get; set; } = "WesternElectric";
    public CustomDriftRuleSettings CustomDriftRule { get; set; } = new();
}

public sealed class CustomDriftRuleSettings
{
    public string Name { get; set; } = "Custom Drift Rule";
    public int WindowSize { get; set; } = 4;
    public decimal SigmaThreshold { get; set; } = 1m;
    public int MinimumPointsBeyondThreshold { get; set; } = 4;
    public string Direction { get; set; } = "SameSide";
    public bool IncludeWesternElectric { get; set; }
    public string WarningBehavior { get; set; } = "Lock";
    public string Notes { get; set; } = "Triggers when the configured number of recent points are beyond the configured sigma threshold.";
}

public sealed class InspectionFrequency
{
    public FrequencyType Type { get; set; }
    public int Value { get; set; }
    public FrequencyUnit Unit { get; set; }
}

public sealed class Job
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string JobNum { get; set; }
    public required string PartNum { get; set; }
}

public sealed class JobNote
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string JobNum { get; set; }
    public required string PartNum { get; set; }
    public required string ResourceId { get; set; }
    public required string OperatorUserId { get; set; }
    public required string NoteText { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public sealed class JobTag
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string JobNum { get; set; }
    public required string PartNum { get; set; }
    public required string ResourceId { get; set; }
    public required string TagName { get; set; }
    public required string TagValue { get; set; }
    public required string OperatorUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PartJobDataField
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PartId { get; set; }
    public string InspectionPhase { get; set; } = "In Process";
    public required string FieldName { get; set; }
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class PartMaterialField
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PartId { get; set; }
    public string InspectionPhase { get; set; } = "In Process";
    public required string MaterialName { get; set; }
    public required string MaterialPartNum { get; set; }
    public string MaterialDescription { get; set; } = "";
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class ResourceMachine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ResourceId { get; set; }
    public string? Description { get; set; }
}

public sealed class Device
{
    public required string DeviceId { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}

public sealed class InspectionMeasurement
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? ClientRecordId { get; set; }
    public string? DeviceId { get; set; }
    public required string JobNum { get; set; }
    public required string PartNum { get; set; }
    public required string ProcessCode { get; set; }
    public int OperationSeq { get; set; }
    public required string ResourceId { get; set; }
    public required string CharacteristicName { get; set; }
    public string InspectionPhase { get; set; } = "In Process";
    public decimal Value { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string OperatorUserId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
}

public sealed class MeasurementEditAudit
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MeasurementId { get; set; }
    public required string JobNum { get; set; }
    public required string PartNum { get; set; }
    public required string ResourceId { get; set; }
    public required string CharacteristicName { get; set; }
    public decimal OldValue { get; set; }
    public decimal NewValue { get; set; }
    public required string OldInspectionPhase { get; set; }
    public required string NewInspectionPhase { get; set; }
    public required string EditedByUserId { get; set; }
    public DateTimeOffset EditedAt { get; set; }
}

public sealed class MaterialChangeLog
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? ClientRecordId { get; set; }
    public string? DeviceId { get; set; }
    public required string JobNum { get; set; }
    public required string PartNum { get; set; }
    public required string MaterialPartNum { get; set; }
    public required string OldLotNum { get; set; }
    public required string NewLotNum { get; set; }
    public decimal? QuantityLoaded { get; set; }
    public required string ResourceId { get; set; }
    public required string OperatorUserId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
}

public sealed class ControlLimitSet
{
    public required string PartNum { get; set; }
    public required string ProcessCode { get; set; }
    public int OperationSeq { get; set; }
    public required string CharacteristicName { get; set; }
    public decimal CenterLine { get; set; }
    public decimal Lcl { get; set; }
    public decimal Ucl { get; set; }
}

public sealed class RuleViolation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AlertId { get; set; }
    public RuleTriggered RuleTriggered { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public List<Guid> MeasurementIds { get; } = [];
}

public sealed class ProcessAlert
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string JobNum { get; set; }
    public required string PartNum { get; set; }
    public required string ResourceId { get; set; }
    public required string CharacteristicName { get; set; }
    public required string OperatorUserId { get; set; }
    public RuleTriggered RuleTriggered { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset LockedAt { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Active;
}

public sealed class AlertOverride
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? ClientRecordId { get; set; }
    public string? DeviceId { get; set; }
    public Guid AlertId { get; set; }
    public required string OperatorUserId { get; set; }
    public required string OverrideUserId { get; set; }
    public required string OverrideRole { get; set; }
    public required string JobNum { get; set; }
    public required string PartNum { get; set; }
    public required string ResourceId { get; set; }
    public required string CharacteristicName { get; set; }
    public RuleTriggered RuleTriggered { get; set; }
    public string CauseCategory { get; set; } = "Unspecified";
    public required string CauseText { get; set; }
    public required string SolutionText { get; set; }
    public string? WhyStandardProcessWasBypassed { get; set; }
    public DateTimeOffset LockedAt { get; set; }
    public DateTimeOffset UnlockedAt { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
}

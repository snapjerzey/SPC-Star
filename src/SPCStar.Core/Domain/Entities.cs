namespace SPCStar.Core.Domain;

public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string UserName { get; set; }
    public required string PasswordHash { get; set; }
    public required string PasswordSalt { get; set; }
    public string Shift { get; set; } = "";
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
    public string ProductGroup { get; set; } = "General Production";
    public string BlankCode { get; set; } = "";
    public string HoleSize { get; set; } = "";
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
    public string Location { get; set; } = "";
    public string InspectionMethod { get; set; } = "";
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
    public decimal? Nominal { get; set; }
    public decimal? Lsl { get; set; }
    public decimal? Usl { get; set; }
    public int SampleSize { get; set; }
    public int DisplayOrder { get; set; }
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
    public CustomWatchOptions Options { get; set; } = new();
    public string Name { get; set; } = "Custom Drift Rule";
    public int WindowSize { get; set; } = 4;
    public decimal SigmaThreshold { get; set; } = 1m;
    public int MinimumPointsBeyondThreshold { get; set; } = 4;
    public string Direction { get; set; } = "SameSide";
    public bool IncludeWesternElectric { get; set; }
    public string WarningBehavior { get; set; } = "Warning";
    public string Notes { get; set; } = "Uses consecutive completed inspection averages moving toward a specification limit. Individual samples determine out-of-spec lockouts.";
}

public sealed class CustomWatchOptions
{
    public bool Enabled { get; set; } = true;
    public bool TrendEnabled { get; set; } = true;
    public bool NearLimitEnabled { get; set; } = true;
    public bool RepeatedNearLimitEnabled { get; set; }
    public string ZoneMode { get; set; } = "PercentSpan";
    public decimal ZonePercent { get; set; } = 10m;
    public decimal ZoneDistance { get; set; }
    public int RepeatedCount { get; set; } = 3;
    public int RepeatedWindow { get; set; } = 5;
    public string Combination { get; set; } = "Any";
    public bool ResetOnEqual { get; set; } = true;
    public bool ResetOnReversal { get; set; } = true;
    public int RecoveryInspections { get; set; } = 2;
}

public sealed class InspectionFrequency
{
    public FrequencyType Type { get; set; }
    public int Value { get; set; }
    public FrequencyUnit Unit { get; set; }
    public int? FirstDueValue { get; set; }
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
    public List<JobNoteReply> Replies { get; set; } = [];
}

public sealed class JobNoteReply
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ClientRequestId { get; init; }
    public required string UserName { get; init; }
    public required string ReplyText { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

public sealed class JobPhaseCompletion
{
    public bool WatchEvaluated { get; set; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string JobNum { get; set; }
    public required string PartNum { get; set; }
    public required string ProcessCode { get; set; }
    public int OperationSeq { get; set; }
    public required string ResourceId { get; set; }
    public required string InspectionPhase { get; set; }
    public int CompletionNumber { get; set; } = 1;
    public required string CompletedByUserId { get; set; }
    public string OperatorShift { get; set; } = "";
    public DateTimeOffset CompletedAt { get; set; }
    public long? MachineCounter { get; set; }
    [System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Populate)]
    public List<Guid> MeasurementIds { get; } = [];
}

public sealed class FailedInspection
{
    [System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Populate)]
    public List<string> RequiredCharacteristicNames { get; } = [];
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string JobNum { get; set; }
    public required string PartNum { get; set; }
    public required string ProcessCode { get; set; }
    public int OperationSeq { get; set; }
    public required string ResourceId { get; set; }
    public required string InspectionPhase { get; set; }
    public int FailureNumber { get; set; } = 1;
    public required string FailedByUserId { get; set; }
    public string OperatorShift { get; set; } = "";
    public DateTimeOffset FailedAt { get; set; }
    public long? MachineCounter { get; set; }
    public string Reason { get; set; } = "";
    public string CauseCategory { get; set; } = "";
    public string CauseText { get; set; } = "";
    public string SolutionText { get; set; } = "";
    public string FailureDetail { get; set; } = "";
    public Guid? AlertId { get; set; }
    [System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Populate)]
    public List<Guid> MeasurementIds { get; } = [];
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
    public string? Instruction { get; set; }
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
    public string DeviceProfile { get; set; } = "keyboard";
    public int SerialBaudRate { get; set; } = 9600;
    public List<string> ProductGroups { get; } = [];
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
    public string WatchReason { get; set; } = "";
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
    public int? GagePinNumber { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string OperatorUserId { get; set; }
    public string OperatorShift { get; set; } = "";
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
    public string Reason { get; set; } = "";
}

public sealed class MachineCounterEditAudit
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompletionId { get; set; }
    public required string JobNum { get; set; }
    public required string PartNum { get; set; }
    public required string ResourceId { get; set; }
    public required string ProcessCode { get; set; }
    public int OperationSeq { get; set; }
    public required string InspectionPhase { get; set; }
    public long? OldMachineCounter { get; set; }
    public long? NewMachineCounter { get; set; }
    public required string EditedByUserId { get; set; }
    public DateTimeOffset EditedAt { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class MaterialChangeLog
{
    public string MaterialName { get; set; } = "";
    public string MaterialDescription { get; set; } = "";
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
    [System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Populate)]
    public List<Guid> MeasurementIds { get; } = [];
}

public sealed class ProcessAlert
{
    public string WatchScope { get; set; } = "";
    public Guid? LastWatchCompletionId { get; set; }
    public int NormalWatchInspections { get; set; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string JobNum { get; set; }
    public required string PartNum { get; set; }
    public required string ResourceId { get; set; }
    public required string CharacteristicName { get; set; }
    public required string OperatorUserId { get; set; }
    public string OperatorShift { get; set; } = "";
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

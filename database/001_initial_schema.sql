CREATE TABLE Users (
    Id TEXT PRIMARY KEY,
    UserName TEXT NOT NULL UNIQUE
);

CREATE TABLE Roles (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL UNIQUE
);

CREATE TABLE Permissions (
    Name TEXT PRIMARY KEY
);

CREATE TABLE UserRoles (
    UserId TEXT NOT NULL,
    RoleId TEXT NOT NULL,
    PRIMARY KEY (UserId, RoleId),
    FOREIGN KEY (UserId) REFERENCES Users(Id),
    FOREIGN KEY (RoleId) REFERENCES Roles(Id)
);

CREATE TABLE RolePermissions (
    RoleId TEXT NOT NULL,
    PermissionName TEXT NOT NULL,
    PRIMARY KEY (RoleId, PermissionName),
    FOREIGN KEY (RoleId) REFERENCES Roles(Id),
    FOREIGN KEY (PermissionName) REFERENCES Permissions(Name)
);

CREATE TABLE Parts (
    Id TEXT PRIMARY KEY,
    PartNum TEXT NOT NULL UNIQUE,
    Description TEXT NOT NULL
);

CREATE TABLE Processes (
    Id TEXT PRIMARY KEY,
    ProcessCode TEXT NOT NULL UNIQUE,
    Description TEXT NOT NULL
);

CREATE TABLE Operations (
    Id TEXT PRIMARY KEY,
    PartId TEXT NOT NULL,
    ProcessId TEXT NOT NULL,
    OperationSeq INTEGER NOT NULL,
    UNIQUE (PartId, ProcessId, OperationSeq),
    FOREIGN KEY (PartId) REFERENCES Parts(Id),
    FOREIGN KEY (ProcessId) REFERENCES Processes(Id)
);

CREATE TABLE Characteristics (
    Id TEXT PRIMARY KEY,
    OperationId TEXT NOT NULL,
    Name TEXT NOT NULL,
    Type TEXT NOT NULL,
    UnitOfMeasure TEXT NOT NULL,
    IsRequiredForCoa INTEGER NOT NULL,
    UNIQUE (OperationId, Name),
    FOREIGN KEY (OperationId) REFERENCES Operations(Id)
);

CREATE TABLE SpecLimits (
    CharacteristicId TEXT PRIMARY KEY,
    Nominal NUMERIC NOT NULL,
    Lsl NUMERIC NOT NULL,
    Usl NUMERIC NOT NULL,
    CHECK (Lsl < Usl),
    FOREIGN KEY (CharacteristicId) REFERENCES Characteristics(Id)
);

CREATE TABLE InspectionPlans (
    Id TEXT PRIMARY KEY,
    CharacteristicId TEXT NOT NULL UNIQUE,
    SampleSize INTEGER NOT NULL CHECK (SampleSize > 0),
    AlertRuleSet TEXT NOT NULL,
    FrequencyType TEXT NOT NULL,
    FrequencyValue INTEGER NOT NULL CHECK (FrequencyValue > 0),
    FrequencyUnit TEXT NOT NULL,
    FOREIGN KEY (CharacteristicId) REFERENCES Characteristics(Id)
);

CREATE TABLE Jobs (
    Id TEXT PRIMARY KEY,
    JobNum TEXT NOT NULL UNIQUE,
    PartNum TEXT NOT NULL
);

CREATE TABLE Resources (
    Id TEXT PRIMARY KEY,
    ResourceId TEXT NOT NULL UNIQUE,
    Description TEXT NULL
);

CREATE TABLE Devices (
    DeviceId TEXT PRIMARY KEY,
    DisplayName TEXT NOT NULL,
    RegisteredAt TEXT NOT NULL,
    LastSeenAt TEXT NULL
);

CREATE TABLE InspectionMeasurements (
    Id TEXT PRIMARY KEY,
    ClientRecordId TEXT NULL,
    DeviceId TEXT NULL,
    JobNum TEXT NOT NULL,
    PartNum TEXT NOT NULL,
    ProcessCode TEXT NOT NULL,
    OperationSeq INTEGER NOT NULL,
    ResourceId TEXT NOT NULL,
    CharacteristicName TEXT NOT NULL,
    Value NUMERIC NOT NULL,
    Timestamp TEXT NOT NULL,
    OperatorUserId TEXT NOT NULL,
    SubmittedAt TEXT NOT NULL,
    SyncedAt TEXT NULL,
    FOREIGN KEY (DeviceId) REFERENCES Devices(DeviceId)
);

CREATE INDEX IX_InspectionMeasurements_Search
ON InspectionMeasurements (JobNum, PartNum, ResourceId, CharacteristicName, Timestamp);

CREATE UNIQUE INDEX UX_InspectionMeasurements_DeviceClientRecord
ON InspectionMeasurements (DeviceId, ClientRecordId)
WHERE DeviceId IS NOT NULL AND ClientRecordId IS NOT NULL;

CREATE TABLE MaterialChangeLogs (
    Id TEXT PRIMARY KEY,
    ClientRecordId TEXT NULL,
    DeviceId TEXT NULL,
    JobNum TEXT NOT NULL,
    PartNum TEXT NOT NULL,
    MaterialPartNum TEXT NOT NULL,
    OldLotNum TEXT NOT NULL,
    NewLotNum TEXT NOT NULL,
    QuantityLoaded NUMERIC NULL,
    ResourceId TEXT NOT NULL,
    OperatorUserId TEXT NOT NULL,
    Timestamp TEXT NOT NULL,
    Reason TEXT NOT NULL,
    SubmittedAt TEXT NOT NULL,
    SyncedAt TEXT NULL,
    FOREIGN KEY (DeviceId) REFERENCES Devices(DeviceId)
);

CREATE UNIQUE INDEX UX_MaterialChangeLogs_DeviceClientRecord
ON MaterialChangeLogs (DeviceId, ClientRecordId)
WHERE DeviceId IS NOT NULL AND ClientRecordId IS NOT NULL;

CREATE TABLE ControlLimitSets (
    PartNum TEXT NOT NULL,
    ProcessCode TEXT NOT NULL,
    OperationSeq INTEGER NOT NULL,
    CharacteristicName TEXT NOT NULL,
    CenterLine NUMERIC NOT NULL,
    Lcl NUMERIC NOT NULL,
    Ucl NUMERIC NOT NULL,
    PRIMARY KEY (PartNum, ProcessCode, OperationSeq, CharacteristicName),
    CHECK (Lcl < CenterLine AND CenterLine < Ucl)
);

CREATE TABLE ProcessAlerts (
    Id TEXT PRIMARY KEY,
    JobNum TEXT NOT NULL,
    PartNum TEXT NOT NULL,
    ResourceId TEXT NOT NULL,
    CharacteristicName TEXT NOT NULL,
    OperatorUserId TEXT NOT NULL,
    RuleTriggered TEXT NOT NULL,
    LockedAt TEXT NOT NULL,
    Status TEXT NOT NULL
);

CREATE TABLE RuleViolations (
    Id TEXT PRIMARY KEY,
    AlertId TEXT NOT NULL,
    RuleTriggered TEXT NOT NULL,
    DetectedAt TEXT NOT NULL,
    MeasurementIdsCsv TEXT NOT NULL,
    FOREIGN KEY (AlertId) REFERENCES ProcessAlerts(Id)
);

CREATE TABLE AlertOverrides (
    Id TEXT PRIMARY KEY,
    ClientRecordId TEXT NULL,
    DeviceId TEXT NULL,
    AlertId TEXT NOT NULL,
    OperatorUserId TEXT NOT NULL,
    OverrideUserId TEXT NOT NULL,
    OverrideRole TEXT NOT NULL,
    JobNum TEXT NOT NULL,
    PartNum TEXT NOT NULL,
    ResourceId TEXT NOT NULL,
    CharacteristicName TEXT NOT NULL,
    RuleTriggered TEXT NOT NULL,
    CauseText TEXT NOT NULL,
    SolutionText TEXT NOT NULL,
    WhyStandardProcessWasBypassed TEXT NULL,
    LockedAt TEXT NOT NULL,
    UnlockedAt TEXT NOT NULL,
    SubmittedAt TEXT NOT NULL,
    SyncedAt TEXT NULL,
    FOREIGN KEY (DeviceId) REFERENCES Devices(DeviceId),
    FOREIGN KEY (AlertId) REFERENCES ProcessAlerts(Id)
);

CREATE UNIQUE INDEX UX_AlertOverrides_DeviceClientRecord
ON AlertOverrides (DeviceId, ClientRecordId)
WHERE DeviceId IS NOT NULL AND ClientRecordId IS NOT NULL;

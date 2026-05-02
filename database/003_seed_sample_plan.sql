INSERT INTO Parts (Id, PartNum, Description) VALUES
('part-p100', 'P100', 'Sample molded widget');

INSERT INTO Processes (Id, ProcessCode, Description) VALUES
('process-mold', 'MOLD', 'Injection molding');

INSERT INTO Operations (Id, PartId, ProcessId, OperationSeq) VALUES
('operation-p100-mold-10', 'part-p100', 'process-mold', 10);

INSERT INTO Characteristics (Id, OperationId, Name, Type, UnitOfMeasure, IsRequiredForCoa) VALUES
('characteristic-p100-diameter', 'operation-p100-mold-10', 'Diameter', 'Variable', 'mm', 1);

INSERT INTO SpecLimits (CharacteristicId, Nominal, Lsl, Usl) VALUES
('characteristic-p100-diameter', 5.0, 4.5, 5.5);

INSERT INTO InspectionPlans (
    Id,
    CharacteristicId,
    SampleSize,
    AlertRuleSet,
    FrequencyType,
    FrequencyValue,
    FrequencyUnit
) VALUES (
    'inspection-plan-p100-diameter',
    'characteristic-p100-diameter',
    1,
    'WesternElectric',
    'Time',
    30,
    'Minutes'
);

INSERT INTO ControlLimitSets (
    PartNum,
    ProcessCode,
    OperationSeq,
    CharacteristicName,
    CenterLine,
    Lcl,
    Ucl
) VALUES (
    'P100',
    'MOLD',
    10,
    'Diameter',
    5.0,
    4.0,
    6.0
);

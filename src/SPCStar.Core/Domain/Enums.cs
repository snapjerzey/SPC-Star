namespace SPCStar.Core.Domain;

public enum CharacteristicType
{
    Variable,
    Attribute
}

public enum FrequencyType
{
    Time,
    Quantity,
    Event
}

public enum FrequencyUnit
{
    Minutes,
    Hours,
    Pieces,
    StartOfJob,
    MaterialChange,
    ToolChange,
    Restart
}

public enum AlertStatus
{
    Active,
    Overridden
}

public enum RuleTriggered
{
    OnePointBeyondControlLimit,
    TwoOfThreeNearControlLimit,
    FourOfFiveApproachingLimit,
    EightConsecutiveOneSideOfCenterline,
    SpecLimitViolation,
    AttributeRejected
}

public enum PassFailStatus
{
    Pass,
    Fail
}

public enum InspectionDueStatus
{
    NotConfigured,
    NotDue,
    DueNow,
    Overdue,
    Completed
}

public enum ChartType
{
    IndividualsMovingRange,
    XbarR,
    Run
}

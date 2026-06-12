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
    Box,
    StartOfJob,
    MaterialChange,
    ToolChange,
    Restart,
    Shift
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
    NelsonTrend,
    CusumShift,
    EwmaShift,
    MovingAverageTrend,
    LinearTrendSlope,
    CustomRuleTriggered,
    AttributeRejected
}

public enum PassFailStatus
{
    Pass,
    Fail
}

public enum CoaStatisticType
{
    Mean,
    StandardDeviation
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

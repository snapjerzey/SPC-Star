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
    EightConsecutiveOneSideOfCenterline
}

public enum PassFailStatus
{
    Pass,
    Fail
}

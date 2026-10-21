namespace SPCStar.Core.Services;

internal static class SchneiderContactLocationRules
{
    internal static readonly string[] DirectionChecks =
    [
        "Contact Location - A - Inside", "Contact Location - A - Outside",
        "Contact Location - B - Inside", "Contact Location - B - Outside"
    ];

    internal static readonly string[] RetiredChecks =
        ["Contact Location - A - In / Out", "Contact Location - B - In / Out"];

    internal static bool IsDirectionCheck(string partNum, string processCode, string name) =>
        string.Equals(partNum, "70122", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(processCode, "Schneider Production", StringComparison.OrdinalIgnoreCase) &&
        DirectionChecks.Contains(name, StringComparer.OrdinalIgnoreCase);

    internal static bool IsCombinedCheck(string partNum, string processCode, string name) =>
        string.Equals(partNum, "70122", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(processCode, "Schneider Production", StringComparison.OrdinalIgnoreCase) &&
        RetiredChecks.Contains(name, StringComparer.OrdinalIgnoreCase);
}

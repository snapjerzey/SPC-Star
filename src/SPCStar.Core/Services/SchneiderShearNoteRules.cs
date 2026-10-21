namespace SPCStar.Core.Services;

internal static class SchneiderShearNoteRules
{
    internal static readonly string[] SeparateChecks = ["Contact Tape Shear - A", "Contact Tape Shear - B"];

    internal static bool IsWithdrawnEntry(string part, string process, string name, string phase) =>
        string.Equals(process, "Schneider Production", StringComparison.OrdinalIgnoreCase) &&
        ((string.Equals(part, "70122", StringComparison.OrdinalIgnoreCase) && SeparateChecks.Contains(name, StringComparer.OrdinalIgnoreCase)) ||
         (string.Equals(part, "70123", StringComparison.OrdinalIgnoreCase) && string.Equals(name, "Shear Force", StringComparison.OrdinalIgnoreCase) &&
          string.Equals(phase, "Coil Change", StringComparison.OrdinalIgnoreCase)));
}

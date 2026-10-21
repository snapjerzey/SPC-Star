using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public static class JobMaterialRules
{
    public static MaterialChangeLog? CurrentIssue(ISpcRepository repository, string jobNum, string partNum, string materialPartNum, string? materialName = null) =>
        repository.MaterialChanges.Where(item =>
            item.JobNum.Equals(jobNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
            item.PartNum.Equals(partNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
            item.MaterialPartNum.Equals(materialPartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(materialName) || MaterialName(repository, item).Equals(materialName.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(item.NewLotNum))
        .OrderByDescending(item => item.Timestamp).ThenByDescending(item => item.SubmittedAt).FirstOrDefault();

    public static string? RequiredIssueMessage(ISpcRepository repository, string jobNum, string partNum)
    {
        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(partNum.Trim(), StringComparison.OrdinalIgnoreCase));
        if (part is null) return null;
        var missing = repository.PartMaterialFields.Where(field => field.PartId == part.Id && field.IsRequired)
            .GroupBy(field => $"{field.MaterialPartNum}|{field.MaterialName}", StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .Where(field => CurrentIssue(repository, jobNum, partNum, field.MaterialPartNum, field.MaterialName) is null)
            .Select(field => $"{field.MaterialName} ({field.MaterialPartNum})").ToArray();
        return missing.Length == 0 ? null : $"Save the material issue at job start before beginning the inspection. Missing material lot: {string.Join(", ", missing)}.";
    }

    private static PartMaterialField? Field(ISpcRepository repository, MaterialChangeLog log)
    {
        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(log.PartNum, StringComparison.OrdinalIgnoreCase));
        var fields = repository.PartMaterialFields.Where(item => item.PartId == part?.Id &&
            item.MaterialPartNum.Equals(log.MaterialPartNum, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(log.MaterialName) || item.MaterialName.Equals(log.MaterialName, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(item => item.MaterialName, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
        return fields.Length == 1 ? fields[0] : null;
    }

    public static string MaterialName(ISpcRepository repository, MaterialChangeLog log) =>
        string.IsNullOrWhiteSpace(log.MaterialName) ? Field(repository, log)?.MaterialName ?? "" : log.MaterialName;

    public static string MaterialDescription(ISpcRepository repository, MaterialChangeLog log) =>
        string.IsNullOrWhiteSpace(log.MaterialDescription) ? Field(repository, log)?.MaterialDescription ?? "" : log.MaterialDescription;
}

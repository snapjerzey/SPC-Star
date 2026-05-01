using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record MaterialChangeLogEntry(
    string JobNum,
    string PartNum,
    string MaterialPartNum,
    string OldLotNum,
    string NewLotNum,
    decimal? QuantityLoaded,
    string ResourceId,
    string OperatorUserId,
    DateTimeOffset Timestamp,
    string Reason);

public sealed class MaterialChangeLogService(InMemorySpcRepository repository)
{
    public ServiceResult<MaterialChangeLog> Record(MaterialChangeLogEntry entry)
    {
        var errors = Validate(entry);
        if (errors.Count > 0)
        {
            return ServiceResult<MaterialChangeLog>.Fail(errors);
        }

        var log = new MaterialChangeLog
        {
            JobNum = entry.JobNum.Trim(),
            PartNum = entry.PartNum.Trim(),
            MaterialPartNum = entry.MaterialPartNum.Trim(),
            OldLotNum = entry.OldLotNum.Trim(),
            NewLotNum = entry.NewLotNum.Trim(),
            QuantityLoaded = entry.QuantityLoaded,
            ResourceId = entry.ResourceId.Trim(),
            OperatorUserId = entry.OperatorUserId.Trim(),
            Timestamp = entry.Timestamp,
            Reason = entry.Reason.Trim()
        };

        repository.MaterialChanges.Add(log);
        return ServiceResult<MaterialChangeLog>.Ok(log);
    }

    private static List<string> Validate(MaterialChangeLogEntry entry)
    {
        var errors = new List<string>();
        Required(entry.JobNum, nameof(entry.JobNum), errors);
        Required(entry.PartNum, nameof(entry.PartNum), errors);
        Required(entry.MaterialPartNum, nameof(entry.MaterialPartNum), errors);
        Required(entry.OldLotNum, nameof(entry.OldLotNum), errors);
        Required(entry.NewLotNum, nameof(entry.NewLotNum), errors);
        Required(entry.ResourceId, nameof(entry.ResourceId), errors);
        Required(entry.OperatorUserId, nameof(entry.OperatorUserId), errors);
        Required(entry.Reason, nameof(entry.Reason), errors);
        if (entry.QuantityLoaded.HasValue && entry.QuantityLoaded.Value <= 0)
        {
            errors.Add("QuantityLoaded must be greater than zero when provided.");
        }

        return errors;
    }

    private static void Required(string value, string field, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} is required.");
        }
    }
}

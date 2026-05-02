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
    string Reason,
    string? DeviceId = null,
    string? ClientRecordId = null,
    DateTimeOffset? SubmittedAt = null);

public sealed class MaterialChangeLogService(ISpcRepository repository)
{
    public ServiceResult<MaterialChangeLog> Record(MaterialChangeLogEntry entry)
    {
        var errors = Validate(entry);
        if (errors.Count > 0)
        {
            return ServiceResult<MaterialChangeLog>.Fail(errors);
        }

        var duplicate = FindDuplicate(entry.DeviceId, entry.ClientRecordId);
        if (duplicate is not null)
        {
            return ServiceResult<MaterialChangeLog>.Ok(duplicate);
        }

        var log = new MaterialChangeLog
        {
            ClientRecordId = CleanOptional(entry.ClientRecordId),
            DeviceId = CleanOptional(entry.DeviceId),
            JobNum = entry.JobNum.Trim(),
            PartNum = entry.PartNum.Trim(),
            MaterialPartNum = entry.MaterialPartNum.Trim(),
            OldLotNum = entry.OldLotNum.Trim(),
            NewLotNum = entry.NewLotNum.Trim(),
            QuantityLoaded = entry.QuantityLoaded,
            ResourceId = entry.ResourceId.Trim(),
            OperatorUserId = entry.OperatorUserId.Trim(),
            Timestamp = entry.Timestamp,
            Reason = entry.Reason.Trim(),
            SubmittedAt = entry.SubmittedAt ?? entry.Timestamp,
            SyncedAt = DateTimeOffset.UtcNow
        };

        repository.MaterialChanges.Add(log);
        return ServiceResult<MaterialChangeLog>.Ok(log);
    }

    private MaterialChangeLog? FindDuplicate(string? deviceId, string? clientRecordId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(clientRecordId))
        {
            return null;
        }

        return repository.MaterialChanges.FirstOrDefault(item =>
            item.DeviceId?.Equals(deviceId.Trim(), StringComparison.OrdinalIgnoreCase) == true &&
            item.ClientRecordId?.Equals(clientRecordId.Trim(), StringComparison.OrdinalIgnoreCase) == true);
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

    private static string? CleanOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

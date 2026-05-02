using SPCStar.Core.Domain;

namespace SPCStar.Core.Services;

public sealed record OfflineSyncRequest(
    IReadOnlyList<InspectionMeasurementEntry>? Measurements,
    IReadOnlyList<MaterialChangeLogEntry>? MaterialChanges,
    IReadOnlyList<AlertOverrideRequest>? AlertOverrides);

public sealed record OfflineSyncAcceptedRecord(
    string EntityType,
    string? DeviceId,
    string? ClientRecordId,
    Guid ServerRecordId);

public sealed record OfflineSyncRejectedRecord(
    string EntityType,
    string? DeviceId,
    string? ClientRecordId,
    IReadOnlyList<string> Errors);

public sealed record OfflineSyncResponse(
    IReadOnlyList<OfflineSyncAcceptedRecord> Accepted,
    IReadOnlyList<OfflineSyncRejectedRecord> Rejected)
{
    public bool HasErrors => Rejected.Count > 0;
}

public sealed class OfflineSyncService(
    InspectionMeasurementService inspectionMeasurementService,
    MaterialChangeLogService materialChangeLogService,
    AlertOverrideService alertOverrideService)
{
    public OfflineSyncResponse Sync(OfflineSyncRequest request)
    {
        var accepted = new List<OfflineSyncAcceptedRecord>();
        var rejected = new List<OfflineSyncRejectedRecord>();

        foreach (var measurement in request.Measurements ?? [])
        {
            AddResult(
                "InspectionMeasurement",
                measurement.DeviceId,
                measurement.ClientRecordId,
                inspectionMeasurementService.EnterMeasurement(measurement),
                accepted,
                rejected);
        }

        foreach (var materialChange in request.MaterialChanges ?? [])
        {
            AddResult(
                "MaterialChange",
                materialChange.DeviceId,
                materialChange.ClientRecordId,
                materialChangeLogService.Record(materialChange),
                accepted,
                rejected);
        }

        foreach (var alertOverride in request.AlertOverrides ?? [])
        {
            AddResult(
                "AlertOverride",
                alertOverride.DeviceId,
                alertOverride.ClientRecordId,
                alertOverrideService.Override(alertOverride),
                accepted,
                rejected);
        }

        return new OfflineSyncResponse(accepted, rejected);
    }

    private static void AddResult<T>(
        string entityType,
        string? deviceId,
        string? clientRecordId,
        ServiceResult<T> result,
        List<OfflineSyncAcceptedRecord> accepted,
        List<OfflineSyncRejectedRecord> rejected)
    {
        if (!result.Succeeded || result.Value is null)
        {
            rejected.Add(new OfflineSyncRejectedRecord(entityType, deviceId, clientRecordId, result.Errors));
            return;
        }

        accepted.Add(new OfflineSyncAcceptedRecord(entityType, deviceId, clientRecordId, IdOf(result.Value)));
    }

    private static Guid IdOf<T>(T value)
    {
        return value switch
        {
            InspectionMeasurement measurement => measurement.Id,
            MaterialChangeLog materialChange => materialChange.Id,
            AlertOverride alertOverride => alertOverride.Id,
            _ => throw new InvalidOperationException($"Unsupported synced entity type {typeof(T).Name}.")
        };
    }
}

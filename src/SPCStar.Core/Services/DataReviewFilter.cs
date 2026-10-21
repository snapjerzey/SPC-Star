using SPCStar.Core.Domain;

namespace SPCStar.Core.Services;

public sealed record DataReviewFilter(string? ProcessCode = null, int? OperationSeq = null,
    string? InspectionPhase = null, string? ResourceId = null, string? CharacteristicName = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null)
{
    public static bool Text(string? filter, string? value) => string.IsNullOrWhiteSpace(filter) ||
        filter.Trim().Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase);

    public bool Context(string? process, int? operation, string? phase, string? resource, DateTimeOffset at) =>
        Text(ProcessCode, process) && (!OperationSeq.HasValue || OperationSeq == operation) &&
        Text(InspectionPhase, phase) && Text(ResourceId, resource) &&
        (!From.HasValue || at >= From) && (!To.HasValue || at <= To);

    public bool Measurement(InspectionMeasurement item) =>
        Context(item.ProcessCode, item.OperationSeq, item.InspectionPhase, item.ResourceId, item.Timestamp) &&
        Text(CharacteristicName, item.CharacteristicName);
}

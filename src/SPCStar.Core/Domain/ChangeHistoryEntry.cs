namespace SPCStar.Core.Domain;

public sealed record SavedFieldChange(string Field, string? Before, string? After);
public sealed record SavedRecordChange(string Area, string Record, string PartNum, string Operation,
    string Phase, string Action, IReadOnlyList<SavedFieldChange> Fields);
public sealed record ChangeHistoryEntry(Guid Id, DateTimeOffset SavedAt, string UserName, string Source,
    string Summary, IReadOnlyList<SavedRecordChange> Changes, string? Reason = null, string? GroupId = null);

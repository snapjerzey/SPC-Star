using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record JobHistoryEntryDto(
    Guid Id,
    string EntryType,
    string JobNum,
    string PartNum,
    string ResourceId,
    string OperatorUserId,
    DateTimeOffset Timestamp,
    string? NoteText = null,
    string? CharacteristicName = null,
    RuleTriggered? RuleTriggered = null,
    string? Detail = null,
    AlertStatus? Status = null,
    string? OverrideUserId = null,
    string? OverrideRole = null,
    string? CauseCategory = null,
    string? CauseText = null,
    string? SolutionText = null,
    DateTimeOffset? UnlockedAt = null,
    string? MaterialPartNum = null,
    string? NewLotNum = null,
    decimal? QuantityLoaded = null,
    string? Reason = null,
    decimal? OldValue = null,
    decimal? NewValue = null,
    string? OldInspectionPhase = null,
    string? NewInspectionPhase = null);

public sealed class JobHistoryService(ISpcRepository repository)
{
    public IReadOnlyList<JobHistoryEntryDto> GetForJob(string jobNum)
    {
        if (string.IsNullOrWhiteSpace(jobNum))
        {
            return [];
        }

        var normalizedJob = jobNum.Trim();
        var notes = repository.JobNotes
            .Where(note => note.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase))
            .Select(note => new JobHistoryEntryDto(
                note.Id,
                "Note",
                note.JobNum,
                note.PartNum,
                note.ResourceId,
                note.OperatorUserId,
                note.Timestamp,
                NoteText: note.NoteText));

        var locks = repository.Alerts
            .Where(alert => alert.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase))
            .Select(alert =>
            {
                var audit = repository.AlertOverrides
                    .Where(overrideRow => overrideRow.AlertId == alert.Id)
                    .OrderByDescending(overrideRow => overrideRow.UnlockedAt)
                    .FirstOrDefault();

                return new JobHistoryEntryDto(
                    alert.Id,
                    "Lock",
                    alert.JobNum,
                    alert.PartNum,
                    alert.ResourceId,
                    alert.OperatorUserId,
                    audit?.UnlockedAt ?? alert.LockedAt,
                    CharacteristicName: alert.CharacteristicName,
                    RuleTriggered: alert.RuleTriggered,
                    Detail: alert.Detail,
                    Status: alert.Status,
                    OverrideUserId: audit?.OverrideUserId,
                    OverrideRole: audit?.OverrideRole,
                    CauseCategory: audit?.CauseCategory,
                    CauseText: audit?.CauseText,
                    SolutionText: audit?.SolutionText,
                    UnlockedAt: audit?.UnlockedAt);
            });

        var materialChanges = repository.MaterialChanges
            .Where(change => change.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase))
            .Select(change => new JobHistoryEntryDto(
                change.Id,
                "Material",
                change.JobNum,
                change.PartNum,
                change.ResourceId,
                change.OperatorUserId,
                change.Timestamp,
                MaterialPartNum: change.MaterialPartNum,
                NewLotNum: change.NewLotNum,
                QuantityLoaded: change.QuantityLoaded,
                Reason: change.Reason));

        var edits = repository.MeasurementEditAudits
            .Where(edit => edit.JobNum.Equals(normalizedJob, StringComparison.OrdinalIgnoreCase))
            .Select(edit => new JobHistoryEntryDto(
                edit.Id,
                "MeasurementEdit",
                edit.JobNum,
                edit.PartNum,
                edit.ResourceId,
                edit.EditedByUserId,
                edit.EditedAt,
                CharacteristicName: edit.CharacteristicName,
                OldValue: edit.OldValue,
                NewValue: edit.NewValue,
                OldInspectionPhase: edit.OldInspectionPhase,
                NewInspectionPhase: edit.NewInspectionPhase));

        return notes
            .Concat(locks)
            .Concat(materialChanges)
            .Concat(edits)
            .OrderByDescending(entry => entry.Timestamp)
            .ToArray();
    }
}
